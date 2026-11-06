using Poly.DomainModeling.Language;

namespace Poly.Mcp.Surface;

/// <summary>
/// One member of a .poly document located by source span. Offsets are into the document text
/// the outline was built from; <see cref="EndOffset"/> is exclusive.
/// </summary>
internal sealed record OutlineNode(
    string Kind,
    string Name,
    string Path,
    int StartOffset,
    int EndOffset,
    int StartLine,
    int EndLine,
    string Indent,
    bool Addressable,
    int OpenBrace,
    int CloseBrace,
    IReadOnlyList<OutlineNode> Children) {
    /// <summary>Set when this member shares a source line with a sibling, so a span edit would touch the neighbour.</summary>
    public int SharedLine { get; init; }
}

/// <summary>
/// Token-driven outline of a .poly document: where each top-level declaration, entity member
/// and stage action starts and ends. It reads the tokenizer's source spans, so comments and
/// formatting stay exactly as the author wrote them. Containers are the document, entities and
/// stages; action, policy, enum, value and contract bodies are opaque.
/// </summary>
internal sealed class DslOutline {
    private DslOutline(IReadOnlyList<OutlineNode> roots) => Roots = roots;

    public IReadOnlyList<OutlineNode> Roots { get; }

    public static DslOutline Build(string text) {
        var tokens = new List<DslToken>();
        var reader = new DslTokenReader(text);
        while (true) {
            var token = reader.Peek();
            if (token.Kind == DslTokenKind.EndOfFile)
                break;
            tokens.Add(token);
            reader.Consume(1);
        }

        var builder = new Builder(text, tokens);
        return new DslOutline(builder.BuildDocument());
    }

    /// <summary>Every node, depth first.</summary>
    public IEnumerable<OutlineNode> All() {
        var stack = new Stack<OutlineNode>(Roots.Reverse());
        while (stack.Count > 0) {
            var node = stack.Pop();
            yield return node;
            foreach (var child in node.Children.Reverse())
                stack.Push(child);
        }
    }

    /// <summary>
    /// Finds an addressable member by dotted path. Null with an error code and message when the
    /// path does not exist, names a member that cannot be targeted, or shares its line.
    /// </summary>
    public OutlineNode? Find(string path, out string? errorCode, out string? error) {
        errorCode = null;
        error = null;
        var node = All().FirstOrDefault(n => string.Equals(n.Path, path, StringComparison.Ordinal));
        if (node is null) {
            errorCode = "EDIT_PATH_NOT_FOUND";
            error = $"No member '{path}'." + NearMisses(path);
            return null;
        }

        if (!node.Addressable) {
            errorCode = "EDIT_NOT_ADDRESSABLE";
            error = $"'{path}' cannot be edited by path.";
            return null;
        }

        if (node.SharedLine > 0) {
            errorCode = "EDIT_NOT_ADDRESSABLE";
            error = $"'{path}' shares line {node.SharedLine} with another member; put each member on its own line first.";
            return null;
        }

        return node;
    }

    public OutlineNode? FindContainer(string path) =>
        All().FirstOrDefault(n => string.Equals(n.Path, path, StringComparison.Ordinal) && n.OpenBrace >= 0);

    public string NearMisses(string path) {
        var wanted = path.Split('.')[^1];
        var near = All()
            .Where(n => n.Addressable && n.Path.Length > 0
                && (n.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                    || n.Path.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                    || wanted.Contains(n.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(n => n.Path)
            .Take(5)
            .ToList();
        if (near.Count == 0)
            near = All().Where(n => n.Addressable).Select(n => n.Path).Take(8).ToList();
        return near.Count == 0 ? "" : $" Did you mean: {string.Join(", ", near)}?";
    }

    private sealed class Builder(string text, List<DslToken> tokens) {
        private readonly int[] _match = MatchBraces(tokens);
        private readonly HashSet<string> _topKinds = ["entity", "enum", "value", "contract", "bind"];

        public IReadOnlyList<OutlineNode> BuildDocument() {
            var nodes = new List<OutlineNode>();
            var i = 0;
            while (i < tokens.Count) {
                var t = tokens[i];
                if (t.Kind == DslTokenKind.Domain && i + 1 < tokens.Count) {
                    nodes.Add(Make("domain", tokens[i + 1].Text, "$domain", i, i + 1, false, -1, -1, []));
                    i += 2;
                    continue;
                }

                if (t.Kind == DslTokenKind.Uses && i + 1 < tokens.Count) {
                    nodes.Add(Make("uses", tokens[i + 1].Text, "uses:" + tokens[i + 1].Text, i, i + 1, false, -1, -1, []));
                    i += 2;
                    continue;
                }

                if (IsNameColon(i)) {
                    i = TopLevel(i, nodes);
                    continue;
                }

                i++;
            }

            MarkSharedLines(nodes);
            return nodes;
        }

        private int TopLevel(int start, List<OutlineNode> nodes) {
            var name = tokens[start].Text;
            var word = start + 2 < tokens.Count ? tokens[start + 2].Text : "";
            var kind = _topKinds.Contains(word) ? word : "unknown";
            var end = start + 1;
            var open = -1;
            var close = -1;
            var j = start + 2;
            var paren = 0;
            for (; j < tokens.Count; j++) {
                var t = tokens[j];
                if (t.Kind == DslTokenKind.LParen) paren++;
                else if (t.Kind == DslTokenKind.RParen) paren--;
                else if (paren == 0 && t.Kind == DslTokenKind.LBrace) {
                    open = j;
                    close = _match[j] >= 0 ? _match[j] : tokens.Count - 1;
                    end = close;
                    j = close + 1;
                    break;
                }
                else if (paren == 0 && IsNameColon(j))
                    break;
                end = j;
            }

            var children = open >= 0 && kind is "entity" ? Members(open, close, name) : [];
            nodes.Add(Make(kind, name, name, start, end, true, open, close, children));
            return j;
        }

        private List<OutlineNode> Members(int open, int close, string parentPath) {
            var members = new List<OutlineNode>();
            int? currentStart = null;
            var currentKind = "";
            var currentName = "";
            var currentEnd = 0;
            var currentOpen = -1;
            var currentClose = -1;
            var currentBlockSeen = false;
            var paren = 0;

            void Flush() {
                if (currentStart is not { } s)
                    return;
                var named = currentKind is not "unnamed";
                var path = $"{parentPath}.{currentName}";
                var children = currentKind == "stage" && currentOpen >= 0
                    ? Members(currentOpen, currentClose, path)
                    : [];
                members.Add(Make(currentKind, currentName, path, s, currentEnd, named, currentOpen, currentClose, children));
                currentStart = null;
                currentOpen = -1;
                currentClose = -1;
                currentBlockSeen = false;
            }

            var i = open + 1;
            while (i < close) {
                var t = tokens[i];
                if (t.Kind == DslTokenKind.LParen) { paren++; Extend(i); i++; continue; }
                if (t.Kind == DslTokenKind.RParen) { paren--; Extend(i); i++; continue; }
                if (paren > 0) { Extend(i); i++; continue; }

                if (t.Kind == DslTokenKind.LBrace) {
                    var m = _match[i] >= 0 ? _match[i] : close;
                    if (currentStart is not null && !currentBlockSeen) {
                        currentBlockSeen = true;
                        currentOpen = i;
                        currentClose = m;
                    }
                    currentEnd = Math.Max(currentEnd, m);
                    i = m + 1;
                    continue;
                }

                if (TryMemberStart(i, currentKind, currentStart is not null && currentKind is "action" or "policy" or "stage" && !currentBlockSeen,
                        out var kind, out var name, out var skip)) {
                    Flush();
                    currentStart = i;
                    currentKind = kind;
                    currentName = name;
                    currentEnd = i + skip - 1;
                    currentBlockSeen = false;
                    i += skip;
                    continue;
                }

                Extend(i);
                i++;
            }

            Flush();
            return members;

            void Extend(int idx) {
                if (currentStart is not null)
                    currentEnd = Math.Max(currentEnd, idx);
            }
        }

        private bool TryMemberStart(int i, string currentKind, bool inHeader, out string kind, out string name, out int skip) {
            kind = "";
            name = "";
            skip = 0;
            var t = tokens[i];

            if (!inHeader && t.Kind is DslTokenKind.When or DslTokenKind.Require or DslTokenKind.Entry or DslTokenKind.Exit) {
                kind = "unnamed";
                name = t.Text;
                skip = 1;
                return true;
            }

            var isPrimitiveName = t.Kind is DslTokenKind.Text or DslTokenKind.NumberType or DslTokenKind.BooleanType;
            if ((t.Kind == DslTokenKind.Identifier || isPrimitiveName)
                && i + 1 < tokens.Count && tokens[i + 1].Kind == DslTokenKind.Colon) {
                name = t.Text;
                skip = 2;
                var third = i + 2 < tokens.Count ? tokens[i + 2] : default;
                kind = third.Kind switch {
                    DslTokenKind.Stage => "stage",
                    DslTokenKind.Action or DslTokenKind.When or DslTokenKind.Require or DslTokenKind.LBrace => "action",
                    DslTokenKind.Policy => "policy",
                    DslTokenKind.Many or DslTokenKind.One or DslTokenKind.Owned => "navigation",
                    _ => "property",
                };
                return true;
            }

            if (t.Kind == DslTokenKind.Identifier && i + 1 < tokens.Count && tokens[i + 1].Kind == DslTokenKind.LParen
                && !LooksLikeAnnotationCall(i)) {
                var depth = 0;
                for (var j = i + 1; j < tokens.Count; j++) {
                    if (tokens[j].Kind == DslTokenKind.LParen) depth++;
                    else if (tokens[j].Kind == DslTokenKind.RParen && --depth == 0) {
                        if (j + 1 < tokens.Count && tokens[j + 1].Kind == DslTokenKind.Colon) {
                            kind = "action";
                            name = t.Text;
                            skip = 1;
                            return true;
                        }
                        break;
                    }
                }
            }

            return false;
        }

        private bool LooksLikeAnnotationCall(int i) {
            if (i + 2 >= tokens.Count)
                return true;
            return tokens[i + 2].Kind is DslTokenKind.StringLiteral or DslTokenKind.Number
                or DslTokenKind.True or DslTokenKind.False or DslTokenKind.Null or DslTokenKind.RParen;
        }

        private bool IsNameColon(int i) =>
            tokens[i].Kind == DslTokenKind.Identifier
            && i + 1 < tokens.Count && tokens[i + 1].Kind == DslTokenKind.Colon;

        private OutlineNode Make(
            string kind, string name, string path, int firstToken, int lastToken,
            bool addressable, int open, int close, IReadOnlyList<OutlineNode> children) {
            var first = tokens[firstToken];
            var last = tokens[Math.Min(lastToken, tokens.Count - 1)];
            var end = last.Offset + last.Length;
            var lineEnd = last.Line;
            for (var k = last.Offset; k < end; k++)
                if (text[k] == '\n') lineEnd++;
            var lineStart = first.Offset;
            while (lineStart > 0 && text[lineStart - 1] is not ('\n' or '\r'))
                lineStart--;
            var indentEnd = lineStart;
            while (indentEnd < text.Length && text[indentEnd] is ' ' or '\t')
                indentEnd++;
            return new OutlineNode(
                kind, name, path, first.Offset, end, first.Line, lineEnd,
                text[lineStart..indentEnd], addressable,
                open >= 0 ? tokens[open].Offset : -1,
                close >= 0 ? tokens[close].Offset : -1,
                children);
        }

        private static IReadOnlyList<OutlineNode> MarkLines(IReadOnlyList<OutlineNode> siblings) {
            var result = new List<OutlineNode>(siblings.Count);
            for (var i = 0; i < siblings.Count; i++) {
                var node = siblings[i];
                var shared = 0;
                if (i > 0 && siblings[i - 1].EndLine == node.StartLine)
                    shared = node.StartLine;
                else if (i + 1 < siblings.Count && siblings[i + 1].StartLine == node.EndLine)
                    shared = node.EndLine;
                result.Add((node with { Children = MarkLines(node.Children) }) with { SharedLine = shared });
            }
            return result;
        }

        private void MarkSharedLines(List<OutlineNode> nodes) {
            var marked = MarkLines(nodes);
            nodes.Clear();
            nodes.AddRange(marked);
        }

        private static int[] MatchBraces(List<DslToken> tokens) {
            var match = new int[tokens.Count];
            Array.Fill(match, -1);
            var stack = new Stack<int>();
            for (var i = 0; i < tokens.Count; i++) {
                if (tokens[i].Kind == DslTokenKind.LBrace) stack.Push(i);
                else if (tokens[i].Kind == DslTokenKind.RBrace && stack.Count > 0) {
                    var open = stack.Pop();
                    match[open] = i;
                    match[i] = open;
                }
            }
            return match;
        }
    }
}