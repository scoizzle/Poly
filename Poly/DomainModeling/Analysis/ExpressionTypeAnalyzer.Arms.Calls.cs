using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

// Calls arm: function/contract-call argument typing (#426) and the built-in numeric result type (#547).

internal sealed partial class ExpressionTypeAnalyzer : INodeAnalyzer {
    /// <summary>
    /// A call's type: a declared function's return type, or a built-in numeric call's result — the operand's own
    /// type for <c>Abs</c>/<c>Floor</c>/<c>Ceiling</c>/<c>Round</c>, and the operands' common numeric type for
    /// <c>Min</c>/<c>Max</c>, resolved by the same operator rule arithmetic uses.
    /// </summary>
    private ExprType InferCall(
        AnalysisContext context, FunctionCall call,
        Dictionary<string, string>? props, Dictionary<string, string>? parameters, Dictionary<string, EnumType> enumTypes) {
        if (FunctionAnalysis.Find(context, call.Name) is { } function)
            return ResolveName(context, function.ReturnType.TypeName, enumTypes);
        if (BuiltinFunctions.Find(context.GetTypeLookup()?.Domain, call.Name) is not { } builtin)
            return ExprType.Unresolved;
        if (call.Arguments.Count != builtin.Parameters.Count)
            return ExprType.Unresolved;
        var arguments = call.Arguments
            .Select(argument => InferType(context, argument, props, parameters, enumTypes)).ToList();
        if (arguments.Any(argument => !IsNumeric(argument)))
            return ExprType.Unresolved;
        return builtin.Function is BuiltinNumericFunction.Min or BuiltinNumericFunction.Max
            ? CommonNumericType(context, arguments[0], arguments[1])
            : arguments[0] with { Optional = false };
    }

    /// <summary>The common numeric type two operands share, as the additive operator resolves it; unresolved when
    /// no operator unites them (<c>Decimal</c> with <c>Float</c>).</summary>
    private static ExprType CommonNumericType(AnalysisContext context, ExprType left, ExprType right) =>
        left.Definition is { } leftDefinition && right.Definition is { } rightDefinition
        && context.TypeSystem.Operator(Poly.Introspection.OperatorKind.Add, leftDefinition, rightDefinition) is { } resolution
            ? new ExprType(resolution.ResultTypeDefinition, Optional: false, DomainName: null)
            : ExprType.Unresolved;

    private void CheckCallArguments(
        AnalysisContext context, FunctionCall call,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        foreach (var argument in call.Arguments)
            WalkExpression(context, argument, props, parameters, enumTypes);
        // Unknown name and wrong arity are reported once per call by FunctionAnalysis.CheckCall.
        var function = FunctionAnalysis.Find(context, call.Name);
        if (function is null) {
            // #426: a name no function declares may be an imported contract endpoint.
            if (context.GetTypeLookup()?.Domain is { } domain
                && ContractCallResolver.IsExported(domain, call.Name)) {
                var argumentType = call.Arguments.Count == 1
                    ? InferType(context, call.Arguments[0], props, parameters, enumTypes).DomainName
                    : null;
                ContractCallResolver.Resolve(context, domain, call, argumentType);
                ContractCallResolver.RejectUnlowerable(context, call);
            }
            // #547: a built-in numeric name no declared function or endpoint owns.
            else if (BuiltinFunctions.Find(context.GetTypeLookup()?.Domain, call.Name) is { } builtin)
                CheckBuiltinArguments(context, call, builtin, props, parameters, enumTypes);
            return;
        }
        if (function.Parameters.Count != call.Arguments.Count)
            return;
        for (var i = 0; i < call.Arguments.Count; i++) {
            var parameterType = function.Parameters[i].Type.TypeName;
            var target = ResolveName(context, parameterType, enumTypes);
            var argument = InferLiteralAware(context, call.Arguments[i], parameterType, enumTypes, props, parameters);
            if (!IsTyped(target)) {
                RequireTypedTarget(context, call, parameterType);
                continue;
            }
            if (!IsTyped(argument)) {
                RequireTyped(context, call.Arguments[i], argument, call, props, parameters, enumTypes);
                continue;
            }
            // A parameter is never optional: a `null` argument or arm is refused (#606).
            if (AssignMismatch(call.Arguments[i], argument, target) is { } mismatch) {
                var where = call.Line > 0 ? $" (line {call.Line}, col {call.Col})" : "";
                context.ReportError(ReferenceEquals(mismatch.Where, call.Arguments[i]) ? call : mismatch.Where,
                    $"Argument '{function.Parameters[i].Name}' of '{call.Name}' expects '{parameterType}', " +
                    $"got '{Describe(mismatch.Source)}'{where}.",
                    DomainModelDiagnosticCodes.FunctionArgumentType);
            }
            else
                WarnIntegerDivisionIntoDecimal(context, call.Arguments[i], target, props, parameters, enumTypes);
        }
    }

    /// <summary>
    /// A built-in numeric call's arguments (#547): every one numeric, present, and of a word the lowering can
    /// emit (<c>Decimal</c>, <c>Number</c>, <c>Int32</c>, <c>Float</c>); <c>Min</c>/<c>Max</c> need a common type;
    /// <c>Round</c>'s digits is a whole <c>Number</c> in 0..28. The call is stamped with its result type, which is
    /// what lowering reads to pick the one <c>System.Math</c> overload family.
    /// </summary>
    private void CheckBuiltinArguments(
        AnalysisContext context, FunctionCall call, BuiltinFunction builtin,
        Dictionary<string, string> props, Dictionary<string, string>? parameters, Dictionary<string, EnumType> enumTypes) {
        if (call.Arguments.Count != builtin.Parameters.Count)
            return;
        var types = call.Arguments
            .Select(argument => InferType(context, argument, props, parameters, enumTypes)).ToList();
        for (var i = 0; i < call.Arguments.Count; i++) {
            var argument = call.Arguments[i];
            var parameter = builtin.Parameters[i];
            var type = types[i];
            if (!IsTyped(type)) {
                RequireTyped(context, argument, type, call, props, parameters, enumTypes);
                continue;
            }
            if (!IsNumeric(type)) {
                Report(context, argument,
                    $"Argument '{parameter}' of '{call.Name}' expects a numeric type, got '{Describe(type)}'.");
                continue;
            }
            if (type.Optional) {
                Report(context, argument,
                    $"Argument '{parameter}' of '{call.Name}' is an optional value (#547): a call has no meaning for an absent operand");
                continue;
            }
            if (DomainTypeSystem.WordOf(type.Definition) is not ("Decimal" or "Number" or "Int32" or "Float")) {
                Report(context, argument,
                    $"Argument '{parameter}' of '{call.Name}' supports Number, Int32, Float and Decimal (got '{Describe(type)}').");
                continue;
            }
            if (builtin.Function is BuiltinNumericFunction.Round && i == 1)
                CheckRoundDigits(context, call, argument, type);
        }
        if (builtin.Function is BuiltinNumericFunction.Min or BuiltinNumericFunction.Max
            && IsNumeric(types[0]) && IsNumeric(types[1])
            && CommonNumericType(context, types[0], types[1]).Definition is null)
            Report(context, call,
                $"'{call.Name}' needs two operands with a common numeric type " +
                $"(got '{Describe(types[0])}' and '{Describe(types[1])}').");
        var result = InferCall(context, call, props, parameters, enumTypes);
        if (result.Definition is not null)
            context.SetMetadata(call, new TypedExpressionMetadata(result));
    }

    /// <summary>Round's digits: a whole <c>Number</c> in the range <c>System.Math.Round</c> accepts.</summary>
    private static void CheckRoundDigits(
        AnalysisContext context, FunctionCall call, DomainExpression argument, ExprType type) {
        if (DomainTypeSystem.WordOf(type.Definition) is "Decimal" or "Float") {
            Report(context, argument,
                $"Argument 'digits' of '{call.Name}' expects 'Number', got '{Describe(type)}'.");
            return;
        }
        if (argument is Literal { Value: long or int } literal) {
            var digits = literal.Value switch { long value => value, int value => value, _ => 0L };
            if (digits is < 0 or > 28)
                Report(context, argument, $"'{call.Name}' digits {digits} is outside the supported range 0 to 28.");
        }
    }
}