namespace Crm.Core.Services;

/// <summary>
/// Бизнес-ошибка, текст которой безопасно показывать пользователю и LLM.
/// MCP-слой превращает её в результат инструмента с isError=true, REST — в ProblemDetails.
/// </summary>
public abstract class CrmException(string message) : Exception(message);

public sealed class CrmNotFoundException(string message) : CrmException(message);

public sealed class CrmValidationException(string message) : CrmException(message);

public sealed class CrmForbiddenException(string message) : CrmException(message);

public sealed class CrmConflictException(string message) : CrmException(message);
