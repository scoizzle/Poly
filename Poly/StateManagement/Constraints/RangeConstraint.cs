namespace Poly.StateManagement;

// public sealed class RangeConstraint : Constraint
// {
//     public RangeConstraint(Property property, IComparable? minValue, IComparable? maxValue) : base(property)
//     {
//         MinValue = minValue;
//         MaxValue = maxValue;
//     }

//     public IComparable? MinValue { get; init; }
//     public IComparable? MaxValue { get; init; }

//     public override Expression BuildExpression(Expression param)
//     {
//         var property = Expression.Property(param, Member.Name);

//         if (!typeof(IComparable).IsAssignableFrom(property.Type))
//             throw new InvalidOperationException($"Property '{Member.Name}' must implement IComparable.");

//         Expression? minCheck = null;
//         if (MinValue != null)
//         {
//             var minValueExpr = Expression.Constant(MinValue, property.Type);
//             minCheck = Expression.GreaterThanOrEqual(property, minValueExpr);
//         }

//         Expression? maxCheck = null;
//         if (MaxValue != null)
//         {
//             var maxValueExpr = Expression.Constant(MaxValue, property.Type);
//             maxCheck = Expression.LessThanOrEqual(property, maxValueExpr);
//         }

//         return (minCheck, maxCheck) switch
//         {
//             (Expression min, Expression max) => Expression.AndAlso(min, max),
//             (Expression min, null) => min,
//             (null, Expression max) => max,
//             _ => Expression.Constant(true)
//         };
//     }
// }
