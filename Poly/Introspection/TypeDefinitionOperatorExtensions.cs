namespace Poly.Introspection;

/// <summary>
/// The consumer-facing door to a platform's operator rules. It reads only <c>ITypeDefinition.ResolveOperator</c> and
/// <c>ResolveConversionFrom</c>, never a platform type: which operators are candidates and how one is chosen is the
/// platform's rule (the .NET one is <c>ClrOperatorResolution</c>).
/// </summary>
internal static class TypeDefinitionOperatorExtensions {
    extension(ITypeDefinition left) {
        /// <summary>
        /// The operator <paramref name="kind"/> applied to <paramref name="left"/> and <paramref name="right"/>
        /// (null for a unary kind), or null when none applies or several do: fail closed. Optionality is ignored: the
        /// caller lifts it (T-06). The operand conversions the chosen operator needs are
        /// <c>parameter.ParameterTypeDefinition.ResolveConversionFrom(operand)</c>.
        /// </summary>
        public ITypeOperator? GetOperator(OperatorKind kind, ITypeDefinition? right) {
            ArgumentNullException.ThrowIfNull(left);
            var arity = OperatorShape.ArityOf(kind);
            if (arity == 2)
                ArgumentNullException.ThrowIfNull(right);
            else if (right is not null)
                throw new ArgumentException($"Operator '{kind}' is unary; a right operand was given.", nameof(right));
            return left.ResolveOperator(kind, right);
        }
    }

    extension(IEnumerable<ITypeDefinition> types) {
        /// <summary>
        /// The unique member every other member is identical to or implicitly converts to, or null.
        /// An empty set, no such member, or several (mutually convertible) is none. A single member is itself.
        /// </summary>
        /// <param name="system">The type system whose conversions decide (#998 T4c); null asks each candidate's door
        /// (interim, removed by T9).</param>
        public ITypeDefinition? GetCommonType(ITypeSystem? system = null) {
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
                    || TypeDefinitionExtensions.ConversionInto(candidate, other, system) is { Kind: ConversionOperatorKind.Implicit });
                if (!everyoneConverts)
                    continue;
                if (common is not null)
                    return null;
                common = candidate;
            }
            return common;
        }
    }
}