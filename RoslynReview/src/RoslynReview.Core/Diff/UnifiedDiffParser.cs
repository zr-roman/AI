using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RoslynReview.Core.Diff;

/// <summary>Parses unified diffs as produced by <c>git diff</c> (plain <c>diff -u</c> output works too).</summary>
public static partial class UnifiedDiffParser
{
    public static IReadOnlyList<FileDiff> Parse(string diff)
    {
        ArgumentNullException.ThrowIfNull(diff);

        var files = new List<FileDiff>();
        FileBuilder? file = null;
        var oldRemaining = 0;
        var newRemaining = 0;
        var newLine = 0;

        foreach (var rawLine in diff.Split('\n'))
        {
            var line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;

            if (file is not null && (oldRemaining > 0 || newRemaining > 0))
            {
                if (line.StartsWith('\\'))
                {
                    continue; // "\ No newline at end of file"
                }

                // Some tools strip the leading space of empty context lines.
                var marker = line.Length == 0 ? ' ' : line[0];
                var content = line.Length == 0 ? string.Empty : line[1..];
                if (marker is '+' or '-' or ' ')
                {
                    switch (marker)
                    {
                        case '+':
                            file.AddLine(newLine++, content);
                            newRemaining--;
                            break;
                        case '-':
                            file.RemoveLine(afterLine: newLine - 1, content);
                            oldRemaining--;
                            break;
                        default:
                            file.EndDeletion();
                            newLine++;
                            oldRemaining--;
                            newRemaining--;
                            break;
                    }

                    if (oldRemaining <= 0 && newRemaining <= 0)
                    {
                        file.EndDeletion();
                        oldRemaining = newRemaining = 0;
                    }

                    continue;
                }

                // Truncated or malformed hunk: treat this line as a header.
                file.EndDeletion();
                oldRemaining = newRemaining = 0;
            }

            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                Finish();
                file = new FileBuilder();
                file.ReadGitHeader(line["diff --git ".Length..]);
            }
            else if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                if (file is null || file.HasHunks)
                {
                    Finish();
                    file = new FileBuilder();
                }

                file.SetOldPath(ParsePath(line[4..], "a/"));
            }
            else if (file is not null && line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                file.SetNewPath(ParsePath(line[4..], "b/"));
            }
            else if (file is not null && HunkHeader().Match(line) is { Success: true } hunk)
            {
                file.HasHunks = true;
                oldRemaining = Count(hunk.Groups["oldCount"]);
                newRemaining = Count(hunk.Groups["newCount"]);
                var newStart = int.Parse(hunk.Groups["newStart"].ValueSpan, CultureInfo.InvariantCulture);

                // An empty range names the line before the hunk: "@@ -5 +4,0 @@" removes lines after new line 4.
                newLine = newRemaining == 0 ? newStart + 1 : newStart;
            }
            else
            {
                file?.ReadExtendedHeader(line);
            }
        }

        Finish();
        return files;

        void Finish()
        {
            if (file is not null)
            {
                files.Add(file.Build());
            }

            file = null;
            oldRemaining = newRemaining = 0;
        }
    }

    [GeneratedRegex(@"^@@ -(?<oldStart>\d+)(?:,(?<oldCount>\d+))? \+(?<newStart>\d+)(?:,(?<newCount>\d+))? @@")]
    private static partial Regex HunkHeader();

    private static int Count(Group group) =>
        group.Success ? int.Parse(group.ValueSpan, CultureInfo.InvariantCulture) : 1;

    private static string? ParsePath(string value, string? prefix)
    {
        var path = value;
        var tab = path.IndexOf('\t', StringComparison.Ordinal);
        if (tab >= 0)
        {
            path = path[..tab]; // "diff -u" appends a timestamp after a tab
        }

        if (path.StartsWith('"'))
        {
            path = Unquote(path);
        }

        if (path == "/dev/null")
        {
            return null;
        }

        return prefix is not null && path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;
    }

    // git quotes paths with unusual characters C-style and escapes non-ASCII bytes as octal: "b/caf\303\251.cs".
    private static string Unquote(string quoted)
    {
        var end = quoted.Length > 1 && quoted[^1] == '"' ? quoted.Length - 1 : quoted.Length;
        var body = quoted[1..end];
        var bytes = new List<byte>(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c != '\\' || i + 1 >= body.Length)
            {
                bytes.AddRange(Encoding.UTF8.GetBytes([c]));
                continue;
            }

            var next = body[++i];
            if (next is >= '0' and <= '7' && i + 2 < body.Length)
            {
                bytes.Add(Convert.ToByte(body.Substring(i, 3), 8));
                i += 2;
                continue;
            }

            bytes.Add(next switch
            {
                'n' => (byte)'\n',
                't' => (byte)'\t',
                'r' => (byte)'\r',
                _ => (byte)next,
            });
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }

    private static int FindClosingQuote(string value)
    {
        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] == '\\')
            {
                i++;
            }
            else if (value[i] == '"')
            {
                return i;
            }
        }

        return value.Length - 1;
    }

    private sealed class FileBuilder
    {
        private readonly List<DiffLine> _added = [];
        private readonly List<DiffDeletion> _deletions = [];
        private List<string>? _pendingDeletion;
        private int _pendingAfterLine;
        private string? _oldPath;
        private string? _newPath;
        private bool _oldIsDevNull;
        private bool _newIsDevNull;
        private bool _created;
        private bool _deleted;
        private bool _renamed;
        private bool _binary;

        public bool HasHunks { get; set; }

        public void SetOldPath(string? path)
        {
            _oldIsDevNull = path is null;
            _oldPath = path ?? _oldPath;
        }

        public void SetNewPath(string? path)
        {
            _newIsDevNull = path is null;
            _newPath = path ?? _newPath;
        }

        // "a/old b/new" is ambiguous when paths contain " b/", so prefer the split where both sides match.
        public void ReadGitHeader(string paths)
        {
            string? oldPart = null;
            string? newPart = null;
            if (paths.StartsWith('"'))
            {
                var end = FindClosingQuote(paths);
                oldPart = paths[..(end + 1)];
                newPart = paths[(end + 1)..].TrimStart();
            }
            else
            {
                var middle = paths.Length / 2;
                if (paths.Length % 2 == 1 && paths.Length > 5 && paths[middle] == ' ' &&
                    paths[2..middle] == paths[(middle + 3)..])
                {
                    oldPart = paths[..middle];
                    newPart = paths[(middle + 1)..];
                }
                else if (paths.IndexOf(" b/", StringComparison.Ordinal) is var split and > 0)
                {
                    oldPart = paths[..split];
                    newPart = paths[(split + 1)..];
                }
            }

            _oldPath = oldPart is null ? null : ParsePath(oldPart, "a/");
            _newPath = newPart is null ? null : ParsePath(newPart, "b/");
        }

        public void ReadExtendedHeader(string line)
        {
            if (line.StartsWith("new file mode", StringComparison.Ordinal))
            {
                _created = true;
            }
            else if (line.StartsWith("deleted file mode", StringComparison.Ordinal))
            {
                _deleted = true;
            }
            else if (line.StartsWith("rename from ", StringComparison.Ordinal))
            {
                _oldPath = ParsePath(line["rename from ".Length..], prefix: null);
                _renamed = true;
            }
            else if (line.StartsWith("rename to ", StringComparison.Ordinal))
            {
                _newPath = ParsePath(line["rename to ".Length..], prefix: null);
                _renamed = true;
            }
            else if (line.StartsWith("Binary files ", StringComparison.Ordinal) ||
                     line.StartsWith("GIT binary patch", StringComparison.Ordinal))
            {
                _binary = true;
            }
        }

        public void AddLine(int number, string text)
        {
            // Removed lines followed by added lines are a replacement; the added lines describe the change.
            _pendingDeletion = null;
            _added.Add(new DiffLine(number, text));
        }

        public void RemoveLine(int afterLine, string text)
        {
            if (_pendingDeletion is null)
            {
                _pendingDeletion = [];
                _pendingAfterLine = Math.Max(0, afterLine);
            }

            _pendingDeletion.Add(text);
        }

        public void EndDeletion()
        {
            if (_pendingDeletion is not null)
            {
                _deletions.Add(new DiffDeletion(_pendingAfterLine, _pendingDeletion));
                _pendingDeletion = null;
            }
        }

        public FileDiff Build()
        {
            EndDeletion();
            var kind =
                _created || _oldIsDevNull ? FileChangeKind.Added :
                _deleted || _newIsDevNull ? FileChangeKind.Deleted :
                _renamed && _oldPath != _newPath ? FileChangeKind.Renamed :
                FileChangeKind.Modified;

            return new FileDiff(
                kind == FileChangeKind.Added ? null : _oldPath,
                kind == FileChangeKind.Deleted ? null : _newPath,
                kind,
                _binary,
                _added,
                _deletions);
        }
    }
}
