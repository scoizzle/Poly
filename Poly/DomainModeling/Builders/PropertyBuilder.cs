using Poly.DomainModeling.TypeExpressions;
using Poly.Introspection;
using Poly.Validation;

using CollectionKind = Poly.DomainModeling.TypeExpressions.CollectionKind;

namespace Poly.DomainModeling.Builders;

public sealed class PropertyBuilder {
    private readonly string _name;
    private readonly List<Constraint> _constraints;
    private TypeExpression? _typeExpression;
    private object? _defaultValue;

    public PropertyBuilder(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        _constraints = [];
    }

    public string Name => _name;

    /// <summary>
    /// Sets the type using a CLR type. Maps to the appropriate primitive type.
    /// </summary>
    public PropertyBuilder OfType<T>() => OfType(typeof(T));

    /// <summary>
    /// Sets the type using a CLR type. Maps to the appropriate primitive type.
    /// </summary>
    public PropertyBuilder OfType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType != null) {
            var innerExpr = MapClrTypeToPrimitive(underlyingType);
            _typeExpression = new OptionalType(innerExpr);
            return this;
        }

        _typeExpression = MapClrTypeToPrimitive(type);
        return this;
    }

    /// <summary>
    /// Sets the type to a reference to another type in the data model.
    /// </summary>
    public PropertyBuilder OfType(string typeName)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        _typeExpression = new ReferenceType(typeName);
        return this;
    }

    /// <summary>
    /// Sets the type using a type expression directly.
    /// </summary>
    public PropertyBuilder OfTypeExpression(TypeExpression typeExpression)
    {
        ArgumentNullException.ThrowIfNull(typeExpression);
        _typeExpression = typeExpression;
        return this;
    }

    /// <summary>
    /// Makes this property nullable/optional.
    /// </summary>
    public PropertyBuilder Optional()
    {
        if (_typeExpression == null)
            throw new InvalidOperationException("Must specify a type before making it optional.");

        if (_typeExpression is not OptionalType) {
            _typeExpression = new OptionalType(_typeExpression);
        }
        return this;
    }

    /// <summary>
    /// Makes this property a list of the current type.
    /// </summary>
    public PropertyBuilder AsList()
    {
        if (_typeExpression == null)
            throw new InvalidOperationException("Must specify a type before making it a list.");

        _typeExpression = new CollectionType(_typeExpression, CollectionKind.List);
        return this;
    }

    /// <summary>
    /// Makes this property an array of the current type.
    /// </summary>
    public PropertyBuilder AsArray()
    {
        if (_typeExpression == null)
            throw new InvalidOperationException("Must specify a type before making it an array.");

        _typeExpression = new CollectionType(_typeExpression, CollectionKind.Array);
        return this;
    }

    /// <summary>
    /// Makes this property a set of the current type.
    /// </summary>
    public PropertyBuilder AsSet()
    {
        if (_typeExpression == null)
            throw new InvalidOperationException("Must specify a type before making it a set.");

        _typeExpression = new CollectionType(_typeExpression, CollectionKind.Set);
        return this;
    }

    public PropertyBuilder WithConstraint(Constraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        ValidateConstraintApplicability(constraint);
        _constraints.Add(constraint);
        return this;
    }

    public PropertyBuilder WithConstraints(params IEnumerable<Constraint> constraints)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        foreach (var constraint in constraints) {
            ValidateConstraintApplicability(constraint);
        }
        _constraints.AddRange(constraints);
        return this;
    }

    private void ValidateConstraintApplicability(Constraint constraint)
    {
        // Can only validate if we have a type set
        if (_typeExpression == null)
            return;

        // Check if the constraint is applicable to the current type
        if (!constraint.IsApplicableTo(_typeExpression)) {
            throw new ConstraintApplicabilityException(_name, constraint, _typeExpression);
        }
    }

    public PropertyBuilder WithDefault(object? defaultValue)
    {
        _defaultValue = defaultValue;
        return this;
    }

    public DataProperty Build()
    {
        if (_typeExpression == null)
            throw new InvalidOperationException($"Property '{_name}' must have a type specified.");

        // Validate all constraints against the final type (in case constraints were added before type was set)
        foreach (var constraint in _constraints) {
            if (!constraint.IsApplicableTo(_typeExpression)) {
                throw new ConstraintApplicabilityException(_name, constraint, _typeExpression);
            }
        }

        return new DataProperty(_name, _typeExpression, _constraints, _defaultValue);
    }

    private static TypeExpression MapClrTypeToPrimitive(Type type) => type switch {
        Type t when t == typeof(bool) => new PrimitiveType(PrimitiveTypeId.Boolean),

        Type t when t == typeof(sbyte) => new PrimitiveType(PrimitiveTypeId.Int8),
        Type t when t == typeof(short) => new PrimitiveType(PrimitiveTypeId.Int16),
        Type t when t == typeof(int) => new PrimitiveType(PrimitiveTypeId.Int32),
        Type t when t == typeof(long) => new PrimitiveType(PrimitiveTypeId.Int64),

        Type t when t == typeof(byte) => new PrimitiveType(PrimitiveTypeId.UInt8),
        Type t when t == typeof(ushort) => new PrimitiveType(PrimitiveTypeId.UInt16),
        Type t when t == typeof(uint) => new PrimitiveType(PrimitiveTypeId.UInt32),
        Type t when t == typeof(ulong) => new PrimitiveType(PrimitiveTypeId.UInt64),

        Type t when t == typeof(float) => new PrimitiveType(PrimitiveTypeId.Float32),
        Type t when t == typeof(double) => new PrimitiveType(PrimitiveTypeId.Float64),
        Type t when t == typeof(decimal) => new PrimitiveType(PrimitiveTypeId.Decimal),

        Type t when t == typeof(string) => new PrimitiveType(PrimitiveTypeId.String),
        Type t when t == typeof(char) => new PrimitiveType(PrimitiveTypeId.Char),

        Type t when t == typeof(DateTime) => new PrimitiveType(PrimitiveTypeId.DateTime),
        Type t when t == typeof(DateOnly) => new PrimitiveType(PrimitiveTypeId.DateOnly),
        Type t when t == typeof(TimeOnly) => new PrimitiveType(PrimitiveTypeId.TimeOnly),
        Type t when t == typeof(TimeSpan) => new PrimitiveType(PrimitiveTypeId.TimeSpan),

        Type t when t == typeof(Guid) => new PrimitiveType(PrimitiveTypeId.Guid),
        Type t when t == typeof(byte[]) => new PrimitiveType(PrimitiveTypeId.ByteArray),

        _ => throw new NotSupportedException($"CLR type '{type.Name}' is not supported. Use OfTypeExpression() for custom types.")
    };
}