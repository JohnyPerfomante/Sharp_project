using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PLAVS.Domain;

namespace PLAVS.Application;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
}
public sealed class AppException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
    public static AppException NotFound() => new(404, "Запис не знайдено або недоступний.");
    public static AppException Invalid(string message) => new(400, message);
    public static AppException Conflict(string message) => new(409, message);
}
public sealed record Actor(string Id, IReadOnlySet<string> Roles)
{
    public bool IsAdmin => Roles.Contains(Domain.Roles.Administrator);
    public bool IsTeacher => Roles.Contains(Domain.Roles.Teacher);
    public bool IsStudent => Roles.Contains(Domain.Roles.Student);
}
public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int PageNumber, int PageSize);
public sealed record ReferenceSpec(Guid Id, string Text, bool Variant = false);
public sealed record KeywordSpec(string Term, string[] Synonyms, double Weight);
public sealed record SettingsSpec(Guid Id, int Version, double Alpha, double Beta, double Gamma, double HighThreshold, double PartialThreshold)
{
    public void Validate()
    {
        double[] values = [Alpha, Beta, Gamma, HighThreshold, PartialThreshold];
        if (values.Any(v => !double.IsFinite(v)) || Alpha < 0 || Beta <= 0 || Gamma < 0 ||
            Math.Abs(Alpha + Beta + Gamma - 1) > .000001 || PartialThreshold <= 0 ||
            HighThreshold <= PartialThreshold || HighThreshold > 100)
            throw AppException.Invalid("Ваги мають бути невід’ємними, β > 0, сума ваг — 1; 0 < частковий поріг < високий поріг ≤ 100.");
    }
    public static SettingsSpec From(AnalysisSettings s) => new(s.Id, s.Version, s.Alpha, s.Beta, s.Gamma, s.HighThreshold, s.PartialThreshold);
}
public sealed record QuestionSnapshot(Guid Id, string Text, int Revision, double MaxPoints, ReferenceSpec[] References, KeywordSpec[] Keywords);
public sealed record AttemptSnapshot(string Title, SettingsSpec Settings, QuestionSnapshot[] Questions);
public sealed record KeywordMatch(string Term, double Weight, bool Found, string? MatchedText, bool Negated);
public sealed record AnalysisDetails(KeywordMatch[] Concepts, string[] Reasons, string[] Warnings, string? SemanticError,
    string? MatchedText, SettingsSpec Settings, int QuestionRevision, bool Truncated);
public sealed record AnalysisOutcome(double Lexical, double? Semantic, double Keywords, double? Score, MatchCategory Category,
    bool NeedsReview, Guid? ReferenceId, string ModelVersion, string AlgorithmVersion, AnalysisDetails Details);
public interface IAnalysisEngine
{
    Task<AnalysisOutcome> AnalyzeAsync(string text, QuestionSnapshot question, SettingsSpec settings, CancellationToken ct = default);
}
public sealed record SemanticStatus(bool Available, string ModelVersion, string Message);
public sealed record Embedding(float[] Vector, bool Truncated);
public interface ISemanticEncoder
{
    SemanticStatus Status { get; }
    Task<Embedding> EncodeAsync(string text, bool cache, CancellationToken ct = default);
}
public interface IWorkflowLock
{
    Task<T> RunAsync<T>(Guid key, Func<Task<T>> action, CancellationToken ct = default);
}
public interface IPlavsDb
{
    DbSet<Discipline> Disciplines { get; }
    DbSet<AcademicGroup> Groups { get; }
    DbSet<GroupMember> GroupMembers { get; }
    DbSet<DisciplineTeacher> DisciplineTeachers { get; }
    DbSet<DisciplineGroup> DisciplineGroups { get; }
    DbSet<Question> Questions { get; }
    DbSet<ReferenceAnswer> ReferenceAnswers { get; }
    DbSet<AnswerVariant> AnswerVariants { get; }
    DbSet<Keyword> Keywords { get; }
    DbSet<QuestionRevision> QuestionRevisions { get; }
    DbSet<Assessment> Tests { get; }
    DbSet<TestQuestion> TestQuestions { get; }
    DbSet<TestGroup> TestGroups { get; }
    DbSet<TestAttempt> Attempts { get; }
    DbSet<StudentAnswer> Answers { get; }
    DbSet<AnalysisResult> Results { get; }
    DbSet<AnalysisRun> AnalysisRuns { get; }
    DbSet<ManualReview> Reviews { get; }
    DbSet<AnalysisSettings> Settings { get; }
    DbSet<AuditEvent> Audit { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
public sealed record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);
public sealed record KeywordInput([Required, StringLength(160, MinimumLength = 2)] string Term, string[] Synonyms, [Range(.01, 100)] double Weight);
public sealed record QuestionInput(Guid DisciplineId, [Required, StringLength(160)] string Topic,
    [Required, StringLength(2000, MinimumLength = 5)] string Text,
    [Required, StringLength(5000, MinimumLength = 5)] string Reference, KeywordInput[] Keywords,
    string[] Variants, QuestionStatus Status, int? Revision = null);
public sealed record TextInput([Required, StringLength(5000, MinimumLength = 3)] string Text);
public sealed record VariantDecision(VariantStatus Status);
public sealed record TestQuestionInput(Guid QuestionId, [Range(.1, 1000)] double MaxPoints);
public sealed record TestInput(Guid DisciplineId, [Required, StringLength(200, MinimumLength = 3)] string Title,
    [StringLength(4000)] string Instructions, bool Published, bool ResultsVisible,
    DateTimeOffset? OpensAt, DateTimeOffset? ClosesAt, [Range(1, 480)] int DurationMinutes,
    TestQuestionInput[] Questions, Guid[] GroupIds);
public sealed record AnswerInput(Guid AttemptId, Guid QuestionId, [Required, StringLength(5000)] string Text);
public sealed record ReviewInput(ReviewDecision Decision, [Range(0, 100)] double? Score, [Required, StringLength(2000, MinimumLength = 3)] string Comment, bool CreateVariant);
public sealed record DisciplineInput([Required, StringLength(160, MinimumLength = 3)] string Name, [StringLength(2000)] string Description);
public sealed record GroupInput([Required, StringLength(80, MinimumLength = 2)] string Name);
public sealed record MemberInput([Required] string UserId);
public sealed record AssignmentInput(string[] TeacherIds, Guid[] GroupIds);
public sealed record UserInput([Required, EmailAddress] string Email, [Required, StringLength(120, MinimumLength = 2)] string FullName,
    [Required] string Role, string? Password, bool Blocked);
public sealed record UserView(string Id, string Email, string FullName, string[] Roles, bool Blocked);
public sealed record DisciplineView(Guid Id, string Name, string Description, int Questions, int Tests, bool Archived);
public sealed record GroupView(Guid Id, string Name, bool Archived, string[] MemberIds);
public sealed record ReferenceView(Guid Id, string Text, bool IsPrimary, bool Active);
public sealed record VariantView(Guid Id, Guid QuestionId, string Text, VariantStatus Status, string Source, string AuthorId, DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt);
public sealed record QuestionView(Guid Id, Guid DisciplineId, string Discipline, string Topic, string Text, QuestionStatus Status,
    int Revision, ReferenceView[] References, VariantView[] Variants, KeywordSpec[] Keywords, DateTimeOffset UpdatedAt);
public sealed record TestView(Guid Id, Guid DisciplineId, string Discipline, string Title, string Instructions,
    bool Published, bool Archived, bool ResultsVisible, DateTimeOffset? OpensAt, DateTimeOffset? ClosesAt, int DurationMinutes,
    TestQuestionInput[] Questions, Guid[] GroupIds, Guid? AttemptId, AttemptStatus? AttemptStatus);
public sealed record StudentQuestion(Guid Id, string Text, double MaxPoints, Guid? AnswerId, string SavedText);
public sealed record AttemptView(Guid Id, string Title, AttemptStatus Status, DateTimeOffset Deadline, StudentQuestion[] Questions);
public sealed record AnalysisView(Guid AnswerId, string Question, string Answer, string StudentId, Guid TestId, string Test,
    AnswerStatus Status, DateTimeOffset CreatedAt, AnalysisOutcome? Analysis, ReviewView[] Reviews, bool ResultsVisible);
public sealed record ReviewView(Guid Id, string ReviewerId, ReviewDecision Decision, double? Score, string Comment, DateTimeOffset CreatedAt);
public sealed record AnswerListItem(Guid Id, string StudentId, string StudentName, string Question, string Test,
    AnswerStatus Status, double? Score, MatchCategory? Category, DateTimeOffset CreatedAt);
public sealed record AuditView(Guid Id, string UserId, string Action, string EntityType, string EntityId, DateTimeOffset CreatedAt);
public sealed record ReportRow(string Discipline, string Group, string Test, int Answers, int Pending, double? Average, int High, int Partial, int Low);
