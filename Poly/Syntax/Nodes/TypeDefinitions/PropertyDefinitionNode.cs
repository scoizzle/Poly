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
        IReadOnlyList<Parameter>? IndexParameters = null,
        IReadOnlyList<Node>? Constraints = null,
        AccessModifier AccessModifier = AccessModifier.Public
    ) : base(Name, PropertyType, IsStatic, AccessModifier) {
        this.Getter = Getter;
        this.Setter = Setter;
        this.Initializer = Initializer ?? (DefaultValue is null ? null : new PropertyInitializerDefinitionNode(DefaultValue));
        this.IndexParameters = IndexParameters;
        this.Constraints = Constraints;
    }

    public PropertyGetterDefinitionNode? Getter { get; }

    public PropertySetterDefinitionNode? Setter { get; }

    public PropertyInitializerDefinitionNode? Initializer { get; }

    public IReadOnlyList<Parameter>? IndexParameters { get; }

    public IReadOnlyList<Node>? Constraints { get; }

    public bool IsReadOnly => Setter is null;

    public Node? DefaultValue => Initializer?.Value;

    public override IEnumerable<Node?> Children {
        get {
            yield return MemberType;
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
        var getterText = Getter is null ? string.Empty : $" {Getter}";
        var setterText = Setter is null ? string.Empty : $" {Setter}";
        var initializerText = DefaultValue is null ? string.Empty : $" = {DefaultValue}";
        return $"{staticPrefix}{MemberType} {Name}{getterText}{setterText}{initializerText}".TrimEnd();
    }
}