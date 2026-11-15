using Microsoft.EntityFrameworkCore;
using PLAVS.Domain;

namespace PLAVS.Application;

public sealed class AccessService(IPlavsDb db)
{
    public IQueryable<Guid> DisciplineIds(Actor actor)
    {
        if (actor.IsAdmin) return db.Disciplines.Select(d => d.Id);
        if (actor.IsTeacher) return db.DisciplineTeachers.Where(t => t.UserId == actor.Id).Select(t => t.DisciplineId);
        return from g in db.DisciplineGroups join m in db.GroupMembers on g.GroupId equals m.GroupId
               where m.UserId == actor.Id select g.DisciplineId;
    }
    public async Task RequireTeacherAsync(Actor actor, Guid disciplineId, CancellationToken ct = default)
    {
        if (!actor.IsTeacher || !await db.DisciplineTeachers.AnyAsync(t => t.UserId == actor.Id && t.DisciplineId == disciplineId, ct))
            throw AppException.NotFound();
    }
    public IQueryable<Assessment> VisibleTests(Actor actor)
    {
        if (actor.IsTeacher || actor.IsAdmin)
            return db.Tests.Where(t => DisciplineIds(actor).Contains(t.DisciplineId));
        return db.Tests.Where(t => t.Published && !t.Archived && db.Disciplines.Any(d => d.Id == t.DisciplineId && !d.Archived) &&
            db.TestGroups.Any(tg => tg.TestId == t.Id && db.Groups.Any(g => g.Id == tg.GroupId && !g.Archived) &&
                db.DisciplineGroups.Any(dg => dg.DisciplineId == t.DisciplineId && dg.GroupId == tg.GroupId) &&
                db.GroupMembers.Any(m => m.GroupId == tg.GroupId && m.UserId == actor.Id)));
    }
    public IQueryable<StudentAnswer> VisibleAnswers(Actor actor)
    {
        if (actor.IsStudent && !actor.IsTeacher)
            return db.Answers.Where(a => a.Submitted && db.Attempts.Any(at => at.Id == a.AttemptId && at.StudentId == actor.Id));
        if (!actor.IsTeacher) return db.Answers.Where(_ => false);
        return db.Answers.Where(a => a.Submitted && db.Attempts.Any(at => at.Id == a.AttemptId &&
            db.Tests.Any(t => t.Id == at.TestId &&
                db.DisciplineTeachers.Any(dt => dt.DisciplineId == t.DisciplineId && dt.UserId == actor.Id) &&
                db.DisciplineGroups.Any(dg => dg.DisciplineId == t.DisciplineId &&
                    db.GroupMembers.Any(gm => gm.GroupId == dg.GroupId && gm.UserId == at.StudentId)))));
    }
    public void Audit(Actor actor, string action, string type, object id)
        => db.Audit.Add(new AuditEvent { UserId = actor.Id, Action = action, EntityType = type, EntityId = id.ToString()! });
}
