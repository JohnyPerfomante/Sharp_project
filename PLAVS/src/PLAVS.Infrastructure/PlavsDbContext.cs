using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PLAVS.Application;
using PLAVS.Domain;

namespace PLAVS.Infrastructure;

public sealed class AppUser : IdentityUser
{
    public string FullName { get; set; } = "";
    public bool Blocked { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class PlavsDbContext(DbContextOptions<PlavsDbContext> options) : IdentityDbContext<AppUser>(options), IPlavsDb
{
    public DbSet<Discipline> Disciplines => Set<Discipline>();
    public DbSet<AcademicGroup> Groups => Set<AcademicGroup>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<DisciplineTeacher> DisciplineTeachers => Set<DisciplineTeacher>();
    public DbSet<DisciplineGroup> DisciplineGroups => Set<DisciplineGroup>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<ReferenceAnswer> ReferenceAnswers => Set<ReferenceAnswer>();
    public DbSet<AnswerVariant> AnswerVariants => Set<AnswerVariant>();
    public DbSet<Keyword> Keywords => Set<Keyword>();
    public DbSet<QuestionRevision> QuestionRevisions => Set<QuestionRevision>();
    public DbSet<Assessment> Tests => Set<Assessment>();
    public DbSet<TestQuestion> TestQuestions => Set<TestQuestion>();
    public DbSet<TestGroup> TestGroups => Set<TestGroup>();
    public DbSet<TestAttempt> Attempts => Set<TestAttempt>();
    public DbSet<StudentAnswer> Answers => Set<StudentAnswer>();
    public DbSet<AnalysisResult> Results => Set<AnalysisResult>();
    public DbSet<AnalysisRun> AnalysisRuns => Set<AnalysisRun>();
    public DbSet<ManualReview> Reviews => Set<ManualReview>();
    public DbSet<AnalysisSettings> Settings => Set<AnalysisSettings>();
    public DbSet<AuditEvent> Audit => Set<AuditEvent>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<AppUser>().Property(u => u.FullName).HasMaxLength(120);
        b.Entity<Discipline>().Property(d => d.Name).HasMaxLength(160);
        b.Entity<AcademicGroup>().HasIndex(g => g.Name).IsUnique();
        b.Entity<AcademicGroup>().Property(g => g.Name).HasMaxLength(80);
        b.Entity<GroupMember>().HasIndex(m => new { m.GroupId, m.UserId }).IsUnique();
        b.Entity<GroupMember>().HasOne<AcademicGroup>().WithMany().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<GroupMember>().HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DisciplineTeacher>().HasIndex(t => new { t.DisciplineId, t.UserId }).IsUnique();
        b.Entity<DisciplineTeacher>().HasOne<Discipline>().WithMany().HasForeignKey(t => t.DisciplineId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DisciplineTeacher>().HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DisciplineGroup>().HasIndex(t => new { t.DisciplineId, t.GroupId }).IsUnique();
        b.Entity<DisciplineGroup>().HasOne<Discipline>().WithMany().HasForeignKey(t => t.DisciplineId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DisciplineGroup>().HasOne<AcademicGroup>().WithMany().HasForeignKey(t => t.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Question>().HasOne<Discipline>().WithMany().HasForeignKey(q => q.DisciplineId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Question>().HasOne<AppUser>().WithMany().HasForeignKey(q => q.AuthorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Question>().HasIndex(q => new { q.DisciplineId, q.Status });
        b.Entity<Question>().Property(q => q.Text).HasMaxLength(2000);
        b.Entity<Question>().Property(q => q.Topic).HasMaxLength(160);
        b.Entity<Question>().Property(q => q.Revision).IsConcurrencyToken();
        b.Entity<Question>().HasMany(q => q.References).WithOne().HasForeignKey(r => r.QuestionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Question>().HasMany(q => q.Variants).WithOne().HasForeignKey(v => v.QuestionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Question>().HasMany(q => q.Keywords).WithOne().HasForeignKey(k => k.QuestionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ReferenceAnswer>().Property(r => r.Text).HasMaxLength(5000);
        b.Entity<ReferenceAnswer>().HasIndex(r => r.QuestionId).IsUnique().HasFilter("\"IsPrimary\" = TRUE");
        b.Entity<AnswerVariant>().Property(v => v.Text).HasMaxLength(5000);
        b.Entity<AnswerVariant>().HasIndex(v => new { v.QuestionId, v.Status });
        b.Entity<AnswerVariant>().HasOne<AppUser>().WithMany().HasForeignKey(v => v.AuthorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AnswerVariant>().HasOne<AppUser>().WithMany().HasForeignKey(v => v.ApprovedBy).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AnswerVariant>().HasOne<StudentAnswer>().WithMany().HasForeignKey(v => v.SourceAnswerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Keyword>().Property(k => k.Term).HasMaxLength(160);
        b.Entity<Keyword>().HasIndex(k => new { k.QuestionId, k.Term }).IsUnique();
        b.Entity<Keyword>().ToTable(t => t.HasCheckConstraint("CK_Keyword_Weight", "\"Weight\" > 0 AND \"Weight\" <= 100"));
        b.Entity<QuestionRevision>().Property(r => r.SnapshotJson).HasColumnType("jsonb");
        b.Entity<QuestionRevision>().HasIndex(r => new { r.QuestionId, r.Revision }).IsUnique();
        b.Entity<QuestionRevision>().HasOne<Question>().WithMany().HasForeignKey(r => r.QuestionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<QuestionRevision>().HasOne<AppUser>().WithMany().HasForeignKey(r => r.AuthorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Assessment>().ToTable("Tests", t => t.HasCheckConstraint("CK_Test_Duration", "\"DurationMinutes\" BETWEEN 1 AND 480"));
        b.Entity<Assessment>().Property(t => t.Title).HasMaxLength(200);
        b.Entity<Assessment>().HasOne<Discipline>().WithMany().HasForeignKey(t => t.DisciplineId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Assessment>().HasOne<AppUser>().WithMany().HasForeignKey(t => t.AuthorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Assessment>().HasMany(t => t.Questions).WithOne().HasForeignKey(tq => tq.TestId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Assessment>().HasMany(t => t.Groups).WithOne().HasForeignKey(tg => tg.TestId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<TestQuestion>().HasIndex(tq => new { tq.TestId, tq.QuestionId }).IsUnique();
        b.Entity<TestQuestion>().HasIndex(tq => new { tq.TestId, tq.Position }).IsUnique();
        b.Entity<TestQuestion>().HasOne<Question>().WithMany().HasForeignKey(tq => tq.QuestionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TestQuestion>().ToTable(t => t.HasCheckConstraint("CK_TestQuestion_Points", "\"MaxPoints\" > 0"));
        b.Entity<TestGroup>().HasIndex(tg => new { tg.TestId, tg.GroupId }).IsUnique();
        b.Entity<TestGroup>().HasOne<AcademicGroup>().WithMany().HasForeignKey(tg => tg.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TestAttempt>().Property(a => a.SnapshotJson).HasColumnType("jsonb");
        b.Entity<TestAttempt>().HasIndex(a => new { a.TestId, a.StudentId }).IsUnique();
        b.Entity<TestAttempt>().HasOne<Assessment>().WithMany().HasForeignKey(a => a.TestId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TestAttempt>().HasOne<AppUser>().WithMany().HasForeignKey(a => a.StudentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TestAttempt>().HasMany(a => a.Answers).WithOne().HasForeignKey(a => a.AttemptId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StudentAnswer>().HasIndex(a => new { a.AttemptId, a.QuestionId }).IsUnique();
        b.Entity<StudentAnswer>().HasIndex(a => new { a.Submitted, a.Status, a.CreatedAt });
        b.Entity<StudentAnswer>().Property(a => a.Text).HasMaxLength(5000);
        b.Entity<StudentAnswer>().HasOne<Question>().WithMany().HasForeignKey(a => a.QuestionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StudentAnswer>().HasOne(a => a.Analysis).WithOne().HasForeignKey<AnalysisResult>(r => r.AnswerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StudentAnswer>().HasMany(a => a.Reviews).WithOne().HasForeignKey(r => r.AnswerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AnalysisResult>().Property(r => r.DetailsJson).HasColumnType("jsonb");
        b.Entity<AnalysisResult>().HasOne<AnalysisSettings>().WithMany().HasForeignKey(r => r.SettingsId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AnalysisResult>().ToTable(t => t.HasCheckConstraint("CK_Result_Range", "\"Lexical\" BETWEEN 0 AND 1 AND \"Keywords\" BETWEEN 0 AND 1 AND (\"Semantic\" IS NULL OR \"Semantic\" BETWEEN 0 AND 1) AND (\"Score\" IS NULL OR \"Score\" BETWEEN 0 AND 100)"));
        b.Entity<ManualReview>().HasOne<AppUser>().WithMany().HasForeignKey(r => r.ReviewerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ManualReview>().Property(r => r.Comment).HasMaxLength(2000);
        b.Entity<ManualReview>().HasIndex(r => new { r.AnswerId, r.CreatedAt });
        b.Entity<ManualReview>().ToTable(t => t.HasCheckConstraint("CK_Review_Score", "\"Score\" IS NULL OR \"Score\" BETWEEN 0 AND 100"));
        b.Entity<AnalysisSettings>().HasIndex(s => s.Version).IsUnique();
        b.Entity<AnalysisSettings>().HasOne<AppUser>().WithMany().HasForeignKey(s => s.AuthorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AnalysisSettings>().ToTable(t => t.HasCheckConstraint("CK_Settings_Weights", "\"Alpha\" >= 0 AND \"Beta\" > 0 AND \"Gamma\" >= 0 AND ABS(\"Alpha\" + \"Beta\" + \"Gamma\" - 1) < 0.000001 AND \"PartialThreshold\" > 0 AND \"HighThreshold\" > \"PartialThreshold\" AND \"HighThreshold\" <= 100"));
        b.Entity<AuditEvent>().Property(a => a.MetadataJson).HasColumnType("jsonb");
        b.Entity<AuditEvent>().HasIndex(a => a.CreatedAt);
        b.Entity<AuditEvent>().HasOne<AppUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AnalysisRun>().Property(r => r.OutcomeJson).HasColumnType("jsonb");
        b.Entity<AnalysisRun>().HasOne<StudentAnswer>().WithMany().HasForeignKey(r => r.AnswerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AnalysisRun>().HasIndex(r => new { r.AnswerId, r.CreatedAt });
        foreach (var type in b.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
            b.Entity(type.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
    }
    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var e in ChangeTracker.Entries<Entity>())
        {
            if (e.State is EntityState.Modified or EntityState.Deleted && e.Entity is AnalysisSettings or ManualReview or AuditEvent or QuestionRevision or AnalysisRun)
                throw new InvalidOperationException("Історичні записи незмінні.");
            if (e.State == EntityState.Modified) e.Entity.UpdatedAt = now;
        }
        return base.SaveChangesAsync(ct);
    }
}
public sealed class WorkflowLock(PlavsDbContext db) : IWorkflowLock
{
    public async Task<T> RunAsync<T>(Guid key, Func<Task<T>> action, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Transaction-scoped advisory locks serialize writes across processes, not only inside one API instance.
        var lockId = BitConverter.ToInt64(key.ToByteArray(), 0);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockId})", ct);
        var result = await action();
        await tx.CommitAsync(ct); return result;
    }
}
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<PlavsDbContext>
{
    public PlavsDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<PlavsDbContext>().UseNpgsql(
        Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? "Host=localhost;Port=5432;Database=plavs;Username=plavs").Options);
}
public sealed class UserDirectory(PlavsDbContext db) : IUserDirectory
{
    public Task<Dictionary<string, string>> NamesAsync(string[] ids, CancellationToken ct) => db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
}
