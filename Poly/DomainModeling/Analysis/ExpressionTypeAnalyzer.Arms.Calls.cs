using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

// Calls arm: function/contract-call argument typing (#426).

internal sealed partial class ExpressionTypeAnalyzer : INodeAnalyzer {
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
}