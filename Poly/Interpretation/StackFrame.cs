using System.Collections.Generic;

namespace Poly.Interpretation;

public sealed class StackFrame(Node currentNode, Dictionary<string, object?> variables, int metadataIndex = -1) {
    public Node CurrentNode { get; set; } = currentNode;
    public Dictionary<string, object?> Variables { get; } = variables;
    public int MetadataIndex { get; set; } = metadataIndex;
    public int SavedStackDepth { get; set; }
}