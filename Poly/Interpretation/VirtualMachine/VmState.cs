using Poly.Interpretation;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.VirtualMachine;

internal sealed class VmState : IDisposable {
    public ValueStack Stack { get; }
    public Heap Heap { get; }
    public Bytecode? Program { get; internal set; }
    public int PC { get; set; }
    public int FrameBase { get; set; } = -1;

    public AnalysisResult? AnalysisResult { get; internal set; }
    public HashSet<int>? BreakpointPCs { get; set; }

    public InterpreterStatus Status { get; internal set; } = InterpreterStatus.Running;
    public InterpreterResult? LastResult { get; private set; }

    public int? PendingExceptionValue { get; set; }
    public int SavedPC { get; set; } = -1;

    public TextWriter? Trace { get; set; }

    public Dictionary<NodeId, string>? NodeDescriptions { get; set; }

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

    public VmState() {
        Stack = new ValueStack();
        Heap = new Heap();
    }

    public bool IsComplete => Status == InterpreterStatus.Completed;
    public bool IsSuspended => Status == InterpreterStatus.Suspended;

    public void Complete(InterpreterResult result) {
        Status = InterpreterStatus.Completed;
        LastResult = result;
    }

    internal void SetLastResultWithoutChangingStatus(InterpreterResult result) {
        LastResult = result;
    }

    public void Dispose() {
        Stack.Dispose();
    }

    internal string? FormatStack(int maxItems = 6) {
        if (Stack.SP <= 0) return "[]";
        var span = Stack.AsSpan();
        int take = int.Min(maxItems, Stack.SP);
        var items = new string?[take];
        for (int i = 0; i < take; i++)
            items[i] = FormatValue(span[Stack.SP - take + i]);
        return "[" + string.Join(", ", items) + "]";
    }

    internal string FormatHeapCount() => Heap.Count > 0 ? $"H:{Heap.Count}" : "";

    private static string FormatValue(int val) {
        if (val >= 0 && val < 1024) return val.ToString();
        return $"0x{val:X8}";
    }
}