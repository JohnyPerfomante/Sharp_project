using Microsoft.EntityFrameworkCore;
using PLAVS.Domain;

namespace PLAVS.Application;

public interface IUserDirectory
{
    Task<Dictionary<string, string>> NamesAsync(string[] ids, CancellationToken ct);
}
public sealed class AnswerService(IPlavsDb db, AccessService access, IWorkflowLock gate, IAnalysisEngine engine, IUserDirectory directory)
{
    public Task<Guid> SaveDraftAsync(Actor actor, AnswerInput input, CancellationToken ct) => gate.RunAsync(input.AttemptId, async () =>
    {
        var attempt = await db.Attempts.SingleOrDefaultAsync(a => a.Id == input.AttemptId && a.StudentId == actor.Id, ct) ?? throw AppException.NotFound();
        if (attempt.Status != AttemptStatus.InProgress || attempt.Deadline <= DateTimeOffset.UtcNow)
            throw AppException.Conflict("Редагування завершено: спробу надіслано або час вичерпано.");
        var snapshot = JsonDefaults.Read<AttemptSnapshot>(attempt.SnapshotJson);
        if (!snapshot.Questions.Any(q => q.Id == input.QuestionId)) throw AppException.Invalid("Питання не належить цій спробі.");
        var answer = await db.Answers.SingleOrDefaultAsync(a => a.AttemptId == attempt.Id && a.QuestionId == input.QuestionId, ct);
        if (answer is null)
        {
            answer = new StudentAnswer { AttemptId = attempt.Id, QuestionId = input.QuestionId };
            db.Answers.Add(answer);
        }
        answer.Text = input.Text.Trim(); await db.SaveChangesAsync(ct); return answer.Id;
    }, ct);
    public Task<Guid[]> SubmitAsync(Actor actor, Guid id, CancellationToken ct) => gate.RunAsync(id, async () =>
    {
        var attempt = await db.Attempts.Include(a => a.Answers).SingleOrDefaultAsync(a => a.Id == id && a.StudentId == actor.Id, ct) ?? throw AppException.NotFound();
        if (attempt.Status == AttemptStatus.Submitted) return attempt.Answers.Select(a => a.Id).ToArray();
        var snapshot = JsonDefaults.Read<AttemptSnapshot>(attempt.SnapshotJson);
        // After the deadline the server accepts only previously saved drafts and fills unanswered items with an empty answer.
        foreach (var q in snapshot.Questions)
        {
            var answer = attempt.Answers.SingleOrDefault(a => a.QuestionId == q.Id);
            if (answer is null)
            {
                answer = new StudentAnswer { AttemptId = id, QuestionId = q.Id };
                attempt.Answers.Add(answer);
            }
            answer.Submitted = true; answer.Status = AnswerStatus.PendingAnalysis;
        }
        attempt.Status = AttemptStatus.Submitted; attempt.SubmittedAt = DateTimeOffset.UtcNow;
        access.Audit(actor, "Attempt.Submit", "Attempt", id);
        await db.SaveChangesAsync(ct); return attempt.Answers.Select(a => a.Id).ToArray();
    }, ct);
    public async Task<Page<AnswerListItem>> ListAsync(Actor actor, AnswerStatus? status, Guid? testId, int page, int size, CancellationToken ct)
    {
        var query = access.VisibleAnswers(actor).AsNoTracking();
        if (status.HasValue) query = query.Where(a => a.Status == status);
        if (testId.HasValue) query = query.Where(a => db.Attempts.Any(at => at.Id == a.AttemptId && at.TestId == testId));
        var total = await query.CountAsync(ct);
        var rows = await (from a in query join at in db.Attempts on a.AttemptId equals at.Id join t in db.Tests on at.TestId equals t.Id
                          orderby a.CreatedAt descending
                          select new { Answer = a, Attempt = at, Test = t }).Skip((page - 1) * size).Take(size).ToArrayAsync(ct);
        var ids = rows.Select(r => r.Answer.Id).ToArray();
        var results = await db.Results.Where(r => ids.Contains(r.AnswerId)).ToDictionaryAsync(r => r.AnswerId, ct);
        var reviews = await db.Reviews.Where(r => ids.Contains(r.AnswerId)).OrderBy(r => r.CreatedAt).ToArrayAsync(ct);
        var names = await directory.NamesAsync(rows.Select(r => r.Attempt.StudentId).Distinct().ToArray(), ct);
        return new(rows.Select(r =>
        {
            var visible = actor.IsTeacher || r.Test.ResultsVisible;
            var snapshot = JsonDefaults.Read<AttemptSnapshot>(r.Attempt.SnapshotJson);
            var result = results.GetValueOrDefault(r.Answer.Id);
            var review = reviews.LastOrDefault(rv => rv.AnswerId == r.Answer.Id);
            return new AnswerListItem(r.Answer.Id, r.Attempt.StudentId, names.GetValueOrDefault(r.Attempt.StudentId, "Студент"), snapshot.Questions.Single(q => q.Id == r.Answer.QuestionId).Text,
                snapshot.Title, r.Answer.Status, visible ? review?.Score ?? result?.Score : null, visible ? result?.Category : null, r.Answer.CreatedAt);
        }).ToArray(), total, page, size);
    }
    public async Task<object> GetAsync(Actor actor, Guid id, CancellationToken ct)
    {
        var a = await access.VisibleAnswers(actor).AsNoTracking().Include(a => a.Analysis).Include(a => a.Reviews).SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw AppException.NotFound();
        var attempt = await db.Attempts.AsNoTracking().SingleAsync(at => at.Id == a.AttemptId, ct);
        var test = await db.Tests.AsNoTracking().SingleAsync(t => t.Id == attempt.TestId, ct);
        var snapshot = JsonDefaults.Read<AttemptSnapshot>(attempt.SnapshotJson);
        var q = snapshot.Questions.Single(q => q.Id == a.QuestionId);
        var reviews = a.Reviews.OrderBy(r => r.CreatedAt).Select(r => new ReviewView(r.Id, r.ReviewerId, r.Decision, r.Score, r.Comment, r.CreatedAt)).ToArray();
        if (!actor.IsTeacher)
        {
            // Explicit projection: no reference text, keyword lists, snapshots or internal settings in student responses.
            var last = reviews.LastOrDefault();
            var score = last?.Score ?? a.Analysis?.Score;
            return new { AnswerId = a.Id, Question = q.Text, Answer = a.Text, Test = snapshot.Title, a.Status, test.ResultsVisible,
                Score = test.ResultsVisible ? score : null, Points = test.ResultsVisible && score.HasValue ? score * q.MaxPoints / 100 : null,
                q.MaxPoints, Category = test.ResultsVisible ? a.Analysis?.Category : null,
                Comment = test.ResultsVisible ? last?.Comment : null, Decision = test.ResultsVisible ? last?.Decision : null,
                a.CreatedAt };
        }
        return new AnalysisView(a.Id, q.Text, a.Text, attempt.StudentId, test.Id, snapshot.Title, a.Status, a.CreatedAt,
            a.Analysis is null ? null : Map(a.Analysis), reviews, test.ResultsVisible);
    }
    public async Task RetryAsync(Actor actor, Guid id, CancellationToken ct)
    {
        if (!actor.IsTeacher || !await access.VisibleAnswers(actor).AnyAsync(a => a.Id == id, ct)) throw AppException.NotFound();
        await gate.RunAsync(id, async () =>
        {
            var a = await db.Answers.SingleAsync(a => a.Id == id, ct);
            if (a.Status == AnswerStatus.Reviewed) throw AppException.Conflict("Перевірене викладачем рішення зберігається. Повторний аналіз не замінює його.");
            a.Status = AnswerStatus.PendingAnalysis;
            access.Audit(actor, "Analysis.Retry", "Answer", id); await db.SaveChangesAsync(ct); return id;
        }, ct);
    }
    public Task<Guid> ReviewAsync(Actor actor, Guid id, ReviewInput input, CancellationToken ct) => gate.RunAsync(id, async () =>
    {
        if (!actor.IsTeacher) throw AppException.NotFound();
        var a = await access.VisibleAnswers(actor).SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw AppException.NotFound();
        if (!Enum.IsDefined(input.Decision) || (input.Decision == ReviewDecision.Defer && input.Score.HasValue) ||
            (input.Decision != ReviewDecision.Defer && (!input.Score.HasValue || !double.IsFinite(input.Score.Value))) ||
            (input.Decision == ReviewDecision.Accept && input.Score != 100) || (input.Decision == ReviewDecision.Reject && input.Score != 0) ||
            (input.Decision == ReviewDecision.Partial && input.Score is not (> 0 and < 100)))
            throw AppException.Invalid("Зарахування — 100%, відхилення — 0%, часткове — від 0 до 100, додаткова перевірка — без бала.");
        db.Reviews.Add(new ManualReview { AnswerId = id, ReviewerId = actor.Id, Decision = input.Decision, Score = input.Score, Comment = input.Comment.Trim() });
        a.Status = input.Decision == ReviewDecision.Defer ? AnswerStatus.NeedsReview : AnswerStatus.Reviewed;
        if (input.CreateVariant)
        {
            if (input.Decision != ReviewDecision.Accept || string.IsNullOrWhiteSpace(a.Text)) throw AppException.Invalid("Кандидат варіації створюється лише з повністю зарахованої непорожньої відповіді.");
            if (!await db.AnswerVariants.AnyAsync(v => v.QuestionId == a.QuestionId && v.Text == a.Text, ct))
                db.AnswerVariants.Add(new AnswerVariant { QuestionId = a.QuestionId, Text = a.Text, AuthorId = actor.Id, Source = "StudentAnswer", SourceAnswerId = id });
        }
        access.Audit(actor, "Answer.Review." + input.Decision, "Answer", id);
        await db.SaveChangesAsync(ct); return id;
    }, ct);
    public Task<Guid> AnalyzeStoredAsync(Guid id, CancellationToken ct) => gate.RunAsync(id, async () =>
    {
        var a = await db.Answers.Include(a => a.Analysis).SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw AppException.NotFound();
        if (!a.Submitted || a.Status != AnswerStatus.PendingAnalysis) return id;
        var attempt = await db.Attempts.SingleAsync(at => at.Id == a.AttemptId, ct);
        var snapshot = JsonDefaults.Read<AttemptSnapshot>(attempt.SnapshotJson);
        var outcome = await engine.AnalyzeAsync(a.Text, snapshot.Questions.Single(q => q.Id == a.QuestionId), snapshot.Settings, ct);
        var r = a.Analysis;
        if (r is null) { r = new AnalysisResult { AnswerId = id }; db.Results.Add(r); }
        r.Lexical = outcome.Lexical; r.Semantic = outcome.Semantic; r.Keywords = outcome.Keywords; r.Score = outcome.Score;
        r.Category = outcome.Category; r.NeedsReview = outcome.NeedsReview; r.MatchedReferenceId = outcome.ReferenceId;
        r.ModelVersion = outcome.ModelVersion; r.AlgorithmVersion = outcome.AlgorithmVersion; r.SettingsId = snapshot.Settings.Id;
        r.DetailsJson = JsonDefaults.Write(outcome.Details);
        db.AnalysisRuns.Add(new AnalysisRun { AnswerId = id, OutcomeJson = JsonDefaults.Write(outcome) });
        a.Status = outcome.NeedsReview ? AnswerStatus.NeedsReview : AnswerStatus.Analyzed;
        await db.SaveChangesAsync(ct); return id;
    }, ct);
    public static AnalysisOutcome Map(AnalysisResult r) => new(r.Lexical, r.Semantic, r.Keywords, r.Score, r.Category, r.NeedsReview,
        r.MatchedReferenceId, r.ModelVersion, r.AlgorithmVersion, JsonDefaults.Read<AnalysisDetails>(r.DetailsJson));
}
