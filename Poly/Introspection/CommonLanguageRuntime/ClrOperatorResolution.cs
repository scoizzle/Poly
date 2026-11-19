namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// C#'s operator selection (12.4.4, 12.4.5), the rule <c>ITypeDefinition.ResolveOperator</c> answers with for the .NET
/// platform and for the C#-surface definitions the analysis builds.
/// </summary>
/// <remarks>
/// Candidates are the operators either operand offers beside the other: a CLR definition's are pair-aware
/// (<see cref="ClrTypeDefinition.OperatorsBeside"/>: <c>UInt32 + Int32</c> is <c>Int64</c>, which neither operand
/// declares), any other definition's are its <see cref="ITypeDefinition.Operators"/> of the kind. A candidate applies when
/// each operand is identical to its parameter type or converts to it implicitly. A candidate whose parameters are all
/// identical wins; otherwise exactly one applicable candidate is returned. Zero, or two or more of either, is none: fail
/// closed. This is not C# 12.6.4 better-conversion: the pair-aware built-in set leaves nothing for it to rank.
/// </remarks>
internal static class ClrOperatorResolution {
    /// <param name="convert">The conversion rule that decides whether an operand applies to a parameter
    /// (source, destination): a type system passes its own (#998 T3), so a foreign parameter type's door never decides.
    /// Null is each parameter type's own door, as the definition doors ask until T9.</param>
    public static ITypeOperator? Resolve(ITypeDefinition left, OperatorKind kind, ITypeDefinition? right,
        Func<ITypeDefinition, ITypeDefinition, ITypeConversion?>? convert = null) {
        ArgumentNullException.ThrowIfNull(left);
        convert ??= static (source, destination) => destination.ResolveConversionFrom(source);
        var arity = OperatorShape.ArityOf(kind);
        var operands = arity == 1 ? new[] { left } : new[] { left, right ?? throw new ArgumentNullException(nameof(right)) };

        var candidates = new List<ITypeOperator>();
        foreach (var offered in Offered(left, right, kind)) {
            if (offered.Parameters.Count() == arity && !candidates.Exists(c => SameOperator(c, offered)))
                candidates.Add(offered);
        }

        var applicable = new List<(ITypeOperator Operator, bool AllIdentical)>();
        foreach (var candidate in candidates) {
            var parameters = candidate.Parameters.ToArray();
            var allIdentical = true;
            var applies = true;
            for (var i = 0; i < arity && applies; i++) {
                var parameterType = parameters[i].ParameterTypeDefinition;
                if (TypeDefinitionExtensions.IsSameType(parameterType, operands[i]))
                    continue;
                allIdentical = false;
                applies = convert(operands[i], parameterType) is { Kind: ConversionOperatorKind.Implicit };
            }
            if (applies)
                applicable.Add((candidate, allIdentical));
        }

        var identical = applicable.Where(static a => a.AllIdentical).ToArray();
        var chosen = identical.Length > 0 ? identical : [.. applicable];
        return chosen.Length == 1 ? chosen[0].Operator : null;
    }

    private static IEnumerable<ITypeOperator> Offered(ITypeDefinition left, ITypeDefinition? right, OperatorKind kind) {
        foreach (var offered in OfferedBy(left, kind, right))
            yield return offered;
        if (right is null || TypeDefinitionExtensions.IsSameType(left, right))
            yield break;
        foreach (var offered in OfferedBy(right, kind, left))
            yield return offered;
    }

    private static IEnumerable<ITypeOperator> OfferedBy(ITypeDefinition definition, OperatorKind kind, ITypeDefinition? other) =>
        definition is ClrTypeDefinition clr
            ? clr.OperatorsBeside(kind, other)
            : definition.Operators.Where(o => o.Kind == kind);

    /// <summary>Same declaring type, member (or none), parameter types and result: one operator reached twice.</summary>
    private static bool SameOperator(ITypeOperator a, ITypeOperator b) {
        if (!TypeDefinitionExtensions.IsSameType(a.DeclaringTypeDefinition, b.DeclaringTypeDefinition)
            || !TypeDefinitionExtensions.IsSameType(a.ResultTypeDefinition, b.ResultTypeDefinition)
            || !string.Equals(a.Method?.Name, b.Method?.Name, StringComparison.Ordinal))
            return false;
        var (pa, pb) = (a.Parameters.ToArray(), b.Parameters.ToArray());
        return pa.Length == pb.Length
            && pa.Zip(pb).All(static pair => TypeDefinitionExtensions.IsSameType(pair.First.ParameterTypeDefinition, pair.Second.ParameterTypeDefinition));
    }
}