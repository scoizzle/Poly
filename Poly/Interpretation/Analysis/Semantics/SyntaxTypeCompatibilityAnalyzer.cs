namespace Poly.Interpretation.Analysis.Semantics;

public static class SyntaxTypeCompatibilityAnalyzerExtensions {
    extension(AnalyzerBuilder builder) {
        /// <summary>Adds the <see cref="SyntaxTypeCompatibilityAnalyzer"/> to the pipeline.</summary>
        public AnalyzerBuilder UseSyntaxTypeCompatibility() {
            builder.AddAnalyzer(new SyntaxTypeCompatibilityAnalyzer());
            return builder;
        }
    }
}

/// <summary>
/// Validate pack on the lowered Syntax AST: operation type compatibility. The
/// interpretation pipeline resolved types (TypeAndMemberResolver) and classified
/// representations (ValueRepresentationAnalysis) but never rejected incompatible
/// operations — a Text member compared to a Number constant, a bool in arithmetic,
/// a non-Boolean operand to <c>not</c>. The VM then silently coerced garbage, and the
/// C# export failed at compile. This pass reports the class at VM-compile time, so the
/// runtime fails loud even for programmatically-constructed or MCP-driven expressions
/// that bypass the DSL authoring analyzer.
///
/// Compatibility is checked at the CLR-type-category level. Unknown and null operands
/// are skipped (the property bag is loosely typed; null comparisons are supported), and
/// enum operands accept string constants (runtime stores enum values as strings).
/// </summary>
internal sealed class SyntaxTypeCompatibilityAnalyzer : INodeAnalyzer {
    public const string Id = "SyntaxTypeCompatibility";
    public string PassName => Id;

    /// <summary>Diagnostic code used by <see cref="Interpreter.Compile"/> to fail loud.</summary>
    public const string DiagnosticCode = "VmTypeCompatibility";

    public string[] Dependencies => [TypeAndMemberResolver.Id, ValueRepresentationAnalyzer.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node))
            return;

        switch (node) {
            case Equal eq:
                CheckComparison(context, eq.LeftHandValue, eq.RightHandValue);
                break;
            case NotEqual ne:
                CheckComparison(context, ne.LeftHandValue, ne.RightHandValue);
                break;
            case LessThan lt:
                CheckComparison(context, lt.LeftHandValue, lt.RightHandValue);
                break;
            case LessThanOrEqual lte:
                CheckComparison(context, lte.LeftHandValue, lte.RightHandValue);
                break;
            case GreaterThan gt:
                CheckComparison(context, gt.LeftHandValue, gt.RightHandValue);
                break;
            case GreaterThanOrEqual gte:
                CheckComparison(context, gte.LeftHandValue, gte.RightHandValue);
                break;
            case Add add:
                CheckArithmetic(context, add.LeftHandValue, add.RightHandValue, isAdd: true);
                break;
            case Subtract sub:
                CheckArithmetic(context, sub.LeftHandValue, sub.RightHandValue, isAdd: false);
                break;
            case Multiply mul:
                CheckArithmetic(context, mul.LeftHandValue, mul.RightHandValue, isAdd: false);
                break;
            case Divide div:
                CheckArithmetic(context, div.LeftHandValue, div.RightHandValue, isAdd: false);
                break;
            case Not not:
                var operand = ClrTypeOf(context, not.Value);
                if (operand is not null && operand != typeof(bool) && CategoryOf(operand) is not (Cat.Unknown or Cat.Null))
                    Report(context, node, $"'not' requires a Boolean operand (got '{operand.Name}')");
                break;
            case And and:
                CheckBooleanOperands(context, and.LeftHandValue, and.RightHandValue);
                break;
            case Or or:
                CheckBooleanOperands(context, or.LeftHandValue, or.RightHandValue);
                break;
            case Assignment a:
                if (a.Destination is Member dest)
                    CheckAssign(context, dest, a.Value);
                if (a.Destination is Variable v)
                    CheckVariableAssign(context, v, a);
                break;
            case Invoke inv:
                CheckInvokeTarget(context, inv);
                break;
            case TypeCast tc:
                CheckTypeCast(context, tc);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private void CheckComparison(AnalysisContext context, Node leftNode, Node rightNode) {
        var left = ClrTypeOf(context, leftNode);
        var right = ClrTypeOf(context, rightNode);
        if (left is null || right is null) return;
        var lc = CategoryOf(left);
        var rc = CategoryOf(right);
        if (lc is Cat.Unknown or Cat.Null || rc is Cat.Unknown or Cat.Null) return;
        if (!Compatible(lc, left, rc, right))
            Report(context, leftNode,
                $"comparison between incompatible types '{left.Name}' and '{right.Name}'");
    }

    private void CheckArithmetic(AnalysisContext context, Node leftNode, Node rightNode, bool isAdd) {
        var left = ClrTypeOf(context, leftNode);
        var right = ClrTypeOf(context, rightNode);
        if (left is null || right is null) return;
        var lc = CategoryOf(left);
        var rc = CategoryOf(right);
        if (lc is Cat.Unknown or Cat.Null || rc is Cat.Unknown or Cat.Null) return;
        // numeric + numeric, or date + number (AddDays lowering), or string + string (concat)
        bool ok = (lc is Cat.Number && rc is Cat.Number)
                  || (lc is Cat.Date && rc is Cat.Number)
                  || (isAdd && lc is Cat.Text && rc is Cat.Text);
        if (!ok)
            Report(context, leftNode,
                $"arithmetic operand is not numeric (got '{left.Name}' and '{right.Name}')");
    }

    private void CheckBooleanOperands(AnalysisContext context, Node leftNode, Node rightNode) {
        var left = ClrTypeOf(context, leftNode);
        var right = ClrTypeOf(context, rightNode);
        if (left is null || right is null) return;
        var lc = CategoryOf(left);
        var rc = CategoryOf(right);
        if (lc is Cat.Unknown or Cat.Null || rc is Cat.Unknown or Cat.Null) return;
        if (lc is not Cat.Boolean || rc is not Cat.Boolean)
            Report(context, leftNode,
                $"'and'/'or' requires Boolean operands (got '{left.Name}' and '{right.Name}')");
    }

    private void CheckInvokeTarget(AnalysisContext context, Invoke invoke) {
        if (invoke.Delegate is Member)
            return;
        if (invoke.Delegate is Lambda lambda) {
            CheckInvokeArity(context, invoke, lambda, allowZeroArgsAsSetArgs: true);
            return;
        }
        if (invoke.Delegate is Variable or Parameter) {
            if (context.GetMetadata<StoredLambdaMetadata>(invoke.Delegate) is { } stored) {
                CheckInvokeArity(context, invoke, stored.Lambda, allowZeroArgsAsSetArgs: false);
                return;
            }
            Report(context, invoke,
                $"Invoke target must be a member, lambda, or stored closure, got {invoke.Delegate.GetType().Name}");
            return;
        }
        Report(context, invoke,
            $"Invoke target must be a member, lambda, or stored closure, got {invoke.Delegate.GetType().Name}");
    }

    private void CheckInvokeArity(AnalysisContext context, Invoke invoke, Lambda lambda, bool allowZeroArgsAsSetArgs) {
        int nParams = lambda.Parameters.Count;
        int nArgs = invoke.Arguments.Length;
        if (allowZeroArgsAsSetArgs && nArgs == 0)
            return;
        if (nArgs == nParams)
            return;
        Report(context, invoke,
            $"lambda has {nParams} parameter(s) but invoke has {nArgs} argument(s)");
    }

    private void CheckVariableAssign(AnalysisContext context, Variable variable, Assignment assignment) {
        var rhsMeta = context.GetMetadata<ValueRepresentationMetadata>(assignment.Value);
        if (rhsMeta is null)
            return;
        if (rhsMeta.Kind is ValueRepresentationKind.Void) {
            Report(context, assignment,
                $"cannot assign void to variable '{variable.Name}'");
            return;
        }

        var prior = context.GetMetadata<VariableAssignedTypeMetadata>(variable);
        if (rhsMeta.Kind is ValueRepresentationKind.Unknown) {
            if (prior is null)
                context.SetMetadata(variable, new VariableAssignedTypeMetadata(null, rhsMeta.Kind));
            else if (prior.Kind is not ValueRepresentationKind.Unknown)
                Report(context, assignment,
                    $"cannot assign '{TypeLabel(rhsMeta.ClrType, rhsMeta.Kind)}' to variable '{variable.Name}' (incompatible with prior '{TypeLabel(prior.ClrType, prior.Kind)}')");
            return;
        }

        var rhsType = rhsMeta.ClrType;
        if (rhsType is not null && CategoryOf(rhsType) is Cat.Null)
            return;

        if (prior is null) {
            context.SetMetadata(variable, new VariableAssignedTypeMetadata(rhsType, rhsMeta.Kind));
            return;
        }

        if (prior.Kind != rhsMeta.Kind) {
            Report(context, assignment,
                $"cannot assign '{TypeLabel(rhsType, rhsMeta.Kind)}' to variable '{variable.Name}' (incompatible with prior '{TypeLabel(prior.ClrType, prior.Kind)}')");
            return;
        }

        if (prior.ClrType is null || rhsType is null || prior.ClrType == rhsType)
            return;

        var destDef = context.TypeDefinitions.GetTypeDefinition(prior.ClrType);
        var srcDef = context.TypeDefinitions.GetTypeDefinition(rhsType);
        if (destDef is not null && srcDef is not null && destDef.IsAssignableFrom(srcDef))
            return;

        if (IsIeeeScalar(prior.ClrType) != IsIeeeScalar(rhsType)) {
            Report(context, assignment,
                $"cannot assign '{rhsType.Name}' to variable '{variable.Name}' (incompatible with prior '{prior.ClrType.Name}')");
            return;
        }

        var pc = CategoryOf(prior.ClrType);
        var rc = CategoryOf(rhsType);
        if (pc is Cat.Unknown || rc is Cat.Unknown || !Compatible(pc, prior.ClrType, rc, rhsType)) {
            Report(context, assignment,
                $"cannot assign '{rhsType.Name}' to variable '{variable.Name}' (incompatible with prior '{prior.ClrType.Name}')");
        }
    }

    private static string TypeLabel(Type? type, ValueRepresentationKind kind) =>
        type?.Name ?? kind.ToString();

    private static bool IsIeeeScalar(Type type) =>
        type == typeof(float) || type == typeof(double);

    private void CheckAssign(AnalysisContext context, Member destination, Node value) {
        var target = ClrTypeOf(context, destination);
        var rhs = ClrTypeOf(context, value);
        if (target is null || rhs is null) return;
        var destDef = context.TypeDefinitions.GetTypeDefinition(target);
        var srcDef = context.TypeDefinitions.GetTypeDefinition(rhs);
        if (destDef is not null && srcDef is not null && destDef.IsAssignableFrom(srcDef))
            return;
        var tc = CategoryOf(target);
        var rc = CategoryOf(rhs);
        if (tc is Cat.Unknown or Cat.Null || rc is Cat.Unknown or Cat.Null) return;
        if (tc is Cat.Enum && rc is Cat.Text) return;
        if (!Compatible(tc, target, rc, rhs, destThenSource: true))
            Report(context, destination,
                $"cannot assign '{rhs.Name}' to '{destination.MemberName}' (type '{target.Name}')");
    }

    private void CheckTypeCast(AnalysisContext context, TypeCast typeCast) {
        var source = ClrTypeOf(context, typeCast.Operand);
        var dest = ClrTypeOf(context, typeCast);
        if (source is null || dest is null || source == dest)
            return;

        var destDef = context.TypeDefinitions.GetTypeDefinition(dest);
        var srcDef = context.TypeDefinitions.GetTypeDefinition(source);
        if (destDef is not null && srcDef is not null) {
            if (destDef.IsAssignableFrom(srcDef))
                return;
            if (destDef.GetConversionFrom(srcDef) is not null)
                return;
            if (srcDef.IsAssignableFrom(destDef))
                return;
        }

        var sc = CategoryOf(source);
        var dc = CategoryOf(dest);
        if (sc is Cat.Unknown or Cat.Null || dc is Cat.Unknown or Cat.Null)
            return;
        if (sc is Cat.Number && dc is Cat.Number)
            return;
        if (dc is Cat.Enum && sc is Cat.Number)
            return;
        if (sc is Cat.Enum && dc is Cat.Number)
            return;

        Report(context, typeCast,
            $"cannot convert '{source.Name}' to '{dest.Name}'");
    }

    private void Report(AnalysisContext context, Node node, string message) =>
        context.ReportError(node, message, DiagnosticCode);

    private enum Cat { Text, Number, Boolean, Date, Enum, Guid, Null, Unknown }

    private static Type? ClrTypeOf(AnalysisContext context, Node node) =>
        context.GetMetadata<ValueRepresentationMetadata>(node)?.ClrType;

    private static Cat CategoryOf(Type type) {
        if (type == typeof(bool)) return Cat.Boolean;
        if (type == typeof(string) || type == typeof(char)) return Cat.Text;
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan))
            return Cat.Date;
        if (type.IsEnum) return Cat.Enum;
        if (type == typeof(Guid)) return Cat.Guid;
        if (type.IsPrimitive || type == typeof(decimal)) return Cat.Number;
        if (type.IsValueType && Nullable.GetUnderlyingType(type) is { } underlying)
            return CategoryOf(underlying);
        return Cat.Unknown;
    }

    private static bool Compatible(Cat lc, Type left, Cat rc, Type right, bool destThenSource = false) {
        if (lc == rc && lc is not Cat.Date) return true;
        if (lc == rc && lc is Cat.Date) {
            bool leftTs = IsClrTimestamp(left);
            bool rightTs = IsClrTimestamp(right);
            bool leftCal = left == typeof(DateOnly);
            bool rightCal = right == typeof(DateOnly);
            if (leftTs == rightTs && leftCal == rightCal)
                return true;
            if (leftCal && rightCal)
                return true;
            if (leftTs && rightTs)
                return true;
            if ((leftCal || leftTs) && (rightCal || rightTs)) {
                if (!destThenSource)
                    return true;
                return leftTs && rightCal;
            }
            return false;
        }
        if (lc is Cat.Enum && rc is Cat.Text) return true;
        if (rc is Cat.Enum && lc is Cat.Text) return true;
        return false;
    }

    private static bool IsClrTimestamp(Type type) =>
        type == typeof(DateTime) || type == typeof(DateTimeOffset);
}

/// <summary>First assignment's representation for a <see cref="Variable"/> in this analysis.
/// Later writes must be slot-compatible or <see cref="SyntaxTypeCompatibilityAnalyzer"/> errors.</summary>
internal sealed record VariableAssignedTypeMetadata(
    Type? ClrType,
    ValueRepresentationKind Kind
) : IAnalysisMetadata;