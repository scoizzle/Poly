namespace Poly.Introspection;

/// <summary>
/// The consumer-facing door to a platform's operator rules. It reads only
/// <c>ITypeDefinition.GetOperators</c> and <c>ResolveConversionFrom</c>, never a platform type.
/// </summary>
internal static class TypeDefinitionOperatorExtensions {
    extension(ITypeDefinition left) {
        /// <summary>
        /// The operator <paramref name="kind"/> applied to <paramref name="left"/> and <paramref name="right"/>
        /// (null for the unary <see cref="OperatorKind.Not"/>), or null when none applies or several do.
        /// </summary>
        /// <remarks>
        /// Candidates are the operators either operand declares. A candidate applies when each operand is
        /// identical to its parameter type or converts to it implicitly. A candidate whose parameters are all
        /// identical wins; otherwise exactly one applicable candidate is returned. Zero, or two or more of either,
        /// is none: fail closed. Optionality is ignored: the caller lifts it (T-06).
        /// The operand conversions the chosen operator needs are <c>parameter.ResolveConversionFrom(operand)</c>.
        /// </remarks>
        public OperatorResolution? GetOperator(OperatorKind kind, ITypeDefinition? right) {
            ArgumentNullException.ThrowIfNull(left);
            var arity = OperatorShape.ArityOf(kind);
            if (arity == 2)
                ArgumentNullException.ThrowIfNull(right);
            else if (right is not null)
                throw new ArgumentException($"Operator '{kind}' is unary; a right operand was given.", nameof(right));

            var operands = arity == 1 ? new[] { left } : new[] { left, right! };
            var candidates = new List<DeclaredOperator>();
            foreach (var declared in Declared(left, right, kind)) {
                if (declared.Signature.Parameters.Count() == arity && !candidates.Exists(c => SameOperator(c, declared)))
                    candidates.Add(declared);
            }

            var applicable = new List<(DeclaredOperator Operator, bool AllIdentical)>();
            foreach (var candidate in candidates) {
                var parameters = candidate.Signature.Parameters.ToArray();
                var allIdentical = true;
                var applies = true;
                for (var i = 0; i < arity && applies; i++) {
                    var parameterType = parameters[i].ParameterTypeDefinition;
                    if (TypeDefinitionExtensions.IsSameType(parameterType, operands[i]))
                        continue;
                    allIdentical = false;
                    applies = parameterType.ResolveConversionFrom(operands[i]) is { Kind: ConversionOperatorKind.Implicit };
                }
                if (applies)
                    applicable.Add((candidate, allIdentical));
            }

            var identical = applicable.Where(static a => a.AllIdentical).ToArray();
            var chosen = identical.Length > 0 ? identical : [.. applicable];
            return chosen.Length == 1
                ? new OperatorResolution(chosen[0].Operator.Signature, chosen[0].Operator.PlatformApplied)
                : null;
        }
    }

    extension(IEnumerable<ITypeDefinition> types) {
        /// <summary>
        /// The unique member every other member is identical to or implicitly converts to, or null.
        /// An empty set, no such member, or several (mutually convertible) is none. A single member is itself.
        /// </summary>
        public ITypeDefinition? GetCommonType() {
            ArgumentNullException.ThrowIfNull(types);
            var distinct = new List<ITypeDefinition>();
            foreach (var type in types) {
                if (type is null)
                    throw new ArgumentNullException(nameof(types), "A type in the set is null.");
                if (!distinct.Exists(d => TypeDefinitionExtensions.IsSameType(d, type)))
                    distinct.Add(type);
            }

            ITypeDefinition? common = null;
            foreach (var candidate in distinct) {
                var everyoneConverts = distinct.TrueForAll(other =>
                    TypeDefinitionExtensions.IsSameType(other, candidate)
                    || candidate.ResolveConversionFrom(other) is { Kind: ConversionOperatorKind.Implicit });
                if (!everyoneConverts)
                    continue;
                if (common is not null)
                    return null;
                common = candidate;
            }
            return common;
        }
    }

    private static IEnumerable<DeclaredOperator> Declared(ITypeDefinition left, ITypeDefinition? right, OperatorKind kind) {
        foreach (var declared in left.GetOperators(kind))
            yield return declared;
        if (right is null || TypeDefinitionExtensions.IsSameType(left, right))
            yield break;
        foreach (var declared in right.GetOperators(kind))
            yield return declared;
    }

    /// <summary>Same declaring type, name, parameter types and result: one operator reached twice.</summary>
    private static bool SameOperator(DeclaredOperator a, DeclaredOperator b) {
        var (x, y) = (a.Signature, b.Signature);
        if (!TypeDefinitionExtensions.IsSameType(x.DeclaringTypeDefinition, y.DeclaringTypeDefinition)
            || !string.Equals(x.Name, y.Name, StringComparison.Ordinal)
            || !TypeDefinitionExtensions.IsSameType(x.MemberTypeDefinition, y.MemberTypeDefinition))
            return false;
        var (px, py) = (x.Parameters.ToArray(), y.Parameters.ToArray());
        return px.Length == py.Length
            && px.Zip(py).All(static pair => TypeDefinitionExtensions.IsSameType(pair.First.ParameterTypeDefinition, pair.Second.ParameterTypeDefinition));
    }
}