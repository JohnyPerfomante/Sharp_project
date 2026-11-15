namespace PLAVS.Domain;

public static class Roles
{
    public const string Administrator = "Administrator", Teacher = "Teacher", Student = "Student";
    public static readonly string[] All = [Administrator, Teacher, Student];
}
public enum QuestionStatus { Draft, Active, Archived }
public enum VariantStatus { Pending, Approved, Rejected, Inactive }
public enum AttemptStatus { InProgress, Submitted }
public enum AnswerStatus { PendingAnalysis, NeedsReview, Analyzed, Reviewed, AnalysisError }
public enum MatchCategory { High, Partial, Low, Unavailable }
public enum ReviewDecision { Accept, Partial, Reject, Defer }

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class Discipline : Entity
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Archived { get; set; }
}
public sealed class AcademicGroup : Entity
{
    public string Name { get; set; } = "";
    public bool Archived { get; set; }
}
public sealed class GroupMember : Entity
{
    public Guid GroupId { get; set; }
    public string UserId { get; set; } = "";
}
public sealed class DisciplineTeacher : Entity
{
    public Guid DisciplineId { get; set; }
    public string UserId { get; set; } = "";
}
public sealed class DisciplineGroup : Entity
{
    public Guid DisciplineId { get; set; }
    public Guid GroupId { get; set; }
}
public sealed class Question : Entity
{
    public Guid DisciplineId { get; set; }
    public string Topic { get; set; } = "";
    public string Text { get; set; } = "";
    public QuestionStatus Status { get; set; } = QuestionStatus.Active;
    public int Revision { get; set; } = 1;
    public string AuthorId { get; set; } = "";
    public List<ReferenceAnswer> References { get; set; } = [];
    public List<AnswerVariant> Variants { get; set; } = [];
    public List<Keyword> Keywords { get; set; } = [];
}
public sealed class ReferenceAnswer : Entity
{
    public Guid QuestionId { get; set; }
    public string Text { get; set; } = "";
    public bool IsPrimary { get; set; }
    public bool Active { get; set; } = true;
}
public sealed class AnswerVariant : Entity
{
    public Guid QuestionId { get; set; }
    public string Text { get; set; } = "";
    public VariantStatus Status { get; set; } = VariantStatus.Pending;
    public string AuthorId { get; set; } = "";
    public string Source { get; set; } = "Teacher";
    public Guid? SourceAnswerId { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
}
public sealed class Keyword : Entity
{
    public Guid QuestionId { get; set; }
    public string Term { get; set; } = "";
    public string[] Synonyms { get; set; } = [];
    public double Weight { get; set; } = 1;
}
public sealed class QuestionRevision : Entity
{
    public Guid QuestionId { get; set; }
    public int Revision { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public string AuthorId { get; set; } = "";
}
public sealed class Assessment : Entity
{
    public Guid DisciplineId { get; set; }
    public string Title { get; set; } = "";
    public string Instructions { get; set; } = "";
    public bool Published { get; set; }
    public bool Archived { get; set; }
    public bool ResultsVisible { get; set; }
    public DateTimeOffset? OpensAt { get; set; }
    public DateTimeOffset? ClosesAt { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public string AuthorId { get; set; } = "";
    public List<TestQuestion> Questions { get; set; } = [];
    public List<TestGroup> Groups { get; set; } = [];
}
public sealed class TestQuestion : Entity
{
    public Guid TestId { get; set; }
    public Guid QuestionId { get; set; }
    public int Position { get; set; }
    public double MaxPoints { get; set; } = 10;
}
public sealed class TestGroup : Entity
{
    public Guid TestId { get; set; }
    public Guid GroupId { get; set; }
}
public sealed class TestAttempt : Entity
{
    public Guid TestId { get; set; }
    public string StudentId { get; set; } = "";
    public AttemptStatus Status { get; set; }
    public DateTimeOffset Deadline { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public List<StudentAnswer> Answers { get; set; } = [];
}
public sealed class StudentAnswer : Entity
{
    public Guid AttemptId { get; set; }
    public Guid QuestionId { get; set; }
    public string Text { get; set; } = "";
    public AnswerStatus Status { get; set; } = AnswerStatus.PendingAnalysis;
    public bool Submitted { get; set; }
    public AnalysisResult? Analysis { get; set; }
    public List<ManualReview> Reviews { get; set; } = [];
}
public sealed class AnalysisResult : Entity
{
    public Guid AnswerId { get; set; }
    public double Lexical { get; set; }
    public double? Semantic { get; set; }
    public double Keywords { get; set; }
    public double? Score { get; set; }
    public MatchCategory Category { get; set; }
    public bool NeedsReview { get; set; }
    public Guid? MatchedReferenceId { get; set; }
    public string ModelVersion { get; set; } = "";
    public string AlgorithmVersion { get; set; } = "";
    public Guid SettingsId { get; set; }
    public string DetailsJson { get; set; } = "{}";
}
public sealed class ManualReview : Entity
{
    public Guid AnswerId { get; set; }
    public string ReviewerId { get; set; } = "";
    public ReviewDecision Decision { get; set; }
    public double? Score { get; set; }
    public string Comment { get; set; } = "";
}
public sealed class AnalysisRun : Entity
{
    public Guid AnswerId { get; set; }
    public string OutcomeJson { get; set; } = "{}";
}
public sealed class AnalysisSettings : Entity
{
    public int Version { get; set; } = 1;
    public double Alpha { get; set; } = .25;
    public double Beta { get; set; } = .55;
    public double Gamma { get; set; } = .20;
    public double HighThreshold { get; set; } = 80;
    public double PartialThreshold { get; set; } = 55;
    public string AuthorId { get; set; } = "";
}
public sealed class AuditEvent : Entity
{
    public string UserId { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string MetadataJson { get; set; } = "{}";
}
