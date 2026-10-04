using System.Text;

namespace SqlAnalyst.Core.Guard;

public enum SqlTokenKind
{
    /// <summary>Слово без кавычек: ключевое слово, идентификатор или имя функции. Value в нижнем регистре.</summary>
    Word,

    /// <summary>"Идентификатор в кавычках". Value без кавычек, регистр сохранён.</summary>
    QuotedIdentifier,

    /// <summary>Любой строковый литерал: '...', E'...', $tag$...$tag$, B'...', X'...'.</summary>
    String,

    Number,

    /// <summary>Позиционный параметр $1 — в разовых запросах не нужен и запрещён.</summary>
    Parameter,

    Punctuation,
    Operator,
}

public sealed record SqlToken(SqlTokenKind Kind, string Value, int Position)
{
    public bool IsWord(string word) => Kind == SqlTokenKind.Word && Value == word;

    public bool IsPunctuation(char c) => Kind == SqlTokenKind.Punctuation && Value.Length == 1 && Value[0] == c;

    /// <summary>Имя идентификатора так, как его понимает PostgreSQL: слова без кавычек приводятся к нижнему регистру.</summary>
    public string? IdentifierName => Kind is SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier ? Value : null;
}

public sealed class SqlTokenizeException(string message, int position) : Exception(message)
{
    public int Position { get; } = position;
}

/// <summary>
/// Лексер диалекта PostgreSQL ровно той точности, что нужна валидатору: строки, комментарии и идентификаторы
/// в кавычках не должны приниматься за ключевые слова, а ключевые слова — прятаться внутри них.
/// Синтаксическое дерево не строим: запрос всё равно разбирает сам PostgreSQL под read-only ролью.
/// </summary>
public static class SqlTokenizer
{
    public static IReadOnlyList<SqlToken> Tokenize(string sql)
    {
        var tokens = new List<SqlToken>();
        var i = 0;

        while (i < sql.Length)
        {
            var c = sql[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // -- строчный комментарий
            if (c == '-' && Peek(sql, i + 1) == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            // /* блочный комментарий */ — в PostgreSQL бывают вложенные
            if (c == '/' && Peek(sql, i + 1) == '*')
            {
                i = SkipBlockComment(sql, i);
                continue;
            }

            // U&'...' и U&"..." позволяют записать любой идентификатор escape-последовательностями: не декодируем, а запрещаем
            if ((c is 'u' or 'U') && Peek(sql, i + 1) == '&' && Peek(sql, i + 2) is '\'' or '"')
            {
                throw new SqlTokenizeException("Строки и идентификаторы с Unicode-экранированием (U&) запрещены.", i);
            }

            // Префиксы строк E'...', B'...', X'...', N'...'
            if ((c is 'e' or 'E' or 'b' or 'B' or 'x' or 'X' or 'n' or 'N') && Peek(sql, i + 1) == '\'')
            {
                var start = i;
                i = SkipQuoted(sql, i + 1, '\'', backslashEscapes: c is 'e' or 'E');
                tokens.Add(new SqlToken(SqlTokenKind.String, sql[start..i], start));
                continue;
            }

            if (c == '\'')
            {
                var start = i;
                i = SkipQuoted(sql, i, '\'', backslashEscapes: false);
                tokens.Add(new SqlToken(SqlTokenKind.String, sql[start..i], start));
                continue;
            }

            if (c == '"')
            {
                var start = i;
                i = SkipQuoted(sql, i, '"', backslashEscapes: false);
                var name = sql[(start + 1)..(i - 1)].Replace("\"\"", "\"", StringComparison.Ordinal);

                if (name.Length == 0)
                {
                    throw new SqlTokenizeException("Пустой идентификатор в кавычках.", start);
                }

                tokens.Add(new SqlToken(SqlTokenKind.QuotedIdentifier, name, start));
                continue;
            }

            if (c == '$')
            {
                var start = i;

                if (char.IsAsciiDigit(Peek(sql, i + 1)))
                {
                    i++;
                    while (i < sql.Length && char.IsAsciiDigit(sql[i]))
                    {
                        i++;
                    }

                    tokens.Add(new SqlToken(SqlTokenKind.Parameter, sql[start..i], start));
                    continue;
                }

                i = SkipDollarQuoted(sql, i);
                tokens.Add(new SqlToken(SqlTokenKind.String, sql[start..i], start));
                continue;
            }

            if (IsIdentifierStart(c))
            {
                var start = i;
                while (i < sql.Length && IsIdentifierPart(sql[i]))
                {
                    i++;
                }

                tokens.Add(new SqlToken(SqlTokenKind.Word, sql[start..i].ToLowerInvariant(), start));
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '.' && char.IsAsciiDigit(Peek(sql, i + 1))))
            {
                var start = i;
                while (i < sql.Length && (char.IsAsciiLetterOrDigit(sql[i]) || sql[i] is '.' or '_'
                    || (sql[i] is '+' or '-' && sql[i - 1] is 'e' or 'E')))
                {
                    i++;
                }

                tokens.Add(new SqlToken(SqlTokenKind.Number, sql[start..i], start));
                continue;
            }

            if (c is '(' or ')' or ',' or ';' or '.' or '[' or ']')
            {
                tokens.Add(new SqlToken(SqlTokenKind.Punctuation, c.ToString(), i));
                i++;
                continue;
            }

            if (IsOperatorChar(c))
            {
                var start = i;
                var op = new StringBuilder();

                // Оператор обрывается перед началом комментария — как в лексере PostgreSQL
                while (i < sql.Length && IsOperatorChar(sql[i])
                    && !(sql[i] == '-' && Peek(sql, i + 1) == '-')
                    && !(sql[i] == '/' && Peek(sql, i + 1) == '*'))
                {
                    op.Append(sql[i]);
                    i++;
                }

                tokens.Add(new SqlToken(SqlTokenKind.Operator, op.ToString(), start));
                continue;
            }

            if (c == ':')
            {
                // :: приведение типа или срез массива
                var start = i;
                i += Peek(sql, i + 1) == ':' ? 2 : 1;
                tokens.Add(new SqlToken(SqlTokenKind.Operator, sql[start..i], start));
                continue;
            }

            throw new SqlTokenizeException($"Неожиданный символ '{c}'.", i);
        }

        return tokens;
    }

    private static char Peek(string sql, int index) => index < sql.Length ? sql[index] : '\0';

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

    private static bool IsOperatorChar(char c) => "+-*/<>=~!@#%^&|`?".Contains(c);

    private static int SkipBlockComment(string sql, int i)
    {
        var start = i;
        var depth = 0;

        while (i < sql.Length)
        {
            if (sql[i] == '/' && Peek(sql, i + 1) == '*')
            {
                depth++;
                i += 2;
            }
            else if (sql[i] == '*' && Peek(sql, i + 1) == '/')
            {
                depth--;
                i += 2;
                if (depth == 0)
                {
                    return i;
                }
            }
            else
            {
                i++;
            }
        }

        throw new SqlTokenizeException("Незакрытый блочный комментарий.", start);
    }

    private static int SkipQuoted(string sql, int i, char quote, bool backslashEscapes)
    {
        var start = i;
        i++;

        while (i < sql.Length)
        {
            if (backslashEscapes && sql[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (sql[i] == quote)
            {
                // Удвоенная кавычка — экранированная кавычка
                if (Peek(sql, i + 1) == quote)
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        throw new SqlTokenizeException(quote == '"' ? "Незакрытый идентификатор в кавычках." : "Незакрытый строковый литерал.", start);
    }

    private static int SkipDollarQuoted(string sql, int i)
    {
        var start = i;
        var tagEnd = i + 1;

        while (tagEnd < sql.Length && sql[tagEnd] != '$')
        {
            if (!IsIdentifierPart(sql[tagEnd]) || sql[tagEnd] == '$')
            {
                throw new SqlTokenizeException("Неожиданный символ '$'.", start);
            }

            tagEnd++;
        }

        if (tagEnd >= sql.Length)
        {
            throw new SqlTokenizeException("Неожиданный символ '$'.", start);
        }

        var tag = sql[i..(tagEnd + 1)];
        var close = sql.IndexOf(tag, tagEnd + 1, StringComparison.Ordinal);

        if (close < 0)
        {
            throw new SqlTokenizeException("Незакрытая строка в долларовых кавычках.", start);
        }

        return close + tag.Length;
    }
}
