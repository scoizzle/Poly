namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a reference to a method parameter in a static context (e.g., inside a static factory method).
/// When used as the subject of a <see cref="Member"/> access, the parameter reference is elided
/// and only the member name is emitted — since static method parameters are directly in scope by name.
/// </summary>
public sealed record ParameterReference : Node {
    public override string ToString() => "(parameter)";

}