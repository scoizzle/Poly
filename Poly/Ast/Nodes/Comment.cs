namespace Poly.Ast.Nodes;

/// <summary>
/// A source comment preserved through the C# surface: the reader keeps it and the writer prints
/// it, so a body round-trips with its notes intact.
/// <para>
/// <b>Inert.</b> A comment emits no code, leaves the ring untouched (so the value the previous
/// statement produced is still the block's value, <c>[x, /* note */]</c> yielding <c>x</c>), and is
/// rejected wherever a value is required. Analysis and the VM treat it as absent.
/// </para>
/// <para>
/// <b>Never a lowering output.</b> Lowering models an effect or fails closed; it does not emit a
/// comment to record what it could not lower. `Poly.Tests/Docs/InertStatementTests` fails if
/// `Poly/DomainModeling` or `Poly.Mcp` constructs one.
/// </para>
/// </summary>
/// <param name="Text">Comment body without the delimiters.</param>
/// <param name="IsBlock">True for <c>/* … */</c>, false for <c>// …</c>.</param>
public sealed record Comment(string Text, bool IsBlock = false) : Statement {
    public override string ToString() => IsBlock ? $"/* {Text} */" : $"// {Text}";
}