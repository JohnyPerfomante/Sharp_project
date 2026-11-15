using Microsoft.EntityFrameworkCore;
using PLAVS.Domain;

namespace PLAVS.Application;

public sealed class ReportingService(IPlavsDb db, AccessService access)
{
    private IQueryable<StudentAnswer> Query(Actor actor) => actor.IsAdmin ? db.Answers.Where(a => a.Submitted) :
        actor.IsTeacher ? access.VisibleAnswers(actor) : access.VisibleAnswers(actor).Where(a => db.Attempts.Any(at => at.Id == a.AttemptId && db.Tests.Any(t => t.Id == at.TestId && t.ResultsVisible)));

    public async Task<ReportRow[]> ReportsAsync(Actor actor, Guid? discipline, Guid? group, Guid? test, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var answers = Query(actor);
        if (from.HasValue) answers = answers.Where(a => a.CreatedAt >= from);
        if (to.HasValue) answers = answers.Where(a => a.CreatedAt <= to);
        var data = await (from a in answers
                          join at in db.Attempts on a.AttemptId equals at.Id
                          join t in db.Tests on at.TestId equals t.Id
                          join d in db.Disciplines on t.DisciplineId equals d.Id
                          where (!discipline.HasValue || d.Id == discipline) && (!test.HasValue || t.Id == test)
                          select new { a.Id, a.Status, at.StudentId, TestId = t.Id, Test = t.Title, DisciplineId = d.Id, Discipline = d.Name,
                              Score = a.Status == AnswerStatus.Reviewed ? db.Reviews.Where(r => r.AnswerId == a.Id).OrderByDescending(r => r.CreatedAt).Select(r => r.Score).FirstOrDefault() :
                                  db.Results.Where(r => r.AnswerId == a.Id).Select(r => r.Score).FirstOrDefault() }).ToArrayAsync(ct);
        var students = data.Select(a => a.StudentId).Distinct().ToArray();
        var memberships = await (from m in db.GroupMembers join g in db.Groups on m.GroupId equals g.Id
                                 where students.Contains(m.UserId) select new { m.UserId, GroupId = g.Id, g.Name }).ToArrayAsync(ct);
        var allowed = await db.DisciplineGroups.ToArrayAsync(ct);
        var latest = await db.Settings.OrderByDescending(s => s.Version).FirstAsync(ct);
        var rows = data.SelectMany(a => memberships.Where(m => m.UserId == a.StudentId && (!group.HasValue || m.GroupId == group) &&
                allowed.Any(dg => dg.DisciplineId == a.DisciplineId && dg.GroupId == m.GroupId)).Select(m => new { Answer = a, Group = m.Name }));
        return rows.GroupBy(r => new { r.Answer.Discipline, r.Group, r.Answer.Test, r.Answer.TestId }).Select(g => new ReportRow(g.Key.Discipline, g.Key.Group, g.Key.Test,
            g.Count(), g.Count(r => r.Answer.Status is AnswerStatus.NeedsReview or AnswerStatus.PendingAnalysis or AnswerStatus.AnalysisError),
            g.Select(r => r.Answer.Score).Average(), g.Count(r => r.Answer.Score >= latest.HighThreshold),
            g.Count(r => r.Answer.Score >= latest.PartialThreshold && r.Answer.Score < latest.HighThreshold), g.Count(r => r.Answer.Score < latest.PartialThreshold))).ToArray();
    }
    public async Task<object> DashboardAsync(Actor actor, CancellationToken ct)
    {
        var scope = access.DisciplineIds(actor);
        var query = Query(actor);
        var counts = await query.GroupBy(_ => 1).Select(g => new
        {
            Answers = g.Count(), Pending = g.Count(a => a.Status == AnswerStatus.NeedsReview || a.Status == AnswerStatus.PendingAnalysis || a.Status == AnswerStatus.AnalysisError),
            Reviewed = g.Count(a => a.Status == AnswerStatus.Reviewed)
        }).SingleOrDefaultAsync(ct);
        var since = DateTimeOffset.UtcNow.AddDays(-13).Date;
        var times = await query.Where(a => a.CreatedAt >= new DateTimeOffset(since, TimeSpan.Zero)).Select(a => a.CreatedAt).ToArrayAsync(ct);
        var activity = Enumerable.Range(0, 14).Select(i => new { Date = since.AddDays(i).ToString("yyyy-MM-dd"), Count = times.Count(t => t.UtcDateTime.Date == since.AddDays(i)) }).ToArray();
        var report = await ReportsAsync(actor, null, null, null, null, null, ct);
        var events = actor.IsAdmin ? await db.Audit.AsNoTracking().OrderByDescending(a => a.CreatedAt).Take(8).Select(a => new AuditView(a.Id, a.UserId, a.Action, a.EntityType, a.EntityId, a.CreatedAt)).ToArrayAsync(ct) : [];
        var weakQuestions = actor.IsStudent && !actor.IsTeacher ? [] : await query.Where(a => a.Analysis != null && a.Analysis.Score < 55)
            .GroupBy(a => a.QuestionId).Select(g => new { QuestionId = g.Key, Count = g.Count() }).OrderByDescending(g => g.Count).Take(5)
            .Join(db.Questions, a => a.QuestionId, q => q.Id, (a, q) => new { Question = q.Text, a.Count }).ToArrayAsync(ct);
        var reasons = actor.IsStudent && !actor.IsTeacher ? [] : await query.Where(a => a.Analysis != null)
            .OrderByDescending(a => a.CreatedAt).Select(a => a.Analysis!.DetailsJson).Take(1000).ToArrayAsync(ct);
        return new { Disciplines = await db.Disciplines.CountAsync(d => scope.Contains(d.Id) && !d.Archived, ct),
            Questions = actor.IsStudent && !actor.IsTeacher ? 0 : await db.Questions.CountAsync(q => scope.Contains(q.DisciplineId) && q.Status != QuestionStatus.Archived, ct),
            Tests = await access.VisibleTests(actor).CountAsync(ct), Answers = counts?.Answers ?? 0, Pending = counts?.Pending ?? 0, Reviewed = counts?.Reviewed ?? 0,
            PendingVariants = actor.IsStudent && !actor.IsTeacher ? 0 : await db.AnswerVariants.CountAsync(v => v.Status == VariantStatus.Pending && db.Questions.Any(q => q.Id == v.QuestionId && scope.Contains(q.DisciplineId)), ct),
            Activity = activity, Distribution = new { High = report.Sum(r => r.High), Partial = report.Sum(r => r.Partial), Low = report.Sum(r => r.Low) },
            Reports = report, Events = events, WeakQuestions = weakQuestions,
            FrequentReasons = reasons.SelectMany(r => JsonDefaults.Read<AnalysisDetails>(r).Concepts.Where(c => !c.Found || c.Negated).Select(c => c.Term)).GroupBy(t => t).OrderByDescending(g => g.Count()).Take(6).Select(g => new { Term = g.Key, Count = g.Count() }).ToArray() };
    }
}
