using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a throw statement that raises an exception.
/// </summary>
/// <remarks>
/// Immediately terminates normal execution and transfers control to exception handling.
/// The exception expression provides the error information to propagate to callers or exception handlers.
/// </remarks>
public sealed record ThrowStatement(Node Exception) : Statement {
    public override IEnumerable<Node?> Children => [Exception];

    /// <inheritdoc />
    public override string ToString() => $"throw {Exception};";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        foreach (var p in Exception.ToPrimitives(context)) yield return p;

        // When inside a protected region, emit ThrowProtected marker (distinct from
        // unprotected throw). This is a placeholder until INT-018 implements real EH.
        var isProtected = context.Analysis.IsInProtectedRegion(this);
        if (isProtected)
            yield return new Primitives.ThrowProtected();
        else
            yield return new Primitives.Throw();
    }
}