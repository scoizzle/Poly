using System.Reflection;

namespace Poly.Introspection.CommonLanguageRuntime;

internal static class ClrAccessModifierResolver {
    public static AccessModifier Resolve(Type type) {
        ArgumentNullException.ThrowIfNull(type);

        return type switch {
            _ when type.IsPublic || type.IsNestedPublic => AccessModifier.Public,
            _ when type.IsNestedPrivate => AccessModifier.Private,
            _ when type.IsNestedFamily || type.IsNestedFamORAssem || type.IsNestedFamANDAssem => AccessModifier.Protected,
            _ => AccessModifier.Internal
        };
    }

    public static AccessModifier Resolve(FieldInfo fieldInfo) {
        ArgumentNullException.ThrowIfNull(fieldInfo);

        return Resolve(
            isPublic: fieldInfo.IsPublic,
            isPrivate: fieldInfo.IsPrivate,
            isProtected: fieldInfo.IsFamily || fieldInfo.IsFamilyOrAssembly || fieldInfo.IsFamilyAndAssembly);
    }

    public static AccessModifier Resolve(MethodBase methodBase) {
        ArgumentNullException.ThrowIfNull(methodBase);

        return Resolve(
            isPublic: methodBase.IsPublic,
            isPrivate: methodBase.IsPrivate,
            isProtected: methodBase.IsFamily || methodBase.IsFamilyOrAssembly || methodBase.IsFamilyAndAssembly);
    }

    public static AccessModifier Resolve(PropertyInfo propertyInfo) {
        ArgumentNullException.ThrowIfNull(propertyInfo);

        var accessors = propertyInfo.GetAccessors(nonPublic: true);
        if (accessors.Length == 0) {
            throw new InvalidOperationException($"Property '{propertyInfo.DeclaringType?.FullName}.{propertyInfo.Name}' has no accessible accessors.");
        }

        return accessors
            .Select(Resolve)
            .OrderByDescending(GetPrecedence)
            .First();
    }

    private static AccessModifier Resolve(bool isPublic, bool isPrivate, bool isProtected) => (isPublic, isPrivate, isProtected) switch {
        (true, _, _) => AccessModifier.Public,
        (_, true, _) => AccessModifier.Private,
        (_, _, true) => AccessModifier.Protected,
        _ => AccessModifier.Internal
    };

    private static int GetPrecedence(AccessModifier modifier) => modifier switch {
        AccessModifier.Public => 3,
        AccessModifier.Protected => 2,
        AccessModifier.Internal => 1,
        _ => 0
    };
}