namespace Poly.DomainModeling;

/// <summary>
/// Effects represent the observable side effects that occur as a result of an action being invoked
/// or a stage transition occurring. Common effects include creating instances, publishing events,
/// and transitioning stages.
/// 
/// Analyzers are responsible for validating bindings, proving data availability at execution time,
/// and producing the metadata required for reliable lowering to an Interpretable AST.
/// </summary>
public abstract record Effect(InvocationResult? Result = null) : DomainObject {
    public override IEnumerable<Node?> Children => Result is not null ? [Result] : [];
}