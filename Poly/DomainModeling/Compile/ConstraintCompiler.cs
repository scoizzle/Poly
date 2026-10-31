using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Where a constraint contribution lands: a semantic anchor, not a method name.
/// Anchors resolve to every matching lowered method, so a renamed or added
/// create path cannot be silently missed.
/// </summary>
public abstract record ConstraintAnchor {
    /// <summary>
    /// The entity's <c>Create</c> factory. Create-in paths call this factory, so
    /// a guard here covers them; a target type's <c>Create{Nav}</c> method creates
    /// the nav's children and is not this anchor.
    /// </summary>
    public sealed record Create(bool AfterConstruction = false) : ConstraintAnchor;

    /// <summary>A named method on the target type, for a one-off anchor.</summary>
    public sealed record Named(string MethodName, bool AfterConstruction = false) : ConstraintAnchor;

    public override string ToString() => this switch {
        Create c => $"Create({SlotName(c.AfterConstruction)})",
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
/// callers. <see cref="AssignObligations"/> names parameters an assign needs,
/// and <see cref="AfterApply"/> rewrites that assign once those parameters
/// are on the method.
/// </summary>
public interface IConstraintCompiler {
    string LibraryId { get; }

    /// <summary>True when <see cref="Compile"/> should run for this constraint.</summary>
    bool Applies(Constraint constraint);

    ConstraintContribution Compile(ConstraintCompileRequest request);

    /// <summary>
    /// Parameters an assign of this constraint needs. Empty when the constraint
    /// has no assign-time obligation. Core installs them before
    /// <see cref="AfterApply"/>.
    /// </summary>
    IReadOnlyList<ConstraintContribution> AssignObligations(
        ConstraintCompileRequest request, TypeDefinitionNode type) => [];

    /// <summary>
    /// Rewrites the module after contributions are installed. The default
    /// returns <paramref name="module"/> unchanged.
    /// </summary>
    ModuleRewrite AfterApply(IReadOnlyList<TypeDefinitionNode> module) =>
        new(module, []);
}

/// <summary>
/// A module after <see cref="IConstraintCompiler.AfterApply"/>.
/// <see cref="Replaced"/> are bodies caches still hold by reference.
/// </summary>
public readonly record struct ModuleRewrite(
    IReadOnlyList<TypeDefinitionNode> Module,
    IReadOnlyList<(Node From, Node To)> Replaced);

/// <summary>
/// One property constraint on one entity, plus the produced parameters that
/// already stand for that property. <see cref="ParameterOn"/> returns the
/// signature parameter of the named method when the property is already an
/// argument there. <see cref="StorageModel"/> is the domain's single storage
/// mapping, so a constraint that needs storage reads the entity's mapping
/// instead of naming a host type.
/// </summary>
public sealed class ConstraintCompileRequest {
    private readonly IReadOnlyDictionary<string, Parameter> _parameters;

    public ConstraintCompileRequest(
        Entity entity,
        Property property,
        Constraint constraint,
        IReadOnlyDictionary<string, Parameter> parameters,
        StorageModel? storage) {
        Entity = entity;
        Property = property;
        Constraint = constraint;
        _parameters = parameters;
        StorageModel = storage;
    }

    public Entity Entity { get; }

    public Property Property { get; }

    public Constraint Constraint { get; }

    /// <summary>The domain's single storage mapping, or null when none was analyzed.</summary>
    public StorageModel? StorageModel { get; }

    /// <summary>The entity's mapping in <see cref="StorageModel"/>, or null.</summary>
    public StorageEntity? Storage => StorageModel?.Entities.FirstOrDefault(e =>
        ReferenceEquals(e.Source, Entity)
        || string.Equals(e.Name, Entity.Name, StringComparison.Ordinal));

    /// <summary>
    /// The entity's storage mapping. Fails closed when the constraint needs
    /// storage and the domain has no mapping for this entity.
    /// </summary>
    public StorageEntity RequireStorage() =>
        Storage ?? throw new InvalidOperationException(
            $"Constraint '{Constraint.GetType().Name}' on {Entity.Name}.{Property.Name} requires storage, "
            + $"but entity '{Entity.Name}' has no storage mapping.");

    public Parameter? ParameterOn(string methodName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        return _parameters.TryGetValue(methodName, out var parameter) ? parameter : null;
    }
}

/// <summary>
/// AST produced by compiling one constraint. <see cref="BeforeStatements"/> run
/// before the anchor method constructs; <see cref="AfterStatements"/> run before
/// its return. <see cref="ImpliedParameters"/> are inserted before the method's
/// first optional parameter and, when marked, threaded through its callers.
/// <see cref="SupportingMethods"/> are added to the same type; they are the
/// methods the statements call.
/// </summary>
public sealed record ConstraintContribution(
    string LibraryId,
    string TargetTypeName,
    ConstraintAnchor Anchor,
    IReadOnlyList<Node> BeforeStatements,
    IReadOnlyList<Node> AfterStatements,
    IReadOnlyList<ImpliedParameter> ImpliedParameters,
    IReadOnlyList<MethodDefinitionNode>? SupportingMethods = null) {
    /// <summary>Set by core to <c>Entity.Property (ConstraintKind)</c>, for diagnostics.</summary>
    public string? Provenance { get; init; }
}

/// <summary>
/// A parameter a compiled constraint needs and the domain did not name.
/// <see cref="ThreadToCallers"/> copies it onto every method that calls the
/// one it was injected into.
/// </summary>
public sealed record ImpliedParameter(string Name, Node Type, bool ThreadToCallers);