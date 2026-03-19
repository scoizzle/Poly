namespace Poly.Interpretation.Analysis.ControlFlow;

/// <summary>
/// Metadata containing control flow analysis results for an AST.
/// </summary>
public sealed record ControlFlowMetadata(ControlFlowGraph Graph) : IAnalysisMetadata;

/// <summary>
/// Builds a control flow graph from an AST and performs reachability analysis.
/// </summary>
public sealed class ControlFlowAnalysisPass : INodeAnalyzer {
    private ControlFlowGraph? _cfg;
    private BasicBlock? _currentBlock;
    private readonly Dictionary<string, BasicBlock> _labeledBlocks = [];
    private readonly List<(GotoStatement Goto, string Label)> _pendingGotos = [];
    private readonly Stack<(BasicBlock Continue, BasicBlock Break)> _loopContexts = new();

    public void Analyze(AnalysisContext context, Node node) {
        _cfg = new ControlFlowGraph();
        _currentBlock = _cfg.CreateBlock();
        _labeledBlocks.Clear();
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

        // Report dead code diagnostics
        foreach (var deadNode in _cfg.DeadCode) {
            context.ReportDiagnostic(deadNode, DiagnosticSeverity.Warning,
                "Unreachable code detected", "CF0002");
        }

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

            case ReturnStatement returnStmt:
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
                AddStatement(gotoStmt);
                _currentBlock.SetTerminator(gotoStmt);
                _pendingGotos.Add((gotoStmt, gotoStmt.Target));
                _currentBlock = null;
                break;

            case LabelDeclaration labelDecl:
                // Start a new block for the label
                var labelBlock = _cfg.CreateBlock();
                if (_currentBlock != null) {
                    _currentBlock.AddSuccessor(labelBlock);
                }
                _currentBlock = labelBlock;
                _labeledBlocks[labelDecl.Name] = labelBlock;
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
        var thenBlock = _cfg.CreateBlock();
        var mergeBlock = _cfg.CreateBlock();

        // Then branch
        conditionBlock.AddSuccessor(thenBlock);
        _currentBlock = thenBlock;
        BuildCfg(context, ifStmt.ThenBranch);

        // Connect then to merge if not terminated
        var afterThen = _currentBlock;

        // Else branch
        if (ifStmt.ElseBranch != null) {
            var elseBlock = _cfg.CreateBlock();
            conditionBlock.AddSuccessor(elseBlock);
            _currentBlock = elseBlock;
            BuildCfg(context, ifStmt.ElseBranch);

            var afterElse = _currentBlock;

            // Connect else to merge if not terminated
            if (afterElse != null) {
                afterElse.AddSuccessor(mergeBlock);
            }
        }
        else {
            // No else branch - condition can fall through to merge
            conditionBlock.AddSuccessor(mergeBlock);
        }

        // Connect then to merge if not terminated
        if (afterThen != null) {
            afterThen.AddSuccessor(mergeBlock);
        }

        // Continue with merge block if any path reaches it
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
        conditionBlock.AddSuccessor(bodyBlock); // Condition true
        conditionBlock.AddSuccessor(exitBlock); // Condition false

        // Body
        _loopContexts.Push((Continue: conditionBlock, Break: exitBlock));
        _currentBlock = bodyBlock;
        BuildCfg(context, whileLoop.Body);

        // Loop back to condition
        if (_currentBlock != null) {
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
        conditionBlock.AddSuccessor(exitBlock); // Exit

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
        conditionBlock.AddSuccessor(bodyBlock);
        conditionBlock.AddSuccessor(exitBlock);

        // Body
        _loopContexts.Push((Continue: iteratorBlock, Break: exitBlock));
        _currentBlock = bodyBlock;
        BuildCfg(context, forLoop.Body);

        // Iterator
        if (_currentBlock != null) {
            _currentBlock.AddSuccessor(iteratorBlock);
        }
        _currentBlock = iteratorBlock;
        if (forLoop.Increment != null) {
            AddStatement(forLoop.Increment);
        }
        iteratorBlock.AddSuccessor(conditionBlock);

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

        // Catch blocks
        if (tryCatch.CatchClauses != null) {
            foreach (var catchClause in tryCatch.CatchClauses) {
                var catchEntry = _cfg.CreateBlock();
                tryBlock.AddSuccessor(catchEntry); // Exception can jump to catch
                _currentBlock = catchEntry;

                // Add the catch exception type info
                if (catchClause.ExceptionType != null) {
                    AddStatement(catchClause.ExceptionType);
                }

                BuildCfg(context, catchClause.Body);

                if (_currentBlock != null) {
                    _currentBlock.AddSuccessor(mergeBlock);
                }
            }
        }

        // Finally block
        if (tryCatch.FinallyBlock != null) {
            var finallyEntry = _cfg.CreateBlock();
            // Try and catches flow through finally
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

    private void BuildSwitchCfg(AnalysisContext context, SwitchStatement switchStmt) {
        if (_currentBlock == null || _cfg == null) return;

        // Switch value
        AddStatement(switchStmt.Value);
        var switchBlock = _currentBlock;
        var exitBlock = _cfg.CreateBlock();

        _loopContexts.Push((Continue: switchBlock, Break: exitBlock));

        foreach (var caseBlock in switchStmt.Cases) {
            var caseEntry = _cfg.CreateBlock();
            switchBlock.AddSuccessor(caseEntry);

            _currentBlock = caseEntry;

            // Case pattern
            AddStatement(caseBlock.Pattern);

            // Case body
            BuildCfg(context, caseBlock.Body);

            // If current block still exists, it can fall through to exit
            if (_currentBlock != null) {
                _currentBlock.AddSuccessor(exitBlock);
            }
        }

        // Default case or implicit fall to exit
        if (switchStmt.DefaultCase != null) {
            var defaultEntry = _cfg.CreateBlock();
            switchBlock.AddSuccessor(defaultEntry);
            _currentBlock = defaultEntry;
            BuildCfg(context, switchStmt.DefaultCase);

            if (_currentBlock != null) {
                _currentBlock.AddSuccessor(exitBlock);
            }
        }

        _loopContexts.Pop();
        _currentBlock = exitBlock;
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

    extension(AnalysisContext context) {
        /// <summary>
        /// Gets the control flow graph for the root node.
        /// </summary>
        public ControlFlowGraph? GetControlFlowGraph(Node rootNode) {
            var metadata = context.GetMetadata<ControlFlowMetadata>(rootNode);
            return metadata?.Graph;
        }
    }

    extension(AnalysisResult result) {
        /// <summary>
        /// Gets the control flow graph from the analysis result.
        /// </summary>
        public ControlFlowGraph? GetControlFlowGraph(Node rootNode) {
            var metadata = result.GetMetadata<ControlFlowMetadata>(rootNode);
            return metadata?.Graph;
        }
    }
}