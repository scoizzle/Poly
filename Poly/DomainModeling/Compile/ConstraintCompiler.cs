using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Compiles one domain constraint into AST, plus the parameters that AST
/// requires. Required, length, and pattern compile in the exporter with an
/// empty parameter list. A library compiler is how another constraint, such
/// as <c>unique</c>, joins the same produced method and names what it needs.
/// Core injects the parameters and threads the ones marked for callers.
/// The compiler does not walk callers.
/// </summary>
public interface IConstraintCompiler {
    string LibraryId { get; }

    /// <summary>True when <see cref="Compile"/> should run for this constraint.</summary>
    bool Applies(Constraint constraint);

    CompiledConstraint Compile(ConstraintCompileRequest request);
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
/// AST produced by compiling one constraint. <see cref="Statements"/> are
/// injected into <see cref="TargetMethod"/> before its first construction, or
/// at the start when that method does not construct. <see cref="ImpliedParameters"/>
/// are appended to that method. <see cref="SupportingMethods"/> are added to
/// the same type; they are the methods the statements call.
/// </summary>
public sealed record CompiledConstraint(
    string TargetMethod,
    IReadOnlyList<Node> Statements,
    IReadOnlyList<ImpliedParameter> ImpliedParameters,
    IReadOnlyList<MethodDefinitionNode>? SupportingMethods = null);

/// <summary>
/// A parameter a compiled constraint needs and the domain did not name.
/// <see cref="ThreadToCallers"/> copies it onto every method that calls the
/// one it was injected into.
/// </summary>
public sealed record ImpliedParameter(string Name, Node Type, bool ThreadToCallers);