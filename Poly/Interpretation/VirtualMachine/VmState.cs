using Poly.Syntax.Analysis;

namespace Poly.Interpretation.VirtualMachine;

internal sealed class VmState : IDisposable {
    public ValueStack Stack { get; } = new();
    public Heap Heap { get; } = new();
    public Bytecode? Program { get; set; }
    public int PC { get; set; }
    public int FrameBase { get; set; } = -1;
    internal int CachedArgSlots { get; set; }
    public int? PendingExceptionValue { get; set; }

    public InterpreterStatus Status { get; internal set; } = InterpreterStatus.Running;
    public InterpreterResult? LastResult { get; private set; }

    public bool IsComplete => Status == InterpreterStatus.Completed;
    public bool IsSuspended => Status == InterpreterStatus.Suspended;
    public bool ShouldStop => Status != InterpreterStatus.Running;

    public void Complete(InterpreterResult result) {
        Status = InterpreterStatus.Completed;
        LastResult = result;
    }

    internal void SetLastResultWithoutChangingStatus(InterpreterResult result) {
        LastResult = result;
    }

    /// <summary>When false, skips the interrupt-bit check on every instruction (hot-path optimization).</summary>
    public bool DebugMode { get; set; }

    public AnalysisResult? AnalysisResult { get; set; }
    public Dictionary<NodeId, string>? NodeDescriptions { get; set; }
    public TextWriter? Trace { get; set; }

    public static Dictionary<NodeId, string> BuildNodeDescriptions(Node root) {
        var map = new Dictionary<NodeId, string>();
        CollectDescriptions(root, map);
        return map;
    }

    private static void CollectDescriptions(Node node, Dictionary<NodeId, string> map) {
        if (!map.ContainsKey(node.Id))
            map[node.Id] = node.ToTraceString();
        foreach (var child in node.Children) {
            if (child is not null)
                CollectDescriptions(child, map);
        }
    }

    public void Reset() {
        PC = 0;
        FrameBase = -1;
        CachedArgSlots = 0;
        PendingExceptionValue = null;
        Status = InterpreterStatus.Running;
        LastResult = null;
        NodeDescriptions = null;
        Stack.Reset();
        Heap.Clear();
    }

    public void Dispose() => Stack.Dispose();
}