using Crm.Core.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api;

/// <summary>Бизнес-ошибки CRM → ProblemDetails с нужным HTTP-статусом (для REST API).</summary>
internal sealed class CrmExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not CrmException crmException)
        {
            return false;
        }

        var status = crmException switch
        {
            CrmNotFoundException => StatusCodes.Status404NotFound,
            CrmForbiddenException => StatusCodes.Status403Forbidden,
            CrmConflictException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = crmException.Message },
        });
    }
}
