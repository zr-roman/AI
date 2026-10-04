namespace Crm.Core.Services;

internal static class Text
{
    /// <summary>Обязательная строка: обрезает пробелы и проверяет длину (лимиты совпадают с колонками БД).</summary>
    public static string Required(string? value, string fieldName, int maxLength) =>
        EnsureLength(Normalize(value) ?? throw new CrmValidationException($"Поле «{fieldName}» обязательно."), fieldName, maxLength);

    public static string? Optional(string? value, string fieldName, int maxLength) =>
        Normalize(value) is { } normalized ? EnsureLength(normalized, fieldName, maxLength) : null;

    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";

    /// <summary>Шаблон для ILIKE '%term%' с экранированием спецсимволов.</summary>
    public static string ContainsPattern(string term) =>
        "%" + term.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";

    public static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at < value.Length - 1 && !value.Contains(' ');
    }

    // Без проверки длинная строка упала бы в БД, и модель получила бы обезличенную ошибку вместо подсказки
    private static string EnsureLength(string value, string fieldName, int maxLength) =>
        value.Length <= maxLength
            ? value
            : throw new CrmValidationException($"Поле «{fieldName}» длиннее {maxLength} символов, сократи текст.");
}
