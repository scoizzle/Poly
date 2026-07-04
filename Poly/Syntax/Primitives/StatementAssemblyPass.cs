using Poly.Syntax.Nodes;

namespace Poly.Syntax.Primitives;

using static ReconstructionHelpers;

/// <summary>
/// Pass 7 (final): Combines all recognized patterns (loops, conditionals, jumps)
/// and expression reconstructions into a reconstructed statement tree.
///
/// Uses recursive descent: walks primitives left-to-right, consuming recognized
/// patterns and falling back to expression reconstruction when no pattern matches.
/// </summary>
internal sealed class StatementAssemblyPass : IReconstructionPass {
    public ReconstructionPhase Phase => ReconstructionPhase.StatementAssembly;

    public void Run(IReadOnlyList<PrimitiveNode> primitives, ReconstructionContext context) {
        int pos = 0;
        var nodes = new List<Node>();
        while (pos < primitives.Count) {
            if (TryReconstructNode(primitives, pos, context, out var node, out var consumed)) {
                if (node is not null) nodes.Add(node);
                pos += Math.Max(consumed, 1);
            }
            else {
                pos++;
            }
        }

        context.ReconstructedRoot = nodes.Count switch {
            0 => new Block(new Constant(0L)),
            1 => nodes[0],
            _ => new Block(nodes, CollectVariables(nodes).Cast<Node>())
        };
    }

    private bool TryReconstructNode(
        IReadOnlyList<PrimitiveNode> primitives,
        int startIndex,
        ReconstructionContext context,
        out Node? result,
        out int consumed) {
        result = null;
        consumed = 0;
        if (startIndex >= primitives.Count) return false;

        // Priority 1: Recognized loops
        if (TryMatchLoop(primitives, startIndex, context, out result, out consumed))
            return true;

        // Priority 2: Recognized conditionals
        if (TryMatchConditional(primitives, startIndex, context, out result, out consumed))
            return true;

        // Priority 3: Return
        if (TryMatchReturn(primitives, startIndex, context, out result, out consumed))
            return consumed > 0;

        // Priority 4: Throw
        if (TryMatchThrow(primitives, startIndex, context, out result, out consumed))
            return consumed > 0;

        // Priority 5: Goto
        if (startIndex < primitives.Count && primitives[startIndex] is Goto gg && gg.Target is Label gl) {
            consumed = 1;
            bool isBreak = (gl.Name ?? "").EndsWith("_exit");
            bool isContinue = (gl.Name ?? "").EndsWith("_header") || (gl.Name ?? "").EndsWith("_cond");
            if (isBreak) result = new BreakStatement();
            else if (isContinue) result = new ContinueStatement();
            else result = new GotoStatement(gl.Name ?? "");
            return true;
        }

        // Priority 6: Label (skip structural labels)
        if (startIndex < primitives.Count && primitives[startIndex] is Label lbl) {
            var name = lbl.Name ?? "";
            if (IsStructuralLabel(name)) {
                consumed = 1;
                result = null; // Skip structural labels
                return true;
            }
            if (startIndex + 1 < primitives.Count) {
                if (TryReconstructNode(primitives, startIndex + 1, context, out var stmt, out var stmtConsumed)) {
                    consumed = 1 + stmtConsumed;
                    result = new LabelDeclaration(name, stmt ?? new Block(new Constant(0L)));
                    return true;
                }
            }
            consumed = 1;
            result = new LabelDeclaration(name, new Block(new Constant(0L)));
            return true;
        }

        // Priority 7: Coalesce
        if (TryMatchCoalesce(primitives, startIndex, context, out result, out consumed))
            return consumed > 0;

        // Priority 8: Switch
        if (TryMatchSwitch(primitives, startIndex, context, out result, out consumed))
            return consumed > 0;

        // Priority 9: Expression
        var expr = new ExpressionReconstructor(context.SlotAnalyzer, context.OuterContext);
        var exprConsumed = expr.Process(primitives, startIndex);
        if (exprConsumed > 0 && expr.HasResult) {
            consumed = exprConsumed;
            result = expr.Result;
            return true;
        }

        // Priority 10: Discard (statement result thrown away)
        if (startIndex < primitives.Count && primitives[startIndex] is Discard) {
            consumed = 1;
            return true;
        }

        return false;
    }

    // ── Loop reconstruction ───────────────────────────────────

    private bool TryMatchLoop(
        IReadOnlyList<PrimitiveNode> primitives,
        int startIndex,
        ReconstructionContext context,
        out Node? result,
        out int consumed) {
        result = null;
        consumed = 0;

        if (context.Loops is null) return false;

        foreach (var loop in context.Loops) {
            if (loop.HeaderIndex < startIndex && loop.ExitIndex > startIndex)
                continue; // Skip loops that started before our position
            if (loop.HeaderIndex != startIndex)
                continue; // Must start exactly at this position

            switch (loop.LoopKind) {
                case "while":
                    return ReconstructWhile(primitives, loop, context, out result, out consumed);
                case "dowhile":
                    return ReconstructDoWhile(primitives, loop, context, out result, out consumed);
                case "for":
                    return ReconstructFor(primitives, loop, context, out result, out consumed);
            }
        }

        return false;
    }

    private bool ReconstructWhile(
        IReadOnlyList<PrimitiveNode> primitives, LoopInfo loop,
        ReconstructionContext context, out Node? result, out int consumed) {
        result = null;
        consumed = loop.ExitIndex - loop.HeaderIndex + 1;

        var condPrims = Slice(primitives, loop.HeaderIndex + 1, loop.CondGotoIndex);
        var bodyPrims = Slice(primitives, loop.BodyStart, loop.BodyEnd);

        var cond = ReconstructExpression(condPrims, context);
        var body = ReconstructBody(bodyPrims, context);

        result = new WhileLoop(cond ?? new Constant(1L), body);
        return true;
    }

    private bool ReconstructDoWhile(
        IReadOnlyList<PrimitiveNode> primitives, LoopInfo loop,
        ReconstructionContext context, out Node? result, out int consumed) {
        result = null;
        consumed = loop.ExitIndex - loop.HeaderIndex + 1;

        var bodyPrims = Slice(primitives, loop.BodyStart, loop.BodyEnd);

        int condStart = -1;
        for (int i = loop.BodyEnd; i < loop.CondGotoIndex; i++) {
            if (primitives[i] is Label { Name: "dowhile_cond" }) { condStart = i + 1; break; }
        }

        Node? cond = null;
        if (condStart > 0) {
            var condPrims = Slice(primitives, condStart, loop.CondGotoIndex);
            cond = ReconstructExpression(condPrims, context);
        }

        var body = ReconstructBody(bodyPrims, context);
        result = new DoWhileLoop(body, cond ?? new Constant(1L));
        return true;
    }

    private bool ReconstructFor(
        IReadOnlyList<PrimitiveNode> primitives, LoopInfo loop,
        ReconstructionContext context, out Node? result, out int consumed) {
        result = null;
        consumed = loop.ExitIndex - loop.HeaderIndex + 1;

        // Init
        Node? init = null;
        int initStart = Math.Max(0, loop.HeaderIndex - 3);
        if (initStart < loop.HeaderIndex) {
            var initPrims = Slice(primitives, initStart, loop.HeaderIndex);
            if (initPrims.Count > 0 && initPrims[^1] is Discard)
                initPrims = Slice(initPrims, 0, initPrims.Count - 1);
            if (initPrims.Count > 0)
                init = ReconstructExpression(initPrims, context);
        }

        // Condition
        Node? cond = null;
        if (loop.CondGotoIndex > loop.HeaderIndex + 1) {
            var condPrims = Slice(primitives, loop.HeaderIndex + 1, loop.CondGotoIndex);
            cond = ReconstructExpression(condPrims, context);
        }

        // Body
        var bodyPrims = Slice(primitives, loop.BodyStart, loop.BodyEnd);
        var body = ReconstructBody(bodyPrims, context);

        // Increment
        Node? incr = null;
        int incrScan = loop.BodyEnd;
        while (incrScan < loop.GotoIndex && primitives[incrScan] is Discard) incrScan++;
        int incrEnd = loop.GotoIndex;
        while (incrEnd > incrScan && primitives[incrEnd - 1] is Discard) incrEnd--;
        if (incrScan < incrEnd) {
            var incrPrims = Slice(primitives, incrScan, incrEnd);
            incr = ReconstructExpression(incrPrims, context);
        }

        result = new ForLoop(init, cond, incr, body);
        return true;
    }

    // ── Conditional reconstruction ────────────────────────────

    private bool TryMatchConditional(
        IReadOnlyList<PrimitiveNode> primitives,
        int startIndex,
        ReconstructionContext context,
        out Node? result,
        out int consumed) {
        result = null;
        consumed = 0;

        if (context.Conditionals is null) return false;

        foreach (var cond in context.Conditionals) {
            int condStart = FindInlineStart(primitives, cond.CondGotoIndex);
            if (condStart != startIndex) continue;

            var condPrims = Slice(primitives, condStart, cond.CondGotoIndex);
            var conditionNode = ReconstructExpression(condPrims, context);
            if (conditionNode is null) return false;

            if (cond.Kind == "ternary") {
                var thenPrims = Slice(primitives, cond.ThenStart, cond.ThenEnd);
                var elsePrims = Slice(primitives, cond.ElseLabelIndex + 1, cond.ElseEnd);
                var thenNode = ReconstructExpression(thenPrims, context);
                var elseNode = ReconstructExpression(elsePrims, context);
                consumed = (cond.LoadLocalIndex ?? cond.MergeLabelIndex) - condStart + 1;
                result = new Conditional(conditionNode,
                    thenNode ?? new Constant(0L),
                    elseNode ?? new Constant(0L));
                return true;
            }

            if (cond.Kind == "if") {
                var thenPrims = Slice(primitives, cond.ThenStart, cond.ThenEnd);
                var thenBody = ReconstructBody(thenPrims, context);
                Node? elseBody = null;
                if (cond.ElseLabelIndex + 1 < cond.ElseEnd) {
                    var elsePrims = Slice(primitives, cond.ElseLabelIndex + 1, cond.ElseEnd);
                    elseBody = ReconstructBody(elsePrims, context);
                }
                consumed = cond.MergeLabelIndex - condStart + 1;
                result = new IfStatement(conditionNode, thenBody, elseBody);
                return true;
            }
        }

        return false;
    }

    // ── Return ────────────────────────────────────────────────

    private bool TryMatchReturn(
        IReadOnlyList<PrimitiveNode> primitives,
        int startIndex,
        ReconstructionContext context,
        out Node? result,
        out int consumed) {
        result = null;
        consumed = 0;

        int returnIdx = -1;
        for (int i = startIndex; i < primitives.Count; i++) {
            if (primitives[i] is Return) { returnIdx = i; break; }
        }
        if (returnIdx < 0) return false;

        if (returnIdx == startIndex) {
            consumed = 1;
            result = new Poly.Syntax.Nodes.Return();
            return true;
        }

        if (returnIdx == startIndex + 1 && primitives[startIndex] is PushConstant { Value: 0L }) {
            consumed = 2;
            result = new Poly.Syntax.Nodes.Return();
            return true;
        }

        var valuePrims = Slice(primitives, startIndex, returnIdx);
        // Try to reconstruct using expression reconstructor
        var expr = new ExpressionReconstructor(
            new SlotAnalyzer(primitives, context.OuterContext),
            context.OuterContext);
        var processed = expr.Process(valuePrims, 0);
        if (processed > 0 && expr.HasResult) {
            consumed = returnIdx - startIndex + 1;
            result = new Poly.Syntax.Nodes.Return(expr.Result);
            return true;
        }

        consumed = returnIdx - startIndex + 1;
        result = new Poly.Syntax.Nodes.Return(new Constant(0L));
        return true;
    }

    // ── Throw ─────────────────────────────────────────────────

    private bool TryMatchThrow(
        IReadOnlyList<PrimitiveNode> primitives,
        int startIndex,
        ReconstructionContext context,
        out Node? result,
        out int consumed) {
        result = null;
        consumed = 0;

        int throwIdx = -1;
        for (int i = Math.Max(0, startIndex); i < primitives.Count; i++) {
            if (primitives[i] is Throw) { throwIdx = i; break; }
        }
        if (throwIdx < 0 || throwIdx <= startIndex) return false;

        var exPrims = Slice(primitives, startIndex, throwIdx);
        var ex = ReconstructExpressionOrDefault(exPrims, context);
        consumed = throwIdx - startIndex + 1;
        result = new ThrowStatement(ex ?? new Constant(0L));
        return true;
    }

    // ── Coalesce ──────────────────────────────────────────────

    private bool TryMatchCoalesce(
        IReadOnlyList<PrimitiveNode> primitives,
        int startIndex,
        ReconstructionContext context,
        out Node? result,
        out int consumed) {
        result = null;
        consumed = 0;

        int dupIdx = -1;
        for (int i = startIndex; i < primitives.Count; i++) {
            if (primitives[i] is Dup) { dupIdx = i; break; }
        }
        if (dupIdx < 0) return false;

        int condGotoIdx = FindCondGoto(primitives, dupIdx + 1, out var targetLabel);
        if (condGotoIdx < 0 || targetLabel != "coalesce_null") return false;
        if (condGotoIdx + 1 >= primitives.Count || primitives[condGotoIdx + 1] is not Return) return false;

        int nullLabelIdx = FindLabel(primitives, condGotoIdx + 2, "coalesce_null");
        if (nullLabelIdx < 0) return false;

        int discardIdx = nullLabelIdx + 1;
        if (discardIdx >= primitives.Count || primitives[discardIdx] is not Discard) return false;

        int returnIdx2 = -1;
        for (int i = discardIdx + 1; i < primitives.Count; i++) {
            if (primitives[i] is Return) { returnIdx2 = i; break; }
        }
        if (returnIdx2 < 0) return false;

        var lhsPrims = Slice(primitives, startIndex, dupIdx);
        var rhsPrims = Slice(primitives, discardIdx + 1, returnIdx2);

        var lhsExpr = new ExpressionReconstructor(context.SlotAnalyzer, context.OuterContext);
        lhsExpr.Process(lhsPrims, 0);
        if (!lhsExpr.HasResult) return false;

        var rhsExpr = new ExpressionReconstructor(context.SlotAnalyzer, context.OuterContext);
        rhsExpr.Process(rhsPrims, 0);

        consumed = returnIdx2 - startIndex + 1;
        result = new Coalesce(lhsExpr.Result!, rhsExpr.Result ?? new Constant(0L));
        return true;
    }

    // ── Switch ────────────────────────────────────────────────

    private bool TryMatchSwitch(
        IReadOnlyList<PrimitiveNode> primitives,
        int startIndex,
        ReconstructionContext context,
        out Node? result,
        out int consumed) {
        result = null;
        consumed = 0;

        int endIdx = FindLabel(primitives, startIndex, "switch_end");
        if (endIdx < 0) return false;

        var casePositions = new List<int>();
        for (int i = startIndex; i < endIdx; i++) {
            if (primitives[i] is Label { Name: "case" }) casePositions.Add(i);
        }
        if (casePositions.Count == 0) return false;

        int firstDupIdx = -1;
        for (int i = startIndex; i < casePositions[0]; i++) {
            if (primitives[i] is Dup) { firstDupIdx = i; break; }
        }
        if (firstDupIdx < 0) return false;

        // Reconstruct value expression
        var valuePrims = Slice(primitives, startIndex, firstDupIdx);
        var valueExpr = new ExpressionReconstructor(context.SlotAnalyzer, context.OuterContext);
        valueExpr.Process(valuePrims, 0);
        if (!valueExpr.HasResult) return false;

        // Reconstruct each case
        var cases = new List<SwitchCase>();
        Node? defaultCase = null;

        for (int c = 0; c < casePositions.Count; c++) {
            int caseIdx = casePositions[c];

            // Find pattern for this case
            Node? patternResult = null;
            for (int i = firstDupIdx; i < caseIdx; i++) {
                if (primitives[i] is CondGoto cg && ReferenceEquals(cg.Target, primitives[caseIdx])) {
                    int eqIdx = -1;
                    for (int j = i - 1; j >= firstDupIdx; j--) {
                        if (primitives[j] is BinaryOp { Op: OpKind.Eq }) { eqIdx = j; break; }
                    }
                    if (eqIdx > 0) {
                        int patStart = -1;
                        for (int k = eqIdx - 1; k >= firstDupIdx; k--) {
                            if (primitives[k] is Dup) { patStart = k + 1; break; }
                        }
                        if (patStart >= 0) {
                            var patternPrims = Slice(primitives, patStart, eqIdx);
                            var pExpr = new ExpressionReconstructor(context.SlotAnalyzer, context.OuterContext);
                            pExpr.Process(patternPrims, 0);
                            patternResult = pExpr.Result;
                        }
                    }
                    break;
                }
            }

            // Body
            int bodyStart = caseIdx + 1;
            if (bodyStart < primitives.Count && primitives[bodyStart] is Discard)
                bodyStart++;

            int bodyEnd = (c + 1 < casePositions.Count) ? casePositions[c + 1] : endIdx;
            var bodyPrims = Slice(primitives, bodyStart, bodyEnd);
            while (bodyPrims.Count > 0 && bodyPrims[^1] is Goto or Label)
                bodyPrims = Slice(bodyPrims, 0, bodyPrims.Count - 1);

            cases.Add(new SwitchCase(patternResult ?? new Constant(0L),
                ReconstructBody(bodyPrims, context)));
        }

        // Default: between the Discard after the value chain and the first case
        for (int i = firstDupIdx + 1; i < casePositions[0]; i++) {
            if (primitives[i] is Discard) {
                var defaultPrims = Slice(primitives, i + 1, casePositions[0]);
                while (defaultPrims.Count > 0 && defaultPrims[^1] is Goto or Label)
                    defaultPrims = Slice(defaultPrims, 0, defaultPrims.Count - 1);
                if (defaultPrims.Count > 0)
                    defaultCase = ReconstructBody(defaultPrims, context);
                break;
            }
        }

        consumed = endIdx - startIndex + 1;
        result = new SwitchStatement(valueExpr.Result!, cases, defaultCase);
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────

    private static Node? ReconstructExpression(
        IReadOnlyList<PrimitiveNode> prims,
        ReconstructionContext context) {
        if (prims.Count == 0) return null;
        var expr = new ExpressionReconstructor(context.SlotAnalyzer, context.OuterContext);
        expr.Process(prims, 0);
        return expr.Result;
    }

    private static Node ReconstructExpressionOrDefault(
        IReadOnlyList<PrimitiveNode> prims,
        ReconstructionContext context) {
        return ReconstructExpression(prims, context) ?? new Constant(0L);
    }

    private Node ReconstructBody(
        IReadOnlyList<PrimitiveNode> prims,
        ReconstructionContext context) {
        if (prims.Count == 0) return new Block(new Constant(0L));

        var nodes = new List<Node>();
        int pos = 0;
        while (pos < prims.Count) {
            if (TryReconstructNode(prims, pos, context, out var node, out var consumed)) {
                if (node is not null) nodes.Add(node);
                pos += Math.Max(consumed, 1);
            }
            else {
                pos++;
            }
        }

        if (nodes.Count == 1) return nodes[0];
        if (nodes.Count > 1) {
            // Collect variable references from all nodes to declare them in the block
            var variables = CollectVariables(nodes).Cast<Node>();
            return new Block(nodes, variables);
        }

        var expr = new ExpressionReconstructor(context.SlotAnalyzer, context.OuterContext);
        var processed = expr.Process(prims, 0);
        if (processed > 0 && expr.HasResult) return expr.Result!;

        return new Block(new Constant(0L));
    }

    /// <summary>
    /// Collect all unique <see cref="Variable"/> references from a list of nodes
    /// for use as block-level variable declarations.
    /// </summary>
    private static IReadOnlyList<Variable?> CollectVariables(IReadOnlyList<Node> nodes) {
        var vars = new HashSet<string>();
        var result = new List<Variable?>();
        foreach (var node in nodes)
            CollectVariablesFromNode(node, vars, result);
        return result;
    }

    private static void CollectVariablesFromNode(Node? node, HashSet<string> seen, List<Variable?> result) {
        if (node is Variable v && !seen.Contains(v.Name)) {
            seen.Add(v.Name);
            result.Add(v);
        }
        if (node is Block block) {
            foreach (var child in block.Nodes)
                CollectVariablesFromNode(child, seen, result);
        }
        else if (node is Assignment assignment) {
            CollectVariablesFromNode(assignment.Destination, seen, result);
            CollectVariablesFromNode(assignment.Value, seen, result);
        }
        else if (node is WhileLoop wl) {
            CollectVariablesFromNode(wl.Condition, seen, result);
            CollectVariablesFromNode(wl.Body, seen, result);
        }
        else if (node is DoWhileLoop dwl) {
            CollectVariablesFromNode(dwl.Body, seen, result);
            CollectVariablesFromNode(dwl.Condition, seen, result);
        }
        else if (node is IfStatement ifs) {
            CollectVariablesFromNode(ifs.Condition, seen, result);
            CollectVariablesFromNode(ifs.ThenBranch, seen, result);
            CollectVariablesFromNode(ifs.ElseBranch, seen, result);
        }
        else if (node is ForLoop fl) {
            CollectVariablesFromNode(fl.Initializer, seen, result);
            CollectVariablesFromNode(fl.Condition, seen, result);
            CollectVariablesFromNode(fl.Increment, seen, result);
            CollectVariablesFromNode(fl.Body, seen, result);
        }
        else if (node is ForEachLoop fel) {
            CollectVariablesFromNode(fel.Collection, seen, result);
            CollectVariablesFromNode(fel.Body, seen, result);
        }
    }

    /// <summary>
    /// Scan backward from the given index to find where an inline expression starts,
    /// stopping at structural boundaries (Label, Goto, Discard after statement boundary).
    /// </summary>
    private static int FindInlineStart(IReadOnlyList<PrimitiveNode> primitives, int fromIndex) {
        int i = fromIndex - 1;
        while (i >= 0 && primitives[i] is not (Goto or CondGoto or Label or Discard or Return))
            i--;
        return i + 1;
    }
}