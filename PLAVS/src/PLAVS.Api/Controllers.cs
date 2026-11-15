using System.Security.Claims;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PLAVS.Application;
using PLAVS.Domain;
using PLAVS.Infrastructure;

namespace PLAVS.Api;

[ApiController]
[Authorize]
public abstract class PlavsController : ControllerBase
{
    protected Actor Actor => new(User.FindFirstValue(ClaimTypes.NameIdentifier)!, User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet());
    protected static void Pagination(ref int page, ref int size) { page = Math.Max(1, Math.Min(page, 100000)); size = Math.Clamp(size, 1, 100); }
}
[Route("api/v1/auth")]
public sealed class AuthController(UserManager<AppUser> users, SignInManager<AppUser> signIn, IAntiforgery antiforgery, AccessService access, PlavsDbContext db, IWebHostEnvironment environment, IConfiguration configuration) : PlavsController
{
    [AllowAnonymous, HttpGet("config")]
    public object Config() => new { Demo = environment.IsDevelopment() && configuration.GetValue<bool>("Seed:DemoUsers") };
    [AllowAnonymous, HttpGet("csrf")]
    public object Csrf() => new { Token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken };
    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest input, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(input.Email.Trim());
        if (user is null || user.Blocked) return Unauthorized(new ProblemDetails { Status = 401, Title = "Невірний email, пароль або обліковий запис недоступний." });
        var result = await signIn.PasswordSignInAsync(user, input.Password, false, lockoutOnFailure: true);
        if (!result.Succeeded) return Unauthorized(new ProblemDetails { Status = 401, Title = "Невірний email, пароль або обліковий запис недоступний." });
        access.Audit(new Actor(user.Id, new HashSet<string>()), "Auth.Login", "User", user.Id); await db.SaveChangesAsync(ct);
        return Ok(new UserView(user.Id, user.Email!, user.FullName, (await users.GetRolesAsync(user)).ToArray(), user.Blocked));
    }
    [HttpGet("me")]
    public async Task<UserView> Me()
    {
        var u = await users.GetUserAsync(User) ?? throw AppException.NotFound();
        return new(u.Id, u.Email!, u.FullName, (await users.GetRolesAsync(u)).ToArray(), u.Blocked);
    }
    [HttpPost("logout")]
    public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return NoContent(); }
}
[Route("api/v1/questions"), Authorize(Roles = Roles.Teacher)]
public sealed class QuestionsController(QuestionService questions) : PlavsController
{
    [HttpGet]
    public Task<Page<QuestionView>> List(string? search, Guid? disciplineId, QuestionStatus? status, CancellationToken ct, int page = 1, int size = 20)
    { Pagination(ref page, ref size); return questions.ListAsync(Actor, search, disciplineId, status, page, size, ct); }
    [HttpGet("{id:guid}")]
    public Task<QuestionView> Get(Guid id, CancellationToken ct) => questions.GetAsync(Actor, id, ct);
    [HttpPost]
    public async Task<IActionResult> Create(QuestionInput input, CancellationToken ct)
    { var id = await questions.CreateAsync(Actor, input, ct); return CreatedAtAction(nameof(Get), new { id }, new { id }); }
    [HttpPut("{id:guid}")]
    public async Task<object> Update(Guid id, QuestionInput input, CancellationToken ct) => new { Id = await questions.UpdateAsync(Actor, id, input, ct) };
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct) { await questions.ArchiveAsync(Actor, id, ct); return NoContent(); }
    [HttpGet("{id:guid}/history")]
    public Task<object[]> History(Guid id, CancellationToken ct) => questions.HistoryAsync(Actor, id, ct);
    [HttpPost("{id:guid}/reference-answers")]
    public async Task<IActionResult> AddReference(Guid id, TextInput input, CancellationToken ct) { await questions.AddReferenceAsync(Actor, id, input.Text, ct); return StatusCode(201); }
    [HttpPut("{id:guid}/reference-answers/{referenceId:guid}")]
    public async Task<IActionResult> UpdateReference(Guid id, Guid referenceId, TextInput input, CancellationToken ct) { await questions.UpdateReferenceAsync(Actor, id, referenceId, input.Text, null, ct); return NoContent(); }
    [HttpDelete("{id:guid}/reference-answers/{referenceId:guid}")]
    public async Task<IActionResult> DisableReference(Guid id, Guid referenceId, CancellationToken ct) { await questions.UpdateReferenceAsync(Actor, id, referenceId, null, false, ct); return NoContent(); }
    [HttpPost("{id:guid}/variants")]
    public async Task<IActionResult> AddVariant(Guid id, TextInput input, CancellationToken ct) { await questions.AddVariantAsync(Actor, id, input.Text, ct); return StatusCode(201); }
    [HttpPut("{id:guid}/variants/{variantId:guid}")]
    public async Task<IActionResult> UpdateVariant(Guid id, Guid variantId, TextInput input, CancellationToken ct) { await questions.UpdateVariantAsync(Actor, id, variantId, input.Text, ct); return NoContent(); }
    [HttpPost("{id:guid}/variants/{variantId:guid}/decision")]
    public async Task<IActionResult> Decide(Guid id, Guid variantId, VariantDecision input, CancellationToken ct) { await questions.DecideVariantAsync(Actor, id, variantId, input.Status, ct); return NoContent(); }
    [HttpPost("{id:guid}/keywords"), HttpPut("{id:guid}/keywords")]
    public async Task<IActionResult> Keywords(Guid id, KeywordInput[] input, CancellationToken ct) { await questions.SetKeywordsAsync(Actor, id, input, ct); return NoContent(); }
}
[Route("api/v1/tests")]
public sealed class TestsController(TestService tests, AnswerService answers) : PlavsController
{
    [HttpGet]
    public Task<Page<TestView>> List(Guid? disciplineId, CancellationToken ct, int page = 1, int size = 20) { Pagination(ref page, ref size); return tests.ListAsync(Actor, page, size, ct, disciplineId); }
    [HttpGet("{id:guid}")]
    public Task<TestView> Get(Guid id, CancellationToken ct) => tests.GetAsync(Actor, id, ct);
    [HttpPost, Authorize(Roles = Roles.Teacher)]
    public async Task<IActionResult> Create(TestInput input, CancellationToken ct) { var id = await tests.CreateAsync(Actor, input, ct); return Created($"/api/v1/tests/{id}", new { id }); }
    [HttpPut("{id:guid}"), Authorize(Roles = Roles.Teacher)]
    public async Task<object> Update(Guid id, TestInput input, CancellationToken ct) => new { Id = await tests.UpdateAsync(Actor, id, input, ct) };
    [HttpDelete("{id:guid}"), Authorize(Roles = Roles.Teacher)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct) { await tests.ArchiveAsync(Actor, id, ct); return NoContent(); }
    [HttpPut("{id:guid}/results-visibility"), Authorize(Roles = Roles.Teacher)]
    public async Task<IActionResult> Visibility(Guid id, [FromBody] bool visible, CancellationToken ct) { await tests.SetVisibilityAsync(Actor, id, visible, ct); return NoContent(); }
    [HttpPost("{id:guid}/start"), Authorize(Roles = Roles.Student)]
    public async Task<object> Start(Guid id, CancellationToken ct) => new { Id = await tests.StartAsync(Actor, id, ct) };
    [HttpGet("attempts/{id:guid}"), Authorize(Roles = Roles.Student)]
    public Task<AttemptView> Attempt(Guid id, CancellationToken ct) => tests.AttemptAsync(Actor, id, ct);
    [HttpPost("attempts/{id:guid}/submit"), Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) => Accepted(new { AnswerIds = await answers.SubmitAsync(Actor, id, ct) });
}
[Route("api/v1/answers")]
public sealed class AnswersController(AnswerService answers, PlavsDbContext db, AccessService access) : PlavsController
{
    [HttpGet("{id:guid}/history"), Authorize(Roles = Roles.Teacher)]
    public async Task<object[]> History(Guid id, CancellationToken ct)
    {
        if (!await access.VisibleAnswers(Actor).AnyAsync(a => a.Id == id, ct)) throw AppException.NotFound();
        var runs = await db.AnalysisRuns.AsNoTracking().Where(r => r.AnswerId == id).OrderByDescending(r => r.CreatedAt).Take(100).ToArrayAsync(ct);
        return runs.Select(r => (object)new { r.Id, r.CreatedAt, Analysis = JsonDefaults.Read<AnalysisOutcome>(r.OutcomeJson) }).ToArray();
    }
    [HttpGet, Authorize(Roles = Roles.Teacher + "," + Roles.Student)]
    public Task<Page<AnswerListItem>> List(AnswerStatus? status, Guid? testId, CancellationToken ct, int page = 1, int size = 20)
    { Pagination(ref page, ref size); return answers.ListAsync(Actor, status, testId, page, size, ct); }
    [HttpPost, Authorize(Roles = Roles.Student)]
    public async Task<object> Save(AnswerInput input, CancellationToken ct) => new { Id = await answers.SaveDraftAsync(Actor, input, ct) };
    [HttpGet("{id:guid}/analysis"), Authorize(Roles = Roles.Teacher + "," + Roles.Student)]
    public Task<object> Get(Guid id, CancellationToken ct) => answers.GetAsync(Actor, id, ct);
    [HttpPost("{id:guid}/reanalyze"), Authorize(Roles = Roles.Teacher)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct) { await answers.RetryAsync(Actor, id, ct); return Accepted(); }
    [HttpPost("{id:guid}/review"), Authorize(Roles = Roles.Teacher)]
    public async Task<IActionResult> Review(Guid id, ReviewInput input, CancellationToken ct) { await answers.ReviewAsync(Actor, id, input, ct); return NoContent(); }
}
[Route("api/v1")]
public sealed class WorkspaceController(AdministrationService admin, ReportingService reports, PlavsDbContext db, ISemanticEncoder semantic) : PlavsController
{
    [HttpGet("disciplines")]
    public Task<DisciplineView[]> Disciplines(CancellationToken ct) => admin.DisciplinesAsync(Actor, ct);
    [HttpPost("disciplines"), Authorize(Roles = Roles.Teacher + "," + Roles.Administrator)]
    public async Task<IActionResult> CreateDiscipline(DisciplineInput input, CancellationToken ct) { var id = await admin.SaveDisciplineAsync(Actor, null, input, ct); return Created($"/api/v1/disciplines/{id}", new { id }); }
    [HttpPut("disciplines/{id:guid}"), Authorize(Roles = Roles.Teacher + "," + Roles.Administrator)]
    public async Task<IActionResult> UpdateDiscipline(Guid id, DisciplineInput input, CancellationToken ct) { await admin.SaveDisciplineAsync(Actor, id, input, ct); return NoContent(); }
    [HttpDelete("disciplines/{id:guid}"), Authorize(Roles = Roles.Teacher + "," + Roles.Administrator)]
    public async Task<IActionResult> ArchiveDiscipline(Guid id, CancellationToken ct) { await admin.ArchiveDisciplineAsync(Actor, id, ct); return NoContent(); }
    [HttpGet("groups"), Authorize(Roles = Roles.Teacher + "," + Roles.Administrator)]
    public Task<GroupView[]> Groups(CancellationToken ct) => admin.GroupsAsync(Actor, ct);
    [HttpGet("directory"), Authorize(Roles = Roles.Teacher + "," + Roles.Administrator)]
    public Task<object> Directory(CancellationToken ct) => admin.DirectoryAsync(Actor, ct);
    [HttpGet("dashboard")]
    public async Task<object> Dashboard(CancellationToken ct)
    {
        object? userCounts = null;
        if (Actor.IsAdmin)
        {
            var roleCounts = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id group ur by r.Name into g select new { Role = g.Key, Count = g.Count() }).ToArrayAsync(ct);
            userCounts = new { Total = await db.Users.CountAsync(ct), Teachers = roleCounts.SingleOrDefault(r => r.Role == Roles.Teacher)?.Count ?? 0, Students = roleCounts.SingleOrDefault(r => r.Role == Roles.Student)?.Count ?? 0 };
        }
        return new { Metrics = await reports.DashboardAsync(Actor, ct), Users = userCounts, Semantic = Actor.IsStudent && !Actor.IsTeacher ? null : semantic.Status };
    }
    [HttpGet("reports"), Authorize(Roles = Roles.Teacher + "," + Roles.Administrator)]
    public Task<ReportRow[]> Reports(Guid? disciplineId, Guid? groupId, Guid? testId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
        => reports.ReportsAsync(Actor, disciplineId, groupId, testId, from, to, ct);
    [HttpGet("reports/export"), Authorize(Roles = Roles.Teacher + "," + Roles.Administrator)]
    public async Task<IActionResult> Export(string? format, Guid? disciplineId, Guid? groupId, Guid? testId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var rows = await reports.ReportsAsync(Actor, disciplineId, groupId, testId, from, to, ct);
        string[] headings = ["Дисципліна", "Група", "Тест", "Відповідей", "Очікують", "Середній %", "Високі", "Часткові", "Низькі"];
        if (format == "xlsx")
        {
            using var workbook = new XLWorkbook(); var sheet = workbook.Worksheets.Add("Результати");
            for (var i = 0; i < headings.Length; i++) sheet.Cell(1, i + 1).Value = headings[i];
            var n = 2;
            foreach (var r in rows)
            {
                sheet.Cell(n, 1).Value = r.Discipline; sheet.Cell(n, 2).Value = r.Group; sheet.Cell(n, 3).Value = r.Test;
                sheet.Cell(n, 4).Value = r.Answers; sheet.Cell(n, 5).Value = r.Pending; if (r.Average.HasValue) sheet.Cell(n, 6).Value = r.Average.Value;
                sheet.Cell(n, 7).Value = r.High; sheet.Cell(n, 8).Value = r.Partial; sheet.Cell(n, 9).Value = r.Low; n++;
            }
            sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#14384F"); sheet.Row(1).Style.Font.FontColor = XLColor.White;
            sheet.Row(1).Style.Font.Bold = true; sheet.SheetView.FreezeRows(1); sheet.Columns().AdjustToContents(10, 55);
            sheet.Column(6).Style.NumberFormat.Format = "0.00";
            using var stream = new MemoryStream(); workbook.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "plavs-report.xlsx");
        }
        if (format is not (null or "csv")) throw AppException.Invalid("Формат експорту: csv або xlsx.");
        static string Cell(string value)
        {
            // Prevent formula injection when CSV files are opened in a spreadsheet application.
            if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@') || value.StartsWith('\t') || value.StartsWith('\r')) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder("\uFEFF").AppendLine(string.Join(';', headings.Select(Cell)));
        foreach (var r in rows) csv.AppendLine(string.Join(';', new[] { r.Discipline, r.Group, r.Test, r.Answers.ToString(), r.Pending.ToString(), r.Average?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) ?? "", r.High.ToString(), r.Partial.ToString(), r.Low.ToString() }.Select(Cell)));
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", "plavs-report.csv");
    }
    [HttpGet("settings"), Authorize(Roles = Roles.Teacher + "," + Roles.Administrator)]
    public async Task<SettingsSpec> Settings(CancellationToken ct) => SettingsSpec.From(await db.Settings.AsNoTracking().OrderByDescending(s => s.Version).FirstAsync(ct));
    [HttpPut("settings"), Authorize(Roles = Roles.Teacher + "," + Roles.Administrator)]
    public Task<SettingsSpec> Settings(SettingsSpec input, CancellationToken ct) => admin.SaveSettingsAsync(Actor, input, ct);
    [HttpGet("settings/history"), Authorize(Roles = Roles.Teacher + "," + Roles.Administrator)]
    public async Task<SettingsSpec[]> SettingsHistory(CancellationToken ct) => (await db.Settings.AsNoTracking().OrderByDescending(s => s.Version).Take(100).ToArrayAsync(ct)).Select(SettingsSpec.From).ToArray();
    [HttpGet("system/health"), Authorize(Roles = Roles.Administrator + "," + Roles.Teacher)]
    public async Task<object> Health(CancellationToken ct) => new { Database = await db.Database.CanConnectAsync(ct), Semantic = semantic.Status, Algorithm = PLAVS.Analysis.AnalysisEngine.Version, UtcNow = DateTimeOffset.UtcNow };
}
[Route("api/v1/admin"), Authorize(Roles = Roles.Administrator)]
public sealed class AdminController(AdministrationService admin, PlavsDbContext db) : PlavsController
{
    [HttpGet("users")]
    public Task<Page<UserView>> Users(string? search, CancellationToken ct, int page = 1, int size = 20) { Pagination(ref page, ref size); return admin.UsersAsync(search, page, size, ct); }
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser(UserInput input, CancellationToken ct) { var id = await admin.SaveUserAsync(Actor, null, input, ct); return Created($"/api/v1/admin/users/{id}", new { id }); }
    [HttpPut("users/{id}")]
    public async Task<IActionResult> UpdateUser(string id, UserInput input, CancellationToken ct) { await admin.SaveUserAsync(Actor, id, input, ct); return NoContent(); }
    [HttpPost("groups")]
    public async Task<object> CreateGroup(GroupInput input, CancellationToken ct) => new { Id = await admin.SaveGroupAsync(Actor, null, input, ct) };
    [HttpPut("groups/{id:guid}")]
    public async Task<IActionResult> UpdateGroup(Guid id, GroupInput input, CancellationToken ct) { await admin.SaveGroupAsync(Actor, id, input, ct); return NoContent(); }
    [HttpDelete("groups/{id:guid}")]
    public async Task<IActionResult> ArchiveGroup(Guid id, CancellationToken ct) { await admin.ArchiveGroupAsync(Actor, id, ct); return NoContent(); }
    [HttpPut("groups/{id:guid}/members")]
    public async Task<IActionResult> Members(Guid id, string[] userIds, CancellationToken ct) { await admin.SetMembersAsync(Actor, id, userIds, ct); return NoContent(); }
    [HttpPut("disciplines/{id:guid}/assignments")]
    public async Task<IActionResult> Assign(Guid id, AssignmentInput input, CancellationToken ct) { await admin.AssignAsync(Actor, id, input, ct); return NoContent(); }
    [HttpGet("audit")]
    public async Task<Page<AuditView>> Audit(CancellationToken ct, int page = 1, int size = 30)
    {
        Pagination(ref page, ref size);
        return new(await db.Audit.AsNoTracking().OrderByDescending(a => a.CreatedAt).Skip((page - 1) * size).Take(size).Select(a => new AuditView(a.Id, a.UserId, a.Action, a.EntityType, a.EntityId, a.CreatedAt)).ToArrayAsync(ct), await db.Audit.CountAsync(ct), page, size);
    }
    [HttpGet("settings")]
    public async Task<SettingsSpec> Settings(CancellationToken ct) => SettingsSpec.From(await db.Settings.AsNoTracking().OrderByDescending(s => s.Version).FirstAsync(ct));
    [HttpPut("settings")]
    public Task<SettingsSpec> Settings(SettingsSpec input, CancellationToken ct) => admin.SaveSettingsAsync(Actor, input, ct);
}
