namespace Poly.StateManagement.Constraints;

// public sealed class MinValueConstraint(Property property, object minValue) : Constraint(property)
// {
//     public object MinValue { get; set; } = minValue;

//     public override Expression BuildExpression(Expression param)
//     {
//         var property = Expression.Property(param, Member.Name);
//         var value = Expression.Constant(MinValue);
//         var convertedValue = Expression.Convert(value, property.Type);
//         var constant = Expression.Constant(convertedValue);
//         return Expression.GreaterThanOrEqual(property, constant);
//     }

//     public override string ToString() => $"{Member.Name} >= {MinValue}";
// }