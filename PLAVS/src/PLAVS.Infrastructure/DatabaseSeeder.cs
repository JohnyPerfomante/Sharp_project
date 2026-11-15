using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PLAVS.Application;
using PLAVS.Domain;

namespace PLAVS.Infrastructure;

public sealed record SeedKeyword(string Term, string[] Synonyms, double Weight);
public sealed record SeedQuestion(string Code, string Discipline, string Topic, string Text, string Reference, string[] Variants, SeedKeyword[] Keywords);
public sealed class DatabaseSeeder(PlavsDbContext db, UserManager<AppUser> users, RoleManager<IdentityRole> roles)
{
    public async Task InitializeRolesAsync()
    {
        foreach (var name in Roles.All)
            if (!await roles.RoleExistsAsync(name)) Ensure(await roles.CreateAsync(new IdentityRole(name)));
    }
    public async Task<string> BootstrapAdminAsync(string email, string password)
    {
        await InitializeRolesAsync();
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null) throw AppException.Conflict("Такий email уже існує. Початкова команда не змінює наявних користувачів.");
        if ((await users.GetUsersInRoleAsync(Roles.Administrator)).Count != 0) throw AppException.Conflict("Адміністратора вже створено. Керуйте користувачами через кабінет.");
        var user = new AppUser { UserName = email, Email = email, FullName = "Адміністратор ПЛАВС", EmailConfirmed = true };
        Ensure(await users.CreateAsync(user, password)); Ensure(await users.AddToRoleAsync(user, Roles.Administrator));
        db.Settings.Add(new AnalysisSettings { AuthorId = user.Id });
        await db.SaveChangesAsync(); return user.Id;
    }
    public async Task SeedDevelopmentAsync(string seedFile, string password)
    {
        await InitializeRolesAsync();
        var admin = await GetOrCreateAsync("admin@plavs.local", "Адміністратор системи", Roles.Administrator, password);
        const string teacherName = "Ірина Миколаївна";
        var teacher = await GetOrCreateAsync("teacher@plavs.local", teacherName, Roles.Teacher, password);
        // Rename an existing demo account while preserving names edited independently by an administrator.
        if (teacher.FullName == "Олена Ткаченко")
        {
            teacher.FullName = teacherName;
            Ensure(await users.UpdateAsync(teacher));
        }
        var student = await GetOrCreateAsync("student@plavs.local", "Назар Кузьменко", Roles.Student, password);
        if (!await db.Settings.AnyAsync()) db.Settings.Add(new AnalysisSettings { AuthorId = admin.Id });
        var group = await db.Groups.SingleOrDefaultAsync(g => g.Name == "ІПЗ-3 СК");
        if (group is null) { group = new AcademicGroup { Name = "ІПЗ-3 СК" }; db.Groups.Add(group); }
        if (!await db.GroupMembers.AnyAsync(m => m.GroupId == group.Id && m.UserId == student.Id))
            db.GroupMembers.Add(new GroupMember { GroupId = group.Id, UserId = student.Id });
        await db.SaveChangesAsync();
        await SeedContentAsync(seedFile, teacher.Id, group.Id);
    }
    public async Task SeedContentAsync(string seedFile, string teacherId, Guid groupId)
    {
        var teacher = await users.FindByIdAsync(teacherId) ?? throw AppException.NotFound();
        if (!await users.IsInRoleAsync(teacher, Roles.Teacher)) throw AppException.Invalid("Автор початкового наповнення має бути викладачем.");
        var seeds = JsonDefaults.Read<SeedQuestion[]>(await File.ReadAllTextAsync(seedFile));
        if (seeds.Length < 70 || seeds.Any(q => q.Variants.Length < 3 || q.Keywords.Length == 0)) throw new InvalidOperationException("Неповний seed-файл.");
        await using var tx = await db.Database.BeginTransactionAsync();
        foreach (var discipline in seeds.GroupBy(q => q.Discipline))
        {
            var d = await db.Disciplines.SingleOrDefaultAsync(d => d.Name == discipline.Key);
            if (d is null) { d = new Discipline { Name = discipline.Key, Description = "Навчальні питання для спеціальностей ІПЗ та КН." }; db.Disciplines.Add(d); }
            if (!await db.DisciplineTeachers.AnyAsync(t => t.DisciplineId == d.Id && t.UserId == teacherId))
                db.DisciplineTeachers.Add(new DisciplineTeacher { DisciplineId = d.Id, UserId = teacherId });
            if (!await db.DisciplineGroups.AnyAsync(g => g.DisciplineId == d.Id && g.GroupId == groupId))
                db.DisciplineGroups.Add(new DisciplineGroup { DisciplineId = d.Id, GroupId = groupId });
            await db.SaveChangesAsync();
            foreach (var s in discipline)
            {
                // Deterministic IDs make seeding safe to repeat without overwriting teacher edits.
                var id = new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("PLAVS.SEED." + s.Code)));
                if (await db.Questions.AnyAsync(q => q.Id == id)) continue;
                db.Questions.Add(new Question { Id = id, DisciplineId = d.Id, Topic = s.Topic, Text = s.Text, AuthorId = teacherId,
                    References = [new ReferenceAnswer { QuestionId = id, Text = s.Reference, IsPrimary = true }],
                    Variants = s.Variants.Select(v => new AnswerVariant { QuestionId = id, Text = v, AuthorId = teacherId, Source = "SeedDraft", Status = VariantStatus.Pending }).ToList(),
                    Keywords = s.Keywords.Select(k => new Keyword { QuestionId = id, Term = k.Term, Synonyms = k.Synonyms, Weight = k.Weight }).ToList() });
            }
            await db.SaveChangesAsync();
            if (!await db.Tests.AnyAsync(t => t.DisciplineId == d.Id))
            {
                var questions = await db.Questions.Where(q => q.DisciplineId == d.Id && q.Status == QuestionStatus.Active).OrderBy(q => q.Id).Take(3).ToArrayAsync();
                var test = new Assessment { DisciplineId = d.Id, Title = discipline.Key + ": базові поняття", Instructions = "Дайте розгорнуті відповіді своїми словами. Збережіть кожну відповідь і надішліть спробу після завершення.",
                    Published = true, ResultsVisible = false, DurationMinutes = 45, AuthorId = teacherId };
                test.Questions = questions.Select((q, i) => new TestQuestion { TestId = test.Id, QuestionId = q.Id, Position = i + 1, MaxPoints = 10 }).ToList();
                test.Groups = [new TestGroup { TestId = test.Id, GroupId = groupId }]; db.Tests.Add(test);
            }
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    private async Task<AppUser> GetOrCreateAsync(string email, string name, string role, string password)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is not null) return user;
        user = new AppUser { UserName = email, Email = email, FullName = name, EmailConfirmed = true };
        Ensure(await users.CreateAsync(user, password)); Ensure(await users.AddToRoleAsync(user, role)); return user;
    }
    private static void Ensure(IdentityResult r)
    {
        if (!r.Succeeded) throw new InvalidOperationException(string.Join("; ", r.Errors.Select(e => e.Description)));
    }
}
