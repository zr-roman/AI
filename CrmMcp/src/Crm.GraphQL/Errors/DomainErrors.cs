using Crm.Core.Services;

namespace Crm.GraphQL.Errors;

// Ошибки бизнес-логики в payload мутаций: mutation conventions + [Error<T>].
// Конструктор, принимающий исключение, Hot Chocolate использует как фабрику: исключение именно этого типа
// перехватывается и превращается в объект в поле errors, а клиент разбирает его через `... on NotFoundError`.
// Остальные исключения остаются обычными GraphQL-ошибками (см. CrmErrorFilter).

public sealed class ValidationError(CrmValidationException exception)
{
    public string Message { get; } = exception.Message;
}

public sealed class NotFoundError(CrmNotFoundException exception)
{
    public string Message { get; } = exception.Message;
}

public sealed class ForbiddenError(CrmForbiddenException exception)
{
    public string Message { get; } = exception.Message;
}

public sealed class ConflictError(CrmConflictException exception)
{
    public string Message { get; } = exception.Message;
}
