using SqlAnalyst.Core.Guard;

namespace SqlAnalyst.Tests.Guard;

public sealed class SqlTokenizerTests
{
    [Fact]
    public void Keywords_inside_strings_comments_and_quotes_are_not_words()
    {
        var tokens = SqlTokenizer.Tokenize("""
            SELECT 'drop', E'it\'s delete', $tag$ insert $tag$, "Update" -- delete
            /* outer /* nested delete */ still comment */ FROM t
            """);

        Assert.Equal(["select", "from", "t"], tokens.Where(t => t.Kind == SqlTokenKind.Word).Select(t => t.Value));
        Assert.Equal(3, tokens.Count(t => t.Kind == SqlTokenKind.String));
        Assert.Contains(tokens, t => t is { Kind: SqlTokenKind.QuotedIdentifier, Value: "Update" });
    }

    [Fact]
    public void Doubled_quotes_do_not_end_a_string()
    {
        var tokens = SqlTokenizer.Tokenize("SELECT 'it''s; drop table x' AS s");

        Assert.DoesNotContain(tokens, t => t.IsPunctuation(';'));
    }

    [Fact]
    public void Unquoted_words_are_folded_to_lower_case()
    {
        var tokens = SqlTokenizer.Tokenize("SeLeCt Customer_ID");

        Assert.Equal(["select", "customer_id"], tokens.Select(t => t.Value));
    }

    [Fact]
    public void Operators_stop_before_comments()
    {
        var tokens = SqlTokenizer.Tokenize("SELECT 1+-- comment\n2");

        Assert.Equal(["select", "1", "+", "2"], tokens.Select(t => t.Value));
    }

    [Theory]
    [InlineData("SELECT U&'\\0041'")]
    [InlineData("SELECT u&\"x\"")]
    public void Unicode_escapes_are_refused(string sql)
    {
        Assert.Throws<SqlTokenizeException>(() => SqlTokenizer.Tokenize(sql));
    }
}
