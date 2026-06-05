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
    private ControlFlowGraph? _cfg;
    private BasicBlock? _currentBlock;
    private readonly Dictionary<string, BasicBlock> _labeledBlocks = [];
    private readonly Dictionary<string, LabelDeclaration> _labelDecls = [];
    private readonly List<(GotoStatement Goto, string Label)> _pendingGotos = [];
    private readonly Stack<(BasicBlock Continue, BasicBlock Break)> _loopContexts = new();

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<ControlFlowAnalysisPass>(node)) {
            return;
        }

        _cfg = new ControlFlowGraph();
        _currentBlock = _cfg.CreateBlock();
        _labeledBlocks.Clear();
        _labelDecls.Clear();
        _pendingGotos.Clear();
        _loopContexts.Clear();

        BuildCfg(context, node);

        // Resolve pending gotos
        foreach (var (gotoStmt, label) in _pendingGotos) {
            if (_labeledBlocks.TryGetValue(label, out var targetBlock)) {
                var sourceBlock = _cfg.GetBlockForNode(gotoStmt);
                sourceBlock?.AddSuccessor(targetBlock);
            }
            else {
                context.ReportDiagnostic(gotoStmt, DiagnosticSeverity.Error,
                    $"Goto target label '{label}' not found", "CF0001");
            }
        }

        // Finalize CFG
        _cfg.IdentifyExitBlocks();
        _cfg.ComputeReachability();

        // Dead label detection (labels only reachable via dead gotos or untar geted)
        foreach (var (name, labelBlock) in _labeledBlocks) {
            if (!labelBlock.IsReachable && _labelDecls.TryGetValue(name, out var decl)) {
                context.ReportDiagnostic(decl, DiagnosticSeverity.Warning, $"Unreachable label '{name}'", "CF0013");
                MarkElidable(context, decl, "CF0013", $"Label declaration '{name}' is unreachable from live code");
            }
        }

        // Report dead code diagnostics
        foreach (var deadNode in _cfg.DeadCode) {
            context.ReportDiagnostic(deadNode, DiagnosticSeverity.Warning,
                "Unreachable code detected", "CF0002");
        }

        // Tag dead/unreachable nodes with Elidable so elision consumers (interpreter block
        // skipping, generators) can uniformly skip them. This covers code after infinite loops
        // etc.
        foreach (var deadNode in _cfg.DeadCode) {
            context.SetMetadata(deadNode, ElidableFlyweight);
        }

        ComputeMustExecuteFacts(context, _cfg);

        // Store CFG as metadata on root node
        context.SetMetadata(node, new ControlFlowMetadata(_cfg));
    }

    private void BuildCfg(AnalysisContext context, Node node) {
        if (_currentBlock == null || _cfg == null) return;

        switch (node) {
            case Block block:
                BuildBlockCfg(context, block);
                break;

            case IfStatement ifStmt:
                BuildIfCfg(context, ifStmt);
                break;

            case WhileLoop whileLoop:
                BuildWhileLoopCfg(context, whileLoop);
                break;

            case DoWhileLoop doWhileLoop:
                BuildDoWhileLoopCfg(context, doWhileLoop);
                break;

            case ForLoop forLoop:
                BuildForLoopCfg(context, forLoop);
                break;

            case ForEachLoop forEachLoop:
                BuildForEachLoopCfg(context, forEachLoop);
                break;

            case Return returnStmt:
                AddStatement(returnStmt);
                _currentBlock.SetTerminator(returnStmt);
                // Return terminates the current block with no successors
                _currentBlock = null;
                break;

            case ThrowStatement throwStmt:
                AddStatement(throwStmt);
                _currentBlock.SetTerminator(throwStmt);
                // Throw terminates the current block
                _currentBlock = null;
                break;

            case BreakStatement breakStmt:
                AddStatement(breakStmt);
                _currentBlock.SetTerminator(breakStmt);
                if (_loopContexts.TryPeek(out var loopCtx)) {
                    _currentBlock.AddSuccessor(loopCtx.Break);
                }
                _currentBlock = null;
                break;

            case ContinueStatement continueStmt:
                AddStatement(continueStmt);
                _currentBlock.SetTerminator(continueStmt);
                if (_loopContexts.TryPeek(out var continueCtx)) {
                    _currentBlock.AddSuccessor(continueCtx.Continue);
                }
                _currentBlock = null;
                break;

            case GotoStatement gotoStmt:
                if (_currentBlock != null) {
                    AddStatement(gotoStmt);
                    _currentBlock.SetTerminator(gotoStmt);
                    _pendingGotos.Add((gotoStmt, gotoStmt.Target));
                    _currentBlock = null;
                }
                // if current was null, this goto is in dead code; ignore for resolution
                break;

            case LabelDeclaration labelDecl:
                // Start a new block for the label
                var labelBlock = _cfg.CreateBlock();
                if (_currentBlock != null) {
                    _currentBlock.AddSuccessor(labelBlock);
                }
                _currentBlock = labelBlock;
                _labeledBlocks[labelDecl.Name] = labelBlock;
                _labelDecls[labelDecl.Name] = labelDecl;
                AddStatement(labelDecl);
                break;

            case TryCatchFinally tryCatch:
                BuildTryCatchCfg(context, tryCatch);
                break;

            case SwitchStatement switchStmt:
                BuildSwitchCfg(context, switchStmt);
                break;

            default:
                // Regular statement - add to current block
                AddStatement(node);
                break;
        }
    }

    private void AddStatement(Node node) {
        if (_currentBlock == null || _cfg == null) return;
        _currentBlock.AddStatement(node);
        _cfg.MapNodeToBlock(node, _currentBlock);
    }

    private void BuildBlockCfg(AnalysisContext context, Block block) {
        foreach (var stmt in block.Nodes) {
            if (_currentBlock == null) {
                // Code after terminator - create new unreachable block
                _currentBlock = _cfg!.CreateBlock();
            }
            BuildCfg(context, stmt);
        }
    }

    private void BuildIfCfg(AnalysisContext context, IfStatement ifStmt) {
        if (_currentBlock == null || _cfg == null) return;

        // Add condition to current block
        AddStatement(ifStmt.Condition);

        var conditionBlock = _currentBlock;
        var mergeBlock = _cfg.CreateBlock();

        bool constTrue = IsStaticallyConstantTrue(context, ifStmt.Condition);
        bool constFalse = IsStaticallyConstantFalse(context, ifStmt.Condition);

        if (constTrue) {
            // Prune else (if any); only then is possible
            var thenBlock = _cfg.CreateBlock();
            conditionBlock.AddSuccessor(thenBlock);
            _currentBlock = thenBlock;
            BuildCfg(context, ifStmt.ThenBranch);
            var afterThen = _currentBlock;
            if (afterThen != null) {
                afterThen.AddSuccessor(mergeBlock);
            }
            if (ifStmt.ElseBranch != null) {
                MarkSubtreeElidable(context, ifStmt.ElseBranch, "CF0004", "Else branch is unreachable because if condition is constantly true");
            }
            // do not wire condition->else
        }
        else if (constFalse) {
            // Prune then; only else (or fall) is possible
            if (ifStmt.ElseBranch != null) {
                var elseBlock = _cfg.CreateBlock();
                conditionBlock.AddSuccessor(elseBlock);
                _currentBlock = elseBlock;
                BuildCfg(context, ifStmt.ElseBranch);
                var afterElse = _currentBlock;
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
            // General case - both (or fall) possible
            var thenBlock = _cfg.CreateBlock();
            conditionBlock.AddSuccessor(thenBlock);
            _currentBlock = thenBlock;
            BuildCfg(context, ifStmt.ThenBranch);
            var afterThen = _currentBlock;

            if (ifStmt.ElseBranch != null) {
                var elseBlock = _cfg.CreateBlock();
                conditionBlock.AddSuccessor(elseBlock);
                _currentBlock = elseBlock;
                BuildCfg(context, ifStmt.ElseBranch);
                var afterElse = _currentBlock;
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

        if (mergeBlock.Predecessors.Count > 0) {
            _currentBlock = mergeBlock;
        }
        else {
            _currentBlock = null;
        }
    }

    private void BuildWhileLoopCfg(AnalysisContext context, WhileLoop whileLoop) {
        if (_currentBlock == null || _cfg == null) return;

        var preLoop = _currentBlock;
        var conditionBlock = _cfg.CreateBlock();
        var bodyBlock = _cfg.CreateBlock();
        var exitBlock = _cfg.CreateBlock();

        preLoop.AddSuccessor(conditionBlock);

        // Condition
        _currentBlock = conditionBlock;
        AddStatement(whileLoop.Condition);

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

        // Body
        _loopContexts.Push((Continue: conditionBlock, Break: exitBlock));
        _currentBlock = bodyBlock;
        if (!constFalse) {
            BuildCfg(context, whileLoop.Body);
        }

        // Loop back to condition (only if body could have executed)
        if (_currentBlock != null && !constFalse) {
            _currentBlock.AddSuccessor(conditionBlock);
        }

        _loopContexts.Pop();
        _currentBlock = exitBlock;
    }

    private void BuildDoWhileLoopCfg(AnalysisContext context, DoWhileLoop doWhileLoop) {
        if (_currentBlock == null || _cfg == null) return;

        var preLoop = _currentBlock;
        var bodyBlock = _cfg.CreateBlock();
        var conditionBlock = _cfg.CreateBlock();
        var exitBlock = _cfg.CreateBlock();

        preLoop.AddSuccessor(bodyBlock);

        // Body first
        _loopContexts.Push((Continue: conditionBlock, Break: exitBlock));
        _currentBlock = bodyBlock;
        BuildCfg(context, doWhileLoop.Body);

        // Condition
        if (_currentBlock != null) {
            _currentBlock.AddSuccessor(conditionBlock);
        }
        _currentBlock = conditionBlock;
        AddStatement(doWhileLoop.Condition);
        conditionBlock.AddSuccessor(bodyBlock); // Loop back
        if (!IsStaticallyInfinite(doWhileLoop, context)) {
            conditionBlock.AddSuccessor(exitBlock); // Exit
        }
        if (IsStaticallyInfinite(doWhileLoop, context)) {
            bool hasEffects = !IsPure(context, doWhileLoop.Body);
            context.SetMetadata(doWhileLoop, hasEffects ? EffectfulInfiniteFlyweight : PureInfiniteFlyweight);
            context.ReportDiagnostic(doWhileLoop, DiagnosticSeverity.Information, "Infinite loop detected", "CF0003");
        }

        _loopContexts.Pop();
        _currentBlock = exitBlock;
    }

    private void BuildForLoopCfg(AnalysisContext context, ForLoop forLoop) {
        if (_currentBlock == null || _cfg == null) return;

        // Initializer
        if (forLoop.Initializer != null) {
            AddStatement(forLoop.Initializer);
        }

        var preLoop = _currentBlock;
        var conditionBlock = _cfg.CreateBlock();
        var bodyBlock = _cfg.CreateBlock();
        var iteratorBlock = _cfg.CreateBlock();
        var exitBlock = _cfg.CreateBlock();

        preLoop.AddSuccessor(conditionBlock);

        // Condition
        _currentBlock = conditionBlock;
        if (forLoop.Condition != null) {
            AddStatement(forLoop.Condition);
        }

        bool isInfinite = IsStaticallyInfinite(forLoop, context);
        bool constFalse = forLoop.Condition != null && IsStaticallyConstantFalse(context, forLoop.Condition);
        conditionBlock.AddSuccessor(bodyBlock);
        if (constFalse) {
            conditionBlock.AddSuccessor(exitBlock);
            MarkSubtreeElidable(context, forLoop.Body, "CF0006", "For body is unreachable because condition is constantly false");
        }
        else if (isInfinite) {
            // no exit
            bool hasEffects = !IsPure(context, forLoop.Body) || (forLoop.Initializer != null && !IsPure(context, forLoop.Initializer)) || (forLoop.Increment != null && !IsPure(context, forLoop.Increment));
            context.SetMetadata(forLoop, hasEffects ? EffectfulInfiniteFlyweight : PureInfiniteFlyweight);
            context.ReportDiagnostic(forLoop, DiagnosticSeverity.Information, "Infinite loop detected", "CF0003");
        }
        else {
            conditionBlock.AddSuccessor(exitBlock);
        }

        // Body
        _loopContexts.Push((Continue: iteratorBlock, Break: exitBlock));
        _currentBlock = bodyBlock;
        if (!constFalse) {
            BuildCfg(context, forLoop.Body);
        }

        // Iterator (if not const false path)
        if (_currentBlock != null && !constFalse) {
            _currentBlock.AddSuccessor(iteratorBlock);
        }
        if (!constFalse) {
            _currentBlock = iteratorBlock;
            if (forLoop.Increment != null) {
                AddStatement(forLoop.Increment);
            }
            iteratorBlock.AddSuccessor(conditionBlock);
        }

        _loopContexts.Pop();
        _currentBlock = exitBlock;
    }

    private void BuildForEachLoopCfg(AnalysisContext context, ForEachLoop forEachLoop) {
        if (_currentBlock == null || _cfg == null) return;

        var preLoop = _currentBlock;
        var conditionBlock = _cfg.CreateBlock();
        var bodyBlock = _cfg.CreateBlock();
        var exitBlock = _cfg.CreateBlock();

        preLoop.AddSuccessor(conditionBlock);

        // Collection expression is evaluated before each iteration check.
        _currentBlock = conditionBlock;
        AddStatement(forEachLoop.Collection);
        conditionBlock.AddSuccessor(bodyBlock);
        conditionBlock.AddSuccessor(exitBlock);

        _loopContexts.Push((Continue: conditionBlock, Break: exitBlock));
        _currentBlock = bodyBlock;
        BuildCfg(context, forEachLoop.Body);

        if (_currentBlock != null) {
            _currentBlock.AddSuccessor(conditionBlock);
        }

        _loopContexts.Pop();
        _currentBlock = exitBlock;
    }

    private void BuildTryCatchCfg(AnalysisContext context, TryCatchFinally tryCatch) {
        if (_currentBlock == null || _cfg == null) return;

        var preTry = _currentBlock;
        var tryBlock = _cfg.CreateBlock();
        var mergeBlock = _cfg.CreateBlock();

        preTry.AddSuccessor(tryBlock);

        // Try block
        _currentBlock = tryBlock;
        BuildCfg(context, tryCatch.TryBlock);
        var afterTry = _currentBlock;

        bool mayThrow = ContainsThrow(context, tryCatch.TryBlock);

        // Catch blocks
        if (tryCatch.CatchClauses != null) {
            foreach (var catchClause in tryCatch.CatchClauses) {
                if (!mayThrow) {
                    // No throw in try subtree => this catch is unreachable from try (model assumption)
                    MarkSubtreeElidable(context, catchClause.Body, "CF0010", "Catch clause is unreachable - no throw statement in try block");
                    continue;
                }
                var catchEntry = _cfg.CreateBlock();
                tryBlock.AddSuccessor(catchEntry); // Exception can jump to catch
                _currentBlock = catchEntry;

                if (catchClause.ExceptionType != null) {
                    AddStatement(catchClause.ExceptionType);
                }

                BuildCfg(context, catchClause.Body);

                if (_currentBlock != null) {
                    _currentBlock.AddSuccessor(mergeBlock);
                }
            }
        }

        // Finally block (usually executes on way out or exception)
        if (tryCatch.FinallyBlock != null) {
            var finallyEntry = _cfg.CreateBlock();
            if (afterTry != null) {
                afterTry.AddSuccessor(finallyEntry);
            }
            _currentBlock = finallyEntry;
            BuildCfg(context, tryCatch.FinallyBlock);

            if (_currentBlock != null) {
                _currentBlock.AddSuccessor(mergeBlock);
            }
        }
        else if (afterTry != null) {
            afterTry.AddSuccessor(mergeBlock);
        }

        _currentBlock = mergeBlock.Predecessors.Count > 0 ? mergeBlock : null;
    }

    private bool ContainsThrow(AnalysisContext context, Node node) {
        if (node is ThrowStatement) return true;
        // Use AnyChild (framework walk) for subtree check -- applies AggregateChildren pattern lesson for consistency and potential future fusion with ShouldAnalyze/visit tracking.
        return this.AnyChild<ControlFlowMetadata>(context, node, (ctx, ch) => ch is ThrowStatement);
    }

    private void BuildSwitchCfg(AnalysisContext context, SwitchStatement switchStmt) {
        if (_currentBlock == null || _cfg == null) return;

        // Switch value
        AddStatement(switchStmt.Value);
        var switchBlock = _currentBlock;
        var exitBlock = _cfg.CreateBlock();

        _loopContexts.Push((Continue: switchBlock, Break: exitBlock));

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

            var caseEntry = _cfg.CreateBlock();
            switchBlock.AddSuccessor(caseEntry);
            _currentBlock = caseEntry;

            AddStatement(caseBlock.Pattern);
            BuildCfg(context, caseBlock.Body);

            if (_currentBlock != null) {
                _currentBlock.AddSuccessor(exitBlock);
            }
        }

        // Default
        if (switchStmt.DefaultCase != null) {
            bool defaultLive = true;
            if (hasConstValue && sawExactMatch) {
                // Conservative: if we saw exact literal case for the const, default is dead (no fallthrough assumed)
                defaultLive = false;
            }
            if (hasConstValue && !defaultLive) {
                MarkSubtreeElidable(context, switchStmt.DefaultCase, "CF0012", "Default case is unreachable - switch value covered by prior cases");
            }
            else {
                var defaultEntry = _cfg.CreateBlock();
                switchBlock.AddSuccessor(defaultEntry);
                _currentBlock = defaultEntry;
                BuildCfg(context, switchStmt.DefaultCase);
                if (_currentBlock != null) {
                    _currentBlock.AddSuccessor(exitBlock);
                }
            }
        }

        _loopContexts.Pop();
        _currentBlock = exitBlock;
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
        return !(ctx.GetMetadata<SideEffectMetadata>(n)?.HasSideEffects ?? true);
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