using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.TreeWalking;


public sealed class TreeWalkingInterpreter(AnalysisResult? analysisResult = null, InterpreterOptions? options = null) : IDisposable {
    private readonly AnalysisResult? _configuredAnalysisResult = analysisResult;
    private readonly InterpreterOptions _options = options ?? InterpreterOptions.Default;
    private readonly InterpretationAnalysisSettings _analysisSettings = InterpretationAnalysisSettings.Default;
    private readonly List<ITreeWalkerCompiler> _compilers = new();
    private readonly List<INodeAnalyzer> _insightAnalyzers = new();
    private readonly List<ILiveStateAnalyzer> _liveStateAnalyzers = new();
    private readonly HashSet<NodeId> _breakpoints = new();
    private InterpreterState? _currentState;
    private AnalysisResult? _lastComputedAnalysisResult;
    private Node? _lastAnalyzedRoot;
    private bool _disposed;

    public AnalysisResult? LastInsightResult { get; private set; }
    public AnalysisResult? LastPreExecutionAnalysisResult { get; private set; }

    public TreeWalkingInterpreter(
        AnalysisResult? analysisResult,
        InterpreterOptions? options,
        InterpretationAnalysisSettings? analysisSettings)
        : this(analysisResult, options) {
        _analysisSettings = analysisSettings ?? InterpretationAnalysisSettings.Default;
    }

    public TreeWalkingInterpreter RegisterCompiler(ITreeWalkerCompiler compiler) {
        _compilers.Add(compiler);
        return this;
    }

    public TreeWalkingInterpreter RegisterInsightAnalyzer(INodeAnalyzer analyzer) {
        _insightAnalyzers.Add(analyzer);
        return this;
    }

    public TreeWalkingInterpreter RegisterLiveStateAnalyzer(ILiveStateAnalyzer analyzer) {
        _liveStateAnalyzers.Add(analyzer);
        return this;
    }

    public TreeWalkingInterpreter BreakOn(Node node) {
        ArgumentNullException.ThrowIfNull(node);
        return BreakOn(node.Id);
    }

    public TreeWalkingInterpreter BreakOn(NodeId nodeId) {
        _breakpoints.Add(nodeId);
        return this;
    }

    public TreeWalkingInterpreter ClearBreakpoint(Node node) {
        ArgumentNullException.ThrowIfNull(node);
        return ClearBreakpoint(node.Id);
    }

    public TreeWalkingInterpreter ClearBreakpoint(NodeId nodeId) {
        _breakpoints.Remove(nodeId);
        return this;
    }

    public TreeWalkingInterpreter ClearBreakpoints() {
        _breakpoints.Clear();
        return this;
    }

    public InterpreterResult Evaluate(Node node) {
        return Evaluate(node, new Dictionary<string, object?>());
    }

    public InterpreterResult Evaluate(Node node, IReadOnlyDictionary<string, object?>? initialVariables = default) {
        ObjectDisposedException.ThrowIf(_disposed, nameof(TreeWalkingInterpreter));
        ArgumentNullException.ThrowIfNull(node);

        if (_currentState is not null) {
            throw new InvalidOperationException("TreeWalkingInterpreter already has an active evaluation");
        }

        var analysis = _configuredAnalysisResult ?? AnalyzeForEvaluation(node, initialVariables, _analysisSettings);
        LastPreExecutionAnalysisResult = analysis;
        EnsureAnalysisCanDriveExecution(analysis, _analysisSettings);

        if (_configuredAnalysisResult is null) {
            _lastComputedAnalysisResult = analysis;
            _lastAnalyzedRoot = node;
        }

        _currentState = new InterpreterState {
            AnalysisResult = analysis
        };
        var state = _currentState;
        state.CallStack.Push(new StackFrame(node, initialVariables));

        if (initialVariables is not null) {
            foreach (var kv in initialVariables) {
                state.Variables[kv.Key] = kv.Value;
            }
        }

        return ContinueEvaluation();
    }

    public InterpreterResult Resume() {
        return Resume(null);
    }

    public InterpreterResult Resume(AnalysisResult? analysisResult) {
        if (_currentState is null)
            throw new InvalidOperationException("No suspended state to resume");
        if (_currentState.Status != InterpreterStatus.Suspended)
            throw new InvalidOperationException("Current state is not suspended");

        if (analysisResult is not null) {
            EnsureAnalysisCanDriveExecution(analysisResult, _analysisSettings);
            LastPreExecutionAnalysisResult = analysisResult;
            _currentState.AnalysisResult = analysisResult;
        }

        _currentState.Resume();
        return ContinueEvaluation();
    }

    private InterpreterResult ContinueEvaluation() {
        var state = _currentState!;
        try {
            while (!state.IsComplete && !state.IsSuspended) {
                var result = ExecuteCurrentNode(state);

                if (result.IsSignal) {
                    HandleSignal(result.Signal!.Value, state);
                }
                else if (result.HasValue) {
                    if (state.CallStack.Count == 1 && !state.IsSuspended) {
                        state.Complete(result);
                    }
                }
                else {
                    AdvanceToNextNode(state);
                }
            }

            if (state.IsSuspended) {
                var suspended = state.Suspend(
                    state.SuspensionReason ?? "Suspended by request",
                    state.SuspendedAtNode);

                LastInsightResult = RunInsightAnalysisOnSuspendedState(suspended);

                return InterpreterResult.FromValue(suspended);
            }

            return state.LastResult ?? InterpreterResult.Void;
        }
        finally {
            if (!state.IsSuspended) {
                state.Dispose();
                _currentState = null;
            }
        }
    }

    private AnalysisResult AnalyzeForEvaluation(
        Node node,
        IReadOnlyDictionary<string, object?>? initialVariables,
        InterpretationAnalysisSettings settings) {
        var analysisSettings = AnalysisSettings.Default
            .With(settings)
            .With(settings.DiagnosticConfiguration)
            .With(settings.SideEffectOptions);

        var analyzer = new AnalyzerBuilder()
            .WithOptions(settings.AnalysisOptions)
            .UseIncrementalAnalysis()
            .UseTypeResolver()
            .UseMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseControlFlowAnalysis()
            .UseConstantFolding()
            .Build()
            .With(context => BindIncomingParameterTypes(context, node, initialVariables));

        if (_lastComputedAnalysisResult is not null && ReferenceEquals(_lastAnalyzedRoot, node)) {
            return analyzer.Analyze(node, _lastComputedAnalysisResult, [node], analysisSettings);
        }

        return analyzer.Analyze(node, analysisSettings);
    }

    private static void BindIncomingParameterTypes(
        AnalysisContext context,
        Node root,
        IReadOnlyDictionary<string, object?>? initialVariables) {
        if (initialVariables is null || initialVariables.Count == 0) {
            return;
        }

        var visited = new HashSet<NodeId>();
        var stack = new Stack<Node>();
        stack.Push(root);

        while (stack.Count > 0) {
            var current = stack.Pop();
            if (!visited.Add(current.Id)) {
                continue;
            }

            if (current is Parameter parameter
                && initialVariables.TryGetValue(parameter.Name, out var value)
                && value is not null) {
                var resolvedType = context.TypeDefinitions.GetTypeDefinition(value.GetType());
                if (resolvedType is not null) {
                    context.SetResolvedType(parameter, resolvedType);
                }
            }

            foreach (var child in current.Children) {
                if (child is not null) {
                    stack.Push(child);
                }
            }
        }
    }

    private static void EnsureAnalysisCanDriveExecution(AnalysisResult analysis, InterpretationAnalysisSettings settings) {
        var analysisDiagnosticConfiguration = analysis.GetSetting<AnalysisDiagnosticConfiguration>()
            ?? AnalysisDiagnosticConfiguration.Default;

        var effectiveDiagnosticConfiguration = analysisDiagnosticConfiguration with {
            TreatWarningsAsErrors =
                analysisDiagnosticConfiguration.TreatWarningsAsErrors ||
                settings.DiagnosticConfiguration.TreatWarningsAsErrors
        };

        var hasWarnings = analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning);
        var hasBlockingDiagnostics = analysis.HasErrors || analysis.HasStructuralFailure ||
            (effectiveDiagnosticConfiguration.TreatWarningsAsErrors && hasWarnings);

        if (!hasBlockingDiagnostics) {
            return;
        }

        var message = analysis.Diagnostics.Count == 0
            ? "Analysis failed before interpretation could start."
            : $"Analysis failed before interpretation could start: {analysis.Diagnostics[0].Severity} {analysis.Diagnostics[0].Message}";

        throw new InvalidOperationException(message);
    }

    private AnalysisResult RunInsightAnalysisOnSuspendedState(SuspendedExecution suspended) {
        if (_liveStateAnalyzers.Count == 0 && _insightAnalyzers.Count == 0) {
            return new AnalysisResult(
                new AnalysisContext(ClrTypeDefinitionRegistry.Shared), AnalysisTelemetry.Empty);
        }

        var context = new AnalysisContext(ClrTypeDefinitionRegistry.Shared);

        foreach (var analyzer in _liveStateAnalyzers) {
            analyzer.AnalyzeSuspendedState(context, suspended);
        }

        if (_insightAnalyzers.Count > 0 && suspended.AtNode is not null) {
            var insightAnalyzer = new InsightAnalyzer();
            foreach (var a in _insightAnalyzers) {
                insightAnalyzer.AddAnalyzer(a);
            }
            var nodeResult = insightAnalyzer.Analyze(suspended.AtNode);
            foreach (var d in nodeResult.Diagnostics) {
                context.ReportDiagnostic(d.Node, d.Severity, d.Message, d.Code);
            }
        }

        return new AnalysisResult(context, AnalysisTelemetry.Empty);
    }

    private InterpreterResult ExecuteCurrentNode(InterpreterState state) {
        var frame = state.CallStack.CurrentFrame;
        var originalNode = frame.CurrentNode;
        var node = originalNode;

        if (node is null) {
            return InterpreterResult.Void;
        }

        if (state.BreakpointSkipNodeId is { } skippedBreakpoint && node.Id == skippedBreakpoint) {
            state.BreakpointSkipNodeId = null;
        }
        else if (_breakpoints.Contains(node.Id)) {
            state.BreakpointSkipNodeId = node.Id;
            state.Suspend($"Breakpoint hit at {node.GetType().Name}", node);
            return InterpreterResult.Void;
        }

        var replacement = state.AnalysisResult?.GetNodeReplacement(node);
        if (replacement is not null) {
            node = replacement;
            frame.CurrentNode = replacement;
        }

        try {
            foreach (var compiler in _compilers) {
                if (compiler.TryEvaluate(node, EvaluateChild, state, out var result)) {
                    return result;
                }
            }

            return node switch {
                Constant c => InterpreterResult.FromValue(c.Value),
                Add a => EvaluateBinary(a.LeftHandValue, a.RightHandValue, state, AddValues),
                Subtract s => EvaluateBinary(s.LeftHandValue, s.RightHandValue, state, SubtractValues),
                Multiply m => EvaluateBinary(m.LeftHandValue, m.RightHandValue, state, MultiplyValues),
                Divide d => EvaluateBinary(d.LeftHandValue, d.RightHandValue, state, DivideValues),
                Modulo m => EvaluateBinary(m.LeftHandValue, m.RightHandValue, state, ModuloValues),
                UnaryMinus u => EvaluateUnary(u.Operand, state, NegateValue),
                And a => EvaluateAnd(a.LeftHandValue, a.RightHandValue, state),
                Or o => EvaluateOr(o.LeftHandValue, o.RightHandValue, state),
                Not n => EvaluateNot(n.Value, state),
                Equal e => EvaluateComparison(e.LeftHandValue, e.RightHandValue, state, (a, b) => Compare(a, b) == 0),
                NotEqual ne => EvaluateComparison(ne.LeftHandValue, ne.RightHandValue, state, (a, b) => Compare(a, b) != 0),
                LessThan lt => EvaluateComparison(lt.LeftHandValue, lt.RightHandValue, state, (a, b) => Compare(a, b) < 0),
                LessThanOrEqual le => EvaluateComparison(le.LeftHandValue, le.RightHandValue, state, (a, b) => Compare(a, b) <= 0),
                GreaterThan gt => EvaluateComparison(gt.LeftHandValue, gt.RightHandValue, state, (a, b) => Compare(a, b) > 0),
                GreaterThanOrEqual ge => EvaluateComparison(ge.LeftHandValue, ge.RightHandValue, state, (a, b) => Compare(a, b) >= 0),
                Conditional c => EvaluateConditional(c, state),
                Block b => EvaluateBlock(b, state),
                IfStatement i => EvaluateIfStatement(i, state),
                WhileLoop w => EvaluateWhileLoop(w, state),
                ForLoop f => EvaluateForLoop(f, state),
                Return r => InterpreterResult.Return(
                    r.Value is not null ? EvaluateChild(r.Value, state).Value : null),
                TypeCast tc => EvaluateTypeCast(tc, state),
                TypeIs ti => EvaluateTypeIs(ti, state),
                Member m => EvaluateMember(m, state),
                IndexAccess i => EvaluateIndexAccess(i, state),
                Variable v => HandleVariable(v, state),
                Parameter p => HandleParameter(p, state),
                Assignment a => HandleAssignment(a, state),
                SuspendNode sn => HandleSuspendNode(sn, state),
                _ => InterpreterResult.Void
            };
        }
        finally {
            frame.CurrentNode = originalNode;
        }
    }

    private InterpreterResult EvaluateChild(Node child, InterpreterState state) {
        var frame = state.CurrentFrame;
        var previousNode = frame.CurrentNode;

        frame.CurrentNode = child;
        try {
            return ExecuteCurrentNode(state);
        }
        finally {
            frame.CurrentNode = previousNode;
        }
    }

    private void AdvanceToNextNode(InterpreterState state) {
        if (state.CallStack.Count == 1 && !state.IsSuspended) {
            state.Complete(InterpreterResult.Void);
        }
    }

    private InterpreterResult HandleVariable(Variable v, InterpreterState state) {
        if (v.Value is not null) {
            return EvaluateChild(v.Value, state);
        }

        if (state.Variables.TryGetValue(v.Name, out var storedValue)) {
            return InterpreterResult.FromValue(storedValue);
        }

        return InterpreterResult.Void;
    }

    private InterpreterResult HandleParameter(Parameter p, InterpreterState state) {
        if (state.Variables.TryGetValue(p.Name, out var value)) {
            return InterpreterResult.FromValue(value);
        }

        if (p.DefaultValue is not null) {
            return EvaluateChild(p.DefaultValue, state);
        }

        return InterpreterResult.Void;
    }

    private InterpreterResult HandleAssignment(Assignment a, InterpreterState state) {
        var valueResult = EvaluateChild(a.Value, state);
        if (!valueResult.HasValue) return InterpreterResult.Void;

        if (!TryAssignToDestination(a.Destination, valueResult.Value, state)) {
            return InterpreterResult.Void;
        }

        return valueResult;
    }

    private InterpreterResult HandleSuspendNode(SuspendNode node, InterpreterState state) {
        var result = EvaluateChild(node.Inner, state);
        if (result.IsSignal) return result;

        state.Suspend(node.Reason ?? "SuspendNode", node);
        return InterpreterResult.Void;
    }

    private InterpreterResult EvaluateBinary(
        Node left, Node right, InterpreterState state, Func<object?, object?, object?> operation) {
        var leftResult = EvaluateChild(left, state);
        var rightResult = EvaluateChild(right, state);

        if (!leftResult.HasValue || !rightResult.HasValue) {
            return InterpreterResult.Void;
        }

        try {
            var result = operation(leftResult.Value, rightResult.Value);
            return InterpreterResult.FromValue(result);
        }
        catch (Exception ex) {
            return InterpreterResult.FromSignal(InterpreterSignal.Throw(ex));
        }
    }

    private object? AddValues(object? a, object? b) {
        dynamic left = a ?? throw new InvalidOperationException("Left operand is null.");
        dynamic right = b ?? throw new InvalidOperationException("Right operand is null.");
        return left + right;
    }

    private object? SubtractValues(object? a, object? b) {
        dynamic left = a ?? throw new InvalidOperationException("Left operand is null.");
        dynamic right = b ?? throw new InvalidOperationException("Right operand is null.");
        return left - right;
    }

    private object? MultiplyValues(object? a, object? b) {
        dynamic left = a ?? throw new InvalidOperationException("Left operand is null.");
        dynamic right = b ?? throw new InvalidOperationException("Right operand is null.");
        return left * right;
    }

    private object? DivideValues(object? a, object? b) {
        dynamic left = a ?? throw new InvalidOperationException("Left operand is null.");
        dynamic right = b ?? throw new InvalidOperationException("Right operand is null.");
        return left / right;
    }

    private object? ModuloValues(object? a, object? b) {
        dynamic left = a ?? throw new InvalidOperationException("Left operand is null.");
        dynamic right = b ?? throw new InvalidOperationException("Right operand is null.");
        return left % right;
    }

    private object? NegateValue(object? value) {
        dynamic operand = value ?? throw new InvalidOperationException("Operand is null.");
        return -operand;
    }

    private InterpreterResult EvaluateUnary(Node operand, InterpreterState state, Func<object?, object?> operation) {
        var operandResult = EvaluateChild(operand, state);
        if (!operandResult.HasValue) {
            return InterpreterResult.Void;
        }

        try {
            var result = operation(operandResult.Value);
            return InterpreterResult.FromValue(result);
        }
        catch (Exception ex) {
            return InterpreterResult.FromSignal(InterpreterSignal.Throw(ex));
        }
    }

    private int Compare(object? a, object? b) {
        if (a is IComparable ca && b is IComparable cb) {
            return ca.CompareTo(cb);
        }
        return 0;
    }

    private InterpreterResult EvaluateAnd(Node left, Node right, InterpreterState state) {
        var leftResult = EvaluateChild(left, state);
        if (leftResult.HasValue && leftResult.Value is false) {
            return InterpreterResult.FromValue(false);
        }
        return EvaluateChild(right, state);
    }

    private InterpreterResult EvaluateOr(Node left, Node right, InterpreterState state) {
        var leftResult = EvaluateChild(left, state);
        if (leftResult.HasValue && leftResult.Value is true) {
            return InterpreterResult.FromValue(true);
        }
        return EvaluateChild(right, state);
    }

    private InterpreterResult EvaluateNot(Node operand, InterpreterState state) {
        var result = EvaluateChild(operand, state);
        if (result.HasValue && result.Value is bool b) {
            return InterpreterResult.FromValue(!b);
        }
        return InterpreterResult.Void;
    }

    private InterpreterResult EvaluateBlock(Block block, InterpreterState state) {
        InterpreterResult lastResult = InterpreterResult.Void;
        var frame = state.CurrentFrame;
        var blockIndexKey = $"BlockIndex:{RuntimeHelpers.GetHashCode(block)}";

        int startIndex = frame.Metadata.TryGetValue(blockIndexKey, out var idx)
            ? (int)idx!
            : 0;

        for (int i = startIndex; i < block.Nodes.Count; i++) {
            if (i < block.Nodes.Count - 1 && state.AnalysisResult is { } analysis && analysis.CanElide(block.Nodes[i])) {
                continue;
            }

            lastResult = EvaluateChild(block.Nodes[i], state);

            if (state.IsSuspended) {
                var suspendedByBreakpoint = state.BreakpointSkipNodeId is { } breakpointNodeId
                    && block.Nodes[i].Id == breakpointNodeId;

                frame.Metadata[blockIndexKey] = suspendedByBreakpoint ? i : i + 1;
                return lastResult;
            }

            if (lastResult.IsSignal) {
                return lastResult;
            }
        }

        frame.Metadata.Remove(blockIndexKey);
        return lastResult;
    }

    private static void HandleSignal(InterpreterSignal signal, InterpreterState state) {
        switch (signal.Kind) {
            case InterpreterSignal.SignalKind.Return:
                state.Complete(InterpreterResult.FromValue(signal.Value));
                break;

            case InterpreterSignal.SignalKind.Throw:
                if (signal.Value is Exception ex) {
                    throw ex;
                }
                break;
        }
    }

    private InterpreterResult EvaluateIfStatement(IfStatement ifStatement, InterpreterState state) {
        var conditionResult = EvaluateChild(ifStatement.Condition, state);
        if (conditionResult.HasValue && conditionResult.Value is bool condition && condition) {
            return EvaluateChild(ifStatement.ThenBranch, state);
        }
        else if (ifStatement.ElseBranch is not null) {
            return EvaluateChild(ifStatement.ElseBranch, state);
        }
        return InterpreterResult.Void;
    }

    private InterpreterResult EvaluateComparison(
        Node left, Node right, InterpreterState state, Func<object?, object?, bool> comparison) {
        var leftResult = EvaluateChild(left, state);
        var rightResult = EvaluateChild(right, state);

        if (!leftResult.HasValue || !rightResult.HasValue) {
            return InterpreterResult.Void;
        }

        try {
            var result = comparison(leftResult.Value, rightResult.Value);
            return InterpreterResult.FromValue(result);
        }
        catch {
            return InterpreterResult.Void;
        }
    }

    private InterpreterResult EvaluateWhileLoop(WhileLoop whileLoop, InterpreterState state) {
        while (true) {
            var conditionResult = EvaluateChild(whileLoop.Condition, state);
            if (conditionResult.HasValue && conditionResult.Value is bool condition && !condition) {
                break;
            }

            var bodyResult = EvaluateChild(whileLoop.Body, state);
            if (bodyResult.IsSignal && bodyResult.Signal is { } signalResult) {
                if (signalResult.Kind == InterpreterSignal.SignalKind.Return || signalResult.Kind == InterpreterSignal.SignalKind.Throw) {
                    return bodyResult;
                }

                if (signalResult.Kind == InterpreterSignal.SignalKind.Break) {
                    break;
                }

                if (signalResult.Kind == InterpreterSignal.SignalKind.Continue) {
                    continue;
                }
            }
        }
        return InterpreterResult.Void;
    }

    private InterpreterResult EvaluateForLoop(ForLoop forLoop, InterpreterState state) {
        if (forLoop.Initializer is not null) {
            EvaluateChild(forLoop.Initializer, state);
        }

        while (true) {
            if (forLoop.Condition is not null) {
                var conditionResult = EvaluateChild(forLoop.Condition, state);
                if (conditionResult.HasValue && conditionResult.Value is bool condition && !condition) {
                    break;
                }
            }

            var bodyResult = EvaluateChild(forLoop.Body, state);
            if (bodyResult.IsSignal && bodyResult.Signal is { } signalResult) {
                if (signalResult.Kind == InterpreterSignal.SignalKind.Return || signalResult.Kind == InterpreterSignal.SignalKind.Throw) {
                    return bodyResult;
                }

                if (signalResult.Kind == InterpreterSignal.SignalKind.Break) {
                    break;
                }

                if (signalResult.Kind != InterpreterSignal.SignalKind.Continue) {
                    break;
                }
            }

            if (forLoop.Increment is not null) {
                EvaluateChild(forLoop.Increment, state);
            }
        }
        return InterpreterResult.Void;
    }

    private InterpreterResult EvaluateTypeCast(TypeCast typeCast, InterpreterState state) {
        var operandResult = EvaluateChild(typeCast.Operand, state);
        if (!operandResult.HasValue) return InterpreterResult.Void;

        return InterpreterResult.FromValue(operandResult.Value);
    }

    private InterpreterResult EvaluateTypeIs(TypeIs typeIs, InterpreterState state) {
        var operandResult = EvaluateChild(typeIs.Operand, state);
        if (!operandResult.HasValue) return InterpreterResult.FromValue(false);

        return InterpreterResult.FromValue(operandResult.Value != null);
    }

    private InterpreterResult EvaluateConditional(Conditional conditional, InterpreterState state) {
        var conditionResult = EvaluateChild(conditional.Condition, state);
        if (!conditionResult.HasValue || conditionResult.Value is not bool condition) {
            return InterpreterResult.Void;
        }

        return EvaluateChild(condition ? conditional.IfTrue : conditional.IfFalse, state);
    }

    private InterpreterResult EvaluateMember(Member member, InterpreterState state) {
        var ownerResult = EvaluateChild(member.Value, state);
        if (!ownerResult.HasValue || ownerResult.Value is null) {
            return InterpreterResult.Void;
        }

        try {
            var owner = ownerResult.Value;

            var resolvedMember = state.AnalysisResult?.GetResolvedMember(member);
            if (resolvedMember is not null) {
                return InterpreterResult.FromValue(ReadResolvedMemberValue(owner, resolvedMember));
            }

            var ownerType = owner.GetType();

            var field = ownerType.GetField(member.MemberName, BindingFlags.Public | BindingFlags.Instance);
            if (field is not null) {
                return InterpreterResult.FromValue(field.GetValue(owner));
            }

            var property = ownerType.GetProperty(member.MemberName, BindingFlags.Public | BindingFlags.Instance);
            if (property is not null) {
                return InterpreterResult.FromValue(property.GetValue(owner));
            }

            return InterpreterResult.Void;
        }
        catch (Exception ex) {
            return InterpreterResult.FromSignal(InterpreterSignal.Throw(ex));
        }
    }

    private InterpreterResult EvaluateIndexAccess(IndexAccess indexAccess, InterpreterState state) {
        var targetResult = EvaluateChild(indexAccess.Value, state);
        if (!targetResult.HasValue || targetResult.Value is null) {
            return InterpreterResult.Void;
        }

        var argumentValues = new object?[indexAccess.Arguments.Length];
        for (int i = 0; i < indexAccess.Arguments.Length; i++) {
            var argResult = EvaluateChild(indexAccess.Arguments[i], state);
            if (!argResult.HasValue) {
                return InterpreterResult.Void;
            }

            argumentValues[i] = argResult.Value;
        }

        try {
            var target = targetResult.Value;

            var resolvedMember = state.AnalysisResult?.GetResolvedMember(indexAccess);
            if (resolvedMember is not null) {
                return InterpreterResult.FromValue(ReadResolvedMemberValue(target, resolvedMember, argumentValues));
            }

            if (target is Array array && argumentValues.Length == 1 && argumentValues[0] is int index) {
                return InterpreterResult.FromValue(array.GetValue(index));
            }

            if (target is IList list && argumentValues.Length == 1 && argumentValues[0] is int listIndex) {
                return InterpreterResult.FromValue(list[listIndex]);
            }

            if (target is IDictionary dictionary && argumentValues.Length == 1) {
                return InterpreterResult.FromValue(dictionary[argumentValues[0]!]);
            }

            var indexer = target.GetType().GetProperty("Item", BindingFlags.Public | BindingFlags.Instance);
            if (indexer is not null) {
                return InterpreterResult.FromValue(indexer.GetValue(target, argumentValues));
            }

            return InterpreterResult.Void;
        }
        catch (Exception ex) {
            return InterpreterResult.FromSignal(InterpreterSignal.Throw(ex));
        }
    }

    private bool TryAssignToDestination(Node destination, object? value, InterpreterState state) {
        switch (destination) {
            case Variable destVar:
                state.Variables[destVar.Name] = value;
                return true;

            case Parameter destParam:
                state.Variables[destParam.Name] = value;
                return true;

            case Member memberAccess:
                return TryAssignToMember(memberAccess, value, state);

            case IndexAccess indexAccess:
                return TryAssignToIndex(indexAccess, value, state);

            default:
                return false;
        }
    }

    private bool TryAssignToMember(Member member, object? value, InterpreterState state) {
        var ownerResult = EvaluateChild(member.Value, state);
        if (!ownerResult.HasValue || ownerResult.Value is null) {
            throw new InvalidOperationException("Cannot assign to member on a null target.");
        }

        var owner = ownerResult.Value;

        var resolvedMember = state.AnalysisResult?.GetResolvedMember(member);
        var updatedOwner = resolvedMember is not null
            ? WriteResolvedMemberValue(owner, resolvedMember, value)
            : SetMemberValue(owner, member.MemberName, value);

        return !DestinationRequiresWriteback(member.Value) || TryAssignToDestination(member.Value, updatedOwner, state);
    }

    private bool TryAssignToIndex(IndexAccess indexAccess, object? value, InterpreterState state) {
        var targetResult = EvaluateChild(indexAccess.Value, state);
        if (!targetResult.HasValue || targetResult.Value is null) {
            throw new InvalidOperationException("Cannot assign via index access on a null target.");
        }

        var argumentValues = new object?[indexAccess.Arguments.Length];
        for (int i = 0; i < indexAccess.Arguments.Length; i++) {
            var argResult = EvaluateChild(indexAccess.Arguments[i], state);
            if (!argResult.HasValue) {
                throw new InvalidOperationException("Index argument did not produce a value.");
            }

            argumentValues[i] = argResult.Value;
        }

        var target = targetResult.Value;

        var resolvedMember = state.AnalysisResult?.GetResolvedMember(indexAccess);
        var updatedTarget = resolvedMember is not null
            ? WriteResolvedMemberValue(target, resolvedMember, value, argumentValues)
            : SetIndexValue(target, argumentValues, value);

        return !DestinationRequiresWriteback(indexAccess.Value) || TryAssignToDestination(indexAccess.Value, updatedTarget, state);
    }

    private static bool DestinationRequiresWriteback(Node destination)
        => destination is Variable or Parameter or Member or IndexAccess;

    private static object SetMemberValue(object owner, string memberName, object? value) {
        var ownerType = owner.GetType();

        var field = ownerType.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
        if (field is not null) {
            field.SetValue(owner, value);
            return owner;
        }

        var property = ownerType.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
        if (property is not null && property.CanWrite) {
            property.SetValue(owner, value);
            return owner;
        }

        throw new InvalidOperationException($"Member '{memberName}' is not writable on type '{ownerType.Name}'.");
    }

    private static object? ReadResolvedMemberValue(object owner, ITypeMember resolvedMember, object?[]? arguments = null) {
        if (!resolvedMember.CanRead) {
            throw new InvalidOperationException($"Resolved member '{resolvedMember.Name}' is not readable.");
        }

        MemberReadDelegate? reader = (resolvedMember as ITypeField)?.Read
                                  ?? (resolvedMember as ITypeProperty)?.Read;
        if (reader is not null) {
            return reader(owner, arguments);
        }

        if (resolvedMember is ClrTypeField clrField) {
            return clrField.FieldInfo.GetValue(owner);
        }

        if (resolvedMember is ClrTypeProperty clrProperty) {
            var args = arguments is { Length: > 0 } ? arguments : null;
            return clrProperty.PropertyInfo.GetValue(owner, args);
        }

        var runtimeType = resolvedMember.DeclaringTypeDefinition.GetRuntimeType();
        if (runtimeType is null) {
            throw new InvalidOperationException($"Resolved member '{resolvedMember.Name}' does not have a runtime declaration type.");
        }

        var bindingFlags = BindingFlags.Public | BindingFlags.Instance;
        var field = runtimeType.GetField(resolvedMember.Name, bindingFlags);
        if (field is not null) {
            return field.GetValue(owner);
        }

        var property = runtimeType.GetProperty(resolvedMember.Name, bindingFlags);
        if (property is not null) {
            var args = arguments is { Length: > 0 } ? arguments : null;
            return property.GetValue(owner, args);
        }

        throw new InvalidOperationException($"Unable to read resolved member '{resolvedMember.Name}' on type '{runtimeType.Name}'.");
    }

    private static object WriteResolvedMemberValue(object owner, ITypeMember resolvedMember, object? value, object?[]? arguments = null) {
        var isWritable = resolvedMember.CanWrite || resolvedMember.CanInitialize;
        if (!isWritable) {
            throw new InvalidOperationException($"Resolved member '{resolvedMember.Name}' is not writable.");
        }

        MemberWriteDelegate? writer = (resolvedMember as ITypeField)?.Write
                                   ?? (resolvedMember as ITypeProperty)?.Write
                                   ?? (resolvedMember as ITypeField)?.Initialize
                                   ?? (resolvedMember as ITypeProperty)?.Initialize;
        if (writer is not null) {
            return writer(owner, value, arguments);
        }

        if (resolvedMember is ClrTypeField clrField) {
            clrField.FieldInfo.SetValue(owner, value);
            return owner;
        }

        if (resolvedMember is ClrTypeProperty clrProperty) {
            if (!clrProperty.CanWrite && !clrProperty.CanInitialize) {
                throw new InvalidOperationException($"Resolved member '{resolvedMember.Name}' is not writable on type '{clrProperty.PropertyInfo.DeclaringType?.Name ?? "Unknown"}'.");
            }

            var args = arguments is { Length: > 0 } ? arguments : null;
            clrProperty.PropertyInfo.SetValue(owner, value, args);
            return owner;
        }

        var runtimeType = resolvedMember.DeclaringTypeDefinition.GetRuntimeType();
        if (runtimeType is null) {
            throw new InvalidOperationException($"Resolved member '{resolvedMember.Name}' does not have a runtime declaration type.");
        }

        var bindingFlags = BindingFlags.Public | BindingFlags.Instance;
        var field = runtimeType.GetField(resolvedMember.Name, bindingFlags);
        if (field is not null) {
            field.SetValue(owner, value);
            return owner;
        }

        var property = runtimeType.GetProperty(resolvedMember.Name, bindingFlags);
        if (property is not null && property.CanWrite) {
            var args = arguments is { Length: > 0 } ? arguments : null;
            property.SetValue(owner, value, args);
            return owner;
        }

        throw new InvalidOperationException($"Resolved member '{resolvedMember.Name}' is not writable on type '{runtimeType.Name}'.");
    }

    private static object SetIndexValue(object target, object?[] argumentValues, object? value) {
        if (target is Array array && argumentValues.Length == 1 && argumentValues[0] is int index) {
            array.SetValue(value, index);
            return target;
        }

        if (target is IList list && argumentValues.Length == 1 && argumentValues[0] is int listIndex) {
            list[listIndex] = value;
            return target;
        }

        if (target is IDictionary dictionary && argumentValues.Length == 1) {
            dictionary[argumentValues[0]!] = value;
            return target;
        }

        var indexer = target.GetType().GetProperty("Item", BindingFlags.Public | BindingFlags.Instance);
        if (indexer is not null && indexer.CanWrite) {
            indexer.SetValue(target, value, argumentValues);
            return target;
        }

        throw new InvalidOperationException($"Index assignment is not supported for type '{target.GetType().Name}'.");
    }

    public void Dispose() {
        if (_disposed) return;
        _currentState?.Dispose();
        _disposed = true;
    }
}