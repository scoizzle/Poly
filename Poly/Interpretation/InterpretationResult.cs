namespace Poly.Interpretation;

/// <summary>
/// Encapsulates the result of interpreting an AST node, along with context and metadata contributed by middleware.
/// </summary>
/// <typeparam name="TResult">The type of the interpreted result (e.g., Expression, string, IL bytecode).</typeparam>
public sealed class InterpretationResult<TResult>(InterpretationContext<TResult> context, TResult value)
{
    /// <summary>
    /// The interpreted result value produced by the pipeline.
    /// Can be modified by middleware as needed.
    /// </summary>
    public TResult Value => value;

    /// <summary>
    /// Retrieves strongly-typed metadata by type.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to retrieve.</typeparam>
    /// <returns>The metadata instance if it exists; otherwise, null.</returns>
    public TMetadata? GetMetadata<TMetadata>() where TMetadata : class => context.GetMetadata<TMetadata>();

    /// <summary>
    /// Checks whether metadata of a given type has been set.
    /// </summary>
    /// <typeparam name="TMetadata">The metadata type to check for.</typeparam>
    /// <returns>True if metadata of this type exists; otherwise, false.</returns>
    public bool HasMetadata<TMetadata>() where TMetadata : class => context.HasMetadata<TMetadata>();
}
