namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The CLR view of a definition: its <see cref="Type"/>, when the platform has one. A CLR fact, so it lives with the
/// CLR provider; a definition from a host with no CLR types has none, and the platform answers null.
/// </summary>
internal static class TypeDefinitionRuntimeTypeExtensions {
    extension(ITypeDefinition typeDefinition) {
        public bool TryGetRuntimeType([NotNullWhen(true)] out Type? runtimeType) {
            ArgumentNullException.ThrowIfNull(typeDefinition);

            if (typeDefinition is IClrTypeDefinition clr) {
                runtimeType = clr.RuntimeType;
                return true;
            }

            runtimeType = null;
            return false;
        }

        public Type? GetRuntimeType() {
            return typeDefinition is IClrTypeDefinition clr ? clr.RuntimeType : null;
        }

        public Type GetRuntimeTypeOrThrow() {
            return typeDefinition is IClrTypeDefinition clr
                ? clr.RuntimeType
                : throw new InvalidOperationException($"Type '{typeDefinition.FullName}' does not have a common language runtime type.");
        }
    }
}