using Microsoft.EntityFrameworkCore;
using PLAVS.Domain;

namespace PLAVS.Application;

public sealed class QuestionService(IPlavsDb db, AccessService access, IWorkflowLock gate)
{
    private IQueryable<Question> Query(Actor actor) => db.Questions.Where(q =>
        db.DisciplineTeachers.Any(t => t.UserId == actor.Id && t.DisciplineId == q.DisciplineId))
        .Include(q => q.References).Include(q => q.Variants).Include(q => q.Keywords).AsSplitQuery();

    public async Task<Page<QuestionView>> ListAsync(Actor actor, string? search, Guid? disciplineId, QuestionStatus? status, int page, int size, CancellationToken ct)
    {
        var query = Query(actor).AsNoTracking();
        if (disciplineId.HasValue) query = query.Where(q => q.DisciplineId == disciplineId);
        if (status.HasValue) query = query.Where(q => q.Status == status);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(q => q.Text.ToLower().Contains(search.ToLower()) || q.Topic.ToLower().Contains(search.ToLower()));
        var count = await query.CountAsync(ct);
        var items = await query.OrderBy(q => q.Topic).ThenBy(q => q.Text).Skip((page - 1) * size).Take(size).ToArrayAsync(ct);
        var names = await db.Disciplines.Where(d => items.Select(i => i.DisciplineId).Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        return new(items.Select(q => Map(q, names[q.DisciplineId])).ToArray(), count, page, size);
    }
    public async Task<QuestionView> GetAsync(Actor actor, Guid id, CancellationToken ct)
    {
        var q = await Query(actor).AsNoTracking().SingleOrDefaultAsync(q => q.Id == id, ct) ?? throw AppException.NotFound();
        return Map(q, await db.Disciplines.Where(d => d.Id == q.DisciplineId).Select(d => d.Name).SingleAsync(ct));
    }
    public async Task<Guid> CreateAsync(Actor actor, QuestionInput input, CancellationToken ct)
    {
        await access.RequireTeacherAsync(actor, input.DisciplineId, ct);
        await ValidateAsync(input, ct);
        var q = new Question { DisciplineId = input.DisciplineId, AuthorId = actor.Id };
        Apply(q, input, true);
        db.Questions.Add(q);
        access.Audit(actor, "Question.Create", "Question", q.Id);
        await db.SaveChangesAsync(ct);
        return q.Id;
    }
    public Task<Guid> UpdateAsync(Actor actor, Guid id, QuestionInput input, CancellationToken ct) => gate.RunAsync(id, async () =>
    {
        var q = await Query(actor).SingleOrDefaultAsync(q => q.Id == id, ct) ?? throw AppException.NotFound();
        if (q.DisciplineId != input.DisciplineId) throw AppException.Invalid("Дисципліну наявного питання змінювати не можна.");
        if (q.Revision != input.Revision) throw AppException.Conflict("Питання змінилося. Оновіть сторінку перед збереженням.");
        await ValidateAsync(input, ct);
        SaveRevision(q, actor);
        db.Keywords.RemoveRange(q.Keywords);
        q.Keywords.Clear();
        Apply(q, input, false);
        access.Audit(actor, "Question.Update", "Question", id);
        await db.SaveChangesAsync(ct);
        return id;
    }, ct);
    public Task<Guid> ArchiveAsync(Actor actor, Guid id, CancellationToken ct) => MutateAsync(actor, id, "Question.Archive", q => q.Status = QuestionStatus.Archived, ct);

    public Task<Guid> AddReferenceAsync(Actor actor, Guid id, string text, CancellationToken ct) => MutateAsync(actor, id, "Reference.Create", q =>
        q.References.Add(new ReferenceAnswer { QuestionId = id, Text = RequireText(text), IsPrimary = false }), ct);
    public Task<Guid> UpdateReferenceAsync(Actor actor, Guid id, Guid refId, string? text, bool? active, CancellationToken ct) => MutateAsync(actor, id, "Reference.Update", q =>
    {
        var r = q.References.SingleOrDefault(r => r.Id == refId) ?? throw AppException.NotFound();
        if (text is not null) r.Text = RequireText(text);
        if (active.HasValue)
        {
            if (!active.Value && r.IsPrimary) throw AppException.Invalid("Основний еталон не можна деактивувати.");
            r.Active = active.Value;
        }
    }, ct);
    public Task<Guid> AddVariantAsync(Actor actor, Guid id, string text, CancellationToken ct) => MutateAsync(actor, id, "Variant.Create", q =>
    {
        text = RequireText(text);
        if (q.Variants.Any(v => v.Text == text)) throw AppException.Conflict("Таке формулювання вже існує.");
        q.Variants.Add(new AnswerVariant { QuestionId = id, Text = text, AuthorId = actor.Id });
    }, ct);
    public Task<Guid> UpdateVariantAsync(Actor actor, Guid id, Guid variantId, string text, CancellationToken ct) => MutateAsync(actor, id, "Variant.Update", q =>
    {
        var v = q.Variants.SingleOrDefault(v => v.Id == variantId) ?? throw AppException.NotFound();
        v.Text = RequireText(text); v.Status = VariantStatus.Pending; v.ApprovedAt = null; v.ApprovedBy = null;
    }, ct);
    public Task<Guid> DecideVariantAsync(Actor actor, Guid id, Guid variantId, VariantStatus status, CancellationToken ct) => MutateAsync(actor, id, "Variant." + status, q =>
    {
        if (!Enum.IsDefined(status) || status == VariantStatus.Pending) throw AppException.Invalid("Оберіть затвердження, відхилення або деактивацію.");
        var v = q.Variants.SingleOrDefault(v => v.Id == variantId) ?? throw AppException.NotFound();
        v.Status = status; v.ApprovedBy = actor.Id; v.ApprovedAt = DateTimeOffset.UtcNow;
    }, ct);
    public Task<Guid> SetKeywordsAsync(Actor actor, Guid id, KeywordInput[] inputs, CancellationToken ct) => MutateAsync(actor, id, "Keyword.Update", q =>
    {
        ValidateKeywords(inputs);
        db.Keywords.RemoveRange(q.Keywords); q.Keywords.Clear();
        q.Keywords = inputs.Select(k => new Keyword { QuestionId = id, Term = k.Term.Trim(), Synonyms = k.Synonyms.Select(s => s.Trim()).ToArray(), Weight = k.Weight }).ToList();
    }, ct);
    public async Task<object[]> HistoryAsync(Actor actor, Guid id, CancellationToken ct)
    {
        _ = await GetAsync(actor, id, ct);
        return await db.QuestionRevisions.AsNoTracking().Where(r => r.QuestionId == id).OrderByDescending(r => r.Revision)
            .Select(r => (object)new { r.Id, r.Revision, r.AuthorId, r.CreatedAt, Snapshot = JsonDefaults.Read<QuestionView>(r.SnapshotJson) }).ToArrayAsync(ct);
    }
    private Task<Guid> MutateAsync(Actor actor, Guid id, string action, Action<Question> mutation, CancellationToken ct) => gate.RunAsync(id, async () =>
    {
        var q = await Query(actor).SingleOrDefaultAsync(q => q.Id == id, ct) ?? throw AppException.NotFound();
        SaveRevision(q, actor);
        mutation(q);
        access.Audit(actor, action, "Question", id);
        await db.SaveChangesAsync(ct);
        return id;
    }, ct);
    private void SaveRevision(Question q, Actor actor)
    {
        db.QuestionRevisions.Add(new QuestionRevision { QuestionId = q.Id, Revision = q.Revision, AuthorId = actor.Id, SnapshotJson = JsonDefaults.Write(Map(q, "")) });
        q.Revision++;
    }
    private async Task ValidateAsync(QuestionInput i, CancellationToken ct)
    {
        if (!Enum.IsDefined(i.Status)) throw AppException.Invalid("Невідомий статус питання.");
        if (!await db.Disciplines.AnyAsync(d => d.Id == i.DisciplineId && !d.Archived, ct)) throw AppException.Invalid("Дисципліна недоступна.");
        ValidateKeywords(i.Keywords);
        if (i.Variants is null || i.Variants.Length > 30 || i.Variants.Any(v => v.Length is < 3 or > 5000)) throw AppException.Invalid("Перевірте тексти варіацій (3–5000 символів, до 30 записів).");
    }
    public static void ValidateKeywords(KeywordInput[] inputs)
    {
        if (inputs is null || inputs.Length is < 1 or > 30 || inputs.Any(k => string.IsNullOrWhiteSpace(k.Term) || k.Term.Length is < 2 or > 160 || !double.IsFinite(k.Weight) || k.Weight is <= 0 or > 100 || k.Synonyms is null || k.Synonyms.Length > 30 || k.Synonyms.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 160)) || inputs.Select(k => k.Term.Trim().ToLowerInvariant()).Distinct().Count() != inputs.Length)
            throw AppException.Invalid("Задайте 1–30 різних понять з додатними вагами та непорожніми синонімами.");
    }
    private static string RequireText(string text) => string.IsNullOrWhiteSpace(text) || text.Trim().Length is < 3 or > 5000
        ? throw AppException.Invalid("Текст має містити 3–5000 символів.") : text.Trim();
    private static void Apply(Question q, QuestionInput i, bool create)
    {
        q.Text = i.Text.Trim(); q.Topic = i.Topic.Trim(); q.Status = i.Status;
        if (create) q.References.Add(new ReferenceAnswer { QuestionId = q.Id, Text = i.Reference.Trim(), IsPrimary = true });
        else q.References.Single(r => r.IsPrimary).Text = i.Reference.Trim();
        q.Keywords = i.Keywords.Select(k => new Keyword { QuestionId = q.Id, Term = k.Term.Trim(), Synonyms = k.Synonyms.Select(s => s.Trim()).ToArray(), Weight = k.Weight }).ToList();
        if (create) q.Variants = i.Variants.Select(v => new AnswerVariant { QuestionId = q.Id, Text = v.Trim(), AuthorId = q.AuthorId }).ToList();
    }
    public static QuestionView Map(Question q, string discipline) => new(q.Id, q.DisciplineId, discipline, q.Topic, q.Text, q.Status, q.Revision,
        q.References.OrderByDescending(r => r.IsPrimary).Select(r => new ReferenceView(r.Id, r.Text, r.IsPrimary, r.Active)).ToArray(),
        q.Variants.OrderBy(v => v.CreatedAt).Select(v => new VariantView(v.Id, v.QuestionId, v.Text, v.Status, v.Source, v.AuthorId, v.CreatedAt, v.ApprovedAt)).ToArray(),
        q.Keywords.Select(k => new KeywordSpec(k.Term, k.Synonyms, k.Weight)).ToArray(), q.UpdatedAt);
}
