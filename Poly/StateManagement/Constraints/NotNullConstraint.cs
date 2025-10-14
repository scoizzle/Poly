namespace Poly.StateManagement;

// public sealed class NotNullConstraint(Property resourceProperty) : Constraint(resourceProperty)
// {
//     public override Expression BuildExpression(Expression param)
//     {
//         var property = Expression.Property(param, Member.Name);
//         if (property.Type.IsValueType && Nullable.GetUnderlyingType(property.Type) == null)
//         {
//             // Non-nullable value types are always "not null"
//             return Expression.Constant(true);
//         }

//         var nullConstant = Expression.Constant(null, property.Type);
//         return Expression.NotEqual(property, nullConstant);
//     }
// }
