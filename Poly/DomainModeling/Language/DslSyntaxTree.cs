namespace Poly.DomainModeling.Language;

/// <summary>
/// The parse stage's output: the domain name, the leading <c>uses</c> ids, and
/// every token of the document. Reads no session or library table, so a caller
/// loads the listed libraries before <see cref="DomainResolver"/> reads their meaning.
/// </summary>
public sealed record DslSyntaxTree(string Name, IReadOnlyList<string> Uses, IReadOnlyList<DslToken> Tokens) {
    /// <summary>
    /// Tokenizes <paramref name="text"/> once and reads <c>domain &lt;Name&gt;</c>
    /// plus the leading <c>uses &lt;id&gt;*</c>. Throws on a duplicate id.
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
        var (name, uses) = ReadHeader(tokens);
        return new DslSyntaxTree(name, uses, tokens);
    }

    private static (string Name, IReadOnlyList<string> Uses) ReadHeader(IReadOnlyList<DslToken> tokens) {
        var uses = new List<string>();
        if (tokens.Count < 2 || tokens[0].Kind != DslTokenKind.Domain || tokens[1].Kind != DslTokenKind.Identifier)
            return ("", uses);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 2; i + 1 < tokens.Count && tokens[i].Kind == DslTokenKind.Uses; i += 2) {
            if (tokens[i + 1].Kind != DslTokenKind.Identifier)
                break;
            var id = tokens[i + 1].Text;
            if (!seen.Add(id))
                throw new FormatException($"Domain lists extension '{id}' more than once.");
            uses.Add(id);
        }
        return (tokens[1].Text, uses);
    }
}
