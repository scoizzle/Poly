using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;

namespace Poly.Introspection.CommonLanguageRuntime;

public sealed class ClrTypeDefinition : ITypeDefinition {
    private readonly Type _type;
    private readonly FrozenSet<ClrTypeMember> _members;
    private readonly FrozenSet<ClrMethod> _methods;

    public ClrTypeDefinition(Type type, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(provider);

        _type = type;
        _members = BuildMemberCollection(type, this, provider);
        _methods = BuildMethodCollection(type, this, provider);
    }

    public string Name => _type.Name;
    public string? Namespace => _type.Namespace;
    public string FullName => _type.FullName ?? _type.Name;
    public Type ClrType => _type;
    public IEnumerable<ClrTypeMember> Members => _members;
    public IEnumerable<ClrMethod> Methods => _methods;
    public IEnumerable<ClrTypeMember> GetMembers(string name) => _members.Where(m => m.Name == name);

    IEnumerable<ITypeMember> ITypeDefinition.Members => Members.Cast<ITypeMember>();
    IEnumerable<IMethod> ITypeDefinition.Methods => Methods.Cast<IMethod>();
    IEnumerable<ITypeMember> ITypeDefinition.GetMembers(string name) => _members.Where(m => m.Name == name).Cast<ITypeMember>();
    Type ITypeDefinition.ReflectedType => _type;

    public override string ToString() => FullName;

    private static FrozenSet<ClrTypeMember> BuildMemberCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        var fields = type
            .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(ConstructMemberField);

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(ConstructMemberProperty);

        return Enumerable.Concat<ClrTypeMember>(fields, properties)
            .ToFrozenSet();

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
            var indexParams = pi.GetIndexParameters();
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
        return type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(ConstructMethod)
            .ToFrozenSet();

        ClrMethod ConstructMethod(MethodInfo mi) {
            ArgumentNullException.ThrowIfNull(mi);

            Lazy<ClrTypeDefinition> returnType = provider.GetDeferredTypeDefinitionResolver(mi.ReturnType);
            IEnumerable<ClrParameter> parameters = mi.GetParameters().Select(pi => ConstructParameter(provider, pi)).ToArray();
            return new ClrMethod(mi, declaringType, returnType, parameters);
        }
    }

    private static ClrParameter ConstructParameter(ClrTypeDefinitionRegistry provider, ParameterInfo pi) {
        ArgumentNullException.ThrowIfNull(pi);
        ArgumentException.ThrowIfNullOrWhiteSpace(pi.Name);

        Lazy<ClrTypeDefinition> type = provider.GetDeferredTypeDefinitionResolver(pi.ParameterType);
        return new ClrParameter(pi.Name, type, pi.Position, pi.IsOptional, pi.DefaultValue);
    }
}