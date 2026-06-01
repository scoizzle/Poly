namespace Poly.Syntax.Nodes;

public sealed record SuspendNode(Node Inner, string? Reason = null) : Node {
    public override IEnumerable<Node?> Children => [Inner];

    public override string ToString() => $"suspend({Inner})";
}