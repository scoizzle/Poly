using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;

namespace Poly.Validation;

public sealed record RuleBuildingContext {
    private const string EntryPointName = "@value";

    public RuleBuildingContext(ITypeDefinition entryPointTypeDefinition)
    {
        ArgumentNullException.ThrowIfNull(entryPointTypeDefinition);

        // Use the entry point type as a type hint to aid semantic analysis.
        var typeName = entryPointTypeDefinition.FullName ?? entryPointTypeDefinition.Name;
        Value = new Parameter(EntryPointName, new TypeReference(typeName));
    }

    /// <summary>
    /// Gets the value being validated in the current context (used for constraints).
    /// For property constraints, use GetMemberAccessor to access specific properties.
    /// For type rules, this is the property value.
    /// </summary>
    public Node Value { get; private init; }

    /// <summary>
    /// Creates a new context with the property value as the entry point
    /// </summary>
    /// <param name="propertyName">The name of the property to scope the context to.</param>
    /// <returns></returns>
    internal RuleBuildingContext GetPropertyContext(string propertyName) => this with { Value = new MemberAccess(Value, propertyName) };
}