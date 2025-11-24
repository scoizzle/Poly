using System.Collections;
using System.Linq.Expressions;
using Poly.Interpretation;
using Poly.Introspection;

namespace Poly.DataModeling.Interpretation;

/// <summary>
/// Accesses a dynamic object's property by name using IDictionary<string, object?> semantics.
/// </summary>
internal sealed class DataModelPropertyAccessor : Value {
    private readonly Value _instance;
    private readonly string _propertyName;
    private readonly ITypeDefinition _memberType;

    public DataModelPropertyAccessor(Value instance, string propertyName, ITypeDefinition memberType) {
        _instance = instance ?? throw new ArgumentNullException(nameof(instance));
        _propertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        _memberType = memberType ?? throw new ArgumentNullException(nameof(memberType));
    }

    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) => _memberType;

    public override Expression BuildExpression(InterpretationContext context) {
        var instanceExpr = _instance.BuildExpression(context);

        // Support object -> IDictionary<string, object?> via casting where possible
        var idictType = typeof(IDictionary);
        if (!idictType.IsAssignableFrom(instanceExpr.Type)) {
            // Try dictionary of string to object
            var genericDictType = typeof(IDictionary<,>).MakeGenericType(typeof(string), typeof(object));
            if (genericDictType.IsAssignableFrom(instanceExpr.Type)) {
                // ok
            } else {
                throw new InvalidOperationException($"Instance expression type '{instanceExpr.Type.Name}' is not a dictionary-compatible type.");
            }
        }

        // We will call the indexer: ((IDictionary<string, object>)instance)[key]
        var dictType = typeof(IDictionary<string, object>);
        var converted = Expression.Convert(instanceExpr, dictType);
        var indexer = dictType.GetProperty("Item");
        var keyExpr = Expression.Constant(_propertyName);
        var indexAccess = Expression.Property(converted, indexer!, keyExpr);

        // Convert to the expected CLR type of the member
        var targetClrType = _memberType.ReflectedType;
        // If member type is object or same type, return as-is
        if (targetClrType == typeof(object)) return indexAccess;
        return Expression.Convert(indexAccess, targetClrType);
    }

    public override string ToString() => $"{_instance}.{_propertyName}";
}
