namespace Poly.Ast.Nodes;

/// <summary>
/// A statement that emits no code and leaves the ring untouched, so the value the previous
/// statement produced is still the block's value (<c>[x, comment]</c> yields <c>x</c>).
/// <para>
/// <b>No producer.</b> Nothing in lowering, analysis or a consumer constructs one: it is the
/// fixture the VM contract is written against (ring liveness, value representation, block
/// yielding, parity entry shape). It is not a placeholder for meaning that failed to lower —
/// that is a hard no (see <c>AGENTS.md</c>), and
/// <c>Poly.Tests/Docs/InertStatementTests.cs</c> fails if production code starts emitting one.
/// </para>
/// <para>
/// Because it is the only statement kind with no ring location, the emitter carries a few arms
/// that exist solely to tolerate it. Those arms are the contract, not dead code: delete them and
/// the "a trailing inert statement keeps the previous value" rule loses its only witness.
/// </para>
/// </summary>
/// <param name="Text">Human-readable label for the fixture; printed as a C# comment, never executed.</param>
public sealed record Comment(string Text) : Statement {
    public override string ToString() => $"/* {Text} */";
}