#nullable enable

using System.Reflection;
using Poly.Serialization;

namespace Poly.Reflection;

public class CoreTypeMember : IMemberInterface
{        
    private CoreTypeMember() { }

    public required string Name { get; init; }

    public required ITypeInterface TypeInterface { get; init; }

    public required Func<object, object>? GetValueDelegate { get; init; }
    
    public required Action<object, object>? SetValueDelegate { get; init; }

    public required SerializeObjectDelegate Serialize { get; init; }

    public required DeserializeObjectDelegate Deserialize { get; init; }

    public bool TryGetValue(object instance, out object? value)
    {
        try {
            value = GetValueDelegate?.Invoke(instance);
            return true;
        }
        catch {
            value = default;
            return false;
        }
    }

    public bool TrySetValue(object instance, object value)
    {
        try {
            SetValueDelegate?.Invoke(instance, value);
            return true;
        }
        catch {
            return false;
        }
    }

    public static IEnumerable<IMemberInterface> GetMemberInterfacesForType(
        Type type)
    {
        foreach (var field in type.GetFields())
        {
            yield return From(field);
        }

        foreach (var property in type.GetProperties())
        {
            yield return From(property);
        }
    }

    public static CoreTypeMember From(
        FieldInfo info)
    {
        var typeInterface = TypeInterfaceRegistry.Get(info.FieldType);

        return new CoreTypeMember {
            Name = info.Name,
            TypeInterface = typeInterface!,
            GetValueDelegate = GetFieldReadMethod(info),
            SetValueDelegate = GetFieldWriteMethod(info),
            Serialize = typeInterface!.SerializeObject,
            Deserialize = typeInterface!.DeserializeObject
        };        
    }

    public static CoreTypeMember From(
        PropertyInfo info)
    {
        var typeInterface = TypeInterfaceRegistry.Get(info.PropertyType);

        return new CoreTypeMember {
            Name = info.Name,
            TypeInterface = typeInterface!,
            GetValueDelegate = GetPropertyReadMethod(info),
            SetValueDelegate = GetPropertyWriteMethod(info),
            Serialize = typeInterface!.SerializeObject,
            Deserialize = typeInterface!.DeserializeObject
        };        
    }

    private static Func<object, object> GetFieldReadMethod(
        FieldInfo fieldInfo)
    {
        if (fieldInfo.IsLiteral) {
            var defaultValue = fieldInfo.GetRawConstantValue();

            return (obj) => { return defaultValue!; };
        }

        var This = Expression.Parameter(typeof(object), "this");
        var conv = Expression.Convert(This, fieldInfo.DeclaringType!);
        var field = Expression.Field(fieldInfo.IsStatic ? null : conv, fieldInfo);
        var box = Expression.Convert(field, typeof(object));

        return Expression.Lambda<Func<object, object>>(box, This).Compile();
    }

    private static Action<object, object>? GetFieldWriteMethod(
        FieldInfo fieldInfo)
    {
        if (fieldInfo.IsInitOnly || fieldInfo.IsLiteral)
            return default;

        var This = Expression.Parameter(typeof(object), "this");
        var value = Expression.Parameter(typeof(object), "value");
        var typedThis = Expression.Convert(This, fieldInfo.DeclaringType!);
        var typedValue = Expression.Convert(value, fieldInfo.FieldType);
        var field = Expression.Field(fieldInfo.IsStatic ? null : typedThis, fieldInfo);
        var asign = Expression.Assign(field, typedValue);

        return Expression.Lambda<Action<object, object>>(asign, new[] { This, value }).Compile();
    }

    private static Func<object, object>? GetPropertyReadMethod(
        PropertyInfo propertyInfo) 
    {
        if (!propertyInfo.CanRead)
            return default;

        var method = propertyInfo.GetMethod;

        if (method?.GetParameters().Length != 0)
            return default;

        var isStatic = method?.IsStatic == true;

        var This = Expression.Parameter(typeof(object), "this");
        var conv = Expression.Convert(This, propertyInfo.DeclaringType!);
        var prop = Expression.Call(isStatic ? default : conv, method!);
        var box  = Expression.Convert(prop, typeof(object));

        return Expression.Lambda<Func<object, object>>(box, This).Compile();
    }

    private static Action<object, object>? GetPropertyWriteMethod(
        PropertyInfo propertyInfo) 
    {
        if (!propertyInfo.CanWrite)
            return default;

        var method = propertyInfo.SetMethod;

        if (method?.GetParameters().Length != 1)
            return default;

        var isStatic = method?.IsStatic == true;

        var This = Expression.Parameter(typeof(object), "this");
        var value  = Expression.Parameter(typeof(object), "value");
        var typedThis  = Expression.Convert(This, propertyInfo.DeclaringType!);
        var typedValue  = Expression.Convert(value, propertyInfo.PropertyType);
        var prop = Expression.Call(isStatic ? default : typedThis, method!, typedValue);

        return Expression.Lambda<Action<object, object>>(prop, new [] { This, value }).Compile();
    }
}