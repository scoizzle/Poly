namespace Poly.Tests.TestHelpers;

/// <summary>
/// The CLR host's ground types, in the order the host's former primitive enum listed them (#998 T10b). Several tests
/// that once enumerated that enum now enumerate these runtime types: the same 24 types, in the same order.
/// </summary>
internal static class ClrBuiltInTypes {
    public static readonly Type[] All = [
        typeof(bool), typeof(sbyte), typeof(short), typeof(int), typeof(long), typeof(byte), typeof(ushort),
        typeof(uint), typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(string), typeof(char),
        typeof(DateTime), typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan), typeof(Guid), typeof(byte[]),
        typeof(object), typeof(nint), typeof(nuint), typeof(DateTimeOffset),
    ];
}