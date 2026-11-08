using System.Text;

namespace Poly.DomainModeling.Language;

/// <summary>
/// The parse stage's output: the domain name, the leading <c>uses</c> ids and their
/// optional surface pins, and every token of the document. Reads no session or library
/// table, so a caller loads the listed libraries before <see cref="DomainResolver"/> reads
/// their meaning.
/// </summary>
public sealed record DslSyntaxTree(string Name, IReadOnlyList<string> Uses, IReadOnlyList<DslToken> Tokens) {
    /// <summary>
    /// Surface version pin for each entry of <see cref="Uses"/>, index-aligned; <c>null</c>
    /// when the line wrote none. A pin is the contiguous same-line token run after the id,
    /// so <c>2024-11-20</c>, <c>v1</c> and <c>1.2.3</c> each read as one value.
    /// </summary>
    public IReadOnlyList<string?> UsesVersions { get; init; } = [];

    /// <summary>Index of the first token after the header's <c>uses</c> lines.</summary>
    public int HeaderEnd { get; init; } = 2;

    /// <summary>
    /// Tokenizes <paramref name="text"/> once and reads <c>domain &lt;Name&gt;</c>
    /// plus the leading <c>uses &lt;id&gt; [version]*</c>. Throws on a duplicate id.
    /// </summary>
    public static DslSyntaxTree Parse(string text) {
        ArgumentNullException.ThrowIfNull(text);
        var reader = new DslTokenReader(text);
        var tokens = new List<DslToken>();
        while (true) {
            var token = reader.Peek();
            tokens.Add(token);
            if (token.Kind == DslTokenKind.EndOfFile)
                break;
            reader.Consume(1);
        }
        var (name, uses, versions, headerEnd) = ReadHeader(tokens);
        return new DslSyntaxTree(name, uses, tokens) {
            UsesVersions = versions,
            HeaderEnd = headerEnd,
        };
    }

    /// <summary>
    /// Reads one <c>uses</c> line starting at <paramref name="usesIndex"/>. Returns false
    /// when that token is not a <c>uses</c> followed by an identifier. <paramref name="nextIndex"/>
    /// is the first token after the line (the id when the shape is not a use).
    /// </summary>
    public static bool TryReadUse(
        IReadOnlyList<DslToken> tokens, int usesIndex, out string id, out string? version, out int nextIndex) {
        ArgumentNullException.ThrowIfNull(tokens);
        id = "";
        version = null;
        nextIndex = usesIndex;
        if (usesIndex < 0 || usesIndex + 1 >= tokens.Count || tokens[usesIndex].Kind != DslTokenKind.Uses)
            return false;

        var idToken = tokens[usesIndex + 1];
        if (idToken.Kind != DslTokenKind.Identifier)
            return false;

        id = idToken.Text;
        var i = usesIndex + 2;
        nextIndex = i;
        if (i >= tokens.Count || tokens[i].Line != idToken.Line || !IsVersionToken(tokens[i].Kind))
            return true;

        var text = new StringBuilder(tokens[i].Text);
        var end = tokens[i].Offset + tokens[i].Length;
        i++;
        while (i < tokens.Count
            && tokens[i].Line == idToken.Line
            && tokens[i].Offset == end
            && IsVersionToken(tokens[i].Kind)) {
            text.Append(tokens[i].Text);
            end = tokens[i].Offset + tokens[i].Length;
            i++;
        }
        version = text.ToString();
        nextIndex = i;
        return true;
    }

    private static bool IsVersionToken(DslTokenKind kind) => kind is
        DslTokenKind.Identifier or DslTokenKind.Number or DslTokenKind.StringLiteral
        or DslTokenKind.Minus or DslTokenKind.Dot;

    private static (string Name, IReadOnlyList<string> Uses, IReadOnlyList<string?> Versions, int HeaderEnd) ReadHeader(
        IReadOnlyList<DslToken> tokens) {
        var uses = new List<string>();
        var versions = new List<string?>();
        if (tokens.Count < 2 || tokens[0].Kind != DslTokenKind.Domain || tokens[1].Kind != DslTokenKind.Identifier)
            return ("", uses, versions, 2);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var i = 2;
        while (TryReadUse(tokens, i, out var id, out var version, out var next)) {
            if (!seen.Add(id))
                throw new FormatException($"Domain lists extension '{id}' more than once.");
            uses.Add(id);
            versions.Add(version);
            i = next;
        }
        return (tokens[1].Text, uses, versions, i);
    }
}