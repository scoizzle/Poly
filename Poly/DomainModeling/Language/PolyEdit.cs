using System.Text.Json;

namespace Poly.DomainModeling.Language;

/// <summary>One splice that landed in the edited document.</summary>
public sealed record AppliedEdit(int Index, string Op, string Path, int Line);

/// <summary>The lines of the edited document an op wrote, so a gate error can be traced to its op.</summary>
public sealed record EditRegion(int Index, int StartLine, int EndLine);

public sealed record EditOutcome(
    string? Text,
    string? ErrorCode,
    string? Error,
    IReadOnlyList<AppliedEdit> Applied,
    IReadOnlyList<EditRegion> Regions) {
    public bool Ok => ErrorCode is null;

    public static EditOutcome Fail(string code, string message) => new(null, code, message, [], []);
}

/// <summary>
/// Splices an agent's edit ops into the author's own stored .poly text. The ops are
/// <c>add</c>, <c>remove</c> and <c>replace</c>; each rebuilds the outline of the text so far, so
/// later ops address the document the earlier ones produced. Nothing here analyzes the result:
/// the caller runs the spliced text through the same gate as a whole-document define.
/// </summary>
public static class PolyEdit {
    private sealed record Op(int Index, string Kind, string? Parent, string? Path, string? Dsl);

    public static EditOutcome Apply(string source, string editJson) {
        var parsed = Parse(editJson, out var failure);
        if (parsed is null)
            return failure!;

        var text = source;
        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var spans = new List<(int Index, int Start, int Length)>();
        var applied = new List<(int Index, string Op, string Path)>();

        foreach (var op in parsed) {
            DslOutline outline;
            try {
                outline = DslOutline.Build(text);
            }
            catch (FormatException ex) {
                return EditOutcome.Fail("EDIT_NOT_ADDRESSABLE", $"The stored document could not be outlined: {ex.Message}");
            }

            (int Start, int End, string Insert)? splice;
            string path;
            string? code, error;
            switch (op.Kind) {
                case "add":
                    path = op.Parent!.Length == 0
                        ? TopLevelPath(op.Dsl!) ?? "(top level)"
                        : $"{op.Parent}.{DeclaredName(op.Dsl!)}";
                    splice = Add(text, nl, outline, op, out code, out error);
                    break;
                case "remove":
                    path = op.Path!;
                    splice = Remove(text, outline, op, out code, out error);
                    break;
                default:
                    path = op.Path!;
                    splice = Replace(text, nl, outline, op, out code, out error);
                    break;
            }

            if (splice is not { } s)
                return EditOutcome.Fail(code!, $"Edit {op.Index} ({op.Kind}): {error}");

            text = string.Concat(text.AsSpan(0, s.Start), s.Insert, text.AsSpan(s.End));
            var delta = s.Insert.Length - (s.End - s.Start);
            for (var k = 0; k < spans.Count; k++) {
                if (spans[k].Start >= s.End)
                    spans[k] = (spans[k].Index, spans[k].Start + delta, spans[k].Length);
            }
            spans.Add((op.Index, s.Start, s.Insert.Length));
            applied.Add((op.Index, op.Kind, path));
        }

        var regions = spans
            .Select(r => RegionOf(text, r.Index, r.Start, r.Length))
            .ToList();
        var appliedEdits = applied
            .Select(a => new AppliedEdit(a.Index, a.Op, a.Path, regions.First(r => r.Index == a.Index).StartLine))
            .ToList();
        return new EditOutcome(text, null, null, appliedEdits, regions);
    }

    private static List<Op>? Parse(string editJson, out EditOutcome? failure) {
        failure = null;
        JsonDocument document;
        try {
            document = JsonDocument.Parse(editJson);
        }
        catch (JsonException ex) {
            failure = EditOutcome.Fail("EDIT_BAD_JSON", $"edit is not valid JSON: {ex.Message}");
            return null;
        }

        using (document) {
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0) {
                failure = EditOutcome.Fail("EDIT_BAD_JSON", "edit must be a non-empty JSON array of ops, e.g. [{\"op\":\"add\",\"parent\":\"Product\",\"dsl\":\"Amount: Decimal\"}].");
                return null;
            }

            var ops = new List<Op>();
            var index = 0;
            foreach (var element in document.RootElement.EnumerateArray()) {
                string? Str(string name) =>
                    element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                        ? v.GetString()
                        : null;

                var kind = Str("op");
                if (kind is not ("add" or "remove" or "replace")) {
                    var message = kind == "rename"
                        ? $"Edit {index}: rename is not available yet; rename by redefining the model with poly."
                        : $"Edit {index}: unknown op '{kind}'. Use add, remove or replace.";
                    failure = EditOutcome.Fail("EDIT_UNKNOWN_OP", message);
                    return null;
                }

                var parent = Str("parent");
                var path = Str("path");
                var dsl = Str("dsl");
                var missing = kind switch {
                    "add" when parent is null || string.IsNullOrWhiteSpace(dsl) => "add needs parent (\"\" for top level) and dsl",
                    "remove" when string.IsNullOrWhiteSpace(path) => "remove needs path",
                    "replace" when string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(dsl) => "replace needs path and dsl",
                    _ => null,
                };
                if (missing is not null) {
                    failure = EditOutcome.Fail("EDIT_BAD_JSON", $"Edit {index}: {missing}.");
                    return null;
                }

                ops.Add(new Op(index++, kind, parent?.Trim(), path?.Trim(), dsl));
            }

            return ops;
        }
    }

    private static (int Start, int End, string Insert)? Add(
        string text, string nl, DslOutline outline, Op op, out string? code, out string? error) {
        code = null;
        error = null;
        var name = DeclaredName(op.Dsl!);

        if (op.Parent!.Length == 0) {
            if (BadSnippet(DslOutline.Inspect(op.Dsl!, topLevel: true)) is { } topLevelFault) {
                code = "EDIT_BAD_SNIPPET";
                error = topLevelFault;
                return null;
            }

            return AddTopLevel(text, nl, outline, op, name, out code, out error);
        }

        var parent = outline.All().FirstOrDefault(n => string.Equals(n.Path, op.Parent, StringComparison.Ordinal));
        if (parent is null) {
            code = "EDIT_PATH_NOT_FOUND";
            error = $"No member '{op.Parent}'." + outline.NearMisses(op.Parent);
            return null;
        }

        if (parent.OpenBrace < 0 || parent.Kind is not ("entity" or "stage")) {
            code = "EDIT_NOT_ADDRESSABLE";
            error = $"'{op.Parent}' cannot take members; add into an entity or a stage.";
            return null;
        }

        if (TopLevelKind(op.Dsl!) == "uses") {
            code = "EDIT_NOT_ADDRESSABLE";
            var (usesLine, usesCol) = FirstTokenPosition(op.Dsl!);
            error = $"'uses' is a top-level declaration (dsl line {usesLine}, col {usesCol}); add it with parent \"\" instead.";
            return null;
        }

        if (name is not null && parent.Children.Any(c => c.Addressable && c.Name == name)) {
            code = "EDIT_NAME_COLLISION";
            error = $"'{op.Parent}' already has a member '{name}'.";
            return null;
        }

        if (BadSnippet(DslOutline.Inspect(op.Dsl!, topLevel: false)) is { } memberFault) {
            code = "EDIT_BAD_SNIPPET";
            error = memberFault;
            return null;
        }

        var parentIndent = parent.Indent;
        var child = parent.Children.FirstOrDefault(c => c.StartLine > parent.StartLine);
        var childIndent = child?.Indent ?? parentIndent + (parentIndent.Contains('\t') ? "\t" : "  ");
        var body = Reindent(op.Dsl!, childIndent, nl, skipFirst: false);

        var close = parent.CloseBrace;
        var lineStart = close;
        while (lineStart > 0 && text[lineStart - 1] is not ('\n' or '\r'))
            lineStart--;
        if (string.IsNullOrWhiteSpace(text[lineStart..close]))
            return (lineStart, lineStart, body + nl);
        return (close, close, nl + body + nl + parentIndent);
    }

    private static (int Start, int End, string Insert)? AddTopLevel(
        string text, string nl, DslOutline outline, Op op, string? name, out string? code, out string? error) {
        code = null;
        error = null;
        var kind = TopLevelKind(op.Dsl!);

        if (kind == "uses") {
            var path = TopLevelPath(op.Dsl!);
            var id = path is not null && path.StartsWith("uses:", StringComparison.Ordinal)
                ? path["uses:".Length..]
                : null;
            if (id is not null && outline.Roots.Any(n => n.Path == "uses:" + id)) {
                code = "EDIT_NAME_COLLISION";
                error = $"The model already uses '{id}'.";
                return null;
            }
        }
        else if (name is not null && outline.Roots.Any(n => n.Kind is not ("domain" or "uses") && n.Name == name)) {
            code = "EDIT_NAME_COLLISION";
            error = $"The model already has a top-level '{name}'.";
            return null;
        }

        var roots = outline.Roots;
        var firstEntity = roots.ToList().FindIndex(n => n.Kind == "entity");
        OutlineNode? anchor = kind switch {
            "uses" => roots.LastOrDefault(n => n.Kind is "domain" or "uses"),
            "enum" or "value" or "contract" => firstEntity > 0 ? roots[firstEntity - 1] : roots.LastOrDefault(),
            // Entities stay grouped: a trailing enum or value root must not pull a new entity past them.
            "entity" => roots.LastOrDefault(n => n.Kind == "entity") ?? roots.LastOrDefault(),
            _ => roots.LastOrDefault(),
        };

        var body = Reindent(op.Dsl!, "", nl, skipFirst: false);
        if (anchor is null)
            return (text.Length, text.Length, text.Length == 0 ? body + nl : nl + body + nl);

        var at = anchor.EndOffset;
        while (at < text.Length && text[at] is not ('\n' or '\r'))
            at++;
        var separator = kind == "uses" ? nl : nl + nl;
        return (at, at, separator + body);
    }

    private static (int Start, int End, string Insert)? Remove(
        string text, DslOutline outline, Op op, out string? code, out string? error) {
        var node = outline.Find(op.Path!, out code, out error);
        if (node is null)
            return null;

        // Dropping the last uses line would leave the stored text disagreeing with the compiled
        // model, which keeps the packs it already loads, so the edit is refused instead.
        if (node.Kind == "uses" && outline.Roots.Count(n => n.Kind == "uses") == 1) {
            code = "EDIT_NOT_ADDRESSABLE";
            error = $"'{op.Path}' (line {node.StartLine}) is the document's only uses line; keep another uses line or redefine the model with poly.";
            return null;
        }

        var lineStart = node.StartOffset;
        while (lineStart > 0 && text[lineStart - 1] is not ('\n' or '\r'))
            lineStart--;
        var lineEnd = node.EndOffset;
        while (lineEnd < text.Length && text[lineEnd] is not ('\n' or '\r'))
            lineEnd++;

        var before = text[lineStart..node.StartOffset];
        var after = text[node.EndOffset..lineEnd].Trim();
        if (string.IsNullOrWhiteSpace(before) && (after.Length == 0 || after.StartsWith("//", StringComparison.Ordinal))) {
            var end = lineEnd;
            if (end < text.Length && text[end] == '\r') end++;
            if (end < text.Length && text[end] == '\n') end++;
            // A top-level node spliced between blank separators ("\n\n<node>\n\n") would leave a
            // doubled blank line behind; drop the separator that now has nothing to separate.
            if (end < text.Length && text[end] is '\r' or '\n' && BlankLineBefore(text, lineStart))
                end += text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1;
            return (lineStart, end, "");
        }

        return (node.StartOffset, node.EndOffset, "");
    }

    /// <summary>Whether the line ending at <paramref name="lineStart"/> is empty.</summary>
    private static bool BlankLineBefore(string text, int lineStart) =>
        lineStart >= 2 && text[lineStart - 1] == '\n' && text[lineStart - 2] is '\n' or '\r';

    private static (int Start, int End, string Insert)? Replace(
        string text, string nl, DslOutline outline, Op op, out string? code, out string? error) {
        var node = outline.Find(op.Path!, out code, out error);
        if (node is null)
            return null;

        // A uses line and a member are disjoint kinds: neither snippet can retarget the other.
        var usesSnippet = TopLevelKind(op.Dsl!) == "uses";
        if (node.Kind == "uses" || usesSnippet) {
            var (dslLine, dslCol) = FirstTokenPosition(op.Dsl!);
            if (node.Kind == "uses" && !usesSnippet) {
                code = "EDIT_NOT_ADDRESSABLE";
                error = $"the dsl for '{op.Path}' must be a uses line (dsl line {dslLine}, col {dslCol}).";
                return null;
            }

            if (node.Kind != "uses") {
                code = "EDIT_NOT_ADDRESSABLE";
                error = $"the dsl for '{op.Path}' must not be a uses line (dsl line {dslLine}, col {dslCol}); edit a uses line by its own path.";
                return null;
            }
        }

        if (BadSnippet(DslOutline.Inspect(op.Dsl!, topLevel: outline.Roots.Contains(node))) is { } fault) {
            code = "EDIT_BAD_SNIPPET";
            error = fault;
            return null;
        }

        var name = DeclaredName(op.Dsl!);
        if (name is not null && name != node.Name) {
            var siblings = SiblingsOf(outline, node);
            if (siblings.Any(s => s.Addressable && s.Name == name)) {
                code = "EDIT_NAME_COLLISION";
                error = $"A sibling of '{op.Path}' is already named '{name}'.";
                return null;
            }
        }

        return (node.StartOffset, node.EndOffset, Reindent(op.Dsl!, node.Indent, nl, skipFirst: true));
    }

    private static IReadOnlyList<OutlineNode> SiblingsOf(DslOutline outline, OutlineNode node) {
        if (outline.Roots.Contains(node))
            return outline.Roots;
        return outline.All().FirstOrDefault(n => n.Children.Contains(node))?.Children ?? [];
    }

    /// <summary>
    /// The <c>EDIT_BAD_SNIPPET</c> failure for a fragment that is not exactly one balanced
    /// declaration, or null when the fragment is usable. Positions are fragment-relative and
    /// labelled <c>dsl line</c> so they are not mistaken for document lines.
    /// </summary>
    private static string? BadSnippet(SnippetShape shape) => shape.Fault switch {
        { Kind: SnippetFaultKind.UnexpectedClose } f =>
            $"the dsl has an unmatched '}}' at dsl line {f.Line}, col {f.Col}; the dsl must be one declaration with balanced braces.",
        { Kind: SnippetFaultKind.Unclosed } f =>
            $"the dsl has an unclosed '{{' at dsl line {f.Line}, col {f.Col}; the dsl must be one declaration with balanced braces.",
        { Kind: SnippetFaultKind.Multiple } f =>
            $"the dsl contains more than one declaration (the second starts at dsl line {f.Line}, col {f.Col}); each op changes exactly one declaration.",
        { Kind: SnippetFaultKind.Empty } =>
            "the dsl does not declare anything, e.g. \"Amount: Decimal\".",
        { Kind: SnippetFaultKind.BadToken } f =>
            $"the dsl could not be read: {f.Detail}",
        _ => null,
    };

    /// <summary>The first token's fragment-relative position, for a kind-mismatch message.</summary>
    private static (int Line, int Col) FirstTokenPosition(string dsl) {
        try {
            var first = new DslTokenReader(dsl).Peek(0);
            return (first.Line, first.Col);
        }
        catch (FormatException) {
            return (1, 1);
        }
    }

    /// <summary>
    /// The path a top-level <c>add</c> lands on: a member name, or <c>uses:&lt;pack&gt;</c> for a
    /// uses line, which the outline addresses as its own root.
    /// </summary>
    private static string? TopLevelPath(string dsl) {
        if (TopLevelKind(dsl) != "uses")
            return DeclaredName(dsl);

        try {
            var tokens = new List<DslToken>();
            var reader = new DslTokenReader(dsl);
            while (true) {
                var token = reader.Peek();
                if (token.Kind == DslTokenKind.EndOfFile)
                    break;
                tokens.Add(token);
                reader.Consume(1);
            }

            return DslSyntaxTree.TryReadUse(tokens, 0, out var id, out _, out _) ? "uses:" + id : null;
        }
        catch (FormatException) {
            return null;
        }
    }

    /// <summary>
    /// The name an <c>add</c>/<c>replace</c> dsl declares: <c>Name: kind</c>, or the marked
    /// <c>public entity Name</c> / <c>public value Name</c> / <c>public action Name</c> form, where
    /// the name follows the kind.
    /// </summary>
    private static string? DeclaredName(string dsl) {
        try {
            var reader = new DslTokenReader(dsl);
            var marked = reader.Peek(0).Kind == DslTokenKind.Public;
            var first = reader.Peek(marked ? 1 : 0);
            var second = reader.Peek(marked ? 2 : 1);
            if (marked)
                return IsKindWord(first) && IsNameToken(second) ? second.Text : null;
            return second.Kind == DslTokenKind.Colon && IsNameToken(first)
                ? first.Text
                : null;
        }
        catch (FormatException) {
            return null;
        }
    }

    /// <summary>A token shaped like a declared name (the same kinds the unmarked form accepts).</summary>
    private static bool IsNameToken(DslToken token) =>
        token.Kind is DslTokenKind.Identifier or DslTokenKind.Text or DslTokenKind.NumberType or DslTokenKind.BooleanType;

    /// <summary>The kind word a leading <c>public</c> modifier may introduce.</summary>
    private static bool IsKindWord(DslToken token) =>
        token.Kind is DslTokenKind.Entity or DslTokenKind.Action || DslGrammar.IsValueWord(token);

    private static string TopLevelKind(string dsl) {
        try {
            var reader = new DslTokenReader(dsl);
            var marked = reader.Peek(0).Kind == DslTokenKind.Public;
            var first = reader.Peek(marked ? 1 : 0);
            if (first.Kind == DslTokenKind.Uses)
                return "uses";
            if (marked)
                return DslGrammar.IsValueWord(first) ? "value" : "entity";
            if (reader.Peek(1).Kind == DslTokenKind.Colon)
                return reader.Peek(2).Text switch {
                    "enum" => "enum",
                    "value" => "value",
                    "contract" => "contract",
                    "bind" => "bind",
                    _ => "entity",
                };
        }
        catch (FormatException) {
        }
        return "entity";
    }

    private static string Reindent(string dsl, string indent, string nl, bool skipFirst) {
        var lines = dsl.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);
        var common = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();
        var shifted = lines.Select((l, i) => {
            var trimmed = string.IsNullOrWhiteSpace(l) ? "" : l[common..].TrimEnd();
            return trimmed.Length == 0 || (i == 0 && skipFirst) ? trimmed : indent + trimmed;
        });
        return string.Join(nl, shifted);
    }

    private static EditRegion RegionOf(string text, int index, int start, int length) {
        var first = start;
        var last = Math.Min(text.Length, start + length);
        while (first < last && char.IsWhiteSpace(text[first])) first++;
        while (last > first && char.IsWhiteSpace(text[last - 1])) last--;
        return new EditRegion(index, LineAt(text, first), LineAt(text, Math.Max(first, last - 1)));
    }

    private static int LineAt(string text, int offset) {
        var line = 1;
        for (var i = 0; i < offset && i < text.Length; i++)
            if (text[i] == '\n') line++;
        return line;
    }
}