using Poly.Introspection;

namespace Poly.Interpretation.Operators;

/// <summary>
/// Represents an index access operation (indexer access) in an interpretation tree.
/// </summary>
/// <remarks>
/// This operator enables accessing indexed members of a value using bracket notation (e.g., <c>array[0]</c> or <c>dictionary["key"]</c>).
/// The indexer is resolved at interpretation time using the type definition system, selecting the best match based on index argument types.
/// </remarks>
public sealed class IndexAccess(Value value, params IEnumerable<Value> indexArguments) : Operator {
    /// <summary>
    /// Gets the value whose indexer is being accessed.
    /// </summary>
    public Value Value { get; } = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>
    /// Gets the index arguments for the indexer.
    /// </summary>
    public IEnumerable<Value> IndexArguments { get; } = indexArguments ?? throw new ArgumentNullException(nameof(indexArguments));

    /// <summary>
    /// Gets the indexer member from the value's type definition.
    /// </summary>
    /// <param name="context">The interpretation context.</param>
    /// <returns>The indexer member metadata.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no indexer is found on the type.</exception>
    private ITypeMember GetIndexer(InterpretationContext context) {
        ITypeDefinition typeDefinition = Value.GetTypeDefinition(context);
        
        // Get all members named "Item" (indexers)
        var indexers = typeDefinition.Members.Where(m => m.Name == "Item").ToList();
        
        if (indexers.Count == 0) {
            throw new InvalidOperationException($"Indexer not found on type '{typeDefinition.Name}'.");
        }
        
        if (indexers.Count == 1) {
            return indexers[0];
        }
        
        // Multiple indexers found - attempt to resolve based on parameter count and types
        var indexerTypes = IndexArguments.Select(arg => arg.GetTypeDefinition(context)).ToArray();
        var argumentCount = IndexArguments.Count();
        
        // TODO: Implement proper overload resolution
        // For now, return the first indexer that matches parameter count
        var matchingIndexer = indexers.FirstOrDefault(idx => 
            idx is ITypeMember member && member.Parameters?.Count() == argumentCount);
        
        return matchingIndexer 
            ?? throw new InvalidOperationException($"No matching indexer found on type '{typeDefinition.Name}' for {argumentCount} argument(s).");
    }

    /// <inheritdoc />
    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) {
        return GetIndexer(context).MemberTypeDefinition;
    }

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context) {
        ITypeMember indexer = GetIndexer(context);
        Value memberAccessor = indexer.GetMemberAccessor(Value, IndexArguments);
        return memberAccessor.BuildExpression(context);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Value}[{string.Join(", ", IndexArguments)}]";
}