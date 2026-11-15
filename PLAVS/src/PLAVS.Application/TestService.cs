using Microsoft.EntityFrameworkCore;
using PLAVS.Domain;

namespace PLAVS.Application;

public sealed class TestService(IPlavsDb db, AccessService access, IWorkflowLock gate)
{
    public async Task<Page<TestView>> ListAsync(Actor actor, int page, int size, CancellationToken ct, Guid? disciplineId = null)
    {
        var query = access.VisibleTests(actor).AsNoTracking().Include(t => t.Questions).Include(t => t.Groups).AsSplitQuery();
        if (disciplineId.HasValue) query = query.Where(t => t.DisciplineId == disciplineId);
        var count = await query.CountAsync(ct);
        var tests = await query.OrderByDescending(t => t.CreatedAt).Skip((page - 1) * size).Take(size).ToArrayAsync(ct);
        var names = await db.Disciplines.ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var ids = tests.Select(t => t.Id).ToArray();
        var attempts = await db.Attempts.Where(a => a.StudentId == actor.Id && ids.Contains(a.TestId)).ToDictionaryAsync(a => a.TestId, ct);
        return new(tests.Select(t => new TestView(t.Id, t.DisciplineId, names[t.DisciplineId], t.Title, t.Instructions, t.Published,
            t.Archived, t.ResultsVisible, t.OpensAt, t.ClosesAt, t.DurationMinutes,
            t.Questions.OrderBy(q => q.Position).Select(q => new TestQuestionInput(q.QuestionId, q.MaxPoints)).ToArray(),
            actor.IsStudent ? [] : t.Groups.Select(g => g.GroupId).ToArray(), attempts.GetValueOrDefault(t.Id)?.Id, attempts.GetValueOrDefault(t.Id)?.Status)).ToArray(), count, page, size);
    }
    public async Task<TestView> GetAsync(Actor actor, Guid id, CancellationToken ct)
    {
        var t = await access.VisibleTests(actor).AsNoTracking().Include(t => t.Questions).Include(t => t.Groups).AsSplitQuery().SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw AppException.NotFound();
        var name = await db.Disciplines.Where(d => d.Id == t.DisciplineId).Select(d => d.Name).SingleAsync(ct);
        var attempt = await db.Attempts.AsNoTracking().SingleOrDefaultAsync(a => a.StudentId == actor.Id && a.TestId == id, ct);
        return new(t.Id, t.DisciplineId, name, t.Title, t.Instructions, t.Published, t.Archived, t.ResultsVisible, t.OpensAt, t.ClosesAt, t.DurationMinutes,
            t.Questions.OrderBy(q => q.Position).Select(q => new TestQuestionInput(q.QuestionId, q.MaxPoints)).ToArray(),
            actor.IsStudent ? [] : t.Groups.Select(g => g.GroupId).ToArray(), attempt?.Id, attempt?.Status);
    }
    public async Task<Guid> CreateAsync(Actor actor, TestInput input, CancellationToken ct)
    {
        await ValidateAsync(actor, input, ct);
        var t = new Assessment { AuthorId = actor.Id, DisciplineId = input.DisciplineId };
        Apply(t, input); db.Tests.Add(t);
        access.Audit(actor, "Test.Create", "Test", t.Id);
        await db.SaveChangesAsync(ct);
        return t.Id;
    }
    public Task<Guid> UpdateAsync(Actor actor, Guid id, TestInput input, CancellationToken ct) => gate.RunAsync(id, async () =>
    {
        var t = await db.Tests.Include(t => t.Questions).Include(t => t.Groups).SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw AppException.NotFound();
        await access.RequireTeacherAsync(actor, t.DisciplineId, ct);
        if (t.DisciplineId != input.DisciplineId) throw AppException.Invalid("Дисципліну тесту змінювати не можна.");
        if (await db.Attempts.AnyAsync(a => a.TestId == id, ct)) throw AppException.Conflict("Тест уже має спроби. Можна лише змінити видимість результатів або архівувати його.");
        await ValidateAsync(actor, input, ct);
        db.TestQuestions.RemoveRange(t.Questions); db.TestGroups.RemoveRange(t.Groups);
        t.Questions.Clear(); t.Groups.Clear();
        Apply(t, input);
        access.Audit(actor, "Test.Update", "Test", id);
        await db.SaveChangesAsync(ct); return id;
    }, ct);
    public Task<Guid> SetVisibilityAsync(Actor actor, Guid id, bool visible, CancellationToken ct) => gate.RunAsync(id, async () =>
    {
        var t = await db.Tests.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw AppException.NotFound();
        await access.RequireTeacherAsync(actor, t.DisciplineId, ct);
        t.ResultsVisible = visible; access.Audit(actor, "Test.ResultsVisibility", "Test", id);
        await db.SaveChangesAsync(ct); return id;
    }, ct);
    public Task<Guid> ArchiveAsync(Actor actor, Guid id, CancellationToken ct) => gate.RunAsync(id, async () =>
    {
        var t = await db.Tests.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw AppException.NotFound();
        await access.RequireTeacherAsync(actor, t.DisciplineId, ct);
        t.Archived = true; access.Audit(actor, "Test.Archive", "Test", id);
        await db.SaveChangesAsync(ct); return id;
    }, ct);
    public Task<Guid> StartAsync(Actor actor, Guid id, CancellationToken ct) => gate.RunAsync(id, async () =>
    {
        if (!actor.IsStudent) throw new AppException(403, "Лише студент може розпочати тест.");
        var test = await access.VisibleTests(actor).Include(t => t.Questions).SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw AppException.NotFound();
        var existing = await db.Attempts.SingleOrDefaultAsync(a => a.TestId == id && a.StudentId == actor.Id, ct);
        if (existing is not null) return existing.Id;
        var now = DateTimeOffset.UtcNow;
        if ((test.OpensAt.HasValue && now < test.OpensAt) || (test.ClosesAt.HasValue && now >= test.ClosesAt))
            throw AppException.Conflict("Тест зараз недоступний за розкладом.");
        var ids = test.Questions.Select(q => q.QuestionId).ToArray();
        var questions = await db.Questions.Where(q => ids.Contains(q.Id)).Include(q => q.References).Include(q => q.Variants).Include(q => q.Keywords).AsSplitQuery().ToDictionaryAsync(q => q.Id, ct);
        if (questions.Count != ids.Length || questions.Values.Any(q => q.Status != QuestionStatus.Active || !q.References.Any(r => r.Active)))
            throw AppException.Conflict("Деякі питання тесту недоступні. Зверніться до викладача.");
        var settings = await db.Settings.OrderByDescending(s => s.Version).FirstAsync(ct);
        var snapshot = new AttemptSnapshot(test.Title, SettingsSpec.From(settings), test.Questions.OrderBy(tq => tq.Position).Select(tq =>
        {
            var q = questions[tq.QuestionId];
            return new QuestionSnapshot(q.Id, q.Text, q.Revision, tq.MaxPoints,
                q.References.Where(r => r.Active).Select(r => new ReferenceSpec(r.Id, r.Text)).Concat(q.Variants.Where(v => v.Status == VariantStatus.Approved).Select(v => new ReferenceSpec(v.Id, v.Text, true))).ToArray(),
                q.Keywords.Select(k => new KeywordSpec(k.Term, k.Synonyms, k.Weight)).ToArray());
        }).ToArray());
        var deadline = now.AddMinutes(test.DurationMinutes);
        if (test.ClosesAt.HasValue && test.ClosesAt < deadline) deadline = test.ClosesAt.Value;
        var attempt = new TestAttempt { TestId = id, StudentId = actor.Id, Deadline = deadline, SnapshotJson = JsonDefaults.Write(snapshot) };
        db.Attempts.Add(attempt); access.Audit(actor, "Attempt.Start", "Attempt", attempt.Id);
        await db.SaveChangesAsync(ct); return attempt.Id;
    }, ct);
    public async Task<AttemptView> AttemptAsync(Actor actor, Guid id, CancellationToken ct)
    {
        var a = await db.Attempts.AsNoTracking().Include(a => a.Answers).SingleOrDefaultAsync(a => a.Id == id && a.StudentId == actor.Id, ct) ?? throw AppException.NotFound();
        var snapshot = JsonDefaults.Read<AttemptSnapshot>(a.SnapshotJson);
        return new(a.Id, snapshot.Title, a.Status, a.Deadline, snapshot.Questions.Select(q =>
        {
            var answer = a.Answers.SingleOrDefault(x => x.QuestionId == q.Id);
            return new StudentQuestion(q.Id, q.Text, q.MaxPoints, answer?.Id, answer?.Text ?? "");
        }).ToArray());
    }
    private async Task ValidateAsync(Actor actor, TestInput i, CancellationToken ct)
    {
        await access.RequireTeacherAsync(actor, i.DisciplineId, ct);
        if (!await db.Disciplines.AnyAsync(d => d.Id == i.DisciplineId && !d.Archived, ct)) throw AppException.Invalid("Дисципліна архівна.");
        if (i.Questions is null || i.Questions.Length is < 1 or > 100 || i.Questions.Select(q => q.QuestionId).Distinct().Count() != i.Questions.Length || i.Questions.Any(q => !double.IsFinite(q.MaxPoints) || q.MaxPoints is <= 0 or > 1000))
            throw AppException.Invalid("Тест має містити 1–100 різних питань із додатними балами.");
        var ids = i.Questions.Select(q => q.QuestionId).ToArray();
        if (await db.Questions.CountAsync(q => ids.Contains(q.Id) && q.DisciplineId == i.DisciplineId && q.Status == QuestionStatus.Active, ct) != ids.Length)
            throw AppException.Invalid("Усі питання мають бути активними й належати дисципліні тесту.");
        if (i.GroupIds is null || i.GroupIds.Length == 0 || i.GroupIds.Distinct().Count() != i.GroupIds.Length || await db.DisciplineGroups.CountAsync(g => g.DisciplineId == i.DisciplineId && i.GroupIds.Contains(g.GroupId) && db.Groups.Any(a => a.Id == g.GroupId && !a.Archived), ct) != i.GroupIds.Length)
            throw AppException.Invalid("Оберіть навчальні групи, призначені дисципліні.");
        if (i.OpensAt.HasValue && i.ClosesAt.HasValue && i.ClosesAt <= i.OpensAt) throw AppException.Invalid("Завершення має бути пізніше за початок.");
    }
    private static void Apply(Assessment t, TestInput i)
    {
        t.Title = i.Title.Trim(); t.Instructions = i.Instructions.Trim(); t.Published = i.Published; t.ResultsVisible = i.ResultsVisible;
        t.OpensAt = i.OpensAt?.ToUniversalTime(); t.ClosesAt = i.ClosesAt?.ToUniversalTime(); t.DurationMinutes = i.DurationMinutes;
        t.Questions = i.Questions.Select((q, p) => new TestQuestion { TestId = t.Id, QuestionId = q.QuestionId, Position = p + 1, MaxPoints = q.MaxPoints }).ToList();
        t.Groups = i.GroupIds.Select(id => new TestGroup { TestId = t.Id, GroupId = id }).ToList();
    }
}
