using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PLAVS.Application;
using PLAVS.Domain;

namespace PLAVS.Infrastructure;

public sealed class AdministrationService(PlavsDbContext db, UserManager<AppUser> users, AccessService access, IWorkflowLock gate)
{
    private static readonly Guid AdministrationLock = Guid.Parse("f4c03563-01e2-4a3e-a4da-000000000001");
    public async Task<Page<UserView>> UsersAsync(string? search, int page, int size, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(u => u.FullName.ToLower().Contains(search.ToLower()) || u.Email!.ToLower().Contains(search.ToLower()));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(u => u.FullName).Skip((page - 1) * size).Take(size).ToArrayAsync(ct);
        var result = new List<UserView>();
        foreach (var u in rows) result.Add(new(u.Id, u.Email!, u.FullName, (await users.GetRolesAsync(u)).ToArray(), u.Blocked));
        return new(result, count, page, size);
    }
    public Task<string> SaveUserAsync(Actor actor, string? id, UserInput input, CancellationToken ct) => gate.RunAsync(AdministrationLock, async () =>
    {
        if (!Roles.All.Contains(input.Role)) throw AppException.Invalid("Невідома роль.");
        var create = id is null;
        var user = create ? new AppUser() : await users.FindByIdAsync(id!) ?? throw AppException.NotFound();
        if (user.Id == actor.Id && (input.Blocked || input.Role != Roles.Administrator)) throw AppException.Invalid("Не можна заблокувати себе або зняти власну роль адміністратора.");
        var current = create ? [] : (await users.GetRolesAsync(user)).ToArray();
        if (current.Contains(Roles.Administrator) && (input.Blocked || input.Role != Roles.Administrator) && (await users.GetUsersInRoleAsync(Roles.Administrator)).Count(u => !u.Blocked) <= 1)
            throw AppException.Conflict("Потрібен хоча б один активний адміністратор.");
        user.FullName = input.FullName.Trim(); user.Blocked = input.Blocked;
        if (create)
        {
            if (string.IsNullOrWhiteSpace(input.Password)) throw AppException.Invalid("Для нового користувача потрібен пароль.");
            user.UserName = input.Email.Trim(); user.Email = input.Email.Trim(); user.EmailConfirmed = true;
            Ensure(await users.CreateAsync(user, input.Password));
        }
        else
        {
            Ensure(await users.SetEmailAsync(user, input.Email.Trim()));
            Ensure(await users.SetUserNameAsync(user, input.Email.Trim()));
            Ensure(await users.UpdateAsync(user));
            if (!string.IsNullOrWhiteSpace(input.Password))
            {
                var token = await users.GeneratePasswordResetTokenAsync(user);
                Ensure(await users.ResetPasswordAsync(user, token, input.Password));
            }
        }
        if (current.Length > 0) Ensure(await users.RemoveFromRolesAsync(user, current));
        Ensure(await users.AddToRoleAsync(user, input.Role));
        Ensure(await users.UpdateSecurityStampAsync(user));
        access.Audit(actor, create ? "User.Create" : "User.Update", "User", user.Id);
        await db.SaveChangesAsync(ct); return user.Id;
    }, ct);
    public async Task<object> DirectoryAsync(Actor actor, CancellationToken ct)
    {
        var ids = access.DisciplineIds(actor);
        var teacherRole = await db.Roles.Where(r => r.Name == Roles.Teacher).Select(r => r.Id).SingleAsync(ct);
        var studentRole = await db.Roles.Where(r => r.Name == Roles.Student).Select(r => r.Id).SingleAsync(ct);
        return new
        {
            Teachers = actor.IsAdmin ? await db.Users.Where(u => db.UserRoles.Any(r => r.UserId == u.Id && r.RoleId == teacherRole)).Select(u => new { u.Id, u.FullName, u.Email }).ToArrayAsync(ct) : [],
            Students = actor.IsAdmin ? await db.Users.Where(u => db.UserRoles.Any(r => r.UserId == u.Id && r.RoleId == studentRole)).Select(u => new { u.Id, u.FullName, u.Email }).ToArrayAsync(ct) : [],
            Assignments = actor.IsAdmin ? await db.DisciplineTeachers.Select(t => new { t.DisciplineId, t.UserId }).ToArrayAsync(ct) : [],
            DisciplineGroups = await db.DisciplineGroups.Where(d => ids.Contains(d.DisciplineId)).Select(g => new { g.DisciplineId, g.GroupId }).ToArrayAsync(ct)
        };
    }
    public async Task<DisciplineView[]> DisciplinesAsync(Actor actor, CancellationToken ct)
    {
        var ids = access.DisciplineIds(actor);
        return await db.Disciplines.AsNoTracking().Where(d => ids.Contains(d.Id)).OrderBy(d => d.Name).Select(d =>
            new DisciplineView(d.Id, d.Name, d.Description, actor.IsStudent && !actor.IsTeacher ? 0 : db.Questions.Count(q => q.DisciplineId == d.Id && q.Status != QuestionStatus.Archived), db.Tests.Count(t => t.DisciplineId == d.Id && !t.Archived && (actor.IsTeacher || actor.IsAdmin || t.Published)), d.Archived)).ToArrayAsync(ct);
    }
    public async Task<Guid> SaveDisciplineAsync(Actor actor, Guid? id, DisciplineInput input, CancellationToken ct)
    {
        var d = id.HasValue ? await db.Disciplines.SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw AppException.NotFound() : new Discipline();
        if (id.HasValue && !actor.IsAdmin) await access.RequireTeacherAsync(actor, id.Value, ct);
        d.Name = input.Name.Trim(); d.Description = input.Description.Trim();
        if (!id.HasValue)
        {
            db.Disciplines.Add(d);
            if (actor.IsTeacher) db.DisciplineTeachers.Add(new DisciplineTeacher { DisciplineId = d.Id, UserId = actor.Id });
        }
        access.Audit(actor, "Discipline.Save", "Discipline", d.Id); await db.SaveChangesAsync(ct); return d.Id;
    }
    public async Task ArchiveDisciplineAsync(Actor actor, Guid id, CancellationToken ct)
    {
        var d = await db.Disciplines.SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw AppException.NotFound();
        if (!actor.IsAdmin) await access.RequireTeacherAsync(actor, id, ct);
        if (await db.Attempts.AnyAsync(a => a.Status == AttemptStatus.InProgress && db.Tests.Any(t => t.Id == a.TestId && t.DisciplineId == id), ct)) throw AppException.Conflict("У дисципліні є незавершені спроби.");
        d.Archived = true;
        foreach (var t in await db.Tests.Where(t => t.DisciplineId == id).ToArrayAsync(ct)) t.Archived = true;
        access.Audit(actor, "Discipline.Archive", "Discipline", id); await db.SaveChangesAsync(ct);
    }
    public async Task<GroupView[]> GroupsAsync(Actor actor, CancellationToken ct)
    {
        var ids = access.DisciplineIds(actor);
        var groups = await db.Groups.Where(g => actor.IsAdmin || db.DisciplineGroups.Any(d => d.GroupId == g.Id && ids.Contains(d.DisciplineId))).OrderBy(g => g.Name).ToArrayAsync(ct);
        var selected = groups.Select(g => g.Id).ToArray();
        var members = actor.IsAdmin ? await db.GroupMembers.Where(m => selected.Contains(m.GroupId)).ToArrayAsync(ct) : [];
        return groups.Select(g => new GroupView(g.Id, g.Name, g.Archived, members.Where(m => m.GroupId == g.Id).Select(m => m.UserId).ToArray())).ToArray();
    }
    public async Task<Guid> SaveGroupAsync(Actor actor, Guid? id, GroupInput input, CancellationToken ct)
    {
        var group = id.HasValue ? await db.Groups.SingleOrDefaultAsync(g => g.Id == id, ct) ?? throw AppException.NotFound() : new AcademicGroup();
        group.Name = input.Name.Trim(); if (!id.HasValue) db.Groups.Add(group);
        access.Audit(actor, "Group.Save", "Group", group.Id); await db.SaveChangesAsync(ct); return group.Id;
    }
    public Task<Guid> SetMembersAsync(Actor actor, Guid groupId, string[] memberIds, CancellationToken ct) => gate.RunAsync(groupId, async () =>
    {
        if (!await db.Groups.AnyAsync(g => g.Id == groupId && !g.Archived, ct)) throw AppException.NotFound();
        var valid = (await users.GetUsersInRoleAsync(Roles.Student)).Select(u => u.Id).ToHashSet();
        if (memberIds.Length > 1000 || memberIds.Distinct().Count() != memberIds.Length || memberIds.Any(id => !valid.Contains(id))) throw AppException.Invalid("Група може містити лише різних студентів.");
        var existing = await db.GroupMembers.Where(m => m.GroupId == groupId).ToArrayAsync(ct);
        db.GroupMembers.RemoveRange(existing.Where(m => !memberIds.Contains(m.UserId)));
        db.GroupMembers.AddRange(memberIds.Where(id => !existing.Any(m => m.UserId == id)).Select(id => new GroupMember { GroupId = groupId, UserId = id }));
        access.Audit(actor, "Group.Members", "Group", groupId); await db.SaveChangesAsync(ct); return groupId;
    }, ct);
    public Task<Guid> AssignAsync(Actor actor, Guid disciplineId, AssignmentInput input, CancellationToken ct) => gate.RunAsync(disciplineId, async () =>
    {
        if (!await db.Disciplines.AnyAsync(d => d.Id == disciplineId, ct)) throw AppException.NotFound();
        var teachers = (await users.GetUsersInRoleAsync(Roles.Teacher)).Select(u => u.Id).ToHashSet();
        if (input.TeacherIds is null || input.GroupIds is null || input.TeacherIds.Distinct().Count() != input.TeacherIds.Length || input.GroupIds.Distinct().Count() != input.GroupIds.Length || input.TeacherIds.Any(id => !teachers.Contains(id)) || await db.Groups.CountAsync(g => input.GroupIds.Contains(g.Id) && !g.Archived, ct) != input.GroupIds.Length)
            throw AppException.Invalid("Перевірте викладачів і групи.");
        var oldT = await db.DisciplineTeachers.Where(t => t.DisciplineId == disciplineId).ToArrayAsync(ct);
        var oldG = await db.DisciplineGroups.Where(g => g.DisciplineId == disciplineId).ToArrayAsync(ct);
        db.DisciplineTeachers.RemoveRange(oldT.Where(t => !input.TeacherIds.Contains(t.UserId)));
        db.DisciplineTeachers.AddRange(input.TeacherIds.Where(id => !oldT.Any(t => t.UserId == id)).Select(id => new DisciplineTeacher { DisciplineId = disciplineId, UserId = id }));
        db.DisciplineGroups.RemoveRange(oldG.Where(g => !input.GroupIds.Contains(g.GroupId)));
        db.DisciplineGroups.AddRange(input.GroupIds.Where(id => !oldG.Any(g => g.GroupId == id)).Select(id => new DisciplineGroup { DisciplineId = disciplineId, GroupId = id }));
        access.Audit(actor, "Discipline.Assign", "Discipline", disciplineId); await db.SaveChangesAsync(ct); return disciplineId;
    }, ct);
    public async Task ArchiveGroupAsync(Actor actor, Guid id, CancellationToken ct)
    {
        var g = await db.Groups.SingleOrDefaultAsync(g => g.Id == id, ct) ?? throw AppException.NotFound();
        g.Archived = true; access.Audit(actor, "Group.Archive", "Group", id); await db.SaveChangesAsync(ct);
    }
    public Task<SettingsSpec> SaveSettingsAsync(Actor actor, SettingsSpec spec, CancellationToken ct) => gate.RunAsync(AdministrationLock, async () =>
    {
        spec.Validate();
        var latest = await db.Settings.OrderByDescending(s => s.Version).FirstOrDefaultAsync(ct);
        if (latest is not null && latest.Version != spec.Version) throw AppException.Conflict("Налаштування вже змінено. Оновіть їх перед збереженням.");
        var s = new AnalysisSettings { Version = (latest?.Version ?? 0) + 1, Alpha = spec.Alpha, Beta = spec.Beta, Gamma = spec.Gamma,
            HighThreshold = spec.HighThreshold, PartialThreshold = spec.PartialThreshold, AuthorId = actor.Id };
        db.Settings.Add(s); access.Audit(actor, "Settings.CreateVersion", "AnalysisSettings", s.Id); await db.SaveChangesAsync(ct); return SettingsSpec.From(s);
    }, ct);
    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw AppException.Invalid("Не вдалося зберегти користувача: " + string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
