using System.Collections.Frozen;
using System.Reflection;

namespace Poly.Introspection.CommonLanguageRuntime;

public sealed class ClrTypeDefinition : ITypeDefinition {
    private readonly Type _type;
    private readonly FrozenDictionary<string, ClrTypeMember> _members;
    private readonly FrozenSet<ClrMethod> _methods;

    public ClrTypeDefinition(Type type, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(provider);

        _type = type;
        _members = BuildMemberDictionary(type, this, provider);
        _methods = BuildMethodCollection(type, this, provider);
    }

    public string Name => _type.Name;
    public string? Namespace => _type.Namespace;
    public string FullName => _type.FullName ?? _type.Name;
    public Type ClrType => _type;
    public IEnumerable<ClrTypeMember> Members => _members.Values;
    public IEnumerable<ClrMethod> Methods => _methods;
    public ClrTypeMember? GetMember(string name) => _members.TryGetValue(name, out var member) ? member : null;

    IEnumerable<ITypeMember> ITypeDefinition.Members => Members.Cast<ITypeMember>();
    IEnumerable<IMethod> ITypeDefinition.Methods => Methods.Cast<IMethod>();
    ITypeMember? ITypeDefinition.GetMember(string name) => _members.TryGetValue(name, out var member) ? member : null;
    Type ITypeDefinition.ReflectedType => _type;

    public override string ToString() => FullName;

    private static FrozenDictionary<string, ClrTypeMember> BuildMemberDictionary(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        var fields = type
            .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(ConstructMemberField);

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(ConstructMemberProperty);

        return Enumerable.Concat<ClrTypeMember>(fields, properties)
            .ToFrozenDictionary(m => m.Name);

        ClrTypeField ConstructMemberField(FieldInfo fi) {
            ArgumentNullException.ThrowIfNull(fi);
            ArgumentNullException.ThrowIfNull(fi.FieldType);
            ArgumentException.ThrowIfNullOrWhiteSpace(fi.Name);

            Lazy<ClrTypeDefinition> type = provider.GetDeferredTypeDefinitionResolver(fi.FieldType);
            return new ClrTypeField(type, declaringType, fi);
        }

        ClrTypeProperty ConstructMemberProperty(PropertyInfo pi) {
            ArgumentNullException.ThrowIfNull(pi);
            ArgumentNullException.ThrowIfNull(pi.PropertyType);
            ArgumentException.ThrowIfNullOrWhiteSpace(pi.Name);

            Lazy<ClrTypeDefinition> type = provider.GetDeferredTypeDefinitionResolver(pi.PropertyType);
            IEnumerable<MethodInfo> accessors = pi.GetAccessors(nonPublic: true);

            bool isStatic = accessors.Any(a => a.IsStatic);
            return new ClrTypeProperty(type, declaringType, pi);
        }
    }

    private static FrozenSet<ClrMethod> BuildMethodCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        return type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(ConstructMethod)
            .ToFrozenSet();

        ClrParameter ConstructParameter(ParameterInfo pi) {
            ArgumentNullException.ThrowIfNull(pi);
            ArgumentException.ThrowIfNullOrWhiteSpace(pi.Name);

            Lazy<ClrTypeDefinition> type = provider.GetDeferredTypeDefinitionResolver(pi.ParameterType);
            return new ClrParameter(pi.Name, type, pi.Position, pi.IsOptional, pi.DefaultValue);
        }

        ClrMethod ConstructMethod(MethodInfo mi) {
            ArgumentNullException.ThrowIfNull(mi);

            Lazy<ClrTypeDefinition> returnType = provider.GetDeferredTypeDefinitionResolver(mi.ReturnType);
            IEnumerable<ClrParameter> parameters = mi.GetParameters().Select(ConstructParameter).ToArray();
            return new ClrMethod(mi.Name, declaringType, returnType, parameters);
        }
    }
}