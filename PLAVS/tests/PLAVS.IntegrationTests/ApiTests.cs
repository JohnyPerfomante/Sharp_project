using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PLAVS.Application;
using PLAVS.Domain;
using PLAVS.Infrastructure;
using Xunit;

namespace PLAVS.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class ApiTests(ApiFixture f)
{
    [Fact] public async Task AnonymousAccessIsDeniedAndLoginNeedsCsrf()
    {
        using var anon=f.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync("/api/v1/questions")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await anon.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("teacher@plavs.local","Plavs!Demo2026"))).StatusCode);
    }
    [Fact] public async Task StudentAndAdministratorCannotReadOrModifyAcademicMaterials()
    {
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Student.GetAsync("/api/v1/questions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Student.GetAsync("/api/v1/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Admin.PostAsJsonAsync("/api/v1/questions",f.QuestionInput(),JsonDefaults.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Admin.GetAsync("/api/v1/answers")).StatusCode);
    }
    [Fact] public async Task SeedHasSeventyQuestionsAndOnlyPendingVariants()
    {
        using var scope=f.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PlavsDbContext>();
        var seed=await db.Questions.Include(q=>q.Variants).Include(q=>q.References).Where(q=>q.Topic!="Інтеграційна перевірка").ToArrayAsync();
        Assert.Equal(70,seed.Length);Assert.All(seed,q=>{Assert.Single(q.References,r=>r.IsPrimary);Assert.Equal(3,q.Variants.Count);Assert.All(q.Variants,v=>Assert.Equal(VariantStatus.Pending,v.Status));});
    }
    [Fact] public async Task QuestionUpdatesPreserveHistoryAndRejectStaleVersion()
    {
        var id=await f.NewQuestionAsync();var input=f.QuestionInput() with {Text="Що таке стек і його вершина?",Revision=1};
        Assert.Equal(HttpStatusCode.OK,(await f.Teacher.PutAsJsonAsync($"/api/v1/questions/{id}",input,JsonDefaults.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await f.Teacher.PutAsJsonAsync($"/api/v1/questions/{id}",input,JsonDefaults.Options)).StatusCode);
        var history=await f.Teacher.GetFromJsonAsync<JsonElement>($"/api/v1/questions/{id}/history");Assert.Equal(1,history.GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent,(await f.Teacher.DeleteAsync($"/api/v1/questions/{id}")).StatusCode);
        var q=await f.Teacher.GetFromJsonAsync<QuestionView>($"/api/v1/questions/{id}",JsonDefaults.Options);Assert.Equal(QuestionStatus.Archived,q!.Status);
    }
    [Fact] public async Task ReferencesAndKeywordsArePersistedInPostgres()
    {
        var id=await f.NewQuestionAsync();
        Assert.Equal(HttpStatusCode.Created,(await f.Teacher.PostAsJsonAsync($"/api/v1/questions/{id}/reference-answers",new TextInput("Стек має вершину й правило LIFO."))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await f.Teacher.PutAsJsonAsync($"/api/v1/questions/{id}/keywords",new[]{new KeywordInput("вершина",["top"],3)},JsonDefaults.Options)).StatusCode);
        var q=await f.Teacher.GetFromJsonAsync<QuestionView>($"/api/v1/questions/{id}",JsonDefaults.Options);
        Assert.Equal(2,q!.References.Length);Assert.Equal(3,q.Keywords.Single().Weight);
        Assert.Equal(HttpStatusCode.BadRequest,(await f.Teacher.DeleteAsync($"/api/v1/questions/{id}/reference-answers/{q.References.Single(r=>r.IsPrimary).Id}")).StatusCode);
    }
    [Fact] public async Task VariantNeedsExplicitApprovalAndEditingResetsIt()
    {
        var id=await f.NewQuestionAsync();var q=await f.Teacher.GetFromJsonAsync<QuestionView>($"/api/v1/questions/{id}",JsonDefaults.Options);var v=q!.Variants.Single();
        Assert.Equal(VariantStatus.Pending,v.Status);
        Assert.Equal(HttpStatusCode.NoContent,(await f.Teacher.PostAsJsonAsync($"/api/v1/questions/{id}/variants/{v.Id}/decision",new VariantDecision(VariantStatus.Approved),JsonDefaults.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await f.Teacher.PutAsJsonAsync($"/api/v1/questions/{id}/variants/{v.Id}",new TextInput("Останній елемент стека повертається першим."))).StatusCode);
        q=await f.Teacher.GetFromJsonAsync<QuestionView>($"/api/v1/questions/{id}",JsonDefaults.Options);Assert.Equal(VariantStatus.Pending,q!.Variants.Single().Status);Assert.Null(q.Variants.Single().ApprovedAt);
    }
    [Fact] public async Task SubmissionIsIdempotentAndCannotBeEditedAfterwards()
    {
        var a=await f.SubmittedAnswerAsync();
        var retry=await f.Student.PostAsync($"/api/v1/tests/attempts/{a.AttemptId}/submit",null);Assert.Equal(HttpStatusCode.Accepted,retry.StatusCode);
        var after=await f.Student.PostAsJsonAsync("/api/v1/answers",new AnswerInput(a.AttemptId,a.QuestionId,"Нова відповідь"));Assert.Equal(HttpStatusCode.Conflict,after.StatusCode);
        using var scope=f.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PlavsDbContext>();Assert.Equal(1,await db.Answers.CountAsync(x=>x.AttemptId==a.AttemptId));
    }
    [Fact] public async Task MissingModelPreservesAnswerAndProducesManualReview()
    {
        var a=await f.SubmittedAnswerAsync();using(var scope=f.Factory.Services.CreateScope())await scope.ServiceProvider.GetRequiredService<AnswerService>().AnalyzeStoredAsync(a.AnswerId,default);
        var response=await f.Teacher.GetFromJsonAsync<AnalysisView>($"/api/v1/answers/{a.AnswerId}/analysis",JsonDefaults.Options);
        Assert.Equal(AnswerStatus.NeedsReview,response!.Status);Assert.Null(response.Analysis!.Score);Assert.Null(response.Analysis.Semantic);Assert.NotEmpty(response.Answer);
        using var saved=f.Factory.Services.CreateScope();var db=saved.ServiceProvider.GetRequiredService<PlavsDbContext>();Assert.Equal(1,await db.AnalysisRuns.CountAsync(r=>r.AnswerId==a.AnswerId));
    }
    [Fact] public async Task StudentProjectionDoesNotLeakReferenceConceptsOrSettings()
    {
        var a=await f.SubmittedAnswerAsync();using(var scope=f.Factory.Services.CreateScope())await scope.ServiceProvider.GetRequiredService<AnswerService>().AnalyzeStoredAsync(a.AnswerId,default);
        var hidden=await f.Student.GetFromJsonAsync<JsonElement>($"/api/v1/answers/{a.AnswerId}/analysis");
        Assert.Equal(JsonValueKind.Null,hidden.GetProperty("score").ValueKind);
        Assert.False(hidden.TryGetProperty("analysis",out _));Assert.False(hidden.TryGetProperty("details",out _));Assert.False(hidden.TryGetProperty("settings",out _));
        var review=new ReviewInput(ReviewDecision.Accept,100,"Коректна повна відповідь.",false);
        Assert.Equal(HttpStatusCode.NoContent,(await f.Teacher.PostAsJsonAsync($"/api/v1/answers/{a.AnswerId}/review",review,JsonDefaults.Options)).StatusCode);
        await f.Teacher.PutAsJsonAsync($"/api/v1/tests/{a.TestId}/results-visibility",true);
        var visible=await f.Student.GetFromJsonAsync<JsonElement>($"/api/v1/answers/{a.AnswerId}/analysis");Assert.Equal(100,visible.GetProperty("score").GetDouble());Assert.False(visible.TryGetProperty("matchedText",out _));
    }
    [Fact] public async Task ApprovedStudentAnswerCreatesPendingCandidateAndImmutableReview()
    {
        var a=await f.SubmittedAnswerAsync();var review=new ReviewInput(ReviewDecision.Accept,100,"Зміст передано правильно.",true);
        Assert.Equal(HttpStatusCode.NoContent,(await f.Teacher.PostAsJsonAsync($"/api/v1/answers/{a.AnswerId}/review",review,JsonDefaults.Options)).StatusCode);
        using var scope=f.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PlavsDbContext>();var candidate=await db.AnswerVariants.SingleAsync(v=>v.SourceAnswerId==a.AnswerId);
        Assert.Equal(VariantStatus.Pending,candidate.Status);Assert.Null(candidate.ApprovedAt);
        Assert.Equal(HttpStatusCode.Conflict,(await f.Teacher.PostAsync($"/api/v1/answers/{a.AnswerId}/reanalyze",null)).StatusCode);
        Assert.Equal(1,await db.Reviews.CountAsync(r=>r.AnswerId==a.AnswerId));
    }
    [Fact] public async Task ForeignStudentAndTeacherCannotAccessAnotherScope()
    {
        var a=await f.SubmittedAnswerAsync();
        using(var scope=f.Factory.Services.CreateScope())
        {
            var users=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            foreach(var(email,role)in new[]{("otherstudent@plavs.local",Roles.Student),("otherteacher@plavs.local",Roles.Teacher)})
            {var u=new AppUser{UserName=email,Email=email,FullName="Чужий користувач"};Assert.True((await users.CreateAsync(u,"Plavs!Demo2026")).Succeeded);await users.AddToRoleAsync(u,role);}
        }
        using var other=await f.LoginAsync("otherstudent@plavs.local");
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/answers/{a.AnswerId}/analysis")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/tests/attempts/{a.AttemptId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await other.PostAsync($"/api/v1/tests/{a.TestId}/start",null)).StatusCode);
        using var otherTeacher=await f.LoginAsync("otherteacher@plavs.local");
        Assert.Equal(HttpStatusCode.NotFound,(await otherTeacher.GetAsync($"/api/v1/questions/{a.QuestionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await otherTeacher.GetAsync($"/api/v1/answers/{a.AnswerId}/analysis")).StatusCode);
    }
    [Fact] public async Task SnapshotIsUnaffectedByLaterQuestionAndSettingsEdits()
    {
        var a=await f.SubmittedAnswerAsync();
        var input=f.QuestionInput() with {Text="Нова редакція питання про стек",Revision=1,Reference="Змінений еталон із новими вимогами."};
        await f.Teacher.PutAsJsonAsync($"/api/v1/questions/{a.QuestionId}",input,JsonDefaults.Options);
        using var scope=f.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PlavsDbContext>();
        var attempt=await db.Attempts.SingleAsync(at=>at.Id==a.AttemptId);var snapshot=JsonDefaults.Read<AttemptSnapshot>(attempt.SnapshotJson);
        Assert.Equal("Що таке стек даних?",snapshot.Questions.Single().Text);Assert.DoesNotContain("Змінений",snapshot.Questions.Single().References.Single().Text);
        Assert.DoesNotContain(snapshot.Questions.Single().References,r=>r.Variant);
    }
    [Fact] public async Task SettingsValidateWeightsAndVersionHistory()
    {
        var current=await f.Teacher.GetFromJsonAsync<SettingsSpec>("/api/v1/settings",JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.BadRequest,(await f.Teacher.PutAsJsonAsync("/api/v1/settings",current! with {Alpha=.9},JsonDefaults.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await f.Teacher.PutAsJsonAsync("/api/v1/settings",current!,JsonDefaults.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await f.Teacher.PutAsJsonAsync("/api/v1/settings",current!,JsonDefaults.Options)).StatusCode);
        var history=await f.Teacher.GetFromJsonAsync<SettingsSpec[]>("/api/v1/settings/history",JsonDefaults.Options);Assert.True(history!.Length>=2);
    }
    [Fact] public async Task ExportsAreRealCsvAndXlsxAndStudentCannotExport()
    {
        await f.SubmittedAnswerAsync();
        var csv=await f.Teacher.GetAsync("/api/v1/reports/export?format=csv");Assert.Equal(HttpStatusCode.OK,csv.StatusCode);Assert.Contains("Дисципліна",await csv.Content.ReadAsStringAsync());
        var xlsx=await f.Teacher.GetByteArrayAsync("/api/v1/reports/export?format=xlsx");Assert.Equal((byte)'P',xlsx[0]);Assert.Equal((byte)'K',xlsx[1]);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Student.GetAsync("/api/v1/reports/export?format=csv")).StatusCode);
    }
    [Fact] public async Task ExpiredAttemptRejectsEditsButPreservesSavedDraftWhenSubmitted()
    {
        var q=await f.NewQuestionAsync();
        var created=await f.Teacher.PostAsJsonAsync("/api/v1/tests",new TestInput(f.DisciplineId,"Дедлайн "+Guid.NewGuid(),"Перевірка часу",true,false,null,null,1,[new(q,10)],[f.GroupId]),JsonDefaults.Options);
        var test=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var started=await f.Student.PostAsync($"/api/v1/tests/{test}/start",null);var attempt=(await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var repeated=await f.Student.PostAsync($"/api/v1/tests/{test}/start",null);Assert.Equal(attempt,(await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        const string draft="Збережена до завершення часу відповідь про стек.";
        Assert.Equal(HttpStatusCode.OK,(await f.Student.PostAsJsonAsync("/api/v1/answers",new AnswerInput(attempt,q,draft))).StatusCode);
        using(var scope=f.Factory.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<PlavsDbContext>();await db.Attempts.Where(a=>a.Id==attempt).ExecuteUpdateAsync(s=>s.SetProperty(a=>a.Deadline,DateTimeOffset.UtcNow.AddMinutes(-1)));}
        Assert.Equal(HttpStatusCode.Conflict,(await f.Student.PostAsJsonAsync("/api/v1/answers",new AnswerInput(attempt,q,"Запізніла зміна"))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,(await f.Student.PostAsync($"/api/v1/tests/attempts/{attempt}/submit",null)).StatusCode);
        using var saved=f.Factory.Services.CreateScope();Assert.Equal(draft,(await saved.ServiceProvider.GetRequiredService<PlavsDbContext>().Answers.SingleAsync(a=>a.AttemptId==attempt)).Text);
    }
    [Fact] public async Task TeacherWithDisciplineButWithoutAssignedStudentGroupCannotReview()
    {
        Guid answerId;
        using(var scope=f.Factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PlavsDbContext>();var users=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var u=new AppUser{UserName="scopedteacher@plavs.local",Email="scopedteacher@plavs.local",FullName="Викладач іншої групи"};await users.CreateAsync(u,"Plavs!Demo2026");await users.AddToRoleAsync(u,Roles.Teacher);
            var studentId=(await users.FindByEmailAsync("student@plavs.local"))!.Id;
            var discipline=new Discipline{Name="Ізольована дисципліна"};db.Disciplines.Add(discipline);db.DisciplineTeachers.Add(new DisciplineTeacher{DisciplineId=discipline.Id,UserId=u.Id});
            var question=new Question{DisciplineId=discipline.Id,AuthorId=u.Id,Topic="Інтеграційна перевірка",Text="Історичне питання без призначення групи"};db.Questions.Add(question);
            var test=new Assessment{DisciplineId=discipline.Id,AuthorId=u.Id,Title="Історичний тест"};db.Tests.Add(test);
            var attempt=new TestAttempt{TestId=test.Id,StudentId=studentId,Status=AttemptStatus.Submitted,Deadline=DateTimeOffset.UtcNow};db.Attempts.Add(attempt);
            var answer=new StudentAnswer{AttemptId=attempt.Id,QuestionId=question.Id,Text="Історична відповідь",Submitted=true};db.Answers.Add(answer);answerId=answer.Id;
            // The teacher is assigned to the discipline, but the student's group is not assigned there.
            await db.SaveChangesAsync();
        }
        using var other=await f.LoginAsync("scopedteacher@plavs.local");
        Assert.Equal(HttpStatusCode.NotFound,(await other.PostAsJsonAsync($"/api/v1/answers/{answerId}/review",new ReviewInput(ReviewDecision.Accept,100,"Недозволена перевірка.",false),JsonDefaults.Options)).StatusCode);
    }
    [Fact] public async Task BlockingAUserRevokesExistingSession()
    {
        const string email="blocked@plavs.local";string id;
        using(var scope=f.Factory.Services.CreateScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();var u=new AppUser{UserName=email,Email=email,FullName="Тест блокування"};await users.CreateAsync(u,"Plavs!Demo2026");await users.AddToRoleAsync(u,Roles.Student);id=u.Id;}
        using var client=await f.LoginAsync(email);Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/v1/auth/me")).StatusCode);
        var changed=await f.Admin.PutAsJsonAsync($"/api/v1/admin/users/{id}",new UserInput(email,"Тест блокування",Roles.Student,null,true));Assert.Equal(HttpStatusCode.NoContent,changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }
    [Fact] public async Task LoginHasRateLimit()
    {
        await using var isolated=new TestFactory(f.Factory.ConnectionString);
        using var client=isolated.CreateClient();await ApiFixture.CsrfAsync(client);
        HttpStatusCode status=HttpStatusCode.OK;
        for(var i=0;i<12;i++){var response=await client.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("unknown@plavs.local","Wrong!Password2026"));status=response.StatusCode;if(status==HttpStatusCode.TooManyRequests)break;}
        Assert.Equal(HttpStatusCode.TooManyRequests,status);
    }
}
