using System.Collections.Concurrent;
using System.Reflection;

namespace Poly.Introspection.Core;

sealed record ClrTypeAdapterProvider : ITypeAdapterProvider
{
    private readonly ConcurrentDictionary<Type, Lazy<ITypeAdapter>> _typeInfoCache = new();

    public ITypeAdapter GetTypeInfo(Type type) => GetTypeInfoFactory(type).Value;

    public ITypeAdapter GetTypeInfo<T>() => GetTypeInfoFactory(type: typeof(T)).Value;

    public ITypeAdapter GetTypeInfo(string typeName) => Type.GetType(typeName) switch
    {
        Type type => GetTypeInfoFactory(type).Value,
        _ => throw new Exception($"Type {typeName} was not found.")
    };

    private Lazy<ITypeAdapter> GetTypeInfoFactory(Type type) => _typeInfoCache.GetOrAdd(type, GetLazyTypeInfoFactory);

    private Lazy<ITypeAdapter> GetLazyTypeInfoFactory(Type type)
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

            return new ClrTypeAdapter(
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

    private Lazy<IEnumerable<IMethodAdapter>> GetLazyMethodsInfoFactory(Type type)
    {
        return new(() =>
        {
            var methods = type
                .GetMethods()
                .Where(method => !method.IsSpecialName)
                .Select(method => new ClrMethodAdapter(
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

    private Lazy<IEnumerable<IMethodParameterAdapter>> GetLazyMethodParametersInfoFactory(MethodBase methodBase)
    {
        return new(() =>
        {
            var parameters = methodBase
                .GetParameters()
                .Select(parameter => new ClrMethodParameterAdapter(
                    name: parameter.Name ?? parameter.Position.ToString(),
                    typeInfoFactory: GetTypeInfoFactory(type: parameter.ParameterType)
                ))
                .ToList();

            return parameters;
        });
    }

    private Lazy<IEnumerable<IMethodAdapter>> GetLazyConstructorsInfoFactory(Type type) =>
        new(() =>
        {
            var constructors = type
                .GetConstructors()
                .Select(ctor => new ClrMethodAdapter(
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

    private Lazy<IEnumerable<ITypeMemberAdapter>> GetLazyFieldsInfoFactory(Type type) =>
        new(() =>
        {
            return type
                .GetFields()
                .Select(field => new ClrFieldAdapter(
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

    private Lazy<IEnumerable<ITypeMemberAdapter>> GetLazyPropertiesInfoFactory(Type type)
    {
        return new(() =>
        {
            return type
                .GetProperties()
                .Select(prop => new ClrPropertyAdapter(
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
