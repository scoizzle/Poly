using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Syntax;

namespace Poly.Interpretation.TreeWalking;

/// <summary>
/// Represents a call frame in the tree-walking virtual machine.
/// Contains local variables and metadata for the current execution context.
/// </summary>
public sealed class StackFrame(Node? currentNode, IReadOnlyDictionary<string, object?>? parameters = null) {
    public Node? CurrentNode { get; set; } = currentNode;

    public Dictionary<string, object?> Variables { get; } = new(parameters ?? Enumerable.Empty<KeyValuePair<string, object?>>());
    public Dictionary<string, object?> Metadata { get; } = new();
}