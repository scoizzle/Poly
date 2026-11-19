namespace Poly.Interpretation.CSharp;

/// <summary>
/// C#'s simple types (C# standard §8.3.5), with the native-sized integers: the closed list the C# specification itself
/// defines, owned by the C# type system (<see cref="ICSharpTypeRules.SimpleTypeOf"/>). No neutral contract names it.
/// Interim (#998 T6a, removed by T10): the CLR host's own built-in table still lists these numbers for its
/// built-in conversion and operator rules; T10 moves those rules into the C# system and folds the two into this one.
/// </summary>
internal enum SimpleType {
    Bool, Char, SByte, Byte, Short, UShort, Int, UInt, Long, ULong, NInt, NUInt, Float, Double, Decimal,
}