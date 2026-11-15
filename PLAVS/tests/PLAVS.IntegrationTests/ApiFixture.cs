using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PLAVS.Application;
using PLAVS.Domain;
using PLAVS.Infrastructure;
using Xunit;

namespace PLAVS.IntegrationTests;

[CollectionDefinition("PostgreSQL", DisableParallelization = true)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
public sealed class TestFactory(string connection) : WebApplicationFactory<Program>
{
    public string ConnectionString => connection;
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", connection);
        builder.UseSetting("Database:AutoMigrate", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISemanticEncoder>(); services.AddSingleton<ISemanticEncoder, UnavailableTestEncoder>();
        });
    }
    private sealed class UnavailableTestEncoder : ISemanticEncoder
    {
        public SemanticStatus Status => new(false,"integration-test-double","Модель вимкнена для перевірки сценарію відмови.");
        public Task<Embedding> EncodeAsync(string text,bool cache,CancellationToken ct=default) => throw new InvalidOperationException(Status.Message);
    }
}
public sealed class ApiFixture : IAsyncLifetime
{
    public TestFactory Factory { get; private set; } = null!;
    public HttpClient Teacher { get; private set; } = null!;
    public HttpClient Student { get; private set; } = null!;
    public HttpClient Admin { get; private set; } = null!;
    public string TeacherId { get; private set; } = "";
    public Guid DisciplineId { get; private set; }
    public Guid GroupId { get; private set; }
    private string adminConnection = "", databaseName = "";
    public async Task InitializeAsync()
    {
        var supplied = Environment.GetEnvironmentVariable("PLAVS_TEST_CONNECTION") ?? throw new InvalidOperationException("Для інтеграційних тестів задайте PLAVS_TEST_CONNECTION (PostgreSQL із правом CREATE DATABASE). Створюється окрема тимчасова БД.");
        var cs = new NpgsqlConnectionStringBuilder(supplied); adminConnection = cs.ConnectionString;
        databaseName = "plavs_test_" + Guid.NewGuid().ToString("N");
        await using (var conn = new NpgsqlConnection(adminConnection))
        { await conn.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"",conn); await command.ExecuteNonQueryAsync(); }
        cs.Database = databaseName;
        Factory = new TestFactory(cs.ConnectionString);
        // This is read before application construction, including in hosts that defer test configuration callbacks.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection",cs.ConnectionString);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlavsDbContext>(); await db.Database.MigrateAsync();
            var root = FindRoot();
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedDevelopmentAsync(Path.Combine(root,"database/seed/questions.json"),"Plavs!Demo2026");
            TeacherId = (await db.Users.SingleAsync(u=>u.Email=="teacher@plavs.local")).Id;
            DisciplineId = await db.DisciplineTeachers.Where(t=>t.UserId==TeacherId).Select(t=>t.DisciplineId).FirstAsync();
            GroupId = await db.Groups.Select(g=>g.Id).FirstAsync();
        }
        Teacher = await LoginAsync("teacher@plavs.local"); Student = await LoginAsync("student@plavs.local"); Admin = await LoginAsync("admin@plavs.local");
    }
    public async Task<HttpClient> LoginAsync(string email)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect=false });
        await CsrfAsync(client);
        var response = await client.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest(email,"Plavs!Demo2026"));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode); await CsrfAsync(client); return client;
    }
    public static async Task CsrfAsync(HttpClient client)
    {
        var data = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",data.GetProperty("token").GetString());
    }
    public QuestionInput QuestionInput() => new(DisciplineId,"Інтеграційна перевірка","Що таке стек даних?","Стек працює за принципом LIFO: останній елемент вилучається першим.",
        [new("стек",[],1),new("LIFO",["останній"],2)], ["Стек повертає спочатку останній доданий елемент."], QuestionStatus.Active);
    public async Task<Guid> NewQuestionAsync()
    {
        var response=await Teacher.PostAsJsonAsync("/api/v1/questions",QuestionInput(),JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.Created,response.StatusCode); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    public async Task<(Guid TestId, Guid AttemptId, Guid AnswerId, Guid QuestionId)> SubmittedAnswerAsync()
    {
        var question=await NewQuestionAsync();
        var input=new TestInput(DisciplineId,"Перевірка "+Guid.NewGuid(),"Опишіть структуру.",true,false,null,null,30,[new(question,10)],[GroupId]);
        var testResponse=await Teacher.PostAsJsonAsync("/api/v1/tests",input,JsonDefaults.Options);Assert.Equal(HttpStatusCode.Created,testResponse.StatusCode);
        var test=(await testResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var started=await Student.PostAsync($"/api/v1/tests/{test}/start",null);Assert.Equal(HttpStatusCode.OK,started.StatusCode);
        var attempt=(await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var saved=await Student.PostAsJsonAsync("/api/v1/answers",new AnswerInput(attempt,question,"Стек має LIFO: останній доданий елемент вилучається першим."),JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.OK,saved.StatusCode);
        var answer=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Accepted,(await Student.PostAsync($"/api/v1/tests/attempts/{attempt}/submit",null)).StatusCode);
        return(test,attempt,answer,question);
    }
    public async Task DisposeAsync()
    {
        Teacher?.Dispose(); Student?.Dispose(); Admin?.Dispose(); if(Factory is not null)await Factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        if(databaseName.Length>0){await using var conn=new NpgsqlConnection(adminConnection);await conn.OpenAsync();await using var command=new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)",conn);await command.ExecuteNonQueryAsync();}
    }
    private static string FindRoot()
    {
        var p=new DirectoryInfo(AppContext.BaseDirectory);
        while(p is not null&&!File.Exists(Path.Combine(p.FullName,"PLAVS.sln")))p=p.Parent;
        return p?.FullName??throw new InvalidOperationException("Solution не знайдено.");
    }
}
