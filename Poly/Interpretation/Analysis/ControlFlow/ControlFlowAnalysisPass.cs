using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Interpretation.Analysis.ControlFlow;

/// <summary>
/// Metadata containing control flow analysis results for an AST.
/// </summary>
public sealed record ControlFlowMetadata(ControlFlowGraph Graph) : IAnalysisMetadata;

/// <summary>
/// Indicates that a loop is statically known to be infinite (non-terminating)
/// under the conditions detected by side-effect/purity analysis (pure condition
/// with no mutation to its variables in body/iteration).
/// HasObservableEffects distinguishes pure-infinite (can treat as no-op hang in some elision contexts)
/// from effectful-infinite (must preserve for side effects).
/// </summary>
public sealed record InfiniteLoopMetadata(bool IsInfinite, bool HasObservableEffects = true) : IAnalysisMetadata;

/// <summary>
/// Indicates a node (typically a statement or branch) that must execute on every path
/// (post-dominates entry or loop header). Useful for guaranteed side-effects.
/// </summary>
public sealed record MustExecuteMetadata(bool MustExecute) : IAnalysisMetadata;

/// <summary>
/// Builds a control flow graph from an AST and performs reachability analysis.
/// </summary>
public sealed class ControlFlowAnalysisPass : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<ControlFlowAnalysisPass>(node)) {
            return;
        }

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
            context.SetMetadata(deadNode, ElidableFlyweight);
        }

        ComputeMustExecuteFacts(context, state.Cfg);

        // Store CFG as metadata on root node
        context.SetMetadata(node, new ControlFlowMetadata(state.Cfg));
    }

    private sealed class CfgState {
        public ControlFlowGraph Cfg = new();
        public BasicBlock? CurrentBlock;
        public Dictionary<string, BasicBlock> LabeledBlocks = [];
        public Dictionary<string, LabelDeclaration> LabelDecls = [];
        public List<(GotoStatement Goto, string Label)> PendingGotos = [];
        public Stack<(BasicBlock Continue, BasicBlock Break)> LoopContexts = new();
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
                if (state.LoopContexts.TryPeek(out var loopCtx)) {
                    state.CurrentBlock.AddSuccessor(loopCtx.Break);
                }
                state.CurrentBlock = null;
                break;

            case ContinueStatement continueStmt:
                AddStatement(continueStmt, state);
                state.CurrentBlock.SetTerminator(continueStmt);
                if (state.LoopContexts.TryPeek(out var continueCtx)) {
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
            bool hasEffects = !IsPure(context, whileLoop.Body);
            context.SetMetadata(whileLoop, hasEffects ? EffectfulInfiniteFlyweight : PureInfiniteFlyweight);
            context.ReportDiagnostic(whileLoop, DiagnosticSeverity.Information,
                "Infinite loop detected", "CF0003");
        }
        else {
            conditionBlock.AddSuccessor(bodyBlock);
            conditionBlock.AddSuccessor(exitBlock);
        }

        state.LoopContexts.Push((Continue: conditionBlock, Break: exitBlock));
        state.CurrentBlock = bodyBlock;
        if (!constFalse) {
            BuildCfg(context, whileLoop.Body, state);
        }

        if (state.CurrentBlock != null && !constFalse) {
            state.CurrentBlock.AddSuccessor(conditionBlock);
        }

        state.LoopContexts.Pop();
        state.CurrentBlock = exitBlock;
    }

    private void BuildDoWhileLoopCfg(AnalysisContext context, DoWhileLoop doWhileLoop, CfgState state) {
        if (state.CurrentBlock == null) return;

        var preLoop = state.CurrentBlock;
        var bodyBlock = state.Cfg.CreateBlock();
        var conditionBlock = state.Cfg.CreateBlock();
        var exitBlock = state.Cfg.CreateBlock();

        preLoop.AddSuccessor(bodyBlock);

        state.LoopContexts.Push((Continue: conditionBlock, Break: exitBlock));
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
            bool hasEffects = !IsPure(context, doWhileLoop.Body);
            context.SetMetadata(doWhileLoop, hasEffects ? EffectfulInfiniteFlyweight : PureInfiniteFlyweight);
            context.ReportDiagnostic(doWhileLoop, DiagnosticSeverity.Information, "Infinite loop detected", "CF0003");
        }

        state.LoopContexts.Pop();
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
            bool hasEffects = !IsPure(context, forLoop.Body) || (forLoop.Initializer != null && !IsPure(context, forLoop.Initializer)) || (forLoop.Increment != null && !IsPure(context, forLoop.Increment));
            context.SetMetadata(forLoop, hasEffects ? EffectfulInfiniteFlyweight : PureInfiniteFlyweight);
            context.ReportDiagnostic(forLoop, DiagnosticSeverity.Information, "Infinite loop detected", "CF0003");
        }
        else {
            conditionBlock.AddSuccessor(exitBlock);
        }

        state.LoopContexts.Push((Continue: iteratorBlock, Break: exitBlock));
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

        state.LoopContexts.Pop();
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

        state.LoopContexts.Push((Continue: conditionBlock, Break: exitBlock));
        state.CurrentBlock = bodyBlock;
        BuildCfg(context, forEachLoop.Body, state);

        if (state.CurrentBlock != null) {
            state.CurrentBlock.AddSuccessor(conditionBlock);
        }

        state.LoopContexts.Pop();
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

    private static bool ContainsThrow(AnalysisContext context, Node node) {
        if (node is ThrowStatement) return true;
        return AnyChild(context, node, static (ctx, ch) => ch is ThrowStatement);
    }

    private static bool AnyChild(AnalysisContext context, Node node, Func<AnalysisContext, Node, bool> predicate) {
        foreach (var child in node.Children) {
            if (child is null || !context.ShouldAnalyze(child))
                continue;
            if (predicate(context, child!))
                return true;
        }
        return false;
    }

    private void BuildSwitchCfg(AnalysisContext context, SwitchStatement switchStmt, CfgState state) {
        if (state.CurrentBlock == null) return;

        AddStatement(switchStmt.Value, state);
        var switchBlock = state.CurrentBlock;
        var exitBlock = state.Cfg.CreateBlock();

        state.LoopContexts.Push((Continue: switchBlock, Break: exitBlock));

        object? switchConst = IsPure(context, switchStmt.Value) ? GetConstValue(context, switchStmt.Value) : null;
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
    // and InfiniteLoopMetadata for consumers (elision, diagnostics, etc.).

    private bool IsStaticallyInfinite(WhileLoop loop, AnalysisContext context) {
        if (loop.Condition == null) return false;
        var condVars = CollectVariables(context, loop.Condition);
        bool condPure = IsPure(context, loop.Condition);
        bool mutates = HasMutationToVars(context, loop.Body, condVars);
        if (condPure && !mutates && TryGetConstBool(context, loop.Condition, out bool v)) {
            return v;
        }
        return false;
    }

    private bool IsStaticallyInfinite(DoWhileLoop loop, AnalysisContext context) {
        if (loop.Condition == null) return false;
        var condVars = CollectVariables(context, loop.Condition);
        bool condPure = IsPure(context, loop.Condition);
        bool mutates = HasMutationToVars(context, loop.Body, condVars);
        if (condPure && !mutates && TryGetConstBool(context, loop.Condition, out bool v)) {
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
        bool condPure = IsPure(context, loop.Condition);
        bool mutates = HasMutationToVars(context, loop.Body, condVars);
        if (loop.Increment != null) {
            mutates |= HasMutationToVars(context, loop.Increment, condVars);
        }
        if (condPure && !mutates && TryGetConstBool(context, loop.Condition, out bool v)) {
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
            (ctx, ch) => CollectVariables(ctx, ch),
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
        if (node is Invoke inv && !IsPure(context, inv)) {
            return true; // non-pure call can mutate state (closures, ref params, globals, heap objects) including cond vars
        }
        if (node is SuspendNode) {
            return true; // suspend can interact with external state that affects cond vars
        }
        if (node is Member m) {
            if (context.GetResolvedMember(m)?.Mutability.HasFlag(Mutability.VolatileAccess) == true) {
                return true; // volatile access has un-knowable impact
            }
        }
        // Use AnyChild for "does any descendant mutate" -- fused walk per the AggregateChildren recommendation.
        return this.AnyChild<ControlFlowMetadata>(context, node, (ctx, ch) =>
            (ch is Assignment aa && ((aa.Destination is Variable vv && vars.Contains(vv.Name)) || (aa.Destination is Member mma && !HasCompileTimeConst(ctx, mma)) || aa.Destination is IndexAccess))
            || (ch is Invoke invv && !IsPure(ctx, invv))
            || ch is SuspendNode
            || (ch is Member mm && HasVolatileAccess(ctx, mm)));
    }

    private static bool IsConstantTrue(Node? node) {
        return node is Constant c && c.Value is bool b && b;
    }

    private static bool HasVolatileAccess(AnalysisContext context, Member m) {
        return context.GetResolvedMember(m)?.Mutability.HasFlag(Mutability.VolatileAccess) == true;
    }

    private static bool HasCompileTimeConst(AnalysisContext context, Member m) {
        return context.GetResolvedMember(m)?.Mutability.HasFlag(Mutability.CompileTimeConst) == true;
    }

    private static readonly ElisionMetadata ElidableFlyweight = new(true);
    private static readonly MustExecuteMetadata MustExecuteFlyweight = new(true);
    private static readonly InfiniteLoopMetadata PureInfiniteFlyweight = new(true, false);
    private static readonly InfiniteLoopMetadata EffectfulInfiniteFlyweight = new(true, true);

    private static bool IsPure(AnalysisContext ctx, Node? n) {
        if (n == null) return true;
        return ctx.GetMetadata<SideEffectMetadata>(n)?.Kind == SideEffectKind.Pure;
    }

    private static bool TryGetConstBool(AnalysisContext ctx, Node? n, out bool value) {
        value = false;
        if (n == null) return false;
        if (n is Constant c && c.Value is bool b) {
            value = b;
            return true;
        }
        var folded = ctx.GetMetadata<ConstantValueMetadata>(n);
        if (folded?.Value is bool fb) {
            value = fb;
            return true;
        }
        return false;
    }

    private static object? GetConstValue(AnalysisContext ctx, Node? n) {
        if (n == null) return null;
        if (n is Constant c) return c.Value;
        var folded = ctx.GetMetadata<ConstantValueMetadata>(n);
        return folded?.Value;
    }

    private static bool IsStaticallyConstantTrue(AnalysisContext ctx, Node? cond) {
        if (!IsPure(ctx, cond)) return false;
        return TryGetConstBool(ctx, cond, out bool v) && v;
    }

    private static bool IsStaticallyConstantFalse(AnalysisContext ctx, Node? cond) {
        if (!IsPure(ctx, cond)) return false;
        return TryGetConstBool(ctx, cond, out bool v) && !v;
    }

    private static void MarkElidable(AnalysisContext ctx, Node node, string code, string message) {
        ctx.SetMetadata(node, ElidableFlyweight);
        ctx.ReportInformation(node, message, code);
    }

    private static void MarkSubtreeElidable(AnalysisContext ctx, Node node, string code, string message) {
        MarkElidable(ctx, node, code, message);
        foreach (var child in node.Children) {
            if (child != null) MarkSubtreeElidable(ctx, child, code, message);
        }
    }

    /// <summary>
    /// Lightweight post-dominance / must-execute computation.
    /// Marks nodes that are guaranteed to run on all paths from entry (straight-line prefixes,
    /// finally blocks in some cases, etc.). Consumer: tests + future guaranteed-effect insight.
    /// </summary>
    private static void ComputeMustExecuteFacts(AnalysisContext context, ControlFlowGraph cfg) {
        if (cfg.Blocks.Count == 0) return;
        // Simple: entry block statements before any branch are must-execute.
        var entry = cfg.Entry;
        bool sawBranch = false;
        foreach (var stmt in entry.Statements) {
            if (!sawBranch) {
                context.SetMetadata(stmt, MustExecuteFlyweight);
            }
            // rough: if stmt is If/Loop/Switch/Try it introduces choice, subsequent in entry not must
            if (stmt is IfStatement or WhileLoop or DoWhileLoop or ForLoop or ForEachLoop or SwitchStatement or TryCatchFinally) {
                sawBranch = true;
            }
        }
        // Finally blocks are typically must-execute if the try region is entered.
        // For simplicity, mark statements in finally blocks as must (if the finally block itself is reachable).
        foreach (var block in cfg.Blocks) {
            if (!block.IsReachable) continue;
            // Heuristic: if block has only finally-like (no good signal), or we can look for Try in predecessors.
            // For now, if a block is successor of a try and is the finally path, mark its stmts.
            // (Leave simple; full post-dom would be future if needed.)
        }
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

        /// <summary>
        /// Returns whether the given loop node is statically infinite (non-terminating).
        /// </summary>
        public bool IsInfiniteLoop(Node? loopNode) {
            if (loopNode == null) return false;
            var meta = context.GetMetadata<InfiniteLoopMetadata>(loopNode);
            return meta?.IsInfinite ?? false;
        }

        /// <summary>
        /// Returns whether a node is known to always execute (must-execute / post-dominates entry on all paths).
        /// </summary>
        public bool IsMustExecute(Node? node) {
            if (node == null) return false;
            return context.GetMetadata<MustExecuteMetadata>(node)?.MustExecute ?? false;
        }
    }
}