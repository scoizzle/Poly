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
        /// Optionality is ignored: the caller lifts it.
        /// </summary>
        public OperatorResolution? GetOperator(OperatorKind kind, ITypeDefinition? right) =>
            throw new NotImplementedException();
    }

    extension(IEnumerable<ITypeDefinition> types) {
        /// <summary>
        /// The unique member every other member is identical to or implicitly converts to, or null.
        /// </summary>
        public ITypeDefinition? GetCommonType() => throw new NotImplementedException();
    }
}