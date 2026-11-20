namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The <c>System.Convert</c> method the CLR surface calls to convert to a destination type. A CLR encoding fact, so it
/// lives here: the analysis states the intent, the host names the method.
/// </summary>
internal static class ClrConvertMethods {
    /// <summary>The <c>Convert.To*</c> method name for the primitive <paramref name="destination"/> denotes, looking
    /// through a nullable form; null when the platform applies the conversion itself or the type has no method.</summary>
    public static string? NameOf(ITypeDefinition destination) {
        ArgumentNullException.ThrowIfNull(destination);
        var underlying = (destination as ClrTypeDefinition)?.NullableOf ?? destination;
        return underlying is ClrTypeDefinition clr ? NameOf(clr.RuntimeType) : null;
    }

    /// <summary>The <c>Convert.To*</c> method name for <paramref name="destination"/>, or null when none converts to it.</summary>
    public static string? NameOf(Type destination) => destination switch {
        Type t when t == typeof(double) => "ToDouble",
        Type t when t == typeof(float) => "ToSingle",
        Type t when t == typeof(decimal) => "ToDecimal",
        Type t when t == typeof(int) => "ToInt32",
        Type t when t == typeof(long) => "ToInt64",
        Type t when t == typeof(short) => "ToInt16",
        Type t when t == typeof(byte) => "ToByte",
        Type t when t == typeof(sbyte) => "ToSByte",
        Type t when t == typeof(uint) => "ToUInt32",
        Type t when t == typeof(ulong) => "ToUInt64",
        Type t when t == typeof(ushort) => "ToUInt16",
        Type t when t == typeof(char) => "ToChar",
        Type t when t == typeof(bool) => "ToBoolean",
        _ => null
    };
}