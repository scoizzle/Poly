using Poly.DomainModeling.TypeExpressions;
using Poly.Introspection;

namespace Poly.Validation;

/// <summary>
/// Exception thrown when a constraint is applied to a type it is not compatible with.
/// </summary>
public class ConstraintApplicabilityException : InvalidOperationException {
    public Constraint Constraint { get; }
    public TypeExpression TypeExpression { get; }
    public string PropertyName { get; }

    public ConstraintApplicabilityException(
        string propertyName,
        Constraint constraint,
        TypeExpression typeExpression)
        : base(FormatMessage(propertyName, constraint, typeExpression))
    {
        PropertyName = propertyName;
        Constraint = constraint;
        TypeExpression = typeExpression;
    }

    public ConstraintApplicabilityException(
        string propertyName,
        Constraint constraint,
        TypeExpression typeExpression,
        Exception innerException)
        : base(FormatMessage(propertyName, constraint, typeExpression), innerException)
    {
        PropertyName = propertyName;
        Constraint = constraint;
        TypeExpression = typeExpression;
    }

    private static string FormatMessage(
        string propertyName,
        Constraint constraint,
        TypeExpression typeExpression)
    {
        var constraintName = constraint.GetType().Name;
        var applicableCategories = constraint.ApplicableCategories;

        return $"Constraint '{constraintName}' cannot be applied to property '{propertyName}' of type '{typeExpression}'. " +
               $"This constraint requires types with categories: {applicableCategories}, " +
               $"but the property type has categories: {typeExpression.Category}.";
    }
}