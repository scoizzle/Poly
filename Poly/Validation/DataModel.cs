using System.Collections.Frozen;

using Poly.Introspection;

namespace Poly.Validation;

public sealed class DataModel {
    private readonly ITypeDefinition _typeDefinition;
    private readonly FrozenSet<Rule> _rules;

    public DataModel(ITypeDefinition typeDefinition, params IEnumerable<Rule> rules) {
        ArgumentNullException.ThrowIfNull(typeDefinition);
        ArgumentNullException.ThrowIfNull(rules);

        _typeDefinition = typeDefinition;
        _rules = rules.ToFrozenSet();
    }

    public IEnumerable<Rule> Rules => _rules;
    public IEnumerable<ITypeMember> Properties => _typeDefinition.Members;
    public ITypeMember? GetMember(string name) => _typeDefinition.GetMember(name);
}