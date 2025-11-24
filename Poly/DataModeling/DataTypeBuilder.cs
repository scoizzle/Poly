namespace Poly.DataModeling;

using Poly.DataModeling.Builders;

public sealed class DataTypeBuilder {
    private string _name;
    private readonly List<DataProperty> _properties;
    private readonly List<Poly.Validation.Rule> _rules;
    private readonly List<RelationshipBuilder> _relationships;
    private readonly List<Mutations.Mutation> _mutations;

    public DataTypeBuilder(string name) {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        _properties = new List<DataProperty>();
        _rules = new List<Poly.Validation.Rule>();
        _relationships = new List<RelationshipBuilder>();
        _mutations = new List<Mutations.Mutation>();
    }

    public string Name => _name;
    public IEnumerable<DataProperty> Properties => _properties;
    public IEnumerable<RelationshipBuilder> Relationships => _relationships;
    public IEnumerable<Mutations.Mutation> Mutations => _mutations;

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

    public DataTypeBuilder AddRule(Poly.Validation.Rule rule) {
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Add(rule);
        return this;
    }

    public DataTypeBuilder DefineMutation(string name, Action<MutationBuilder> configure) {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new MutationBuilder(_name, name);
        configure(builder);
        _mutations.Add(builder.Build());
        return this;
    }

    public DataType Build() => new DataType(_name, _properties, _rules, _mutations);
}
