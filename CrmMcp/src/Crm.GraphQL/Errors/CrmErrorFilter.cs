using Crm.Core.Services;
using HotChocolate;
using HotChocolate.Execution;

namespace Crm.GraphQL.Errors;

/// <summary>
/// Бизнес-ошибки в запросах (query) превращает в понятное сообщение и код в extensions.code.
/// По умолчанию Hot Chocolate прячет текст исключения за «Unexpected Execution Error», а текст CrmException
/// безопасно показывать клиенту — так же, как это делают REST (ProblemDetails) и MCP (isError).
/// </summary>
public sealed class CrmErrorFilter : IErrorFilter
{
    public IError OnError(IError error)
    {
        if (error.Exception is not CrmException exception)
        {
            return error;
        }

        var code = exception switch
        {
            CrmNotFoundException => "NOT_FOUND",
            CrmForbiddenException => "FORBIDDEN",
            CrmConflictException => "CONFLICT",
            _ => "VALIDATION_FAILED",
        };

        // Ошибка ожидаемая: стек вызовов клиенту не нужен даже в режиме отладки
        return error
            .WithMessage(exception.Message)
            .WithCode(code)
            .WithException(null);
    }
}
