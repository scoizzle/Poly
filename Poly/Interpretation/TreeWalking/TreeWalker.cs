using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

using Poly.Interpretation.Analysis;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.TreeWalking;

public sealed class TreeWalker : IDisposable {
    private readonly AnalysisResult? _analysisResult;
    private readonly InterpreterOptions _options;
    private readonly List<ITreeWalkerCompiler> _compilers = new();
    private readonly List<INodeAnalyzer> _insightAnalyzers = new();
    private readonly List<ILiveStateAnalyzer> _liveStateAnalyzers = new();
    private InterpreterState? _currentState;
    private bool _disposed;

    public AnalysisResult? LastInsightResult { get; private set; }

    public TreeWalker(AnalysisResult? analysisResult = null, InterpreterOptions? options = null) {
        _analysisResult = analysisResult;
        _options = options ?? InterpreterOptions.Default;
    }

    public TreeWalker RegisterCompiler(ITreeWalkerCompiler compiler) {
        _compilers.Add(compiler);
        return this;
    }

    public TreeWalker RegisterInsightAnalyzer(INodeAnalyzer analyzer) {
        _insightAnalyzers.Add(analyzer);
        return this;
    }

    public TreeWalker RegisterLiveStateAnalyzer(ILiveStateAnalyzer analyzer) {
        _liveStateAnalyzers.Add(analyzer);
        return this;
    }

    public InterpreterResult Evaluate(Node node) {
        return Evaluate(node, new Dictionary<string, object?>());
    }

    public InterpreterResult Evaluate(Node node, IReadOnlyDictionary<string, object?> initialVariables) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_currentState is not null)
            throw new InvalidOperationException("TreeWalker already has an active evaluation");

        _currentState = new InterpreterState();

        var rootFrame = new StackFrame(node);
        _currentState.CallStack.Push(rootFrame);

        foreach (var kv in initialVariables) {
            _currentState.Variables[kv.Key] = kv.Value;
        }

        return ContinueEvaluation();
    }

    public InterpreterResult Resume() {
        if (_currentState is null)
            throw new InvalidOperationException("No suspended state to resume");
        if (!_currentState.IsSuspended)
            throw new InvalidOperationException("Current state is not suspended");

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
                    state.ValueStack.Push(result.Value);
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

            return state.LastResult ?? InterpreterResult.None;
        }
        finally {
            if (!state.IsSuspended) {
                state.Dispose();
                _currentState = null;
            }
        }
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
        var node = state.CurrentFrame.CurrentNode;

        foreach (var compiler in _compilers) {
            if (compiler.TryEvaluate(node, EvaluateChild, state, out var result)) {
                return result;
            }
        }

        return node switch {
            Constant c => InterpreterResult.FromValue(c.Value),
            Add a => EvaluateBinary(a.LeftHandValue, a.RightHandValue, state, (x, y) => AddValues(x, y)),
            Subtract s => EvaluateBinary(s.LeftHandValue, s.RightHandValue, state, (x, y) => SubtractValues(x, y)),
            Multiply m => EvaluateBinary(m.LeftHandValue, m.RightHandValue, state, (x, y) => MultiplyValues(x, y)),
            Divide d => EvaluateBinary(d.LeftHandValue, d.RightHandValue, state, (x, y) => DivideValues(x, y)),
            Modulo m => EvaluateBinary(m.LeftHandValue, m.RightHandValue, state, (x, y) => ModuloValues(x, y)),
            UnaryMinus u => EvaluateUnary(u.Operand, state, x => NegateValue(x)),
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
            Return r => InterpreterResult.FromSignal(InterpreterSignal.Return(
                r.Value is not null ? EvaluateChild(r.Value, state).Value : null)),
            TypeCast tc => EvaluateTypeCast(tc, state),
            TypeIs ti => EvaluateTypeIs(ti, state),
            Member m => EvaluateMember(m, state),
            IndexAccess i => EvaluateIndexAccess(i, state),
            Variable v => HandleVariable(v, state),
            Parameter p => HandleParameter(p, state),
            Assignment a => HandleAssignment(a, state),
            SuspendNode sn => HandleSuspendNode(sn, state),
            _ => InterpreterResult.None
        };
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
            state.Complete(InterpreterResult.None);
        }
    }

    private InterpreterResult HandleVariable(Variable v, InterpreterState state) {
        if (v.Value is not null) {
            return EvaluateChild(v.Value, state);
        }

        if (state.Variables.TryGetValue(v.Name, out var storedValue)) {
            return InterpreterResult.FromValue(storedValue);
        }

        return InterpreterResult.None;
    }

    private InterpreterResult HandleParameter(Parameter p, InterpreterState state) {
        if (state.Variables.TryGetValue(p.Name, out var value)) {
            return InterpreterResult.FromValue(value);
        }

        if (p.DefaultValue is not null) {
            return EvaluateChild(p.DefaultValue, state);
        }

        return InterpreterResult.None;
    }

    private InterpreterResult HandleAssignment(Assignment a, InterpreterState state) {
        var valueResult = EvaluateChild(a.Value, state);
        if (!valueResult.HasValue) return InterpreterResult.None;

        if (!TryAssignToDestination(a.Destination, valueResult.Value, state)) {
            return InterpreterResult.None;
        }

        return valueResult;
    }

    private InterpreterResult HandleSuspendNode(SuspendNode node, InterpreterState state) {
        var result = EvaluateChild(node.Inner, state);
        if (result.IsSignal) return result;

        state.Suspend(node.Reason ?? "SuspendNode", node);
        return InterpreterResult.None;
    }

    private InterpreterResult EvaluateBinary(
        Node left, Node right, InterpreterState state, Func<object?, object?, object?> operation) {
        var leftResult = EvaluateChild(left, state);
        var rightResult = EvaluateChild(right, state);

        if (!leftResult.HasValue || !rightResult.HasValue) {
            return InterpreterResult.None;
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
            return InterpreterResult.None;
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
        return InterpreterResult.None;
    }

    private InterpreterResult EvaluateBlock(Block block, InterpreterState state) {
        InterpreterResult lastResult = InterpreterResult.None;
        var frame = state.CurrentFrame;
        var blockIndexKey = $"BlockIndex:{RuntimeHelpers.GetHashCode(block)}";

        int startIndex = frame.Metadata.TryGetValue(blockIndexKey, out var idx)
            ? (int)idx!
            : 0;

        for (int i = startIndex; i < block.Nodes.Count; i++) {
            lastResult = EvaluateChild(block.Nodes[i], state);

            if (state.IsSuspended) {
                frame.Metadata[blockIndexKey] = i + 1;
                return lastResult;
            }

            if (lastResult.IsSignal) {
                return lastResult;
            }
        }

        frame.Metadata.Remove(blockIndexKey);
        return lastResult;
    }

    private void HandleSignal(InterpreterSignal signal, InterpreterState state) {
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
        return InterpreterResult.None;
    }

    private InterpreterResult EvaluateComparison(
        Node left, Node right, InterpreterState state, Func<object?, object?, bool> comparison) {
        var leftResult = EvaluateChild(left, state);
        var rightResult = EvaluateChild(right, state);

        if (!leftResult.HasValue || !rightResult.HasValue) {
            return InterpreterResult.None;
        }

        try {
            var result = comparison(leftResult.Value, rightResult.Value);
            return InterpreterResult.FromValue(result);
        }
        catch {
            return InterpreterResult.None;
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
        return InterpreterResult.None;
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
        return InterpreterResult.None;
    }

    private InterpreterResult EvaluateTypeCast(TypeCast typeCast, InterpreterState state) {
        var operandResult = EvaluateChild(typeCast.Operand, state);
        if (!operandResult.HasValue) return InterpreterResult.None;

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
            return InterpreterResult.None;
        }

        return EvaluateChild(condition ? conditional.IfTrue : conditional.IfFalse, state);
    }

    private InterpreterResult EvaluateMember(Member member, InterpreterState state) {
        var ownerResult = EvaluateChild(member.Value, state);
        if (!ownerResult.HasValue || ownerResult.Value is null) {
            return InterpreterResult.None;
        }

        try {
            var owner = ownerResult.Value;
            var ownerType = owner.GetType();

            var field = ownerType.GetField(member.MemberName, BindingFlags.Public | BindingFlags.Instance);
            if (field is not null) {
                return InterpreterResult.FromValue(field.GetValue(owner));
            }

            var property = ownerType.GetProperty(member.MemberName, BindingFlags.Public | BindingFlags.Instance);
            if (property is not null) {
                return InterpreterResult.FromValue(property.GetValue(owner));
            }

            return InterpreterResult.None;
        }
        catch (Exception ex) {
            return InterpreterResult.FromSignal(InterpreterSignal.Throw(ex));
        }
    }

    private InterpreterResult EvaluateIndexAccess(IndexAccess indexAccess, InterpreterState state) {
        var targetResult = EvaluateChild(indexAccess.Value, state);
        if (!targetResult.HasValue || targetResult.Value is null) {
            return InterpreterResult.None;
        }

        var argumentValues = new object?[indexAccess.Arguments.Length];
        for (int i = 0; i < indexAccess.Arguments.Length; i++) {
            var argResult = EvaluateChild(indexAccess.Arguments[i], state);
            if (!argResult.HasValue) {
                return InterpreterResult.None;
            }

            argumentValues[i] = argResult.Value;
        }

        try {
            var target = targetResult.Value;
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

            return InterpreterResult.None;
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
        var updatedOwner = SetMemberValue(owner, member.MemberName, value);

        return destinationRequiresWriteback(member.Value)
            ? TryAssignToDestination(member.Value, updatedOwner, state)
            : true;
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
        var updatedTarget = SetIndexValue(target, argumentValues, value);

        return destinationRequiresWriteback(indexAccess.Value)
            ? TryAssignToDestination(indexAccess.Value, updatedTarget, state)
            : true;
    }

    private static bool destinationRequiresWriteback(Node destination)
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