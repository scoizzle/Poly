namespace Poly.Interpretation.CSharp;

/// <summary>
/// C#'s simple types (C# standard §8.3.5), with the native-sized integers: the closed list the C# specification itself
/// defines, owned by the C# type system (<see cref="ICSharpTypeRules.SimpleTypeOf"/>). No neutral contract names it.
/// C#'s built-in conversion and operator tables key on it (<see cref="CSharpBuiltIns"/>; #998 T10 removed the CLR host's
/// own table).
/// </summary>
internal enum SimpleType {
    Bool, Char, SByte, Byte, Short, UShort, Int, UInt, Long, ULong, NInt, NUInt, Float, Double, Decimal,
}