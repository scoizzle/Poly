namespace Poly.Extensions;

public static class TypeExtensions {
    public static bool IsNullable(this Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

    public static string SafeName(this Type type) =>
        type.FullName ?? type.Name;
}