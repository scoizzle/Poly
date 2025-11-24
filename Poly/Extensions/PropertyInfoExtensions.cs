using System.Reflection;

namespace Poly.Extensions;

public static class PropertyInfoExtensions {
    public static bool IsStatic(this PropertyInfo source, bool nonPublic = false)
        => source.GetAccessors(nonPublic).Any(x => x.IsStatic);

    public static string GetDistinctName(this PropertyInfo source)
        => source.GetIndexParameters() switch {
            { Length: > 0 } @params => $"{source.Name}[{string.Join(", ", @params.Select(p => p.ParameterType.Name))}]",
            _ => source.Name
        };
}