using Poly.Syntax;

namespace Poly.Interpretation.TreeWalking;

/// <summary>
/// Represents a call frame in the tree-walking virtual machine.
/// Contains the current node being executed, local variables, parameters,
/// and return information.
/// </summary>
public sealed class StackFrame {
    public Node CurrentNode { get; set; }
    public Dictionary<string, object?> Locals { get; } = new();
    public object? ThisInstance { get; }
    public Node? ReturnAddress { get; }           // where to resume after this frame
    public Dictionary<string, object?> Metadata { get; } = new();

    public StackFrame(Node entryPoint, object? thisInstance = null, Node? returnAddress = null) {
        CurrentNode = entryPoint;
        ThisInstance = thisInstance;
        ReturnAddress = returnAddress;
    }
}