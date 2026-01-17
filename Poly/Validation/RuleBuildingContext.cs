using Poly.Interpretation;
using Poly.Interpretation.Expressions;
using Poly.Introspection;

namespace Poly.Validation;

public sealed record RuleBuildingContext {
    private const string EntryPointName = "@value";

    public RuleBuildingContext(ITypeDefinition entryPointTypeDefinition)
    {
        ArgumentNullException.ThrowIfNull(entryPointTypeDefinition);
        Value = new NamedReference(EntryPointName);
        TypeDefinition = entryPointTypeDefinition;
    }

    private RuleBuildingContext(Interpretable value, ITypeDefinition typeDefinition)
    {
        Value = value;
        TypeDefinition = typeDefinition;
    }

    /// <summary>
    /// Gets the value being validated in the current context (used for constraints).
    /// For property constraints, use GetPropertyContext to access specific properties.
    /// For type rules, this is the property value.
    /// </summary>
    public Interpretable Value { get; private init; }

    /// <summary>
    /// Gets the type definition of the value being validated.
    /// </summary>
    public ITypeDefinition TypeDefinition { get; private init; }

    /// <summary>
    /// Creates a new context with the property value as the entry point
    /// </summary>
    /// <param name="propertyName">The name of the property to scope the context to.</param>
    /// <returns></returns>
    internal RuleBuildingContext GetPropertyContext(string propertyName)
    {
        var property = TypeDefinition.Properties.FirstOrDefault(p => p.Name == propertyName);
        if (property == null)
            throw new ArgumentException($"Property '{propertyName}' not found on type '{TypeDefinition.Name}'.", nameof(propertyName));

        var memberAccess = new MemberAccess(Value, property.Name);
        return new RuleBuildingContext(memberAccess, property.MemberTypeDefinition);
    }
}