using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;

namespace Poly.DomainModeling.Lowering;

// Function calls: bind lowered arguments to the function's parameters and lower the body there, or emit the one
// System.Math call a built-in numeric name means (#547).

public sealed partial class DomainExpressionLoweringPass {
    /// <summary>
    /// A call binds each lowered argument to the function's parameter and lowers the body
    /// there, so the body's value reads the argument nodes directly (slice 2 of #406). A body is
    /// pure by analysis (DMFUN005): it names only its parameters, so the nested pass needs no
    /// subject. Fail closed here as well, so a call that skipped analysis never lowers to a
    /// wrong shape: an unknown function, wrong arity, or a recursive cycle throws.
    /// A name no declared function owns may be a built-in numeric function (#547); a declared function wins.
    /// </summary>
    protected override LoweredExpression FunctionCall(FunctionCall call) {
        if (BuiltinFunctions.Find(_domain, call.Name) is { } builtin)
            return LowerBuiltinCall(call, builtin);
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

    /// <summary>
    /// A built-in numeric call lowers to the one <c>System.Math</c> call with that meaning, so the interpreter and
    /// the printed C# execute one operation. The analyzer stamped the call's result type; its word picks the
    /// overload family (<c>decimal</c> or <c>double</c>). A whole result type (<c>Number</c>, <c>Int32</c>) needs no
    /// call for <c>Round</c>, <c>Floor</c> or <c>Ceiling</c>: the value is already whole, so it lowers to the operand
    /// itself, which is the exact C# answer for an integer. Anything else fails closed with a positioned error
    /// instead of emitting a shape that cannot run.
    /// </summary>
    private LoweredExpression LowerBuiltinCall(FunctionCall call, BuiltinFunction builtin) {
        if (call.Arguments.Count != builtin.Parameters.Count)
            throw new InvalidOperationException(
                $"Cannot lower call to '{call.Name}' (line {call.Line}, col {call.Col}): " +
                $"it expects {builtin.Parameters.Count} argument(s), got {call.Arguments.Count}.");
        var arguments = call.Arguments.Select(Route).ToList();
        var values = arguments.Select(argument => argument.Value).ToList();
        var word = _analysis.GetMetadata<TypedExpressionMetadata>(call)?.Type.Definition is { } result
            ? DomainTypeSystem.WordOf(result)
            : null;
        var value = (builtin.Function, word) switch {
            (BuiltinNumericFunction.Abs, "Decimal" or "Float" or "Number" or "Int32") =>
                MathCall("Abs", values[0]),
            (BuiltinNumericFunction.Min, "Decimal" or "Float" or "Number" or "Int32") =>
                MathCall("Min", values[0], values[1]),
            (BuiltinNumericFunction.Max, "Decimal" or "Float" or "Number" or "Int32") =>
                MathCall("Max", values[0], values[1]),
            (BuiltinNumericFunction.Floor, "Decimal" or "Float") => MathCall("Floor", values[0]),
            (BuiltinNumericFunction.Ceiling, "Decimal" or "Float") => MathCall("Ceiling", values[0]),
            (BuiltinNumericFunction.Round, "Decimal" or "Float") =>
                MathCall("Round", values[0], Digits(values[1]), AwayFromZero()),
            // An integral value is whole: rounding it, flooring it or taking its ceiling is that value.
            (BuiltinNumericFunction.Round or BuiltinNumericFunction.Floor or BuiltinNumericFunction.Ceiling,
                "Number" or "Int32") => values[0],
            _ => throw new InvalidOperationException(
                $"Cannot lower call to '{call.Name}' (line {call.Line}, col {call.Col}): " +
                $"the analyzer stated no numeric result type this lowering can emit (got '{word ?? "none"}')."),
        };
        return LoweredExpression.Combine([.. arguments, LoweredExpression.Of(value)], value);
    }

    /// <summary>A static <c>System.Math</c> call.</summary>
    private static Node MathCall(string method, params Node[] arguments) =>
        new Invoke(new Member(TypeReference.To(typeof(Math)), method), arguments);

    /// <summary>Round's digit count as the <c>int</c> the method takes; a non-literal digits is cast.</summary>
    private static Node Digits(Node digits) => digits switch {
        Constant { Value: int value } => new Constant(value),
        Constant { Value: long value } when value is >= int.MinValue and <= int.MaxValue => new Constant((int)value),
        _ => new TypeCast(digits, TypeReference.To<int>()),
    };

    /// <summary>Round's midpoint rule: away from zero, the money rule, never to even.</summary>
    private static Node AwayFromZero() =>
        new Member(TypeReference.To<MidpointRounding>(), nameof(MidpointRounding.AwayFromZero));
}