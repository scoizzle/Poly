using Poly.Validation;

namespace Poly.DataModeling.Builders;

public sealed class PropertyBuilder {
    private readonly string _name;
    private readonly List<Constraint> _constraints;
    private Type? _propertyType;
    private string? _dataModelTypeName;
    private object? _defaultValue;

    public PropertyBuilder(string name) {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        _constraints = [];
    }

    public string Name => _name;

    public PropertyBuilder OfType<T>() {
        _propertyType = typeof(T);
        return this;
    }

    public PropertyBuilder OfType(Type type) {
        ArgumentNullException.ThrowIfNull(type);
        _propertyType = type;
        return this;
    }

    public PropertyBuilder OfType(string typeName) {
        ArgumentNullException.ThrowIfNull(typeName);
        _dataModelTypeName = typeName;
        return this;
    }

    public PropertyBuilder WithConstraint(Constraint constraint) {
        ArgumentNullException.ThrowIfNull(constraint);
        _constraints.Add(constraint);
        return this;
    }

    public PropertyBuilder WithConstraints(params IEnumerable<Constraint> constraints) {
        ArgumentNullException.ThrowIfNull(constraints);
        _constraints.AddRange(constraints);
        return this;
    }

    public PropertyBuilder WithDefault(object? defaultValue) {
        _defaultValue = defaultValue;
        return this;
    }

    public DataProperty Build() {
        if (_dataModelTypeName != null) {
            return new ReferenceProperty(_name, _dataModelTypeName, _constraints, _defaultValue);
        }

        if (_propertyType == null)
            throw new InvalidOperationException($"Property '{_name}' must have a type specified using OfType<T>(), OfType(Type), or OfDataType(string).");

        return _propertyType switch {
            Type t when t == typeof(string) => new StringProperty(_name, _constraints, _defaultValue),
            Type t when t == typeof(int) => new Int32Property(_name, _constraints, _defaultValue),
            Type t when t == typeof(long) => new Int64Property(_name, _constraints, _defaultValue),
            Type t when t == typeof(double) => new DoubleProperty(_name, _constraints, _defaultValue),
            Type t when t == typeof(bool) => new BooleanProperty(_name, _constraints, _defaultValue),
            Type t when t == typeof(Guid) => new GuidProperty(_name, _constraints, _defaultValue),
            Type t when t == typeof(DateTime) || t == typeof(DateTime?) => new DateTimeProperty(_name, _constraints, _defaultValue),
            Type t when t == typeof(DateOnly) || t == typeof(DateOnly?) => new DateOnlyProperty(_name, _constraints, _defaultValue),
            Type t when t == typeof(TimeOnly) || t == typeof(TimeOnly?) => new TimeOnlyProperty(_name, _constraints, _defaultValue),
            _ => throw new NotSupportedException($"Property type '{_propertyType.Name}' is not supported.")
        };
    }
}
