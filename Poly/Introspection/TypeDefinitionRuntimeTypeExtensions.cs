namespace Poly.Introspection;

using Poly.Introspection.CommonLanguageRuntime;

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