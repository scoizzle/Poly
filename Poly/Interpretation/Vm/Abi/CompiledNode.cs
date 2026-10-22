using System.Linq.Expressions;

using Poly.Ast.Nodes;

namespace Poly.Interpretation.Vm.Abi;

/// <summary>
/// A compiled node: the code to run, and where its result lives once that code has
/// run. <see cref="Result"/> is the other half of the emitter's contract. Before
/// it, "the result is on top of the ring" was ambient state: every consumer
/// re-derived <c>RingDepth - 1</c>, and every emitter that did not follow the
/// convention patched the cursor with <c>RingDepth = slot + 1</c>.
/// <para>Naming the location is what lets a consumer release it. A node that
/// produces no value — a loop, a branch, a jump, a throw — has no result location,
/// and <see cref="Result"/> is null rather than a lie about slot depth.</para>
/// </summary>
internal readonly record struct CompiledNode(Expression Code, SlotLocation? Result) {

    /// <summary>The result location, or fail closed: a node that produces a value
    /// must be able to say where it is.</summary>
    public SlotLocation RequireResult(Node node) =>
        Result ?? throw new InvalidOperationException(
            $"VM compile rejected: '{node.GetType().Name}' produced no result location.");
}