namespace Poly.StateManagement;

// public sealed class LengthConstraint : Constraint
// {
//     public LengthConstraint(Property property, int? minLength, int? maxLength) : base(property)
//     {
//         MinLength = minLength;
//         MaxLength = maxLength;
//     }

//     public int? MinLength { get; init; }
//     public int? MaxLength { get; init; }

//     public override Expression BuildExpression(Expression param)
//     {
//         var property = Expression.Property(param, Member.Name);
//         var lengthProperty = Expression.Property(property, "Length");

//         if (lengthProperty.Type != typeof(int))
//             throw new InvalidOperationException($"Property '{Member.Name}' must be a type that has a Length property of type int.");

//         var minValue = Expression.Constant(MinLength ?? null);
//         var minCheck = Expression.GreaterThanOrEqual(lengthProperty, minValue);

//         var maxValue = Expression.Constant(MaxLength ?? null);
//         var maxCheck = Expression.LessThanOrEqual(lengthProperty, maxValue);
//         return (MinLength, MaxLength) switch
//         {
//             (int, int) => Expression.AndAlso(minCheck, maxCheck),
//             (int, null) => minCheck,
//             (null, int) => maxCheck,
//             _ => Expression.Constant(true)
//         };
//     }
// }
