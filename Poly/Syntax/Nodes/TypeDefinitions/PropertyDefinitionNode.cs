namespace Poly.Syntax.Nodes;

/// <summary>
/// AST node representing a property definition on a type.
/// </summary>
public sealed record PropertyDefinitionNode : MemberDefinitionNode {
    public PropertyDefinitionNode(
        string Name,
        Node PropertyType,
        Node? DefaultValue = null,
        PropertyGetterDefinitionNode? Getter = null,
        PropertySetterDefinitionNode? Setter = null,
        PropertyInitializerDefinitionNode? Initializer = null,
        bool IsStatic = false,
        bool IsVolatile = false,
        bool IsReadOnly = false,  // explicit; computed from Setter if not provided
        bool IsConst = false,
        IReadOnlyList<Parameter>? IndexParameters = null,
        IReadOnlyList<Node>? Constraints = null,
        AccessModifier AccessModifier = AccessModifier.Public
    ) : base(Name, PropertyType, IsStatic, AccessModifier) {
        this.Getter = Getter;
        this.Setter = Setter;
        this.Initializer = Initializer ?? (DefaultValue is null ? null : new PropertyInitializerDefinitionNode(DefaultValue));
        this.IsVolatile = IsVolatile;
        // Enforce IsConst implies IsReadOnly for consistency with fields (const properties are read-only by nature).
        this.IsReadOnly = IsReadOnly || IsConst || Setter is null;
        this.IsConst = IsConst;
        this.IndexParameters = IndexParameters;
        this.Constraints = Constraints;
    }

    public PropertyGetterDefinitionNode? Getter { get; }

    public PropertySetterDefinitionNode? Setter { get; }

    public PropertyInitializerDefinitionNode? Initializer { get; }

    public bool IsVolatile { get; }

    public IReadOnlyList<Parameter>? IndexParameters { get; }

    public IReadOnlyList<Node>? Constraints { get; }

    /// <summary>
    /// Attributes applied to this property.
    /// </summary>
    public IReadOnlyList<AttributeNode> Attributes { get; init; } = [];

    public bool IsReadOnly { get; }

    public bool IsConst { get; }

    public Mutability Mutability {
        get {
            var m = Mutability.Mutable;
            if (IsReadOnly) m |= Mutability.ReadOnlyAfterInit;
            if (IsConst) m |= Mutability.CompileTimeConst;
            if (IsVolatile) m |= Mutability.VolatileAccess;
            return m;
        }
    }

    public Node? DefaultValue => Initializer?.Value;

    public override IEnumerable<Node?> Children {
        get {
            yield return MemberType;
            foreach (var a in Attributes) yield return a;
            yield return Getter;
            yield return Setter;
            yield return Initializer;
            if (IndexParameters != null)
                foreach (var parameter in IndexParameters) yield return parameter;
            if (Constraints != null)
                foreach (var constraint in Constraints) yield return constraint;
        }
    }

    public override string ToString() {
        var staticPrefix = IsStatic ? "static " : "";
        var constPrefix = IsConst ? "const " : "";
        var readonlyPrefix = (IsReadOnly && !IsConst && Setter is not null) ? "readonly " : "";
        var volatilePrefix = IsVolatile ? "volatile " : "";
        var getterText = Getter is null ? string.Empty : $" {Getter}";
        var setterText = Setter is null ? string.Empty : $" {Setter}";
        var initializerText = DefaultValue is null ? string.Empty : $" = {DefaultValue}";
        return $"{staticPrefix}{constPrefix}{readonlyPrefix}{volatilePrefix}{MemberType} {Name}{getterText}{setterText}{initializerText}".TrimEnd();
    }
}