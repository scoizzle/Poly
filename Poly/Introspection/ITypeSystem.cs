namespace Poly.Introspection;

/// <summary>
/// A type system: the definitions it prescribes and how they are resolved, the words it binds to them, and the rules
/// that relate them (identity, nullability, element type, assignability, conversion resolution, operator resolution
/// and binary-operator promotion). A definition declares only itself; what the definitions mean together is the
/// system's. A system may be layered on another (<see cref="Target"/>) and answers what it does not own from it.
/// Decision: docs/decisions/2026-11-19-a-type-system-owns-definitions-vocabulary-and-rules.md.
/// </summary>
public interface ITypeSystem {
    /// <summary>The system this one is layered on, or null for a base system.</summary>
    ITypeSystem? Target { get; }

    /// <summary>The sources whose definitions the system prescribes (#998 S1).</summary>
    IReadOnlyList<ITypeSource> Sources { get; }

    /// <summary>The definition a qualified name names, or null. A bare name is the analysis scope's to bind (#998 S2a).</summary>
    ITypeDefinition? Resolve(string qualifiedName);

    /// <summary>The definition one of the system's own words binds (a keyword, a prelude name), or null.</summary>
    ITypeDefinition? Bind(string word);

    /// <summary>The system's own word for <paramref name="definition"/>, or null when it spells it by its name.</summary>
    string? Spell(ITypeDefinition definition);

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> are the same type under this system's identity.</summary>
    bool IsSame(ITypeDefinition a, ITypeDefinition b);

    /// <summary>The type <paramref name="definition"/> is this system's nullable form of, or null.</summary>
    ITypeDefinition? NullableOf(ITypeDefinition definition);

    /// <summary>The element type <paramref name="definition"/> yields as a sequence, or null.</summary>
    ITypeDefinition? ElementType(ITypeDefinition definition);

    /// <summary>True when a <paramref name="source"/> value is a <paramref name="destination"/> value with no conversion.</summary>
    bool IsAssignableWithoutConversion(ITypeDefinition destination, ITypeDefinition source);

    /// <summary>
    /// The conversion from <paramref name="source"/> to <paramref name="destination"/>, or null: the system's global
    /// rules (promotion, widening, nullable lifting, built-in conversions) combined with the conversions both types
    /// declare.
    /// </summary>
    ITypeConversion? Conversion(ITypeDefinition source, ITypeDefinition destination);

    /// <summary>
    /// The operator <paramref name="kind"/> on <paramref name="left"/> and <paramref name="right"/> (null for a unary
    /// kind), or null when none applies or several do. The system decides, whichever systems the operands come from:
    /// it reads the operators both types declare and applies its own promotion and predefined operators.
    /// </summary>
    ITypeOperator? Operator(OperatorKind kind, ITypeDefinition left, ITypeDefinition? right);

    /// <summary>
    /// The system's <typeparamref name="TRule"/>: one registered with it, else the system itself when it implements the
    /// rule, else its <see cref="Target"/>'s; null when none answers.
    /// </summary>
    TRule? Rule<TRule>() where TRule : class, ITypeSystemRule;
}