namespace Poly.DomainModeling.Language;

/// <summary>
/// One member of a .poly document located by source span. Offsets are into the document text
/// the outline was built from; <see cref="EndOffset"/> is exclusive.
/// </summary>
public sealed record OutlineNode(
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

/// <summary>Why a <c>dsl</c> fragment cannot stand in for exactly one member.</summary>
internal enum SnippetFaultKind { Empty, UnexpectedClose, Unclosed, Multiple, BadToken }

/// <summary>Where a fragment fails, positioned relative to the fragment (1-based).</summary>
internal sealed record SnippetFault(SnippetFaultKind Kind, int Line, int Col, string? Detail = null);

/// <summary>How many declarations a fragment holds, and the fault that makes it unusable.</summary>
internal sealed record SnippetShape(int Declarations, SnippetFault? Fault);

/// <summary>
/// Token-driven outline of a .poly document: where each top-level declaration, entity member
/// and stage action starts and ends. It reads the tokenizer's source spans, so comments and
/// formatting stay exactly as the author wrote them. Containers are the document, entities and
/// stages; action, policy, enum, value and contract bodies are opaque.
/// </summary>
public sealed class DslOutline {
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

    /// <summary>
    /// Checks that an edit fragment is exactly one balanced declaration. A member fragment must
    /// start at a member (or, with <paramref name="topLevel"/>, at a top-level declaration); the
    /// returned positions are relative to the fragment, so they are never document lines.
    /// </summary>
    internal static SnippetShape Inspect(string fragment, bool topLevel) {
        var tokens = new List<DslToken>();
        try {
            var reader = new DslTokenReader(fragment);
            while (true) {
                var token = reader.Peek();
                if (token.Kind == DslTokenKind.EndOfFile)
                    break;
                tokens.Add(token);
                reader.Consume(1);
            }
        }
        catch (FormatException ex) {
            return new SnippetShape(0, new SnippetFault(SnippetFaultKind.BadToken, 0, 0, ex.Message));
        }

        return new Builder(fragment, tokens).InspectFragment(topLevel);
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
                    nodes.Add(Make("domain", Label(tokens[i + 1]), "$domain", i, i + 1, false, -1, -1, []));
                    i += 2;
                    continue;
                }

                if (t.Kind == DslTokenKind.Uses
                    && DslSyntaxTree.TryReadUse(tokens, i, out var usesId, out _, out var usesEnd)) {
                    // The span covers the version tokens too, so a whole-line edit of a versioned
                    // uses line leaves no ` v1` remnant behind.
                    nodes.Add(Make("uses", usesId, "uses:" + usesId, i, usesEnd - 1, true, -1, -1, []));
                    i = usesEnd;
                    continue;
                }

                if (t.Kind == DslTokenKind.Public && i + 2 < tokens.Count
                    && (tokens[i + 1].Kind == DslTokenKind.Entity || DslGrammar.IsValueWord(tokens[i + 1]))) {
                    i = MarkedTop(i, nodes);
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

        /// <summary>
        /// One balanced declaration, with positions relative to the fragment. The fault with the
        /// earliest offset wins, so a stray <c>'}'</c> is reported before a later second
        /// declaration, and a fragment that starts inside a block is empty rather than a member.
        /// </summary>
        public SnippetShape InspectFragment(bool topLevel) {
            var unmatchedClose = -1;
            var open = new Stack<int>();
            for (var k = 0; k < tokens.Count; k++) {
                if (tokens[k].Kind == DslTokenKind.LBrace)
                    open.Push(k);
                else if (tokens[k].Kind == DslTokenKind.RBrace) {
                    if (open.Count == 0) {
                        unmatchedClose = k;
                        break;
                    }
                    open.Pop();
                }
            }

            var starts = ScanDeclarations(topLevel);
            var faults = new List<(int Offset, SnippetFault Fault)>();
            if (unmatchedClose >= 0)
                faults.Add((tokens[unmatchedClose].Start, Fault(SnippetFaultKind.UnexpectedClose, unmatchedClose)));
            if (open.Count > 0) {
                var unclosed = open.Min();
                faults.Add((tokens[unclosed].Start, Fault(SnippetFaultKind.Unclosed, unclosed)));
            }
            if (starts.Count > 1)
                faults.Add((tokens[starts[1]].Start, Fault(SnippetFaultKind.Multiple, starts[1])));

            if (faults.Count > 0)
                return new SnippetShape(starts.Count, faults.MinBy(f => f.Offset).Fault);

            if (starts.Count == 0 || starts[0] != 0)
                return new SnippetShape(starts.Count, new SnippetFault(SnippetFaultKind.Empty, 0, 0));

            return new SnippetShape(starts.Count, null);

            SnippetFault Fault(SnippetFaultKind kind, int index) =>
                new(kind, tokens[index].Line, tokens[index].Col);
        }

        /// <summary>
        /// The token index of each declaration start. A header <c>require</c>/<c>when</c> extends
        /// the declaration it follows (as in <see cref="Members"/>), and a matched brace block is
        /// skipped wholesale so a body never counts as a second declaration.
        /// </summary>
        private List<int> ScanDeclarations(bool topLevel) {
            var starts = new List<int>();
            int? currentStart = null;
            var currentKind = "";
            var blockSeen = false;
            var paren = 0;
            var i = 0;
            while (i < tokens.Count) {
                var t = tokens[i];

                // Inside a parameter list nothing can start a declaration: `(book: Book)` is
                // not a member, and its colon would otherwise read as one.
                if (t.Kind == DslTokenKind.LParen) {
                    paren++;
                    i++;
                    continue;
                }

                if (t.Kind == DslTokenKind.RParen) {
                    if (paren > 0) paren--;
                    i++;
                    continue;
                }

                if (paren > 0) {
                    i++;
                    continue;
                }

                if (t.Kind == DslTokenKind.LBrace && _match[i] >= 0) {
                    blockSeen = true;
                    i = _match[i] + 1;
                    continue;
                }

                var inHeader = currentStart is not null
                    && currentKind is "action" or "policy" or "stage" && !blockSeen;

                string kind;
                int skip;
                var start = topLevel
                    ? TryTopLevelStart(i, out kind, out skip)
                    : TryMemberStart(i, currentKind, inHeader, out kind, out _, out skip);

                if (start) {
                    starts.Add(i);
                    currentStart = i;
                    currentKind = kind;
                    blockSeen = false;
                    paren = 0;
                    i += skip;
                    continue;
                }

                i++;
            }

            return starts;
        }

        private bool TryTopLevelStart(int i, out string kind, out int skip) {
            kind = "";
            skip = 0;
            var t = tokens[i];
            if (t.Kind == DslTokenKind.Domain && i + 1 < tokens.Count) {
                kind = "domain";
                skip = 2;
                return true;
            }

            if (t.Kind == DslTokenKind.Uses
                && DslSyntaxTree.TryReadUse(tokens, i, out _, out _, out var usesEnd)) {
                kind = "uses";
                skip = usesEnd - i;
                return true;
            }

            if (t.Kind == DslTokenKind.Public && i + 2 < tokens.Count
                && (tokens[i + 1].Kind == DslTokenKind.Entity || DslGrammar.IsValueWord(tokens[i + 1]))) {
                kind = tokens[i + 1].Kind == DslTokenKind.Entity ? "entity" : "value";
                skip = 3;
                return true;
            }

            // A top-level `function` declaration starts with the word and is not the legacy
            // `function: entity` name form (mirrors DomainResolver.IsFunctionDeclarationStart).
            if (t.Kind == DslTokenKind.Identifier
                && t.TextEquals("function")
                && !(i + 1 < tokens.Count && tokens[i + 1].Kind == DslTokenKind.Colon)) {
                kind = "function";
                skip = 2;
                return true;
            }

            if (IsNameColon(i)) {
                kind = "member";
                skip = 2;
                return true;
            }

            return false;
        }

        /// <summary>A node label: an identifier's name; any other token (number, string, punctuation) by its source slice, never decoded as a word.</summary>
        private static string Label(DslToken token) =>
            token.Kind == DslTokenKind.Identifier ? token.Name() : token.Span.ToString();

        private int TopLevel(int start, List<OutlineNode> nodes) {
            // TopLevel starts at IsNameColon, so the name is an identifier; the kind word is unchecked.
            var name = tokens[start].Name();
            var word = start + 2 < tokens.Count ? DslTokenText.Word(tokens[start + 2]) : "";
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

        private int MarkedTop(int start, List<OutlineNode> nodes) {
            var kind = tokens[start + 1].Name();
            var name = Label(tokens[start + 2]);
            var end = start + 2;
            var open = -1;
            var close = -1;
            var j = start + 3;
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
                else if (paren == 0 && (IsNameColon(j) || tokens[j].Kind == DslTokenKind.Public))
                    break;
                end = j;
            }

            var children = open >= 0 && kind == "entity" ? Members(open, close, name) : [];
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
                name = t.Name();
                skip = 1;
                return true;
            }

            if (t.Kind == DslTokenKind.Public && i + 2 < tokens.Count && tokens[i + 1].Kind == DslTokenKind.Action) {
                kind = "action";
                name = Label(tokens[i + 2]);
                skip = 3;
                return true;
            }

            var isPrimitiveName = t.Kind is DslTokenKind.Text or DslTokenKind.NumberType or DslTokenKind.BooleanType;
            if ((t.Kind == DslTokenKind.Identifier || isPrimitiveName)
                && i + 1 < tokens.Count && tokens[i + 1].Kind == DslTokenKind.Colon) {
                name = t.Name();
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
                            name = t.Name();
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
            var end = last.Start + last.Length;
            var lineEnd = last.Line;
            for (var k = last.Start; k < end; k++)
                if (text[k] == '\n') lineEnd++;
            var lineStart = first.Start;
            while (lineStart > 0 && text[lineStart - 1] is not ('\n' or '\r'))
                lineStart--;
            var indentEnd = lineStart;
            while (indentEnd < text.Length && text[indentEnd] is ' ' or '\t')
                indentEnd++;
            return new OutlineNode(
                kind, name, path, first.Start, end, first.Line, lineEnd,
                text[lineStart..indentEnd], addressable,
                open >= 0 ? tokens[open].Start : -1,
                close >= 0 ? tokens[close].Start : -1,
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