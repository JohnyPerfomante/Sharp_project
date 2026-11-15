using System.Security.Claims;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using PLAVS.Analysis;
using PLAVS.Api;
using PLAVS.Application;
using PLAVS.Infrastructure;

var commands = new[] { "--migrate", "--seed", "--bootstrap-admin" };
var builder = WebApplication.CreateBuilder(args.Where(a => !commands.Contains(a)).ToArray());
var connection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Задайте ConnectionStrings__DefaultConnection. Див. README.md або scripts/run.py.");
builder.Services.AddDbContext<PlavsDbContext>(o => o.UseNpgsql(connection));
builder.Services.AddScoped<IPlavsDb>(s => s.GetRequiredService<PlavsDbContext>());
builder.Services.AddIdentity<AppUser, IdentityRole>(o =>
{
    o.Password.RequiredLength = 12; o.Password.RequireDigit = true; o.Password.RequireLowercase = true;
    o.Password.RequireUppercase = true; o.Password.RequireNonAlphanumeric = true;
    o.User.RequireUniqueEmail = true; o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<PlavsDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = ".PLAVS.Auth"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
var keyDirectory = builder.Configuration["DataProtection:KeyDirectory"];
if (!string.IsNullOrWhiteSpace(keyDirectory))
{
    Directory.CreateDirectory(keyDirectory);
    builder.Services.AddDataProtection().SetApplicationName("PLAVS").PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
}
var knownProxy = builder.Configuration["ReverseProxy:KnownProxy"];
if (!string.IsNullOrWhiteSpace(knownProxy)) builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownProxies.Add(System.Net.IPAddress.Parse(knownProxy)); o.ForwardLimit = 1;
});
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; });
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails(); builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new() { Title = "ПЛАВС API", Version = "v1", Description = "Cookie-автентифікація. Отримайте CSRF-токен через /auth/csrf, передавайте X-CSRF-TOKEN у запитах зміни даних. Після входу отримайте новий токен." }));
builder.Services.AddScoped<IWorkflowLock, WorkflowLock>(); builder.Services.AddScoped<IUserDirectory, UserDirectory>();
builder.Services.AddScoped<AccessService>(); builder.Services.AddScoped<QuestionService>(); builder.Services.AddScoped<TestService>();
builder.Services.AddScoped<AnswerService>(); builder.Services.AddScoped<ReportingService>(); builder.Services.AddScoped<AdministrationService>(); builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddSingleton<LexicalComparer>(); builder.Services.AddSingleton<KeywordMatcher>(); builder.Services.AddSingleton<MlTextFeatures>();
builder.Services.AddSingleton<ISemanticEncoder>(_ =>
{
    var directory = builder.Configuration["Model:Directory"];
    if (string.IsNullOrWhiteSpace(directory)) directory = OnnxSentenceEncoder.DefaultDirectory;
    return new OnnxSentenceEncoder(Path.GetFullPath(directory, builder.Environment.ContentRootPath));
});
builder.Services.AddSingleton<IAnalysisEngine, AnalysisEngine>();
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<AnalysisWorker>();

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PlavsDbContext>();
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    if (args.Contains("--migrate") || builder.Configuration.GetValue<bool>("Database:AutoMigrate")) await db.Database.MigrateAsync();
    if (args.Contains("--migrate")) return;
    if (args.Contains("--bootstrap-admin"))
    {
        var email = builder.Configuration["BootstrapAdmin:Email"] ?? throw new InvalidOperationException("Задайте BootstrapAdmin__Email.");
        var password = builder.Configuration["BootstrapAdmin:Password"] ?? throw new InvalidOperationException("Задайте BootstrapAdmin__Password.");
        await seeder.BootstrapAdminAsync(email, password); return;
    }
    if (!builder.Environment.IsEnvironment("Testing"))
    {
        await seeder.InitializeRolesAsync();
        var seedPath = Path.Combine(AppContext.BaseDirectory, "Seed/questions.json");
        if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Seed:DemoUsers"))
            await seeder.SeedDevelopmentAsync(seedPath, builder.Configuration["Seed:DemoPassword"] ?? "Plavs!Demo2026");
        else if (args.Contains("--seed"))
        {
            var teacherId = builder.Configuration["Seed:TeacherId"] ?? throw new InvalidOperationException("Задайте Seed__TeacherId.");
            var groupId = Guid.Parse(builder.Configuration["Seed:GroupId"] ?? throw new InvalidOperationException("Задайте Seed__GroupId."));
            await seeder.SeedContentAsync(seedPath, teacherId, groupId);
        }
        if (args.Contains("--seed")) return;
    }
}
app.UseExceptionHandler();
if (!string.IsNullOrWhiteSpace(knownProxy)) app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")) { app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (context, next) =>
{
    if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing") && !context.Request.IsHttps)
    {
        await Results.Problem(statusCode: 400, title: "Для роботи системи потрібне захищене HTTPS-з’єднання.").ExecuteAsync(context);
        return;
    }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    var scriptPolicy = app.Environment.IsDevelopment() && context.Request.Path.StartsWithSegments("/swagger") ? "'self' 'unsafe-inline'" : "'self'";
    context.Response.Headers["Content-Security-Policy"] = $"default-src 'self'; script-src {scriptPolicy}; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
    await next();
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.UseRouting(); app.UseAuthentication(); app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true && context.Request.Path.StartsWithSegments("/api"))
    {
        var users = context.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var user = await users.GetUserAsync(context.User);
        var stamp = context.User.FindFirstValue("AspNet.Identity.SecurityStamp");
        if (user is null || user.Blocked || user.SecurityStamp != stamp)
        { await context.RequestServices.GetRequiredService<SignInManager<AppUser>>().SignOutAsync(); context.Response.StatusCode = 401; return; }
    }
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { await Results.Problem(statusCode: 400, title: "Не вдалося перевірити захист запиту. Оновіть сторінку.").ExecuteAsync(context); return; }
    }
    await next();
});
app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    // JSON is same-origin; Swagger UI is configured with a local script to attach the antiforgery token.
    app.UseSwagger(); app.UseSwaggerUI(o => { o.SwaggerEndpoint("/swagger/v1/swagger.json", "ПЛАВС v1"); o.InjectJavascript("/js/swagger-csrf.js"); });
}
app.MapControllers();
app.MapMethods("/api/{**path}", ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE"], () => Results.Problem(statusCode: 404, title: "API-маршрут не знайдено."));
app.MapFallbackToFile("index.html");
app.Run();
public partial class Program;
