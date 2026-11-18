namespace Poly.Introspection;

/// <summary>
/// A statement a platform makes about one of its types, asked for by its own type through
/// <see cref="ITypeDefinition.GetFact{TFact}"/>. The set is open: a consumer with a question of its own declares its
/// own fact, and the platform that knows the answer states it.
/// </summary>
public abstract record TypeFact;

/// <summary>
/// The shape facts Introspection's own consumers ask of a definition through
/// <see cref="ITypeDefinition.GetFact{TFact}"/>. A fact is a statement a platform makes about one of its types; the
/// set is open, so a consumer with a question of its own declares its own fact type and the platform that knows the
/// answer states it. Facts are not exclusive: a definition states every one that is true of it.
/// </summary>
/// <param name="Underlying">The type an enumeration's constants are values of, or null when the platform states none.</param>
public sealed record Enumeration(ITypeDefinition? Underlying) : TypeFact;

/// <summary>A value of the type is copied and compared by its content, not shared by reference.</summary>
public sealed record ValueSemantics : TypeFact {
    public static readonly ValueSemantics Instance = new();
}

/// <summary>The type is an interface: a contract of members a type implements, with no state of its own.</summary>
public sealed record InterfaceContract : TypeFact {
    public static readonly InterfaceContract Instance = new();
}

/// <summary>The type is a delegate: a value that is called.</summary>
public sealed record DelegateSignature : TypeFact {
    public static readonly DelegateSignature Instance = new();
}

/// <summary>The type is a ground type: its values are not described by parts the model can see into.</summary>
public sealed record Atomic : TypeFact {
    public static readonly Atomic Instance = new();
}

/// <summary>The type's values are the two truth values.</summary>
public sealed record Truth : TypeFact {
    public static readonly Truth Instance = new();
}

/// <summary>
/// The type's values are numbers, and this is how the platform represents them.
/// </summary>
/// <param name="IsIntegral">Whole numbers only.</param>
/// <param name="IsSigned">Negative values are representable.</param>
/// <param name="Bits">The width of the representation; the platform's word width when <paramref name="IsNativeSized"/>.</param>
/// <param name="IsDecimal">A base-ten fraction, exact for decimal literals; false for a binary floating point.</param>
/// <param name="IsNativeSized">The width is the platform word's, not fixed by the type.</param>
public sealed record Numeric(bool IsIntegral, bool IsSigned, int Bits, bool IsDecimal = false, bool IsNativeSized = false) : TypeFact;

/// <summary>The type's values are text.</summary>
/// <param name="IsSingleCharacter">Exactly one character, not a sequence of them.</param>
public sealed record Text(bool IsSingleCharacter) : TypeFact;

/// <summary>The type's values are points or spans of time.</summary>
/// <param name="HasDate">A calendar date is part of the value.</param>
/// <param name="HasTime">A time of day is part of the value.</param>
/// <param name="HasOffset">An offset from UTC is part of the value.</param>
/// <param name="IsDuration">A length of time, not a point in it.</param>
public sealed record Temporal(bool HasDate, bool HasTime, bool HasOffset = false, bool IsDuration = false) : TypeFact;

/// <summary>The type's values are globally unique identifiers.</summary>
public sealed record UniqueIdentifier : TypeFact {
    public static readonly UniqueIdentifier Instance = new();
}

/// <summary>The type's values are sequences of bytes with no further structure.</summary>
public sealed record Binary : TypeFact {
    public static readonly Binary Instance = new();
}

/// <summary>The type every value of the platform is a value of.</summary>
public sealed record TopType : TypeFact {
    public static readonly TopType Instance = new();
}