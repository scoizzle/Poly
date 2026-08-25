namespace Poly.Ast.Nodes;

/// <summary>
/// Generic host-method invocation. The target is the execution environment
/// (<c>VmState.Host</c>), not a node in the tree — the same CallExternal
/// prints as a bare call in C# (<c>Notify("Active")</c>) and dispatches on
/// the host at VM runtime.
/// </summary>
/// <remarks>
/// Domain concepts that need store/clocks (Create / Link / Notify / Outbound / time)
/// lower to this node rather than a domain VM opcode. The emitter looks up
/// <see cref="MethodName"/> on the host by name and arity and fail-closes when
/// the host is null or the method is missing.
/// </remarks>
/// <param name="MethodName">Host method name (e.g. <c>Notify</c>).</param>
/// <param name="Arguments">Arguments passed to the host method.</param>
public sealed record CallExternal(string MethodName, params Node[] Arguments) : Expression {
    public override IEnumerable<Node?> Children => Arguments;

    public override string ToString() => $"{MethodName}({string.Join(", ", Arguments)})";
}
