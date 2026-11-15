using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PLAVS.Application;

namespace PLAVS.Api;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var pg = exception as PostgresException ?? exception.InnerException as PostgresException;
        var status = exception switch
        {
            AppException e => e.Status,
            DbUpdateConcurrencyException => 409,
            _ when pg?.SqlState is "23505" or "40001" or "40P01" => 409,
            _ when pg?.SqlState is "23503" or "23514" or "22001" => 400,
            NpgsqlException => 503,
            DbUpdateException when exception.InnerException is NpgsqlException => 503,
            _ => 500
        };
        var title = exception is AppException app ? app.Message : status switch
        {
            409 => "Запис уже існує або змінився. Оновіть сторінку та повторіть дію.",
            400 => "Дані не відповідають обмеженням бази. Перевірте форму.",
            503 => "База даних тимчасово недоступна. Спробуйте пізніше.",
            _ => "Внутрішня помилка. Повідомте адміністратору ідентифікатор запиту."
        };
        if (status >= 500) logger.LogError("Request failed: {ExceptionType}; trace {TraceId}", exception.GetType().Name, context.TraceIdentifier);
        context.Response.StatusCode = status;
        await problems.WriteAsync(new ProblemDetailsContext { HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Extensions = { ["traceId"] = context.TraceIdentifier } } });
        return true;
    }
}
