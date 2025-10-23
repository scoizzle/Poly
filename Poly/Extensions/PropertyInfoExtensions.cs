using System.Reflection;

namespace Poly.Extensions;

public static class PropertyInfoExtensions {
    public static bool IsStatic(this PropertyInfo source, bool nonPublic = false)
        => source.GetAccessors(nonPublic).Any(x => x.IsStatic);
}