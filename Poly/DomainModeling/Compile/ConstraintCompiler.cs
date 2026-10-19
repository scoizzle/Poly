using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Where a constraint contribution lands: a semantic anchor, not a method name.
/// Anchors resolve to every matching lowered method, so a renamed or added
/// create path cannot be silently missed.
/// </summary>
public abstract record ConstraintAnchor {
    /// <summary>The entity's <c>Create</c> factory.</summary>
    public sealed record Create(bool AfterConstruction = false) : ConstraintAnchor;

    /// <summary>Every <c>Create{Nav}</c> create-in factory (not <c>Create</c>).</summary>
    public sealed record CreateIn(bool AfterConstruction = false) : ConstraintAnchor;

    /// <summary>A named method on the target type, for a one-off anchor.</summary>
    public sealed record Named(string MethodName, bool AfterConstruction = false) : ConstraintAnchor;

    public override string ToString() => this switch {
        Create c => $"Create({SlotName(c.AfterConstruction)})",
        CreateIn c => $"CreateIn({SlotName(c.AfterConstruction)})",
        Named n => $"Named({n.MethodName}, {SlotName(n.AfterConstruction)})",
        _ => GetType().Name
    };

    private static string SlotName(bool after) => after ? "after" : "before";
}

/// <summary>
/// Compiles one domain constraint into AST, plus the parameters that AST
/// requires. Required, length, and pattern compile in the exporter with an
/// empty parameter list. A library compiler is how another constraint, such
/// as <c>unique</c>, joins the same produced method and names what it needs.
/// Core places the statements at the anchor, adds the supporting methods, and
/// threads the parameters marked for callers. The compiler does not walk
/// callers.
/// </summary>
public interface IConstraintCompiler {
    string LibraryId { get; }

    /// <summary>True when <see cref="Compile"/> should run for this constraint.</summary>
    bool Applies(Constraint constraint);

    ConstraintContribution Compile(ConstraintCompileRequest request);
}

/// <summary>
/// One property constraint on one entity, plus the produced parameters that
/// already stand for that property. <see cref="ParameterOn"/> returns the
/// signature parameter of the named method when the property is already an
/// argument there.
/// </summary>
public sealed class ConstraintCompileRequest {
    private readonly IReadOnlyDictionary<string, Parameter> _parameters;

    public ConstraintCompileRequest(
        Entity entity,
        Property property,
        Constraint constraint,
        IReadOnlyDictionary<string, Parameter> parameters) {
        Entity = entity;
        Property = property;
        Constraint = constraint;
        _parameters = parameters;
    }

    public Entity Entity { get; }

    public Property Property { get; }

    public Constraint Constraint { get; }

    public Parameter? ParameterOn(string methodName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        return _parameters.TryGetValue(methodName, out var parameter) ? parameter : null;
    }
}

/// <summary>
/// AST produced by compiling one constraint. <see cref="Anchor"/> is null only
/// for a contribution that adds supporting methods and nothing else.
/// <see cref="BeforeStatements"/> run before the anchor method constructs;
/// <see cref="AfterStatements"/> run before its return. <see cref="ImpliedParameters"/>
/// are inserted before the method's first optional parameter and, when marked,
/// threaded through its callers. <see cref="SupportingMethods"/> are added to
/// the same type; they are the methods the statements call.
/// </summary>
public sealed record ConstraintContribution(
    string LibraryId,
    string TargetTypeName,
    ConstraintAnchor? Anchor,
    IReadOnlyList<Node> BeforeStatements,
    IReadOnlyList<Node> AfterStatements,
    IReadOnlyList<ImpliedParameter> ImpliedParameters,
    IReadOnlyList<MethodDefinitionNode>? SupportingMethods = null);

/// <summary>
/// A parameter a compiled constraint needs and the domain did not name.
/// <see cref="ThreadToCallers"/> copies it onto every method that calls the
/// one it was injected into.
/// </summary>
public sealed record ImpliedParameter(string Name, Node Type, bool ThreadToCallers);