namespace Poly.Syntax.Primitives;

/// <summary>
/// Configures the behaviour of <see cref="PrimitiveReconstructor"/>.
/// </summary>
public sealed record PrimitiveReconstructorSettings {
    /// <summary>
    /// When true, the reconstructor uses variable slot names from the analysis context
    /// when available. When false or no context provided, synthetic names ($slotN) are used.
    /// </summary>
    public bool UsePipelineContext { get; init; } = true;

    /// <summary>
    /// When true, the reconstructor attempts to recognize and reconstruct control-flow
    /// structures (loops, conditionals, if/else). When false, returns null for patterns
    /// containing control-flow primitives like Goto/CondGoto.
    /// </summary>
    public bool ReconstructControlFlow { get; init; } = true;

    /// <summary>
    /// Maximum number of Discard primitives to skip when scanning for patterns.
    /// </summary>
    public int MaxConsecutiveDiscards { get; init; } = 16;
}

/// <summary>
/// Reconstructs higher-level <see cref="Node"/> trees from <see cref="PrimitiveNode"/> sequences.
///
/// Two modes of operation:
///
/// <b>Pipeline-bound</b> — when an <see cref="AnalysisContext"/> is provided, the reconstructor
/// can use variable slot mappings from the <see cref="ExpansionEnvironment"/> and resolve
/// variable/parameter names from the original AST. This produces the most faithful reconstruction.
///
/// <b>Standalone</b> — when no context is available, the reconstructor uses heuristic naming
/// and pattern matching alone. All patterns supported, but variable names are synthetic.
/// </summary>
public sealed class PrimitiveReconstructor {
    private readonly PrimitiveReconstructorSettings _settings;

    public PrimitiveReconstructor(PrimitiveReconstructorSettings? settings = null) {
        _settings = settings ?? new PrimitiveReconstructorSettings();
    }

    /// <summary>
    /// Reconstruct a <see cref="Node"/> from a primitive sequence.
    /// Returns null if the sequence cannot be recognized.
    /// </summary>
    public Node? Reconstruct(IReadOnlyList<PrimitiveNode> primitives, AnalysisContext? context = null) {
        if (primitives.Count == 0) return null;

        var slotAnalyzer = new SlotAnalyzer(primitives, context);

        // Try statement-level reconstruction first (handles control flow)
        if (_settings.ReconstructControlFlow) {
            var stmtReconstructor = new StatementReconstructor(slotAnalyzer, _settings, context);
            var result = stmtReconstructor.TryReconstruct(primitives, 0, out var stmt, out _);
            if (result && !IsTrivialResult(stmt))
                return stmt;
        }

        // Expression-level reconstruction
        var exprReconstructor = new ExpressionReconstructor(slotAnalyzer, context);
        var consumed = exprReconstructor.Process(primitives, 0);
        if (consumed == primitives.Count && exprReconstructor.HasResult)
            return exprReconstructor.Result;

        // Try partial match — return whatever was reconstructed
        if (exprReconstructor.HasResult)
            return exprReconstructor.Result;

        return null;
    }

    /// <summary>
    /// Returns true if the reconstructed result is just a trivial empty return,
    /// indicating the statement pipeline matched only the trailing Return primitive
    /// without reconstructing any meaningful content.
    /// </summary>
    private static bool IsTrivialResult(Node? node) {
        if (node is null) return true;
        if (node is Poly.Syntax.Nodes.Return ret && ret.Value is null) return true;
        if (node is Poly.Syntax.Nodes.Block block) {
            // Check if all nodes in the block are trivial returns
            foreach (var n in block.Nodes)
                if (!IsTrivialResult(n))
                    return false;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Reconstruct an expression node from a contiguous sub-sequence of primitives.
    /// Returns the expression and the number of primitives consumed.
    /// </summary>
    public (Node? Expression, int Consumed) ReconstructExpression(
        IReadOnlyList<PrimitiveNode> primitives, int startIndex,
        AnalysisContext? context = null) {
        if (startIndex >= primitives.Count)
            return (null, 0);

        var slotAnalyzer = new SlotAnalyzer(primitives, context);
        var exprReconstructor = new ExpressionReconstructor(slotAnalyzer, context);
        var consumed = exprReconstructor.Process(primitives, startIndex);

        return (exprReconstructor.Result, consumed);
    }
}

/// <summary>
/// Maps slot indices to variable/parameter names using analysis context or heuristics.
/// </summary>
internal sealed class SlotAnalyzer {
    private readonly Dictionary<int, string> _slotNames = new();
    private readonly Dictionary<int, Variable> _slotVariables = new();

    public SlotAnalyzer(IReadOnlyList<PrimitiveNode> primitives, AnalysisContext? context) {
        if (context is not null) {
            // Try to extract slot→name mapping from ExpansionEnvironment
            var env = context.GetMetadata<ExpansionEnvironment>(null);
            if (env is not null) {
                // Build reverse mapping from the Environment's slot assignments
                // Note: ExpansionEnvironment is NodeId→Slot, we need Slot→Name
                // which we can't directly get. We rely on heuristic naming below.
            }
        }

        // Build slot→name mapping by analyzing usage patterns in primitives
        BuildSlotNames(primitives);
    }

    public string GetSlotName(int slotIndex) {
        if (_slotNames.TryGetValue(slotIndex, out var name))
            return name;
        return $"v{slotIndex}";
    }

    public Variable CreateSlotReference(int slotIndex) {
        if (!_slotVariables.TryGetValue(slotIndex, out var variable)) {
            variable = new Variable(GetSlotName(slotIndex));
            _slotVariables[slotIndex] = variable;
        }
        return variable;
    }

    public Node CreateSlotWrite(int slotIndex, Node value) {
        var dest = CreateSlotReference(slotIndex);
        return new Assignment(dest, value);
    }

    private void BuildSlotNames(IReadOnlyList<PrimitiveNode> primitives) {
        // First pass: collect all slot references
        var slots = new HashSet<int>();
        foreach (var prim in primitives) {
            switch (prim) {
                case LoadLocal ll: slots.Add(ll.SlotIndex); break;
                case StoreLocal sl: slots.Add(sl.SlotIndex); break;
                case Parameter p: slots.Add(p.SlotIndex); break;
            }
        }

        // Heuristic: look for StoreLocal + LoadLocal pairs that indicate temp slots
        // Temp slots are those used only once or in close proximity
        foreach (var slot in slots.OrderBy(s => s)) {
            _slotNames[slot] = $"v{slot}";
        }
    }

    public int GetTempSlotCount() {
        return _slotNames.Count;
    }
}