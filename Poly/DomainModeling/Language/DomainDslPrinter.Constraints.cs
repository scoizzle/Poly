using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Language;

// DomainDslPrinter partial: constraint printing and its print host (G-00b mechanical split).
public sealed partial class DomainDslPrinter {
    /// <summary>
    /// Canonical, invariant-culture text for one constraint. Used by the contract surface
    /// fingerprint, which must not depend on the current locale or a second renderer.
    /// </summary>
    public string PrintConstraintText(Constraint constraint) {
        ArgumentNullException.ThrowIfNull(constraint);
        return PrintConstraint(constraint);
    }

    private string PrintConstraint(Constraint constraint) =>
        ConstraintKinds.Of(constraint)?.Print?.Invoke(constraint, new PrintHost(this))
        ?? $"?{constraint.GetType().Name}";

    private sealed class PrintHost(DomainDslPrinter printer) : IConstraintPrintHost {
        public string Expression(DomainExpression expression) => printer.PrintExpression(expression);

        public string Literal(object? value) => PrintLiteralValue(value);

        public string EscapeString(string value) => EscapeStringLiteral(value);
    }
}