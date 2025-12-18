using System.Collections.Frozen;
using System.Reflection;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// CLR-backed implementation of <see cref="ITypeDefinition"/> that uses reflection to surface
/// fields, properties, and methods, with immutable frozen collections for fast lookups.
/// Thread-safe for concurrent reads after construction.
/// </summary>
internal sealed class ClrTypeDefinition : ITypeDefinition {
    private readonly Type _type;
    private readonly FrozenSet<ClrTypeField> _fields;
    private readonly FrozenSet<ClrTypeProperty> _properties;
    private readonly FrozenSet<ClrMethod> _methods;
    private readonly FrozenSet<ClrTypeMember> _allMembers;
    private readonly FrozenDictionary<string, FrozenSet<ClrTypeMember>> _membersByName;
    private readonly ClrTypeDefinitionRegistry _provider;
    private ITypeDefinition? _baseType;
    private IEnumerable<ITypeDefinition>? _interfaces;
    private IEnumerable<ClrParameter>? _genericParameters;

    public ClrTypeDefinition(Type type, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(provider);

        _type = type;
        _provider = provider;
        _fields = BuildFieldCollection(type, this, provider);
        _properties = BuildPropertyCollection(type, this, provider);
        _methods = BuildMethodCollection(type, this, provider);

        var combined = Enumerable.Empty<ClrTypeMember>()
            .Concat(_fields)
            .Concat(_properties)
            .Concat(_methods)
            .ToFrozenSet();
        _allMembers = combined;
        _membersByName = combined
            .GroupBy(m => m.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToFrozenSet());
    }

    public string Name => _type.Name;
    public string? Namespace => _type.Namespace;
    public string FullName => _type.FullName ?? _type.Name;
    public Type ClrType => _type;

    public IEnumerable<ITypeField> Fields => _fields;
    public IEnumerable<ITypeProperty> Properties => _properties;
    public IEnumerable<ITypeMethod> Methods => _methods;
    public IEnumerable<ITypeMember> Members => _allMembers;
    public IEnumerable<ITypeMember> GetMembers(string name)
        => _membersByName.TryGetValue(name, out var members) ? members : Enumerable.Empty<ITypeMember>();

    public bool TryGetMethod(string name, IEnumerable<Type> parameterTypes, out ITypeMethod? method) {
        var paramTypes = parameterTypes.ToList();
        method = _methods.FirstOrDefault(m => 
            m.Name == name &&
            m.Parameters.Count() == paramTypes.Count &&
            m.Parameters.Select(p => ((IParameter)p).ParameterTypeDefinition.ReflectedType).SequenceEqual(paramTypes));
        return method != null;
    }

    public ITypeMethod? GetBestMatchingMethod(string name, IEnumerable<Type> argumentTypes) {
        var argTypes = argumentTypes.ToList();
        var overloads = _methods.Where(m => m.Name == name).ToList();
        
        // Exact match
        var exact = overloads.FirstOrDefault(m => 
            m.Parameters.Count() == argTypes.Count &&
            m.Parameters.Select(p => ((IParameter)p).ParameterTypeDefinition.ReflectedType).SequenceEqual(argTypes));
        if (exact != null) return exact;
        
        // TODO: Implement best-match logic with assignability
        return null;
    }

    public ITypeDefinition? BaseType {
        get {
            if (_baseType is not null) return _baseType;

            ITypeDefinition? resolved;
            if (_type.IsGenericType && !_type.IsGenericTypeDefinition) {
                var genericDef = _type.GetGenericTypeDefinition();
                resolved = _provider.GetTypeDefinition(genericDef);
            }
            else if (_type.BaseType != null) {
                resolved = _provider.GetTypeDefinition(_type.BaseType);
            }
            else {
                resolved = null;
            }

            return _baseType = resolved;
        }
    }

    public IEnumerable<ITypeDefinition> Interfaces {
        get {
            return _interfaces ??= _type
                .GetInterfaces()
                .Select(i => _provider.GetTypeDefinition(i))
                .ToList();
        }
    }

    public IEnumerable<IParameter>? GenericParameters {
        get {
            if (!_type.IsGenericType) return null;
            if (_genericParameters is not null) return _genericParameters;

            var args = _type.GetGenericArguments();
            Type[] namesSource;
            if (_type.IsGenericTypeDefinition) {
                namesSource = args; // placeholders
            }
            else {
                namesSource = _type.GetGenericTypeDefinition().GetGenericArguments();
            }

            var parameters = new List<ClrParameter>(args.Length);
            for (int i = 0; i < args.Length; i++) {
                var paramName = namesSource[i].Name;
                var lazyType = _provider.GetDeferredTypeDefinitionResolver(args[i]);
                parameters.Add(new ClrParameter(paramName, lazyType, i, isOptional: false, defaultValue: null));
            }

            return _genericParameters = parameters;
        }
    }

    IEnumerable<ITypeMember> ITypeDefinition.Members => Members.Cast<ITypeMember>();
    IEnumerable<ITypeMember> ITypeDefinition.GetMembers(string name) => GetMembers(name).Cast<ITypeMember>();
    Type ITypeDefinition.ReflectedType => _type;
    IEnumerable<IParameter>? ITypeDefinition.GenericParameters => GenericParameters;

    public override string ToString() => FullName;

    private static readonly BindingFlags MemberSearchCriteria = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static FrozenSet<ClrTypeField> BuildFieldCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        var fields = type
            .GetFields(MemberSearchCriteria)
            .Select(ConstructMemberField)
            .ToFrozenSet();

        return fields;

        ClrTypeField ConstructMemberField(FieldInfo fi) {
            ArgumentNullException.ThrowIfNull(fi);
            ArgumentNullException.ThrowIfNull(fi.FieldType);
            ArgumentException.ThrowIfNullOrWhiteSpace(fi.Name);

            Lazy<ClrTypeDefinition> type = provider.GetDeferredTypeDefinitionResolver(fi.FieldType);
            return new ClrTypeField(type, declaringType, fi);
        }
    }


    private static FrozenSet<ClrTypeProperty> BuildPropertyCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        var properties = type.GetProperties(MemberSearchCriteria)
            .Select(ConstructMemberProperty)
            .ToFrozenSet();

        return properties;

        ClrTypeProperty ConstructMemberProperty(PropertyInfo pi) {
            ArgumentNullException.ThrowIfNull(pi);
            ArgumentNullException.ThrowIfNull(pi.PropertyType);
            ArgumentException.ThrowIfNullOrWhiteSpace(pi.Name);

            Lazy<ClrTypeDefinition> type = provider.GetDeferredTypeDefinitionResolver(pi.PropertyType);
            IEnumerable<MethodInfo> accessors = pi.GetAccessors(nonPublic: true);
            ParameterInfo[] indexParams = pi.GetIndexParameters();
            IEnumerable<ClrParameter>? parameters = indexParams.Length > 0
                ? indexParams
                    .OrderBy(pi => pi.Position)
                    .Select(pi => ConstructParameter(provider, pi))
                    .ToArray()
                : null;

            return new ClrTypeProperty(type, declaringType, parameters, pi);
        }
    }


    private static FrozenSet<ClrMethod> BuildMethodCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        var methods = type
            .GetMethods(MemberSearchCriteria)
            .Where(mi => !mi.IsSpecialName) // Exclude property accessors and other special methods
            .Select(ConstructMethod)
            .ToFrozenSet();

        return methods;

        ClrMethod ConstructMethod(MethodInfo mi) {
            ArgumentNullException.ThrowIfNull(mi);

            Lazy<ClrTypeDefinition> returnType = provider.GetDeferredTypeDefinitionResolver(mi.ReturnType);
            IEnumerable<ClrParameter> parameters = mi.GetParameters().Select(pi => ConstructParameter(provider, pi)).ToArray();
            return new ClrMethod(returnType, declaringType, parameters, mi);
        }
    }

    static ClrParameter ConstructParameter(ClrTypeDefinitionRegistry provider, ParameterInfo pi) {
        ArgumentNullException.ThrowIfNull(pi);

        // Array indexers and some built-in methods may have null parameter names
        string parameterName = pi.Name ?? $"param{pi.Position}";

        Lazy<ClrTypeDefinition> type = provider.GetDeferredTypeDefinitionResolver(pi.ParameterType);
        return new ClrParameter(parameterName, type, pi.Position, pi.IsOptional, pi.DefaultValue);
    }
}