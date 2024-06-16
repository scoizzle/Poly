using System.Reflection;
using Poly.Serialization;

namespace Poly.Reflection;

[DebuggerDisplay("({TypeInterface.Type.Name}.{Name})")]
public class CoreTypeMember : IMemberAdapter
{
    private CoreTypeMember() { }

    public required StringView Name { get; init; }

    public required ITypeAdapter TypeInterface { get; init; }

    public required Func<object, object>? GetValueDelegate { get; init; }

    public required Action<object, object>? SetValueDelegate { get; init; }

    public bool TryGetValue(object instance, out object? value)
    {
        if (GetValueDelegate is null)
        {
            value = default;
            return false;
        }

        value = GetValueDelegate(instance);
        return true;
    }

    public bool TrySetValue(object instance, object value)
    {
        if (SetValueDelegate is null)
            return false;

        SetValueDelegate(instance, value);
        return true;
    }

    public static IEnumerable<IMemberAdapter> GetMemberInterfacesForType(
        Type type)
    {
        var fields = type.GetFields();
        var properties = type.GetProperties();
        var memberAdapters = new List<IMemberAdapter>();

        memberAdapters.AddRange(fields.Select(From));
        memberAdapters.AddRange(properties.Select(From));

        return memberAdapters;
    }

    public static CoreTypeMember From(
        FieldInfo info)
    {
        var typeInterface = TypeAdapterRegistry.Get(info.FieldType);

        return new CoreTypeMember
        {
            Name = info.Name,
            TypeInterface = typeInterface!,
            GetValueDelegate = GetFieldReadMethod(info),
            SetValueDelegate = GetFieldWriteMethod(info)
        };
    }

    public static CoreTypeMember From(
        PropertyInfo info)
    {
        var typeInterface = TypeAdapterRegistry.Get(info.PropertyType);

        return new CoreTypeMember
        {
            Name = info.Name,
            TypeInterface = typeInterface!,
            GetValueDelegate = GetPropertyReadMethod(info),
            SetValueDelegate = GetPropertyWriteMethod(info)
        };
    }

    private static Func<object, object> GetFieldReadMethod(
        FieldInfo fieldInfo)
    {
        if (fieldInfo.IsLiteral)
        {
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
        var box = Expression.Convert(prop, typeof(object));

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
        var value = Expression.Parameter(typeof(object), "value");
        var typedThis = Expression.Convert(This, propertyInfo.DeclaringType!);
        var typedValue = Expression.Convert(value, propertyInfo.PropertyType);
        var prop = Expression.Call(isStatic ? default : typedThis, method!, typedValue);

        return Expression.Lambda<Action<object, object>>(prop, new[] { This, value }).Compile();
    }
}