using System.Collections.Concurrent;
using System.Reflection;

namespace Poly.Introspection.Core;

sealed record ClrTypeInfoProvider : ITypeInfoProvider
{
    private readonly ConcurrentDictionary<Type, Lazy<ITypeInfo>> _typeInfoCache = new();

    public ITypeInfo GetTypeInfo(Type type) => GetTypeInfoFactory(type).Value;

    public ITypeInfo GetTypeInfo<T>() => GetTypeInfoFactory(type: typeof(T)).Value;

    public ITypeInfo GetTypeInfo(string typeName)
    {
        return Type.GetType(typeName) switch
        {
            Type type => GetTypeInfoFactory(type).Value,
            _ => throw new Exception($"Type {typeName} was not found.")
        };
    }

    private Lazy<ITypeInfo> GetTypeInfoFactory(Type type) => _typeInfoCache.GetOrAdd(type, GetLazyTypeInfoFactory);

    private Lazy<ITypeInfo> GetLazyTypeInfoFactory(Type type)
    {
        return new(() =>
        {
            var accessModifiers = type switch
            {
                { IsPublic: true } => ClrAccessModifier.Public,
                { IsNotPublic: true } => ClrAccessModifier.Internal,
                _ => ClrAccessModifier.None
            };

            var fields = GetLazyFieldsInfoFactory(type);
            var properties = GetLazyPropertiesInfoFactory(type);
            var constructors = GetLazyConstructorsInfoFactory(type);
            var methods = GetLazyMethodsInfoFactory(type);
            var attributes = GetLazyAttributesFactory(type);

            return new ClrTypeInfo(
                type: type,
                accessModifiers: accessModifiers,
                fields: fields,
                properties: properties,
                constructors: constructors,
                methods: methods,
                attributes: attributes);
        });

        static Lazy<IEnumerable<Attribute>> GetLazyAttributesFactory(Type type)
        {
            return new(() => Attribute.GetCustomAttributes(type));
        }
    }

    private Lazy<IEnumerable<IMethodInfo>> GetLazyMethodsInfoFactory(Type type)
    {
        return new(() =>
        {
            var methods = type
                .GetMethods()
                .Where(method => !method.IsSpecialName)
                .Select(method => new ClrMethodInfo(
                    name: method.Name,
                    parameters: GetLazyMethodParametersInfoFactory(method),
                    returnTypeFactory: GetTypeInfoFactory(type),
                    attributesFactory: GetLazyAttributesFactory(method)
                ))
                .ToList();

            return methods;
        });

        static Lazy<IEnumerable<Attribute>> GetLazyAttributesFactory(MethodInfo methodInfo)
        {
            return new(() => Attribute.GetCustomAttributes(methodInfo));
        }
    }

    private Lazy<IEnumerable<IMethodParameterInfo>> GetLazyMethodParametersInfoFactory(MethodBase methodBase)
    {
        return new(() =>
        {
            var parameters = methodBase
                .GetParameters()
                .Select(parameter => new ClrMethodParameterInfo(
                    name: parameter.Name ?? parameter.Position.ToString(),
                    typeInfoFactory: GetTypeInfoFactory(type: parameter.ParameterType)
                ))
                .ToList();

            return parameters;
        });
    }

    private Lazy<IEnumerable<IMethodInfo>> GetLazyConstructorsInfoFactory(Type type) =>
        new(() =>
        {
            var constructors = type
                .GetConstructors()
                .Select(ctor => new ClrMethodInfo(
                    name: ctor.Name,
                    parameters: GetLazyMethodParametersInfoFactory(ctor),
                    returnTypeFactory: GetTypeInfoFactory(type),
                    attributesFactory: GetLazyAttributesFactory(ctor)
                ))
                .ToList();

            return constructors;
        });

    static Lazy<IEnumerable<Attribute>> GetLazyAttributesFactory(ConstructorInfo constructorInfo)
    {
        return new(() => Attribute.GetCustomAttributes(constructorInfo));
    }

    private Lazy<IEnumerable<IMemberInfo>> GetLazyFieldsInfoFactory(Type type) =>
        new(() =>
        {
            return type
                .GetFields()
                .Select(field => new ClrFieldInfo(
                    name: field.Name,
                    accessModifier: field switch
                    {
                        { IsPublic: true } => ClrAccessModifier.Public,
                        { IsPrivate: true } => ClrAccessModifier.Private,
                        { IsFamilyOrAssembly: true } => ClrAccessModifier.ProtectedInternal,
                        { IsFamilyAndAssembly: true } => ClrAccessModifier.PrivateProtected,
                        { IsFamily: true } => ClrAccessModifier.Protected,
                        { IsAssembly: true } => ClrAccessModifier.Internal,
                        _ => ClrAccessModifier.None
                    },
                    lifetimeModifier: field.IsStatic ? ClrLifetimeModifier.Static : ClrLifetimeModifier.Instance,
                    typeInfoFactory: GetTypeInfoFactory(type: field.FieldType)
                ))
                .ToList();
        });

    private Lazy<IEnumerable<IMemberInfo>> GetLazyPropertiesInfoFactory(Type type)
    {
        return new(() =>
        {
            return type
                .GetProperties()
                .Select(prop => new ClrPropertyInfo(
                    name: prop.Name,
                    accessModifiers: GetTopLevelPropertyAccessModifier(prop),
                    lifetimeModifiers: prop.GetAccessors(true).Any(accessor => accessor.IsStatic)
                        ? ClrLifetimeModifier.Static
                        : ClrLifetimeModifier.Instance,
                    isReadable: prop.CanRead,
                    isWritable: prop.CanWrite,
                    typeInfoFactory: GetTypeInfoFactory(type: prop.PropertyType)
                ))
                .ToList();
        });

        static ClrAccessModifier GetTopLevelPropertyAccessModifier(PropertyInfo property)
        {
            var modifiers = property
                .GetAccessors()
                .Select(e => e switch
                {
                    { IsPublic: true } => ClrAccessModifier.Public,
                    { IsPrivate: true } => ClrAccessModifier.Private,
                    { IsFamilyOrAssembly: true } => ClrAccessModifier.ProtectedInternal,
                    { IsFamilyAndAssembly: true } => ClrAccessModifier.PrivateProtected,
                    { IsFamily: true } => ClrAccessModifier.Protected,
                    { IsAssembly: true } => ClrAccessModifier.Internal,
                    _ => ClrAccessModifier.None
                })
                .OrderBy(e => e)
                .FirstOrDefault();
            return modifiers;
        }
    }
}
