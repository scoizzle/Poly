
using Poly.Introspection;
using Poly.Validation;

namespace Poly.DataModeling;

public sealed record DataType(string Name, IEnumerable<DataProperty> Properties, IEnumerable<Rule> Rules, IEnumerable<Mutations.Mutation> Mutations) : ITypeDefinition {
    string? ITypeDefinition.Namespace { get; }
    IEnumerable<ITypeMember> ITypeDefinition.Members => Properties;
    IEnumerable<IMethod> ITypeDefinition.Methods => Enumerable.Empty<IMethod>();
    Type ITypeDefinition.ReflectedType => typeof(Dictionary<string, object?>);
    
    IEnumerable<ITypeMember> ITypeDefinition.GetMembers(string name) => Properties.Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}