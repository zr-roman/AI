namespace SqlAnalyst.Agent;

/// <summary>
/// Последний рубеж — проверка готового ответа перед показом пользователю. Ловит то, что не должно
/// попасть в ответ ни при каком раскладе: canary-метки из закрытых таблиц и пересказ системных инструкций.
/// </summary>
internal static class OutputGuard
{
    public static string Check(string answer, string? serverInstructions, IReadOnlyList<string> canaries)
    {
        if (canaries.Any(c => answer.Contains(c, StringComparison.OrdinalIgnoreCase)))
        {
            return "[Ответ заблокирован: в нём оказались данные из закрытой части базы. Сообщите администратору.]";
        }

        // Длинный дословный кусок инструкций сервера в ответе — признак утечки промпта
        if (serverInstructions is { Length: > 0 }
            && serverInstructions.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Length >= 60)
                .Any(line => answer.Contains(line, StringComparison.Ordinal)))
        {
            return "[Ответ заблокирован: он пересказывает служебные инструкции.]";
        }

        return answer;
    }
}
