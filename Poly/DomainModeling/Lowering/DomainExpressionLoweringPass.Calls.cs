using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Lowering;

// Function calls: bind lowered arguments to the function's parameters and lower the body there.

public sealed partial class DomainExpressionLoweringPass {
    /// <summary>
    /// A call binds each lowered argument to the function's parameter and lowers the body
    /// there, so the body's value reads the argument nodes directly (slice 2 of #406). A body is
    /// pure by analysis (DMFUN005): it names only its parameters, so the nested pass needs no
    /// subject. Fail closed here as well, so a call that skipped analysis never lowers to a
    /// wrong shape: an unknown function, wrong arity, or a recursive cycle throws.
    /// </summary>
    protected override LoweredExpression FunctionCall(FunctionCall call) {
        var function = _domain.Functions.FirstOrDefault(f => string.Equals(f.Name, call.Name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Cannot lower call to '{call.Name}' (line {call.Line}, col {call.Col}): " +
                "no such function is declared in the domain being lowered.");
        if (call.Arguments.Count != function.Parameters.Count)
            throw new InvalidOperationException(
                $"Cannot lower call to '{call.Name}' (line {call.Line}, col {call.Col}): " +
                $"it expects {function.Parameters.Count} argument(s), got {call.Arguments.Count}.");
        if (_activeCalls.Contains(call.Name, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Cannot lower call to '{call.Name}' (line {call.Line}, col {call.Col}): " +
                $"recursion ({string.Join(" -> ", _activeCalls)} -> {call.Name}) is not supported.");

        var arguments = call.Arguments.Select(Route).ToList();
        var bound = new Dictionary<string, Node>(StringComparer.Ordinal);
        for (var i = 0; i < arguments.Count; i++)
            bound[function.Parameters[i].Name] = arguments[i].Value;

        var nested = new DomainExpressionLoweringPass(_context with {
            Parameters = bound,
            ActionParameterNames = null,
            EnumPropertyNames = null,
        }) { _activeCalls = [.. _activeCalls, call.Name] };
        var body = nested.LowerExpression(function.Body, _currentSubject);
        return LoweredExpression.Combine([.. arguments, body], body.Value);
    }
}