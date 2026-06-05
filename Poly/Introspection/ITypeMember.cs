namespace Poly.Introspection;

/// <summary>
/// Describes the mutability and mutation-causing characteristics of a member.
/// This is the canonical first-class concept for answering questions like:
/// "Is this thing mutable, or does accessing/using it cause mutations?"
/// 
/// Used by SideEffectAnalysis (for HasSideEffects / elision decisions),
/// ControlFlowAnalysis (HasMutationToVars for termination and reachability),
/// ConstantFolding, emission, and other insight passes.
/// 
/// Flags allow combinations (e.g. a volatile readonly field). Note that CompileTimeConst value already includes the ReadOnlyAfterInit bit.
/// Clr implementations derive from reflection (IsLiteral, IsInitOnly, IsVolatile modreqs) with safe fallbacks (false / Mutable) when unknowable.
/// </summary>
[Flags]
public enum Mutability {
    /// <summary>Default: the member can be read and written without special restrictions.</summary>
    Mutable = 0,

    /// <summary>
    /// The member can be read freely but written only during initialization
    /// (e.g. readonly fields, init-only setters, get-only properties).
    /// </summary>
    ReadOnlyAfterInit = 1 << 0,

    /// <summary>
    /// The member's value is known at compile time (C# 'const' / literal).
    /// The named value `CompileTimeConst` already includes the `ReadOnlyAfterInit` bit
    /// (CompileTimeConst implies ReadOnlyAfterInit).
    /// Enables aggressive folding and "no mutation possible" assumptions.
    /// </summary>
    CompileTimeConst = ReadOnlyAfterInit | (1 << 1),

    /// <summary>
    /// Accesses (read or write) to the member have "volatile" semantics:
    /// un-knowable external impact (hardware, other threads, memory-mapped I/O, etc.).
    /// Accesses must be performed as written; no elision or reordering across other volatile accesses.
    /// Can be combined with ReadOnlyAfterInit etc.
    /// </summary>
    VolatileAccess = 1 << 2,
}

/// <summary>
/// Represents a member of a type (field, property, or method) in the introspection system.
/// Implementations should be immutable and safe for concurrent reads.
/// </summary>
public interface ITypeMember {
    /// <summary>
    /// Gets the member name. For indexers, this is typically "Item".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the type of the member (field/property type or method return type).
    /// </summary>
    ITypeDefinition MemberTypeDefinition { get; }

    /// <summary>
    /// Gets the type that declares this member.
    /// </summary>
    ITypeDefinition DeclaringTypeDefinition { get; }

    /// <summary>
    /// Gets parameters for callable members (methods) or index parameters for indexer properties.
    /// Returns null for fields and parameterless properties.
    /// </summary>
    IEnumerable<IParameter>? Parameters { get; }

    /// <summary>
    /// Gets the visibility of this member.
    /// </summary>
    AccessModifier AccessModifier { get; }

    /// <summary>
    /// Gets whether this member is instance- or type-scoped.
    /// </summary>
    LifetimeModifier LifetimeModifier { get; }

    /// <summary>
    /// Gets the mutability and mutation semantics of this member.
    /// See <see cref="Mutability"/> for details and values.
    /// </summary>
    Mutability Mutability { get; }
}