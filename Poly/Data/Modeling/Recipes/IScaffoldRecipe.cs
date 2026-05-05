namespace Poly.Data.Modeling.Recipes;

/// <summary>
/// Base interface for domain scaffolding recipes. Each recipe builds atomic, rollback-safe
/// domain structures using transactional mutations.
/// </summary>
public interface IScaffoldRecipe {
    /// <summary>Gets the recipe's human-readable name.</summary>
    string Name { get; }

    /// <summary>
    /// Applies the recipe to the given domain via transactional mutation.
    /// Must be idempotent: recipes should validate preconditions and fail fast on conflicts.
    /// </summary>
    void BuildInto(Domain domain);
}