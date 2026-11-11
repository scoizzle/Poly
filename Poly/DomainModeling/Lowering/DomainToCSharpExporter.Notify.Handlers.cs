namespace Poly.DomainModeling.Lowering;

// Notify handlers: subscription handler bodies (stage gates, quantifier loops, peer parameters).
public sealed partial class DomainToCSharpExporter {
    /// <summary>Splice a lowered handler body's statements/locals after a prefix (D13).</summary>
    private static Block Prefixed(List<Node> prefix, Node body, List<Node>? locals = null) {
        if (body is Block block) {
            prefix.AddRange(block.Nodes);
            locals ??= [];
            locals.AddRange(block.Variables);
        }
        else {
            prefix.Add(body);
        }
        return new Block(prefix, locals ?? []);
    }
}