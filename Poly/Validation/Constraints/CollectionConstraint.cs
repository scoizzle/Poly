namespace Poly.Validation.Constraints;
using Poly.Interpretation.AbstractSyntaxTree;

// public sealed class CollectionConstraint(Property property, int? minCount, int? maxCount, List<Rule>? elementRules) : Constraint(property)
// {
//     public int? MinCount { get; set; } = minCount;
//     public int? MaxCount { get; set; } = maxCount;
//     public List<Rule>? ElementRules { get; set; } = elementRules;

//     // public override Node BuildNode(Node param)
//     // {
//     //     var property = Node.Property(param, Member.Name);
//     //     var propertyType = property.Type;

//     //     if (propertyType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)) is not { } enumerableInterface)
//     //         throw new InvalidOperationException($"Property '{Member.Name}' must a type that implements IEnumerable<T>.");

//     //     var countCheck = GetElementCountCheckNode(property, MinCount, MaxCount);
//     //     var rulesCheck = GetElementRulesCheckNode(property, ElementRules);

//     //     return (countCheck, rulesCheck) switch
//     //     {
//     //         (null, null) => Node.Constant(true),
//     //         (Node countExpr, null) => countExpr,
//     //         (null, Node rulesExpr) => rulesExpr,
//     //         (Node countExpr, Node rulesExpr) => Node.AndAlso(countExpr, rulesExpr)
//     //     };
//     // }

//     // public override string ToString()
//     // {
//     //     var sb = new StringBuilder();

//     //     if (MinCount.HasValue)
//     //     {
//     //         sb.Append($"{Member.Name}.Count >= {MinCount}");
//     //     }
//     //     if (MaxCount.HasValue)
//     //     {
//     //         if (sb.Length > 0)
//     //             sb.Append(" and ");
//     //         sb.Append($"{Member.Name}.Count <= {MaxCount}");
//     //     }

//     //     if (ElementRules != null && ElementRules.Any())
//     //     {
//     //         if (sb.Length > 0)
//     //             sb.Append(" and ");
//     //         sb.Append("rules: [");
//     //         bool first = true;
//     //         foreach (var rule in ElementRules)
//     //         {
//     //             if (!first)
//     //                 sb.Append(", ");
//     //             sb.Append(rule);
//     //             first = false;
//     //         }
//     //         sb.Append("]");
//     //     }

//     //     return sb.ToString();
//     // }

//     // static Node? GetElementCountCheckNode(Node collectionNode, int? minCount, int? maxCount)
//     // {
//     //     if (minCount == null && maxCount == null)
//     //         return null;

//     //     var countNode = GetCountNode(collectionNode);

//     //     return (minCount, maxCount) switch
//     //     {
//     //         (int min, int max) => Node.AndAlso(
//     //             Node.GreaterThanOrEqual(countNode, Node.Constant(min)),
//     //             Node.LessThanOrEqual(countNode, Node.Constant(max))
//     //         ),
//     //         (int min, null) => Node.GreaterThanOrEqual(countNode, Node.Constant(min)),
//     //         (null, int max) => Node.LessThanOrEqual(countNode, Node.Constant(max)),
//     //         _ => null
//     //     };

//     //     static Node GetCountNode(Node collectionNode)
//     //     {
//     //         var countProperty = collectionNode.Type.GetProperty("Count");
//     //         if (countProperty != null)
//     //         {
//     //             return Node.Property(collectionNode, countProperty);
//     //         }

//     //         var countMethod = collectionNode.Type.GetMethod("Count");
//     //         if (countMethod != null)
//     //         {
//     //             return Node.Call(collectionNode, countMethod);
//     //         }

//     //         var enumerableCountMethod = typeof(Enumerable).GetMethods()
//     //             .FirstOrDefault(m => m.Name == "Count" && m.GetParameters().Length == 1)?
//     //             .MakeGenericMethod(collectionNode.Type.GetGenericArguments()[0]);

//     //         if (enumerableCountMethod != null)
//     //         {
//     //             return Node.Call(enumerableCountMethod, collectionNode);
//     //         }

//     //         throw new InvalidOperationException("Unable to find a suitable Count property or method.");
//     //     }
//     // }

//     // static Node? GetElementRulesCheckNode(Node collectionNode, List<Rule>? rules)
//     // {
//     //     if (rules == null || !rules.Any())
//     //         return null;

//     //     var elementType = collectionNode.Type.GetGenericArguments().FirstOrDefault() ?? typeof(object);
//     //     var elementParam = Node.Parameter(elementType, "e");

//     //     Node? combinedRuleNode = rules.Aggregate<Rule, Node>(
//     //         seed: Node.Constant(true),
//     //         func: (current, rule) => Node.AndAlso(current, rule.BuildNode(elementParam))
//     //     );

//     //     foreach (var rule in rules)
//     //     {
//     //         var ruleNode = rule.BuildNode(elementParam);
//     //         combinedRuleNode = combinedRuleNode == null
//     //             ? ruleNode
//     //             : Node.AndAlso(combinedRuleNode, ruleNode);
//     //     }

//     //     if (combinedRuleNode == null)
//     //         return null;

//     //     var anyMethod = typeof(Enumerable).GetMethods()
//     //         .First(m => m.Name == "All" && m.GetParameters().Length == 2)
//     //         .MakeGenericMethod(elementType);

//     //     return Node.Call(anyMethod, collectionNode, Node.Lambda(combinedRuleNode, elementParam));
//     // }
// }