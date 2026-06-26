namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a switch statement that conditionally executes one of many branches based on a value.
/// </summary>
/// <remarks>
/// A value is matched against one or more case patterns, and the corresponding case body is executed.
/// A default case may be executed if no other cases match. All case bodies should have compatible types.
/// </remarks>
public sealed record SwitchStatement(Node Value, IReadOnlyList<SwitchCase> Cases, Node? DefaultCase = null) : Node {
    public override IEnumerable<Node?> Children => [Value, .. Cases.SelectMany(c => c.Children), DefaultCase];

    /// <inheritdoc />
    public override string ToString() {
        var cases = string.Join(" ", Cases.Select(c => c.ToString()));
        var defaultStr = DefaultCase is not null ? $" default: {DefaultCase}" : "";
        return $"switch ({Value}) {{ {cases}{defaultStr} }}";
    }

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        var endLabel = new Poly.Syntax.Primitives.Label("switch_end");
        var caseLabels = Cases.Select(_ => new Poly.Syntax.Primitives.Label("case")).ToList();

        // Emit value once; dup it for each comparison
        foreach (var p in Value.ToPrimitives(context)) yield return p;

        for (int i = 0; i < Cases.Count; i++) {
            yield return new Poly.Syntax.Primitives.Dup();

            foreach (var p in Cases[i].Pattern.ToPrimitives(context)) yield return p;
            yield return new Poly.Syntax.Primitives.BinaryOp(Poly.Syntax.Primitives.OpKind.Eq);
            yield return new Poly.Syntax.Primitives.CondGoto(caseLabels[i]);
        }

        // No case matched
        yield return new Poly.Syntax.Primitives.Discard(); // discard original value
        if (DefaultCase is not null) {
            foreach (var p in DefaultCase.ToPrimitives(context)) yield return p;
        }
        yield return new Poly.Syntax.Primitives.Goto(endLabel);

        // Case bodies (each enters with value on stack, discards it)
        for (int i = 0; i < Cases.Count; i++) {
            yield return caseLabels[i];
            yield return new Poly.Syntax.Primitives.Discard(); // discard original value
            foreach (var p in Cases[i].Body.ToPrimitives(context)) yield return p;
            // Prevent fallthrough to next case
            if (i < Cases.Count - 1 || DefaultCase is not null)
                yield return new Poly.Syntax.Primitives.Goto(endLabel);
        }

        yield return endLabel;
    }
}

/// <summary>
/// Represents a single case in a switch statement.
/// </summary>
/// <remarks>
/// A switch case matches a specific value (or set of values) and executes the associated body.
/// </remarks>
public sealed record SwitchCase(Node Pattern, Node Body) {
    public IEnumerable<Node?> Children => [Pattern, Body];

    /// <inheritdoc />
    public override string ToString() => $"case {Pattern}: {Body}";
}