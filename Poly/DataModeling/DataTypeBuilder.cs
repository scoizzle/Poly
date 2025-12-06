namespace Poly.DataModeling;

using Poly.DataModeling.Builders;
using Poly.DataModeling.Mutations;
using Poly.DataModeling.Mutations.Builders;

public sealed class DataTypeBuilder {
    private string _name;
    private readonly List<DataProperty> _properties;
    private readonly List<Validation.Rule> _rules;
    private readonly List<RelationshipBuilder> _relationships;
    private readonly List<Mutation> _mutations;

    public DataTypeBuilder(string name) {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        _properties = new List<DataProperty>();
        _rules = new List<Validation.Rule>();
        _relationships = new List<RelationshipBuilder>();
        _mutations = new List<Mutation>();
    }

    public string Name => _name;
    public IEnumerable<DataProperty> Properties => _properties;
    public IEnumerable<RelationshipBuilder> Relationships => _relationships;
    public IEnumerable<Mutation> Mutations => _mutations;

    public DataTypeBuilder SetName(string name) {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        return this;
    }

    public DataTypeBuilder AddProperty(DataProperty property) {
        ArgumentNullException.ThrowIfNull(property);
        _properties.Add(property);
        return this;
    }

    public DataTypeBuilder AddProperty(string name, Action<PropertyBuilder> configure) {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new PropertyBuilder(name);
        configure(builder);
        _properties.Add(builder.Build());
        return this;
    }

    public RelationshipBuilder HasOne(string? propertyName = null) {
        var builder = new RelationshipBuilder(_name, propertyName, RelationshipBuilder.SourceCardinality.One);
        _relationships.Add(builder);
        return builder;
    }

    public RelationshipBuilder HasMany(string? propertyName = null) {
        var builder = new RelationshipBuilder(_name, propertyName, RelationshipBuilder.SourceCardinality.Many);
        _relationships.Add(builder);
        return builder;
    }

    public DataTypeBuilder AddRule(Validation.Rule rule) {
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Add(rule);
        return this;
    }

    public DataTypeBuilder HasMutation(string name, Action<MutationBuilder> configure) {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new MutationBuilder(_name, name);
        configure(builder);
        _mutations.Add(builder.Build());
        return this;
    }

    public DataTypeBuilder HasMutation(string name, IEnumerable<Action<PreconditionBuilder>> preconditions, IEnumerable<Action<EffectBuilder>> effects) {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(preconditions);
        ArgumentNullException.ThrowIfNull(effects);
        var builder = new MutationBuilder(_name, name);
        foreach (var precondition in preconditions) {
            builder.WithPrecondition(precondition);
        }
        foreach (var effect in effects) {
            builder.HasEffect(effect);
        }
        _mutations.Add(builder.Build());
        return this;
    }

    public DataType Build() => new DataType(_name, _properties, _rules, _mutations);
}
