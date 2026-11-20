namespace Poly.Introspection;

/// <summary>
/// The base every type-system rule implements: a question a consumer, or the language that owns it, asks a type system
/// (<see cref="ITypeSystem.Rule{TRule}"/>). A rule is declared outside Introspection by its owner, so the set of questions
/// is open while no list of kinds or categories stands on the contracts. Empty by design, and it never classifies.
/// </summary>
public interface ITypeSystemRule;