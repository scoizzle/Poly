namespace Poly.Validation.Constraints;

// public sealed class CollectionConstraint(Property property, int? minCount, int? maxCount, List<Rule>? elementRules) : Constraint(property)
// {
//     public int? MinCount { get; set; } = minCount;
//     public int? MaxCount { get; set; } = maxCount;
//     public List<Rule>? ElementRules { get; set; } = elementRules;

//     public override Expression BuildExpression(Expression param)
//     {
//         var property = Expression.Property(param, Member.Name);
//         var propertyType = property.Type;

//         if (propertyType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)) is not { } enumerableInterface)
//             throw new InvalidOperationException($"Property '{Member.Name}' must a type that implements IEnumerable<T>.");

//         var countCheck = GetElementCountCheckExpression(property, MinCount, MaxCount);
//         var rulesCheck = GetElementRulesCheckExpression(property, ElementRules);

//         return (countCheck, rulesCheck) switch
//         {
//             (null, null) => Expression.Constant(true),
//             (Expression countExpr, null) => countExpr,
//             (null, Expression rulesExpr) => rulesExpr,
//             (Expression countExpr, Expression rulesExpr) => Expression.AndAlso(countExpr, rulesExpr)
//         };
//     }

//     public override string ToString()
//     {
//         var sb = new StringBuilder();

//         if (MinCount.HasValue)
//         {
//             sb.Append($"{Member.Name}.Count >= {MinCount}");
//         }
//         if (MaxCount.HasValue)
//         {
//             if (sb.Length > 0)
//                 sb.Append(" and ");
//             sb.Append($"{Member.Name}.Count <= {MaxCount}");
//         }

//         if (ElementRules != null && ElementRules.Any())
//         {
//             if (sb.Length > 0)
//                 sb.Append(" and ");
//             sb.Append("rules: [");
//             bool first = true;
//             foreach (var rule in ElementRules)
//             {
//                 if (!first)
//                     sb.Append(", ");
//                 sb.Append(rule);
//                 first = false;
//             }
//             sb.Append("]");
//         }

//         return sb.ToString();
//     }

//     static Expression? GetElementCountCheckExpression(Expression collectionExpression, int? minCount, int? maxCount)
//     {
//         if (minCount == null && maxCount == null)
//             return null;

//         var countExpression = GetCountExpression(collectionExpression);

//         return (minCount, maxCount) switch
//         {
//             (int min, int max) => Expression.AndAlso(
//                 Expression.GreaterThanOrEqual(countExpression, Expression.Constant(min)),
//                 Expression.LessThanOrEqual(countExpression, Expression.Constant(max))
//             ),
//             (int min, null) => Expression.GreaterThanOrEqual(countExpression, Expression.Constant(min)),
//             (null, int max) => Expression.LessThanOrEqual(countExpression, Expression.Constant(max)),
//             _ => null
//         };

//         static Expression GetCountExpression(Expression collectionExpression)
//         {
//             var countProperty = collectionExpression.Type.GetProperty("Count");
//             if (countProperty != null)
//             {
//                 return Expression.Property(collectionExpression, countProperty);
//             }

//             var countMethod = collectionExpression.Type.GetMethod("Count");
//             if (countMethod != null)
//             {
//                 return Expression.Call(collectionExpression, countMethod);
//             }

//             var enumerableCountMethod = typeof(Enumerable).GetMethods()
//                 .FirstOrDefault(m => m.Name == "Count" && m.GetParameters().Length == 1)?
//                 .MakeGenericMethod(collectionExpression.Type.GetGenericArguments()[0]);

//             if (enumerableCountMethod != null)
//             {
//                 return Expression.Call(enumerableCountMethod, collectionExpression);
//             }

//             throw new InvalidOperationException("Unable to find a suitable Count property or method.");
//         }
//     }

//     static Expression? GetElementRulesCheckExpression(Expression collectionExpression, List<Rule>? rules)
//     {
//         if (rules == null || !rules.Any())
//             return null;

//         var elementType = collectionExpression.Type.GetGenericArguments().FirstOrDefault() ?? typeof(object);
//         var elementParam = Expression.Parameter(elementType, "e");

//         Expression? combinedRuleExpression = rules.Aggregate<Rule, Expression>(
//             seed: Expression.Constant(true),
//             func: (current, rule) => Expression.AndAlso(current, rule.BuildExpression(elementParam))
//         );

//         foreach (var rule in rules)
//         {
//             var ruleExpression = rule.BuildExpression(elementParam);
//             combinedRuleExpression = combinedRuleExpression == null
//                 ? ruleExpression
//                 : Expression.AndAlso(combinedRuleExpression, ruleExpression);
//         }

//         if (combinedRuleExpression == null)
//             return null;

//         var anyMethod = typeof(Enumerable).GetMethods()
//             .First(m => m.Name == "All" && m.GetParameters().Length == 2)
//             .MakeGenericMethod(elementType);

//         return Expression.Call(anyMethod, collectionExpression, Expression.Lambda(combinedRuleExpression, elementParam));
//     }
// }
