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
            if (!genericDictType.IsAssignableFrom(instanceExpr.Type)) {
                throw new InvalidOperationException($"Instance expression type '{instanceExpr.Type.Name}' is not a dictionary-compatible type.");
            }
        }

        // Use TryGetValue pattern for safer dictionary access
        var dictType = typeof(IDictionary<string, object?>);

        // (IDictionary<string, object?>)instance
        var converted = Expression.Convert(instanceExpr, dictType);
        // (IDictionary<string, object?>)instance.TryGetValue
        var tryGetValueMethod = dictType.GetMethod("TryGetValue");
        var valueVar = Expression.Variable(typeof(object), "value");
        var keyExpr = Expression.Constant(_propertyName);
        // (IDictionary<string, object?>)instance.TryGetValue(key, out value)
        var tryGetValueCall = Expression.Call(converted, tryGetValueMethod!, keyExpr, valueVar);

        // If TryGetValue returns false, use default value for the target type
        var targetClrType = _memberType.ReflectedType;
        var defaultValue = Expression.Default(targetClrType);
        Expression convertedValue = targetClrType == typeof(object)
            ? valueVar
            : Expression.Convert(valueVar, targetClrType);

        return Expression.Block(
            [valueVar],
            Expression.Condition(
                tryGetValueCall,
                convertedValue,
                defaultValue
            )
        );
    }

    public override string ToString() => $"{_instance}.{_propertyName}";
}