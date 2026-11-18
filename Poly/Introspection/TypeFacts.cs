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