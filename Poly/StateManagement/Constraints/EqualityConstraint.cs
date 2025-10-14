namespace Poly.StateManagement.Validation.Rules;

// public sealed class EqualityConstraint(Property property, object value) : Constraint(property)
// {
//     public object Value { get; set; } = value;

//     public override Expression BuildExpression(Expression param)
//     {
//         var property = Expression.Property(param, Member.Name);
//         var value = Expression.Constant(Value);
//         var convertedValue = Expression.Convert(value, property.Type);
//         var constant = Expression.Constant(convertedValue);
//         return Expression.Equal(property, constant);
//     }

//     public override string ToString() => $"{Member.Name} == {Value}";
// }
