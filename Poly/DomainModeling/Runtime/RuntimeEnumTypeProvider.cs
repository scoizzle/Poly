using System.Linq.Expressions;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.DomainModeling.Runtime;

/// <summary>
/// Dictionary This stores stage and domain enum values as strings. Printed
/// trees write <c>PatronStatus.Active</c> / <c>LoanStage.Overdue</c>; AST enum
/// types have no static field read in the VM, so this provider exposes those
/// names as static string members.
/// </summary>
internal sealed class RuntimeEnumTypeProvider : ITypeDefinitionProvider {
    private readonly ITypeDefinitionProvider _inner;
    private readonly Dictionary<string, ITypeDefinition> _enums;

    private RuntimeEnumTypeProvider(
        ITypeDefinitionProvider inner, Dictionary<string, ITypeDefinition> enums) {
        _inner = inner;
        _enums = enums;
    }

    public ITypeDefinition? GetTypeDefinition(string name) =>
        _enums.TryGetValue(name, out var type) ? type : _inner.GetTypeDefinition(name);

    public ITypeDefinition? GetTypeDefinition(Type type) => _inner.GetTypeDefinition(type);

    internal static ITypeDefinitionProvider Wrap(ITypeDefinitionProvider inner, Domain? domain) {
        if (domain is null)
            return inner;
        if (inner is RuntimeEnumTypeProvider wrapped)
            return wrapped;
        var enums = Build(domain);
        return enums.Count == 0 ? inner : new RuntimeEnumTypeProvider(inner, enums);
    }

    private static Dictionary<string, ITypeDefinition> Build(Domain domain) {
        var stringType = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(string));
        var enums = new Dictionary<string, ITypeDefinition>(StringComparer.Ordinal);
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        foreach (var entity in domain.Types.OfType<Entity>()) {
            if (entity.Stages.Count == 0)
                continue;
            var name = analysis.GetStructure(entity)?.StageEnumTypeName ?? $"{entity.Name}Stage";
            Add(enums, name, entity.Stages.Select(s => s.Name), stringType);
        }
        foreach (var enumType in domain.Types.OfType<EnumType>())
            Add(enums, enumType.Name, enumType.MemberNames, stringType);
        return enums;
    }

    private static void Add(
        Dictionary<string, ITypeDefinition> enums,
        string typeName,
        IEnumerable<string> members,
        ITypeDefinition stringType) {
        if (enums.ContainsKey(typeName))
            throw new InvalidOperationException(
                $"Enum type name '{typeName}' is declared more than once. " +
                "A domain enum cannot share a name with a generated stage enum.");
        var type = new RuntimeEnumType(typeName, members, stringType);
        enums[typeName] = type;
    }

    private sealed class RuntimeEnumType : ITypeDefinition {
        public RuntimeEnumType(
            string name, IEnumerable<string> members, ITypeDefinition stringType) {
            Name = name;
            Fields = members
                .Select(m => (ITypeField)new RuntimeEnumMember(m, this, stringType))
                .ToArray();
        }

        public string Name { get; }
        public string? Namespace => null;
        public AccessModifier AccessModifier => AccessModifier.Public;
        public ITypeDefinition? BaseType => null;
        public IEnumerable<ITypeDefinition> Interfaces => [];
        public IEnumerable<IParameter> GenericParameters => [];
        public IEnumerable<ITypeMember> Members => Fields;
        public IEnumerable<ITypeField> Fields { get; }
        public IEnumerable<ITypeProperty> Properties => [];
        public IEnumerable<ITypeMethod> Methods => [];
        public IEnumerable<ITypeConstructor> Constructors => [];
        public Poly.Introspection.PrimitiveType? PrimitiveType => null;
        public TypeCategory TypeCategory => TypeCategory.None;
    }

    private sealed class RuntimeEnumMember(
        string name, ITypeDefinition declaring, ITypeDefinition stringType) : ITypeField {
        public string Name { get; } = name;
        public ITypeDefinition MemberTypeDefinition { get; } = stringType;
        public ITypeDefinition DeclaringTypeDefinition { get; } = declaring;
        public IEnumerable<IParameter> Parameters => [];
        public AccessModifier AccessModifier => AccessModifier.Public;
        public LifetimeModifier LifetimeModifier => LifetimeModifier.Static;
        public Mutability Mutability => Mutability.CompileTimeConst;
        public Expression? EmitRead(Expression? instance) =>
            Expression.Constant(Name, typeof(object));
    }
}
