namespace Poly.Syntax.DomainModeling;

public sealed record Literal(object? Value) : Node;

public sealed record Member(Node Target, string Name) : Node {
    public override IEnumerable<Node?> Children => [Target];
}

public sealed record And(Node Left, Node Right) : Node {
    public override IEnumerable<Node?> Children => [Left, Right];
}

public sealed record Or(Node Left, Node Right) : Node {
    public override IEnumerable<Node?> Children => [Left, Right];
}

public sealed record Equal(Node Left, Node Right) : Node {
    public override IEnumerable<Node?> Children => [Left, Right];
}

public sealed record NotEqual(Node Left, Node Right) : Node {
    public override IEnumerable<Node?> Children => [Left, Right];
}

public sealed record GreaterThanOrEqual(Node Left, Node Right) : Node {
    public override IEnumerable<Node?> Children => [Left, Right];
}

public sealed record LessThanOrEqual(Node Left, Node Right) : Node {
    public override IEnumerable<Node?> Children => [Left, Right];
}