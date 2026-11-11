using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;

using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Language;

// DomainDslPrinter partial: expression printing and the expression dispatch printer (G-00b mechanical split).
public sealed partial class DomainDslPrinter {
    private string PrintExpression(DomainExpression expr) {
        return ExpressionPrinter.Run(this, expr);
    }

    /// <summary>
    /// Expression dispatch for printing. Methods are named by the Expression subtype.
    /// The verb (print) comes from the containing DomainDslPrinter.
    /// </summary>
    private sealed class ExpressionPrinter : DomainExpressionDispatch<string> {
        private readonly DomainDslPrinter _printer;

        private ExpressionPrinter(DomainDslPrinter printer) => _printer = printer;

        protected override string Default() =>
            throw new InvalidOperationException(
                $"Cannot print expression type '{CurrentExpr?.GetType().Name}': no registered print binder or pattern.");

        private DomainExpression? CurrentExpr { get; set; }

        public static string Run(DomainDslPrinter printer, DomainExpression expr) {
            var ep = new ExpressionPrinter(printer) { CurrentExpr = expr };
            return ep.Print(expr);
        }

        private string Print(DomainExpression expr) {
            CurrentExpr = expr;
            return _printer.TryPrintBoundExpression(expr) ?? Route(expr);
        }

        protected override string PropertyAccess(PropertyAccess p) => p.Name;
        protected override string ParameterAccess(ParameterAccess p) => p.Name;
        protected override string Literal(Literal l) => DomainDslPrinter.PrintLiteral(l);

        protected override string OwnedAccess(OwnedAccess o) =>
            $"{o.OwnedName} {Run(_printer, o.Inner)}";

        protected override string RelationshipNavigation(RelationshipNavigation r) =>
            _printer.PrintRelationshipNav(r);

        protected override string Comparison(Comparison c) =>
            $"{Run(_printer, c.Left)} {DomainDslPrinter.PrintComparisonKind(c.Kind)} {Run(_printer, c.Right)}";

        protected override string And(And a) =>
            $"({Run(_printer, a.Left)} and {Run(_printer, a.Right)})";

        protected override string Or(Or o) =>
            $"({Run(_printer, o.Left)} or {Run(_printer, o.Right)})";

        protected override string Not(Not n) =>
            n.Operand is Poly.DomainModeling.Ontology.Comparison
                or Poly.DomainModeling.Ontology.And
                or Poly.DomainModeling.Ontology.Or
                or Poly.DomainModeling.Ontology.Add
                or Poly.DomainModeling.Ontology.Subtract
                or Poly.DomainModeling.Ontology.Multiply
                or Poly.DomainModeling.Ontology.Divide
                or Poly.DomainModeling.Ontology.Not
                ? $"not ({Run(_printer, n.Operand)})"
                : $"not {Run(_printer, n.Operand)}";

        protected override string Add(Add a) =>
            $"({Run(_printer, a.Left)} + {Run(_printer, a.Right)})";

        protected override string Subtract(Subtract s) =>
            $"({Run(_printer, s.Left)} - {Run(_printer, s.Right)})";

        protected override string Multiply(Multiply m) =>
            $"({Run(_printer, m.Left)} * {Run(_printer, m.Right)})";

        protected override string Divide(Divide d) =>
            $"({Run(_printer, d.Left)} / {Run(_printer, d.Right)})";

        protected override string Exists(Exists e) =>
            $"{Run(_printer, e.Target)} exists";

        protected override string NotExists(NotExists n) =>
            $"not {Run(_printer, n.Target)} exists";

        protected override string AnyExpr(AnyExpr a) =>
            $"any {a.RelationshipName} where {Run(_printer, a.Body)}";

        protected override string AllExpr(AllExpr a) =>
            $"all {a.RelationshipName} where {Run(_printer, a.Body)}";

        protected override string NoneExpr(NoneExpr n) =>
            $"none {n.RelationshipName} where {Run(_printer, n.Body)}";

        protected override string CountExpr(CountExpr c) =>
            c.Body is not null
                ? $"count {c.RelationshipName} where {Run(_printer, c.Body)}"
                : $"count {c.RelationshipName}";

        protected override string MatchExpr(MatchExpr m) =>
            "match { "
            + string.Join(", ", m.Arms.Select(a => $"{Run(_printer, a.Condition)} -> {Run(_printer, a.Value)}")
                .Append($"else -> {Run(_printer, m.Else)}"))
            + " }";
        protected override string FunctionCall(FunctionCall f) =>
            $"{f.Name}({string.Join(", ", f.Arguments.Select(a => Run(_printer, a)))})";

        protected override string SumExpr(SumExpr s) =>
            $"sum {s.RelationshipName} {s.PropertyName}";
    }

    /// <summary>
    /// Prints a RelationshipNavigation in subject-first form.
    /// Simple body (PropertyAccess or Comparison) → "Rel Prop" / "Rel Prop op value"
    /// Complex body (And/Or etc) → "Rel where body" 
    /// </summary>
    private string PrintRelationshipNav(RelationshipNavigation rn) {
        var rel = rn.RelationshipName;
        var body = rn.TargetProperty;

        // Simple: Rel Prop
        if (body is PropertyAccess pa)
            return $"{rel} {pa.Name}";

        // Simple: Rel Prop op value (Comparison with PropertyAccess on left)
        if (body is Comparison comp && comp.Left is PropertyAccess)
            return $"{rel} {PrintExpression(body)}";

        // Complex: Rel where body
        return $"{rel} where {PrintExpression(body)}";
    }
}