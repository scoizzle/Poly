namespace Poly.StateManagement.Constraints;

// public sealed class MaxValueConstraint(Property property, object maxValue) : Constraint(property)
// {
//     public object MaxValue { get; set; } = maxValue;

//     public override Expression BuildExpression(Expression param)
//     {
//         var property = Expression.Property(param, Member.Name);
//         var value = Expression.Constant(MaxValue);
//         var convertedValue = Expression.Convert(value, property.Type);
//         var constant = Expression.Constant(convertedValue);
//         return Expression.LessThanOrEqual(property, constant);
//     }

//     public override string ToString() => $"{Member.Name} <= {MaxValue}";
// }
