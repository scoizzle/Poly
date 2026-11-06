using System.Text.Json;

using Poly.DomainModeling.Language;

namespace Poly.Mcp.Surface;

/// <summary>One splice that landed in the edited document.</summary>
internal sealed record AppliedEdit(int Index, string Op, string Path, int Line);

/// <summary>The lines of the edited document an op wrote, so a gate error can be traced to its op.</summary>
internal sealed record EditRegion(int Index, int StartLine, int EndLine);

internal sealed record EditOutcome(
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
internal static class PolyEdit {
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
                        ? DeclaredName(op.Dsl!) ?? "(top level)"
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

        if (op.Parent!.Length == 0)
            return AddTopLevel(text, nl, outline, op, name, out code, out error);

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

        if (name is not null && parent.Children.Any(c => c.Addressable && c.Name == name)) {
            code = "EDIT_NAME_COLLISION";
            error = $"'{op.Parent}' already has a member '{name}'.";
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
            var id = op.Dsl!.Trim().Split(default(char[]), StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
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
            return (lineStart, end, "");
        }

        return (node.StartOffset, node.EndOffset, "");
    }

    private static (int Start, int End, string Insert)? Replace(
        string text, string nl, DslOutline outline, Op op, out string? code, out string? error) {
        var node = outline.Find(op.Path!, out code, out error);
        if (node is null)
            return null;

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

    private static string? DeclaredName(string dsl) {
        try {
            var reader = new DslTokenReader(dsl);
            var first = reader.Peek(0);
            var second = reader.Peek(1);
            return second.Kind == DslTokenKind.Colon
                && first.Kind is DslTokenKind.Identifier or DslTokenKind.Text or DslTokenKind.NumberType or DslTokenKind.BooleanType
                    ? first.Text
                    : null;
        }
        catch (FormatException) {
            return null;
        }
    }

    private static string TopLevelKind(string dsl) {
        try {
            var reader = new DslTokenReader(dsl);
            var first = reader.Peek(0);
            if (first.Kind == DslTokenKind.Uses)
                return "uses";
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