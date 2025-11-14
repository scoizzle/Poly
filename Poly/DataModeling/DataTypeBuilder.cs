namespace Poly.DataModeling;

public sealed class DataTypeBuilder {
    private string _name;
    private readonly List<DataProperty> _properties;
    private readonly List<Validation.Rule> _rules;

    public DataTypeBuilder(string name) {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        _properties = new List<DataProperty>();
        _rules = new List<Validation.Rule>();
    }

    public string Name => _name;
    public IEnumerable<DataProperty> Properties => _properties;

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

    public DataTypeBuilder AddRule(Validation.Rule rule) {
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Add(rule);
        return this;
    }

    public DataType Build() => new DataType(_name, _properties, _rules);
}