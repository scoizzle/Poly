using System.Reflection;

namespace Poly.Introspection;

public sealed class TypeInformationProvider {
    private readonly TypeInformationFactoryCache _typeInformationFactoryCache;

    public TypeInformationProvider(TypeInformationFactoryCache? typeInformationCache = default) {
        _typeInformationFactoryCache = typeInformationCache ?? new();
    }

    public TypeInformation GetTypeInformation(Type type) => GetTypeInformationFactory(type).Value;

    private Lazy<TypeInformation> GetTypeInformationFactory(Type type) {
        return _typeInformationFactoryCache.GetOrAdd(type, CreateTypeInformationFactory);

        Lazy<TypeInformation> CreateTypeInformationFactory(Type type) {
            return new Lazy<TypeInformation>(() => {
                return new TypeInformation(
                    name: type.Name,
                    namespace_: type.Namespace ?? string.Empty,
                    globallyUniqueName: type.FullName ?? type.Name,
                    fields: GetFieldInformation(type),
                    properties: GetPropertyInformationFactory(type),
                    methods: GetMethodInformationFactory(type)
                );
            });
        }
    }

    private Lazy<IEnumerable<FieldInformation>> GetFieldInformation(Type type) {
        return CreateFieldInformationFactory(type);
        
        Lazy<IEnumerable<FieldInformation>> CreateFieldInformationFactory(Type type) {
            return new Lazy<IEnumerable<FieldInformation>>(() => {
                return type
                    .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Select(field => new FieldInformation(
                        name: field.Name,
                        accessModifiers: field.IsPublic ? AccessModifiers.Public : AccessModifiers.Private,
                        lifetimeModifiers: field.IsStatic ? LifetimeModifiers.Static : LifetimeModifiers.Instance,
                        type: GetTypeInformationFactory(field.FieldType),
                        declaringType: field.DeclaringType is not null
                            ? GetTypeInformationFactory(field.DeclaringType) 
                            : null
                    ))
                    .ToArray();
            });
        }
    }

    private Lazy<IEnumerable<PropertyInformation>> GetPropertyInformationFactory(Type type) {
        return CreatePropertyInformationFactory(type);
        
        Lazy<IEnumerable<PropertyInformation>> CreatePropertyInformationFactory(Type type) {
            return new Lazy<IEnumerable<PropertyInformation>>(() => {
                return type
                    .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Select(prop => new PropertyInformation(
                        name: prop.Name,
                        accessModifiers: prop.GetMethod?.IsPublic == true ? AccessModifiers.Public : AccessModifiers.Private,
                        lifetimeModifiers: prop.GetMethod?.IsStatic == true ? LifetimeModifiers.Static : LifetimeModifiers.Instance,
                        type: GetTypeInformationFactory(prop.PropertyType),
                        declaringType: prop.DeclaringType != null
                            ? GetTypeInformationFactory(prop.DeclaringType) 
                            : null
                    ))
                    .ToArray();
            });
        }
    }

    private Lazy<IEnumerable<MethodInformation>> GetMethodInformationFactory(Type type) {
        return CreateMethodInformationFactory(type);
        
        Lazy<IEnumerable<MethodInformation>> CreateMethodInformationFactory(Type type) {
            return new Lazy<IEnumerable<MethodInformation>>(() => {
                return type
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Select(method => new MethodInformation(
                        name: method.Name,
                        accessModifiers: method.IsPublic ? AccessModifiers.Public : AccessModifiers.Private,
                        lifetimeModifiers: method.IsStatic ? LifetimeModifiers.Static : LifetimeModifiers.Instance,
                        returnType: GetTypeInformationFactory(method.ReturnType),
                        declaringType: method.DeclaringType != null
                            ? GetTypeInformationFactory(method.DeclaringType)
                            : null,
                        parameters: GetMethodParameters(method)
                    ))
                    .ToArray();
            });
        }
    }

    private IEnumerable<MethodParameterInformation> GetMethodParameters(MethodInfo method)
        => method
            .GetParameters()
            .Select(param => new MethodParameterInformation(
                Position: param.Position,
                Name: param.Name ?? string.Empty,
                Type: GetTypeInformationFactory(param.ParameterType).Value,
                IsOptional: param.IsOptional,
                DefaultValue: param.DefaultValue
            ))
            .ToArray();
}
