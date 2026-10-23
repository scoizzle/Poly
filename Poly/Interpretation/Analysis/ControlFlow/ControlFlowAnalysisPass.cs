using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Interpretation.Analysis.ControlFlow;

/// <summary>
/// Metadata containing control flow analysis results for an AST.
/// </summary>
public sealed record ControlFlowMetadata(ControlFlowGraph Graph) : IAnalysisMetadata;

/// <summary>
/// Builds a control flow graph from an AST and performs reachability analysis.
/// </summary>
public sealed class ControlFlowAnalysisPass : INodeAnalyzer {
    public const string Id = "ControlFlow";
    public string PassName => Id;
    public void Analyze(AnalysisContext context, Node node) {
        var state = new CfgState();
        state.CurrentBlock = state.Cfg.CreateBlock();
        BuildCfg(context, node, state);

        // Resolve pending gotos
        foreach (var (gotoStmt, label) in state.PendingGotos) {
            if (state.LabeledBlocks.TryGetValue(label, out var targetBlock)) {
                var sourceBlock = state.Cfg.GetBlockForNode(gotoStmt);
                sourceBlock?.AddSuccessor(targetBlock);
            }
            else {
                context.ReportDiagnostic(gotoStmt, DiagnosticSeverity.Error,
                    $"Goto target label '{label}' not found", "CF0001");
            }
        }

        // Finalize CFG
        state.Cfg.IdentifyExitBlocks();
        state.Cfg.ComputeReachability();

        // Dead label detection
        foreach (var (name, labelBlock) in state.LabeledBlocks) {
            if (!labelBlock.IsReachable && state.LabelDecls.TryGetValue(name, out var decl)) {
                context.ReportDiagnostic(decl, DiagnosticSeverity.Warning, $"Unreachable label '{name}'", "CF0013");
                MarkElidable(context, decl, "CF0013", $"Label declaration '{name}' is unreachable from live code");
            }
        }

        // Report dead code diagnostics
        foreach (var deadNode in state.Cfg.DeadCode) {
            context.ReportDiagnostic(deadNode, DiagnosticSeverity.Warning,
                "Unreachable code detected", "CF0002");
        }

        // Tag dead/unreachable nodes with Elidable
        foreach (var deadNode in state.Cfg.DeadCode) {
            ElisionFacts.Mark(context, deadNode, ElisionReason.Unreachable);
        }

        // Store CFG as metadata on root node
        context.SetMetadata(node, new ControlFlowMetadata(state.Cfg));
    }

    private sealed class CfgState {
        public ControlFlowGraph Cfg = new();
        public BasicBlock? CurrentBlock;

        // String-keyed dictionaries for goto → label resolution.
        // TODO: Replace with NodeId-keyed lookup once node-locator infra exists.
        public Dictionary<string, BasicBlock> LabeledBlocks = [];
        public Dictionary<string, LabelDeclaration> LabelDecls = [];
        public List<(GotoStatement Goto, string Label)> PendingGotos = [];

        /// <summary>Stack used only for break-in-switch resolution
        /// (switches don't get ResolvedJumpTarget metadata yet).
        /// Loop break/continue uses <see cref="LoopTargets"/> instead.</summary>
        public Stack<(BasicBlock Continue, BasicBlock Break)> LoopContexts = new();

        /// <summary>Maps a loop's NodeId to its continue/break blocks.
        /// Populated by loop builders, consumed by break/continue handlers
        /// via <see cref="ResolvedJumpTarget"/> metadata.
        /// Key is the WhileLoop/ForLoop/etc. node's <see cref="Node.Id"/>.</summary>
        public Dictionary<NodeId, (BasicBlock Continue, BasicBlock Break)> LoopTargets = new();
    }

    private void BuildCfg(AnalysisContext context, Node node, CfgState state) {
        if (state.CurrentBlock == null) return;

        switch (node) {
            case Block block:
                BuildBlockCfg(context, block, state);
                break;

            case IfStatement ifStmt:
                BuildIfCfg(context, ifStmt, state);
                break;

            case WhileLoop whileLoop:
                BuildWhileLoopCfg(context, whileLoop, state);
                break;

            case DoWhileLoop doWhileLoop:
                BuildDoWhileLoopCfg(context, doWhileLoop, state);
                break;

            case ForLoop forLoop:
                BuildForLoopCfg(context, forLoop, state);
                break;

            case ForEachLoop forEachLoop:
                BuildForEachLoopCfg(context, forEachLoop, state);
                break;

            case Return returnStmt:
                AddStatement(returnStmt, state);
                state.CurrentBlock.SetTerminator(returnStmt);
                state.CurrentBlock = null;
                break;

            case ThrowStatement throwStmt:
                AddStatement(throwStmt, state);
                state.CurrentBlock.SetTerminator(throwStmt);
                state.CurrentBlock = null;
                break;

            case BreakStatement breakStmt:
                AddStatement(breakStmt, state);
                state.CurrentBlock.SetTerminator(breakStmt);
                if (!TryResolveBreak(breakStmt, context, state)) {
                    // Fallback: break inside a switch (no ResolvedJumpTarget yet).
                    if (state.LoopContexts.TryPeek(out var loopCtx))
                        state.CurrentBlock.AddSuccessor(loopCtx.Break);
                }
                state.CurrentBlock = null;
                break;

            case ContinueStatement continueStmt:
                AddStatement(continueStmt, state);
                state.CurrentBlock.SetTerminator(continueStmt);
                if (!TryResolveContinue(continueStmt, context, state)) {
                    // Fallback: continue inside a switch (unusual but handled).
                    if (state.LoopContexts.TryPeek(out var continueCtx))
                        state.CurrentBlock.AddSuccessor(continueCtx.Continue);
                }
                state.CurrentBlock = null;
                break;

            case GotoStatement gotoStmt:
                if (state.CurrentBlock != null) {
                    AddStatement(gotoStmt, state);
                    state.CurrentBlock.SetTerminator(gotoStmt);
                    state.PendingGotos.Add((gotoStmt, gotoStmt.Target));
                    state.CurrentBlock = null;
                }
                break;

            case LabelDeclaration labelDecl:
                var labelBlock = state.Cfg.CreateBlock();
                if (state.CurrentBlock != null) {
                    state.CurrentBlock.AddSuccessor(labelBlock);
                }
                state.CurrentBlock = labelBlock;
                state.LabeledBlocks[labelDecl.Name] = labelBlock;
                state.LabelDecls[labelDecl.Name] = labelDecl;
                AddStatement(labelDecl, state);
                break;

            case TryCatchFinally tryCatch:
                BuildTryCatchCfg(context, tryCatch, state);
                break;

            case SwitchStatement switchStmt:
                BuildSwitchCfg(context, switchStmt, state);
                break;

            default:
                AddStatement(node, state);
                break;
        }
    }

    private static void AddStatement(Node node, CfgState state) {
        if (state.CurrentBlock == null) return;
        state.CurrentBlock.AddStatement(node);
        state.Cfg.MapNodeToBlock(node, state.CurrentBlock);
    }

    private void BuildBlockCfg(AnalysisContext context, Block block, CfgState state) {
        foreach (var stmt in block.Nodes) {
            if (state.CurrentBlock == null) {
                state.CurrentBlock = state.Cfg.CreateBlock();
            }
            BuildCfg(context, stmt, state);
        }
    }

    private void BuildIfCfg(AnalysisContext context, IfStatement ifStmt, CfgState state) {
        if (state.CurrentBlock == null) return;

        AddStatement(ifStmt.Condition, state);

        var conditionBlock = state.CurrentBlock;
        var mergeBlock = state.Cfg.CreateBlock();

        bool constTrue = IsStaticallyConstantTrue(context, ifStmt.Condition);
        bool constFalse = IsStaticallyConstantFalse(context, ifStmt.Condition);

        if (constTrue) {
            var thenBlock = state.Cfg.CreateBlock();
            conditionBlock.AddSuccessor(thenBlock);
            state.CurrentBlock = thenBlock;
            BuildCfg(context, ifStmt.ThenBranch, state);
            var afterThen = state.CurrentBlock;
            if (afterThen != null) {
                afterThen.AddSuccessor(mergeBlock);
            }
            if (ifStmt.ElseBranch != null) {
                MarkSubtreeElidable(context, ifStmt.ElseBranch, "CF0004", "Else branch is unreachable because if condition is constantly true");
            }
        }
        else if (constFalse) {
            if (ifStmt.ElseBranch != null) {
                var elseBlock = state.Cfg.CreateBlock();
                conditionBlock.AddSuccessor(elseBlock);
                state.CurrentBlock = elseBlock;
                BuildCfg(context, ifStmt.ElseBranch, state);
                var afterElse = state.CurrentBlock;
                if (afterElse != null) {
                    afterElse.AddSuccessor(mergeBlock);
                }
            }
            else {
                conditionBlock.AddSuccessor(mergeBlock);
            }
            MarkSubtreeElidable(context, ifStmt.ThenBranch, "CF0005", "Then branch is unreachable because if condition is constantly false");
        }
        else {
            var thenBlock = state.Cfg.CreateBlock();
            conditionBlock.AddSuccessor(thenBlock);
            state.CurrentBlock = thenBlock;
            BuildCfg(context, ifStmt.ThenBranch, state);
            var afterThen = state.CurrentBlock;

            if (ifStmt.ElseBranch != null) {
                var elseBlock = state.Cfg.CreateBlock();
                conditionBlock.AddSuccessor(elseBlock);
                state.CurrentBlock = elseBlock;
                BuildCfg(context, ifStmt.ElseBranch, state);
                var afterElse = state.CurrentBlock;
                if (afterElse != null) {
                    afterElse.AddSuccessor(mergeBlock);
                }
            }
            else {
                conditionBlock.AddSuccessor(mergeBlock);
            }

            if (afterThen != null) {
                afterThen.AddSuccessor(mergeBlock);
            }
        }

        state.CurrentBlock = mergeBlock.Predecessors.Count > 0 ? mergeBlock : null;
    }

    private void BuildWhileLoopCfg(AnalysisContext context, WhileLoop whileLoop, CfgState state) {
        if (state.CurrentBlock == null) return;

        var preLoop = state.CurrentBlock;
        var conditionBlock = state.Cfg.CreateBlock();
        var bodyBlock = state.Cfg.CreateBlock();
        var exitBlock = state.Cfg.CreateBlock();

        preLoop.AddSuccessor(conditionBlock);

        state.CurrentBlock = conditionBlock;
        AddStatement(whileLoop.Condition, state);

        bool isInfinite = IsStaticallyInfinite(whileLoop, context);
        bool constFalse = IsStaticallyConstantFalse(context, whileLoop.Condition);

        if (constFalse) {
            conditionBlock.AddSuccessor(exitBlock);
            MarkSubtreeElidable(context, whileLoop.Body, "CF0006", "While body is unreachable because condition is constantly false");
        }
        else if (isInfinite) {
            conditionBlock.AddSuccessor(bodyBlock);
            context.ReportDiagnostic(whileLoop, DiagnosticSeverity.Information,
                "Infinite loop detected", "CF0003");
        }
        else {
            conditionBlock.AddSuccessor(bodyBlock);
            conditionBlock.AddSuccessor(exitBlock);
        }

        state.LoopTargets[whileLoop.Id] = (conditionBlock, exitBlock);
        state.CurrentBlock = bodyBlock;
        if (!constFalse) {
            BuildCfg(context, whileLoop.Body, state);
        }

        if (state.CurrentBlock != null && !constFalse) {
            state.CurrentBlock.AddSuccessor(conditionBlock);
        }

        state.CurrentBlock = exitBlock;
    }

    private void BuildDoWhileLoopCfg(AnalysisContext context, DoWhileLoop doWhileLoop, CfgState state) {
        if (state.CurrentBlock == null) return;

        var preLoop = state.CurrentBlock;
        var bodyBlock = state.Cfg.CreateBlock();
        var conditionBlock = state.Cfg.CreateBlock();
        var exitBlock = state.Cfg.CreateBlock();

        preLoop.AddSuccessor(bodyBlock);

        state.LoopTargets[doWhileLoop.Id] = (conditionBlock, exitBlock);
        state.CurrentBlock = bodyBlock;
        BuildCfg(context, doWhileLoop.Body, state);

        if (state.CurrentBlock != null) {
            state.CurrentBlock.AddSuccessor(conditionBlock);
        }
        state.CurrentBlock = conditionBlock;
        AddStatement(doWhileLoop.Condition, state);
        conditionBlock.AddSuccessor(bodyBlock);
        if (!IsStaticallyInfinite(doWhileLoop, context)) {
            conditionBlock.AddSuccessor(exitBlock);
        }
        if (IsStaticallyInfinite(doWhileLoop, context)) {
            context.ReportDiagnostic(doWhileLoop, DiagnosticSeverity.Information, "Infinite loop detected", "CF0003");
        }

        state.CurrentBlock = exitBlock;
    }

    private void BuildForLoopCfg(AnalysisContext context, ForLoop forLoop, CfgState state) {
        if (state.CurrentBlock == null) return;

        if (forLoop.Initializer != null) {
            AddStatement(forLoop.Initializer, state);
        }

        var preLoop = state.CurrentBlock;
        var conditionBlock = state.Cfg.CreateBlock();
        var bodyBlock = state.Cfg.CreateBlock();
        var iteratorBlock = state.Cfg.CreateBlock();
        var exitBlock = state.Cfg.CreateBlock();

        preLoop.AddSuccessor(conditionBlock);

        state.CurrentBlock = conditionBlock;
        if (forLoop.Condition != null) {
            AddStatement(forLoop.Condition, state);
        }

        bool isInfinite = IsStaticallyInfinite(forLoop, context);
        bool constFalse = forLoop.Condition != null && IsStaticallyConstantFalse(context, forLoop.Condition);
        conditionBlock.AddSuccessor(bodyBlock);
        if (constFalse) {
            conditionBlock.AddSuccessor(exitBlock);
            MarkSubtreeElidable(context, forLoop.Body, "CF0006", "For body is unreachable because condition is constantly false");
        }
        else if (isInfinite) {
            context.ReportDiagnostic(forLoop, DiagnosticSeverity.Information, "Infinite loop detected", "CF0003");
        }
        else {
            conditionBlock.AddSuccessor(exitBlock);
        }

        state.LoopTargets[forLoop.Id] = (iteratorBlock, exitBlock);
        state.CurrentBlock = bodyBlock;
        if (!constFalse) {
            BuildCfg(context, forLoop.Body, state);
        }

        if (state.CurrentBlock != null && !constFalse) {
            state.CurrentBlock.AddSuccessor(iteratorBlock);
        }
        if (!constFalse) {
            state.CurrentBlock = iteratorBlock;
            if (forLoop.Increment != null) {
                AddStatement(forLoop.Increment, state);
            }
            iteratorBlock.AddSuccessor(conditionBlock);
        }

        state.CurrentBlock = exitBlock;
    }

    private void BuildForEachLoopCfg(AnalysisContext context, ForEachLoop forEachLoop, CfgState state) {
        if (state.CurrentBlock == null) return;

        var preLoop = state.CurrentBlock;
        var conditionBlock = state.Cfg.CreateBlock();
        var bodyBlock = state.Cfg.CreateBlock();
        var exitBlock = state.Cfg.CreateBlock();

        preLoop.AddSuccessor(conditionBlock);

        state.CurrentBlock = conditionBlock;
        AddStatement(forEachLoop.Collection, state);
        conditionBlock.AddSuccessor(bodyBlock);
        conditionBlock.AddSuccessor(exitBlock);

        state.LoopTargets[forEachLoop.Id] = (conditionBlock, exitBlock);
        state.CurrentBlock = bodyBlock;
        BuildCfg(context, forEachLoop.Body, state);

        if (state.CurrentBlock != null) {
            state.CurrentBlock.AddSuccessor(conditionBlock);
        }

        state.CurrentBlock = exitBlock;
    }

    private void BuildTryCatchCfg(AnalysisContext context, TryCatchFinally tryCatch, CfgState state) {
        if (state.CurrentBlock == null) return;

        var preTry = state.CurrentBlock;
        var tryBlock = state.Cfg.CreateBlock();
        var mergeBlock = state.Cfg.CreateBlock();

        preTry.AddSuccessor(tryBlock);

        state.CurrentBlock = tryBlock;
        BuildCfg(context, tryCatch.TryBlock, state);
        var afterTry = state.CurrentBlock;

        bool mayThrow = ContainsThrow(context, tryCatch.TryBlock);

        if (tryCatch.CatchClauses != null) {
            foreach (var catchClause in tryCatch.CatchClauses) {
                if (!mayThrow) {
                    MarkSubtreeElidable(context, catchClause.Body, "CF0010", "Catch clause is unreachable - no throw statement in try block");
                    continue;
                }
                var catchEntry = state.Cfg.CreateBlock();
                tryBlock.AddSuccessor(catchEntry);
                state.CurrentBlock = catchEntry;

                if (catchClause.ExceptionType != null) {
                    AddStatement(catchClause.ExceptionType, state);
                }

                BuildCfg(context, catchClause.Body, state);

                if (state.CurrentBlock != null) {
                    state.CurrentBlock.AddSuccessor(mergeBlock);
                }
            }
        }

        if (tryCatch.FinallyBlock != null) {
            var finallyEntry = state.Cfg.CreateBlock();
            if (afterTry != null) {
                afterTry.AddSuccessor(finallyEntry);
            }
            state.CurrentBlock = finallyEntry;
            BuildCfg(context, tryCatch.FinallyBlock, state);

            if (state.CurrentBlock != null) {
                state.CurrentBlock.AddSuccessor(mergeBlock);
            }
        }
        else if (afterTry != null) {
            afterTry.AddSuccessor(mergeBlock);
        }

        state.CurrentBlock = mergeBlock.Predecessors.Count > 0 ? mergeBlock : null;
    }

    private bool ContainsThrow(AnalysisContext context, Node node) {
        if (node is ThrowStatement) return true;
        return this.AnyChild<ControlFlowMetadata>(context, node, static (_, ch) => ch is ThrowStatement);
    }

    private void BuildSwitchCfg(AnalysisContext context, SwitchStatement switchStmt, CfgState state) {
        if (state.CurrentBlock == null) return;

        AddStatement(switchStmt.Value, state);
        var switchBlock = state.CurrentBlock;
        var exitBlock = state.Cfg.CreateBlock();

        state.LoopContexts.Push((Continue: switchBlock, Break: exitBlock));

        object? switchConst = context.HasSideEffects(switchStmt.Value) ? null : GetConstValue(context, switchStmt.Value);
        bool hasConstValue = switchConst != null;
        bool sawExactMatch = false;

        foreach (var caseBlock in switchStmt.Cases) {
            bool live = true;
            if (hasConstValue && caseBlock.Pattern is Constant pc) {
                live = Equals(pc.Value, switchConst);
                if (live) sawExactMatch = true;
            }
            if (hasConstValue && !live) {
                MarkSubtreeElidable(context, caseBlock.Body, "CF0011", $"Switch case is unreachable because value is constantly {switchConst}");
                continue;
            }

            var caseEntry = state.Cfg.CreateBlock();
            switchBlock.AddSuccessor(caseEntry);
            state.CurrentBlock = caseEntry;

            AddStatement(caseBlock.Pattern, state);
            BuildCfg(context, caseBlock.Body, state);

            if (state.CurrentBlock != null) {
                state.CurrentBlock.AddSuccessor(exitBlock);
            }
        }

        if (switchStmt.DefaultCase != null) {
            bool defaultLive = true;
            if (hasConstValue && sawExactMatch) {
                defaultLive = false;
            }
            if (hasConstValue && !defaultLive) {
                MarkSubtreeElidable(context, switchStmt.DefaultCase, "CF0012", "Default case is unreachable - switch value covered by prior cases");
            }
            else {
                var defaultEntry = state.Cfg.CreateBlock();
                switchBlock.AddSuccessor(defaultEntry);
                state.CurrentBlock = defaultEntry;
                BuildCfg(context, switchStmt.DefaultCase, state);
                if (state.CurrentBlock != null) {
                    state.CurrentBlock.AddSuccessor(exitBlock);
                }
            }
        }

        state.LoopContexts.Pop();
        state.CurrentBlock = exitBlock;
    }

    // --- Infinite loop / termination helpers (migrated from SideEffectAnalyzer) ---
    // These use purity facts (HasSideEffects) from prior passes (e.g. SideEffectAnalysis)
    // and simple mutation checks. They enable precise CFG (no impossible exit edges)
    // and the CF0003 diagnostic.

    private bool IsStaticallyInfinite(WhileLoop loop, AnalysisContext context) {
        if (loop.Condition == null) return false;
        var condVars = CollectVariables(context, loop.Condition);
        bool condPure = !context.HasSideEffects(loop.Condition);
        bool mutates = HasMutationToVars(context, loop.Body, condVars);
        if (condPure && !mutates && GetConstBool(context, loop.Condition) is { } v) {
            return v;
        }
        return false;
    }

    private bool IsStaticallyInfinite(DoWhileLoop loop, AnalysisContext context) {
        if (loop.Condition == null) return false;
        var condVars = CollectVariables(context, loop.Condition);
        bool condPure = !context.HasSideEffects(loop.Condition);
        bool mutates = HasMutationToVars(context, loop.Body, condVars);
        if (condPure && !mutates && GetConstBool(context, loop.Condition) is { } v) {
            return v;
        }
        return false;
    }

    private bool IsStaticallyInfinite(ForLoop loop, AnalysisContext context) {
        if (loop.Condition == null) {
            // for(;;) or for(;true;) style - if no cond, and no way to break, it's infinite (conservative, body may have breaks but we don't analyze that here)
            // To be more precise we could scan body for break but for static here treat no-cond as potentially inf
            return true;
        }
        var condVars = CollectVariables(context, loop.Condition);
        bool condPure = !context.HasSideEffects(loop.Condition);
        bool mutates = HasMutationToVars(context, loop.Body, condVars);
        if (loop.Increment != null) {
            mutates |= HasMutationToVars(context, loop.Increment, condVars);
        }
        if (condPure && !mutates && GetConstBool(context, loop.Condition) is { } v) {
            return v;
        }
        return false;
    }

    private HashSet<string> CollectVariables(AnalysisContext context, Node node) {
        // Use AggregateChildren for the children part of subtree var collection (SideEffect Aggregate pattern for fused walk + reduce; self handled locally for top node).
        var result = new HashSet<string>();
        if (node is Variable v) result.Add(v.Name);
        if (node is Parameter p) result.Add(p.Name); // parameters can be part of condition state or mutated from context
        var fromChildren = this.AggregateChildren(
            context,
            node,
            CollectVariables,
            (a, b) => { a.UnionWith(b); return a; },
            new HashSet<string>()
        );
        result.UnionWith(fromChildren);
        return result;
    }

    private bool HasMutationToVars(AnalysisContext context, Node node, HashSet<string> vars) {
        if (node is Assignment a && a.Destination is Variable v && vars.Contains(v.Name)) {
            return true;
        }
        if (node is Assignment a2 && a2.Destination is Member mm) {
            var res = context.GetResolvedMember(mm);
            if (res?.Mutability.HasFlag(Mutability.CompileTimeConst) == true) {
                return false; // const cannot be mutated at runtime; no state change impact
            }
            return true; // member assign (volatile or mutable) has potential impact
        }
        if (node is Assignment a3 && a3.Destination is IndexAccess) {
            return true;
        }
        if (node is Invoke inv && context.HasSideEffects(inv)) {
            return true; // non-pure call can mutate state (closures, ref params, globals, reference objects) including cond vars
        }
        if (node is SuspendNode) {
            return true; // suspend can interact with external state that affects cond vars
        }
        if (node is Member m) {
            if (context.GetResolvedMember(m)?.Mutability.HasFlag(Mutability.VolatileAccess) == true) {
                return true; // volatile access has un-knowable impact
            }
        }
        return this.AnyChild<ControlFlowMetadata>(context, node, (ctx, ch) =>
            (ch is Assignment aa && ((aa.Destination is Variable vv && vars.Contains(vv.Name)) || (aa.Destination is Member mma && !HasCompileTimeConst(ctx, mma)) || aa.Destination is IndexAccess))
            || (ch is Invoke invv && ctx.HasSideEffects(invv))
            || ch is SuspendNode
            || (ch is Member mm && HasVolatileAccess(ctx, mm)));
    }

    private static bool HasVolatileAccess(AnalysisContext context, Member m) {
        return context.GetResolvedMember(m)?.Mutability.HasFlag(Mutability.VolatileAccess) == true;
    }

    private static bool HasCompileTimeConst(AnalysisContext context, Member m) {
        return context.GetResolvedMember(m)?.Mutability.HasFlag(Mutability.CompileTimeConst) == true;
    }

    /// <summary>The node's constant value, or <c>null</c> when it is not known: a
    /// <see cref="Constant"/> literal or what constant folding published for the node.</summary>
    internal static object? GetConstValue(INodeMetadataProvider provider, Node? n) {
        if (n == null) return null;
        if (n is Constant c) return c.Value;
        return provider.GetMetadata<ConstantValueMetadata>(n)?.Value;
    }

    /// <summary>The node's constant boolean value, or <c>null</c> when it is not known.
    /// Unknown is never <c>false</c>: every consumer distinguishes the two.</summary>
    internal static bool? GetConstBool(INodeMetadataProvider provider, Node? n) =>
        GetConstValue(provider, n) as bool?;

    private static bool IsStaticallyConstantTrue(AnalysisContext ctx, Node? cond) =>
        !ctx.HasSideEffects(cond) && GetConstBool(ctx, cond) is true;

    private static bool IsStaticallyConstantFalse(AnalysisContext ctx, Node? cond) =>
        !ctx.HasSideEffects(cond) && GetConstBool(ctx, cond) is false;

    private static void MarkElidable(AnalysisContext ctx, Node node, string code, string message) {
        ElisionFacts.Mark(ctx, node, ElisionReason.Unreachable);
        ctx.ReportInformation(node, message, code);
    }

    private static void MarkSubtreeElidable(AnalysisContext ctx, Node node, string code, string message) {
        MarkElidable(ctx, node, code, message);
        foreach (var child in node.Children) {
            if (child != null) MarkSubtreeElidable(ctx, child, code, message);
        }
    }

    // ── Metadata-driven jump resolution ────────────────────────

    /// <summary>
    /// Resolves a break statement via <see cref="ResolvedJumpTarget"/> metadata.
    /// Returns true if the metadata was present and the target loop was found
    /// in <see cref="CfgState.LoopTargets"/>.
    /// </summary>
    private static bool TryResolveBreak(
        BreakStatement breakStmt,
        AnalysisContext context,
        CfgState state) {
        var target = context.GetMetadata<ResolvedJumpTarget>(breakStmt);
        if (target is null)
            return false;

        if (state.CurrentBlock is not null &&
            state.LoopTargets.TryGetValue(target.TargetNodeId, out var blocks)) {
            state.CurrentBlock.AddSuccessor(blocks.Break);
            return true;
        }

        // Metadata points to a loop node — if that loop isn't in the dictionary
        // something is wrong; fall through to fallback.
        return false;
    }

    /// <summary>
    /// Resolves a continue statement via <see cref="ResolvedJumpTarget"/> metadata.
    /// Returns true if the metadata was present and the target loop was found
    /// in <see cref="CfgState.LoopTargets"/>.
    /// </summary>
    private static bool TryResolveContinue(
        ContinueStatement continueStmt,
        AnalysisContext context,
        CfgState state) {
        var target = context.GetMetadata<ResolvedJumpTarget>(continueStmt);
        if (target is null)
            return false;

        if (state.CurrentBlock is not null &&
            state.LoopTargets.TryGetValue(target.TargetNodeId, out var blocks)) {
            state.CurrentBlock.AddSuccessor(blocks.Continue);
            return true;
        }

        return false;
    }
}

public static class ControlFlowAnalysisExtensions {
    extension(AnalyzerBuilder builder) {
        /// <summary>
        /// Adds control flow analysis to the analyzer.
        /// This builds a Control Flow Graph (CFG) and performs reachability analysis.
        /// </summary>
        public AnalyzerBuilder UseControlFlowAnalysis() {
            builder.AddAnalyzer(new ControlFlowAnalysisPass());
            return builder;
        }
    }

    extension(INodeMetadataProvider context) {
        /// <summary>
        /// Gets the control flow graph for the root node.
        /// </summary>
        public ControlFlowGraph? GetControlFlowGraph(Node rootNode) {
            var metadata = context.GetMetadata<ControlFlowMetadata>(rootNode);
            return metadata?.Graph;
        }
    }
}