namespace Poly.DataModeling.Builders;

using Poly.DataModeling.Mutations;
using Poly.Validation;

public sealed class MutationBuilder {
    private readonly string _targetTypeName;
    private readonly string _name;
    private readonly List<MutationParameter> _parameters = new();
    private readonly List<MutationCondition> _preconditions = new();
    private readonly EffectBuilder _effects = new();

    internal MutationBuilder(string targetTypeName, string name) {
        _targetTypeName = targetTypeName ?? throw new ArgumentNullException(nameof(targetTypeName));
        _name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public MutationBuilder Param(string name, Action<PropertyBuilder> configure) {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);
        var pb = new PropertyBuilder(name);
        configure(pb);
        var property = pb.Build();
        _parameters.Add(new MutationParameter(name, property));
        return this;
    }

    public MutationBuilder WithPrecondition(string propertyName, Constraint constraint) {
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(constraint);
        _preconditions.Add(new MutationCondition(propertyName, constraint));
        return this;
    }

    public MutationBuilder Effects(Action<EffectBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_effects);
        return this;
    }

    internal Mutation Build() => new Mutation(_name, _targetTypeName, _parameters, _preconditions, _effects.Build());
}
