using Microsoft.EntityFrameworkCore;
using PLAVS.Application;
using PLAVS.Domain;
using PLAVS.Infrastructure;

namespace PLAVS.Api;

public sealed class AnalysisWorker(IServiceScopeFactory scopes, ILogger<AnalysisWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<PlavsDbContext>();
                var expired = await db.Attempts.AsNoTracking().Where(a => a.Status == AttemptStatus.InProgress && a.Deadline <= DateTimeOffset.UtcNow).OrderBy(a => a.Deadline).Select(a => new { a.Id, a.StudentId }).Take(20).ToArrayAsync(stoppingToken);
                foreach (var a in expired)
                {
                    await using var submissionScope = scopes.CreateAsyncScope();
                    await submissionScope.ServiceProvider.GetRequiredService<AnswerService>().SubmitAsync(new Actor(a.StudentId, new HashSet<string> { Roles.Student }), a.Id, stoppingToken);
                }
                var ids = await db.Answers.AsNoTracking().Where(a => a.Submitted && a.Status == AnswerStatus.PendingAnalysis).OrderBy(a => a.CreatedAt).Select(a => a.Id).Take(20).ToArrayAsync(stoppingToken);
                foreach (var id in ids)
                {
                    try
                    {
                        await using var analysisScope = scopes.CreateAsyncScope();
                        await analysisScope.ServiceProvider.GetRequiredService<AnswerService>().AnalyzeStoredAsync(id, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception ex)
                    {
                        logger.LogWarning("Analysis failed: answer {Id}, type {Type}", id, ex.GetType().Name);
                        await using var errorScope = scopes.CreateAsyncScope();
                        var errorDb = errorScope.ServiceProvider.GetRequiredService<PlavsDbContext>();
                        await errorDb.Answers.Where(a => a.Id == id && a.Status == AnswerStatus.PendingAnalysis).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AnswerStatus.AnalysisError).SetProperty(a => a.UpdatedAt, DateTimeOffset.UtcNow), stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning("Analysis queue unavailable: {Type}", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
