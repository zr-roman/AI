using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlAnalyst.Core.Output;

/// <summary>
/// Эвристика: похож ли текст из данных на попытку управлять моделью (prompt injection).
/// Детектор не обязан быть идеальным — данные и так приходят модели в «конверте» недоверенного текста,
/// а у агента нет инструментов, которыми можно навредить. Его задача — убрать самые явные атаки
/// из контекста и оставить след в аудите.
/// </summary>
public static partial class InjectionDetector
{
    private static readonly (Regex Pattern, string Label)[] Rules =
    [
        (IgnoreInstructionsEn(), "просьба игнорировать инструкции"),
        (IgnoreInstructionsRu(), "просьба игнорировать инструкции"),
        (RoleOverride(), "попытка сменить роль модели"),
        (FakeRoleMarker(), "поддельная реплика system/assistant"),
        (FakeEnvelope(), "попытка закрыть конверт данных"),
        (ToolInvocation(), "указание вызвать инструмент или выполнить SQL"),
        (SecretsRequest(), "запрос секретов или закрытых схем"),
        (AnswerDictation(), "диктует модели ответ"),
    ];

    public static string? Detect(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 12)
        {
            return null;
        }

        text = Normalize(text);

        foreach (var (pattern, label) in Rules)
        {
            if (pattern.IsMatch(text))
            {
                return label;
            }
        }

        return null;
    }

    /// <summary>
    /// Убирает приёмы обхода регулярок: невидимые символы между буквами («ig\u200Bnore»),
    /// полноширинные и стилизованные буквы (NFKC), лишние пробелы и подчёркивания вместо пробелов.
    /// </summary>
    internal static string Normalize(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsWhiteSpace(c) ? ' ' : c);
        }

        return MultiSpace().Replace(sb.ToString(), " ");
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();

    [GeneratedRegex(@"\b(ignore|disregard|forget|override)\b.{0,40}\b(previous|prior|above|earlier|all|system|your)\b.{0,30}\b(instructions?|rules|prompts?|guidelines|context)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex IgnoreInstructionsEn();

    [GeneratedRegex(@"(игнорируй|игнорировать|забудь|забыть|не\s+обращай\s+внимания|отмени)\w*.{0,40}(инструкци|правил|указани|промпт|ограничени)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex IgnoreInstructionsRu();

    [GeneratedRegex(@"\b(you\s+are\s+now|act\s+as|pretend\s+to\s+be|new\s+instructions|maintenance\s+mode|developer\s+mode|jailbreak)\b|ты\s+теперь|теперь\s+ты|новые\s+инструкции|режим\s+(разработчика|обслуживания)", RegexOptions.IgnoreCase)]
    private static partial Regex RoleOverride();

    [GeneratedRegex(@"(^|\n|\s)(system|assistant|developer|human|user)\s*:|<\|?(system|assistant|im_start|im_end)|\[/?(INST|SYS)\]", RegexOptions.IgnoreCase)]
    private static partial Regex FakeRoleMarker();

    [GeneratedRegex(@"</?\s*(query_result|untrusted[-_]data|data|tool_result|function_results|system-reminder)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FakeEnvelope();

    [GeneratedRegex(@"\b(run_query|sample_rows|describe_table|list_tables|render_chart)\b|\b(drop|truncate)\s+(table|schema|database|view)\b|\b(delete\s+from|insert\s+into|alter\s+table|grant\s+all)\b|выполни\s+(запрос|sql|команду)", RegexOptions.IgnoreCase)]
    private static partial Regex ToolInvocation();

    [GeneratedRegex(@"\binternal\s*\.\s*\w+|\b(show|reveal|print|dump|list|покажи|выведи|раскрой|перечисли)\b.{0,30}(api[_\s-]?keys?|passwords?|парол|секрет|secrets?|credentials?|ключ)", RegexOptions.IgnoreCase)]
    private static partial Regex SecretsRequest();

    [GeneratedRegex(@"\b(tell|answer|respond\s+to|say\s+to)\s+the\s+user\b|(скажи|сообщи|ответь)\s+(пользователю|что)", RegexOptions.IgnoreCase)]
    private static partial Regex AnswerDictation();
}
