using Poly.DomainModeling.Ontology;

using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Structural checks for declared functions and calls (#406 slice 2): unique names, pure
/// bodies, no recursion, and per-call name/arity. Type checks (argument types, return type)
/// need the expression scope and live in <see cref="ExpressionTypeAnalyzer"/>.
/// </summary>
internal static class FunctionAnalysis {
    public static DomainFunction? Find(AnalysisContext context, string name) =>
        context.GetTypeLookup()?.Domain.Functions
            .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));

    public static void CheckDeclarations(AnalysisContext context, Domain domain) {
        foreach (var group in domain.Functions.GroupBy(f => f.Name, StringComparer.Ordinal).Where(g => g.Count() > 1)) {
            foreach (var duplicate in group.Skip(1))
                context.ReportError(duplicate,
                    $"Function '{group.Key}' is declared more than once.",
                    DomainModelDiagnosticCodes.FunctionDuplicate);
        }

        foreach (var function in domain.Functions) {
            CheckPure(context, function, function.Body);
            CheckRecursion(context, domain, function);
        }
    }

    private static void CheckPure(AnalysisContext context, DomainFunction function, DomainExpression expression) {
        switch (expression) {
            case ParameterAccess or Literal:
                return;
            case Add or Subtract or Multiply or Divide or Comparison or And or Or or Not:
                break;
            case FunctionCall:
                break;
            case PropertyAccess property:
                context.ReportError(function,
                    $"Function '{function.Name}' body names '{property.Name}', which is not one of its parameters; " +
                    "a function is pure and may name only its parameters.",
                    DomainModelDiagnosticCodes.FunctionImpureBody);
                return;
            default:
                context.ReportError(function,
                    $"Function '{function.Name}' body uses a {expression.GetType().Name} expression; " +
                    "a function is pure and may use only parameters, literals, arithmetic, comparison, logic, and calls.",
                    DomainModelDiagnosticCodes.FunctionImpureBody);
                return;
        }
        foreach (var child in expression.Children.OfType<DomainExpression>())
            CheckPure(context, function, child);
    }

    private static void CheckRecursion(AnalysisContext context, Domain domain, DomainFunction function) {
        var path = FindCycle(domain, function.Name, function.Name, [function.Name]);
        if (path is not null)
            context.ReportError(function,
                $"Function '{function.Name}' is recursive ({string.Join(" -> ", path)}); recursion is not supported.",
                DomainModelDiagnosticCodes.FunctionRecursion);
    }

    private static List<string>? FindCycle(Domain domain, string start, string current, List<string> path) {
        var function = domain.Functions.FirstOrDefault(f => string.Equals(f.Name, current, StringComparison.Ordinal));
        if (function is null)
            return null;
        foreach (var callee in CallsIn(function.Body).Select(c => c.Name).Distinct(StringComparer.Ordinal)) {
            if (string.Equals(callee, start, StringComparison.Ordinal))
                return [.. path, callee];
            if (path.Contains(callee, StringComparer.Ordinal))
                continue;
            if (FindCycle(domain, start, callee, [.. path, callee]) is { } found)
                return found;
        }
        return null;
    }

    public static IEnumerable<FunctionCall> CallsIn(DomainExpression expression) {
        if (expression is FunctionCall call)
            yield return call;
        foreach (var child in expression.Children.OfType<DomainExpression>())
            foreach (var nested in CallsIn(child))
                yield return nested;
    }

    public static void CheckCall(AnalysisContext context, FunctionCall call) {
        var where = call.Line > 0 ? $" (line {call.Line}, col {call.Col})" : "";
        var function = Find(context, call.Name);
        if (function is null) {
            context.ReportError(call,
                $"Call to unknown function '{call.Name}'{where}.",
                DomainModelDiagnosticCodes.FunctionUnknown);
            return;
        }
        if (call.Arguments.Count != function.Parameters.Count)
            context.ReportError(call,
                $"Function '{call.Name}' expects {function.Parameters.Count} argument(s), got {call.Arguments.Count}{where}.",
                DomainModelDiagnosticCodes.FunctionArity);
    }

    public static void CheckProperty(AnalysisContext context, Property property) {
        foreach (var constraint in property.Constraints.OfType<DefaultValueConstraint>()) {
            foreach (var call in CallsIn(constraint.Expression))
                context.ReportError(property,
                    $"Call to '{call.Name}' in the default of '{property.Name}' is not supported; " +
                    "a default is lowered without a function scope. Assign the call in an action or entry effect instead.",
                    DomainModelDiagnosticCodes.FunctionCallPosition);
        }
    }
}