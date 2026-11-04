namespace Poly.Tests.Interpretation;

/// <summary>
/// The ordered-primitive differential: many well-tested nodes composed into one method body, where
/// each step reads state the previous step wrote. <see cref="AstParity"/> runs one program per row on
/// a fresh VM, so it can see a single shape but never a history.
/// </summary>
public class AstParitySequenceTests {
    /// <summary>
    /// The sequences whose two sides disagree. Each is evidence for one defect class, pinned with both
    /// disposals exactly: the row still runs on both sides, and a pin only matches that exact pair.
    /// An entry here is a defect to fix, not a way to keep a red row green.
    /// </summary>
    public static IReadOnlyList<KnownDivergence> PinnedDivergences { get; } = [
        new(
            Where: "TryCatch_UnwindThenReuseLocals",
            Vm: ParityDisposal.Executed,
            VmOutcome: "long:41",
            Printed: ParityDisposal.Executed,
            PrintedOutcomePrefix: "long:42",
            Finding: "the writer drops the leading statements of a multi-statement block used as a catch "
                + "body: the printed catch is `{ acc = 41; }` and `caught = 1` is absent, so the finally "
                + "and the fold after it see a flag the VM set and the printed side never did"),
        new(
            Where: "IntOverflow_OrderChangesResult",
            Vm: ParityDisposal.Executed,
            VmOutcome: "long:-2147483648",
            Printed: ParityDisposal.Executed,
            PrintedOutcomePrefix: "long:-2147483647",
            Finding: "integer division whose right operand is a bare Constant node is computed in IEEE and "
                + "floored: -2147483647 / 2 gives -1073741824 instead of -1073741823. The same expression "
                + "with a Variable divisor agrees, and the divergence is shape-sensitive, so it is the "
                + "operand's slot classification rather than the operator"),
    ];

    /// <summary>
    /// Sequences that pair state axes the single-row corpus keeps apart: ring depth against statement
    /// order, unwind against locals, loop back-edges against ring reuse, closure capture against
    /// iteration, heap lifetime against loops, and numeric promotion against accumulator order.
    /// Each case must execute on both sides — an agreeing pair of refusals proves nothing.
    /// </summary>
    public static IEnumerable<AstParitySequence.SequenceCase> Cases() {
        yield return RingDepth_NestedArithmeticAcrossStatements();
        yield return TryCatch_UnwindThenReuseLocals();
        yield return ForLoop_AccumulateNestedArithmetic();
        yield return WhileLoop_ShiftAndXorInterleaved();
        yield return Closure_CapturedLocalAcrossIterations();
        yield return String_BuiltInsideLoop();
        yield return MixedNumeric_PromotionAcrossAccumulatorOrder();
        yield return IntOverflow_OrderChangesResult();
        yield return IntDivision_TruncatesTowardZero();
        yield return IntDivision_NegativeOperandsTruncateTowardZero();
    }

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task OrderedPrimitives_PrintedCSharp_AgreesWithTheVm(AstParitySequence.SequenceCase sequence) {
        var row = AstParitySequence.Compare(sequence);
        await Assert.That(row.Vm).IsEqualTo(ParityDisposal.Executed)
            .Because($"{sequence.Label}: the VM refused the sequence — {row.VmOutcome}");
        await Assert.That(row.Printed).IsEqualTo(ParityDisposal.Executed)
            .Because($"{sequence.Label}: the printed side refused the sequence — {row.PrintedOutcome}");

        var pinned = PinnedDivergences.FirstOrDefault(pin => pin.Where == sequence.Label);
        if (pinned is null) {
            await Assert.That(row.Agrees).IsTrue().Because(AstParitySequence.Describe(sequence));
            return;
        }
        await Assert.That(row.Agrees).IsFalse()
            .Because($"{sequence.Label} now agrees on both sides; the pinned divergence is stale — delete its entry");
        await Assert.That(pinned.Matches(row)).IsTrue()
            .Because($"{sequence.Label} diverges differently than its pin records");
    }

    /// <summary>
    /// Anti-vacuity control: a harness that returned a constant, or that never ran one side, would
    /// still "agree" on every case above. Two programs with different values must disagree — both
    /// through the oracle's primitives and through this harness's own seam.
    /// </summary>
    [Test]
    public async Task NegativeControl_TheOutcomeIsComputedOnBothSides() {
        var vmOne = AstParity.RunVm(new Add(Wrap(1), Wrap(1)));
        var vmTwo = AstParity.RunVm(new Add(Wrap(1), Wrap(2)));
        await Assert.That(vmOne.Outcome).IsNotEqualTo(vmTwo.Outcome)
            .Because("the VM outcome did not change with the program");

        var printedOne = AstParity.RunPrinted(new Add(Wrap(1), Wrap(1)));
        var printedTwo = AstParity.RunPrinted(new Add(Wrap(1), Wrap(2)));
        await Assert.That(printedOne.Outcome).IsNotEqualTo(printedTwo.Outcome)
            .Because("the printed outcome did not change with the program");

        var sequences = Cases().ToArray();
        var first = AstParitySequence.Compare(sequences[0]).VmOutcome;
        await Assert.That(sequences.Skip(1).Any(c => AstParitySequence.Compare(c).VmOutcome != first)).IsTrue()
            .Because("every sequence produced the same outcome, which a constant-returning Compare would also do");
    }

    /// <summary>
    /// The cases and the pins cannot drift apart: a pin with no case validates nothing, and a case
    /// that silently disappears takes its evidence with it while the suite stays green.
    /// </summary>
    [Test]
    public async Task Pins_NameLiveCases_AndEveryCaseHasAStepAfterTheSeed() {
        var labels = Cases().Select(sequence => sequence.Label).ToArray();
        await Assert.That(labels.Length).IsEqualTo(10)
            .Because("the planned sequence set is ten cases; changing it is a deliberate edit of this guard");
        await Assert.That(PinnedDivergences.Select(pin => pin.Where).Except(labels)).IsEmpty()
            .Because("a pin with no live case is a stale finding, not evidence");
        foreach (var sequence in Cases())
            await Assert.That(sequence.Steps.Count).IsGreaterThanOrEqualTo(2)
                .Because($"{sequence.Label}: a one-step case passes the every-step check vacuously");
    }

    /// <summary>
    /// Every step after the seed must carry the sequence: a step that changes nothing is dead weight
    /// and would let a case report agreement while exercising less than it claims.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task EveryStepChangesTheObservableOutcome(AstParitySequence.SequenceCase sequence) {
        var previous = AstParitySequence.Compare(sequence, prefixSteps: 1).VmOutcome;
        for (var count = 2; count <= sequence.Steps.Count; count++) {
            var row = AstParitySequence.Compare(sequence, prefixSteps: count);
            await Assert.That(row.VmOutcome).IsNotEqualTo(previous)
                .Because($"{sequence.Label}: step {count - 1} '{sequence.Steps[count - 1].Label}' left the observable untouched");
            previous = row.VmOutcome;
        }
    }

    /// <summary>
    /// The localizer splits a case into prefixes, so it must agree with the whole-program verdict on
    /// every case: an agreeing sequence localizes to nothing, and a diverging one names a step.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task FirstDivergence_AgreesWithTheWholeProgramVerdict(AstParitySequence.SequenceCase sequence) {
        var whole = AstParitySequence.Compare(sequence);
        var first = AstParitySequence.FirstDivergence(sequence);
        await Assert.That(first is null).IsEqualTo(whole.Agrees)
            .Because($"{sequence.Label}: the prefix walk and the whole-program run disagree about whether the sequence diverges");
    }

    /// <summary>
    /// Pin 2's attribution, made executable: swapping only the divisor's node kind — a <c>Constant</c>
    /// versus a <c>Variable</c> holding the same 2 — decides whether the two sides agree. If the defect
    /// is fixed this fails, which is the signal to delete that pin and this control together.
    /// </summary>
    [Test]
    public async Task ConstantDivisor_IsWhatMakesIntegerDivisionDiverge() {
        static AstParitySequence.SequenceCase Build(bool constantDivisor) {
            var acc = new Variable("acc");
            var x = new Variable("x");
            var b = new Variable("b");
            var divisor = constantDivisor ? (Node)Wrap(2) : b;
            Variable[] variables = constantDivisor ? [acc, x] : [acc, x, b];
            List<Node> seed = [new Assignment(acc, Wrap(0)), new Assignment(x, Wrap(-2147483647))];
            if (!constantDivisor)
                seed.Add(new Assignment(b, Wrap(2)));
            return new($"ConstantDivisor={constantDivisor}", [
                new("seed", new Block(seed)),
                new("halve", new Assignment(acc, new Divide(x, divisor))),
            ], variables, acc);
        }

        var withVariable = AstParitySequence.Compare(Build(false));
        var withConstant = AstParitySequence.Compare(Build(true));

        await Assert.That(withVariable.Vm).IsEqualTo(ParityDisposal.Executed).Because(withVariable.Describe());
        await Assert.That(withVariable.Printed).IsEqualTo(ParityDisposal.Executed).Because(withVariable.Describe());
        await Assert.That(withConstant.Vm).IsEqualTo(ParityDisposal.Executed).Because(withConstant.Describe());
        await Assert.That(withConstant.Printed).IsEqualTo(ParityDisposal.Executed).Because(withConstant.Describe());

        await Assert.That(withVariable.Agrees).IsTrue()
            .Because($"a Variable divisor agrees: {withVariable.Describe()}");
        await Assert.That(withConstant.Agrees).IsFalse()
            .Because("the constant-divisor divergence is gone; delete its pin in PinnedDivergences and this control");
        await Assert.That(withConstant.VmOutcome).IsEqualTo("long:-1073741824");
        await Assert.That(withConstant.PrintedOutcome).IsEqualTo("long:-1073741823");
    }

    // ── The cases ───────────────────────────────────────────────

    static AstParitySequence.SequenceCase RingDepth_NestedArithmeticAcrossStatements() {
        var acc = new Variable("acc");
        var a = new Variable("a");
        var b = new Variable("b");
        var c = new Variable("c");
        return new("RingDepth_NestedArithmeticAcrossStatements", [
            new("seed", new Block(
                new Assignment(acc, Wrap(0)),
                new Assignment(a, Wrap(7)),
                new Assignment(b, Wrap(3)),
                new Assignment(c, Wrap(2)))),
            new("two products", new Assignment(acc, new Subtract(new Multiply(new Add(a, b), new Add(c, a)), new Multiply(b, c)))),
            new("read the accumulator", new Assignment(acc, new Add(new Multiply(new Subtract(acc, a), new Add(b, c)), new Multiply(a, c)))),
            new("deep, reading the accumulator twice", new Assignment(acc, new Subtract(new Multiply(new Add(acc, b), new Subtract(a, b)), new Multiply(new Add(c, acc), new Subtract(b, c))))),
        ], [acc, a, b, c], acc);
    }

    static AstParitySequence.SequenceCase TryCatch_UnwindThenReuseLocals() {
        var acc = new Variable("acc");
        var den = new Variable("den");
        var caught = new Variable("caught");
        return new("TryCatch_UnwindThenReuseLocals", [
            new("seed", new Block(
                new Assignment(acc, Wrap(0)),
                new Assignment(den, Wrap(0)),
                new Assignment(caught, Wrap(0)))),
            new("unwind, then reuse the locals", new TryCatchFinally(
                new Assignment(acc, new Divide(Wrap(10), den)),
                [new CatchClause(TypeReference.To<DivideByZeroException>(), null, new Block(
                    new Assignment(caught, Wrap(1)),
                    new Assignment(acc, Wrap(41))))],
                new Assignment(acc, new Add(acc, Wrap(1))))),
            new("fold the catch flag back in", new Assignment(acc, new Subtract(acc, caught))),
        ], [acc, den, caught], acc);
    }

    static AstParitySequence.SequenceCase ForLoop_AccumulateNestedArithmetic() {
        var acc = new Variable("acc");
        var i = new Variable("i");
        return new("ForLoop_AccumulateNestedArithmetic", [
            new("seed", new Assignment(acc, Wrap(0))),
            new("accumulate", new ForLoop(
                new Assignment(i, Wrap(0)),
                new LessThan(i, Wrap(5)),
                new Assignment(i, new Add(i, Wrap(1))),
                new Assignment(acc, new Add(acc, new Subtract(new Multiply(i, Wrap(3)), new Add(i, Wrap(1))))),
                DeclaredVariables: [i])),
        ], [acc], acc);
    }

    static AstParitySequence.SequenceCase WhileLoop_ShiftAndXorInterleaved() {
        var acc = new Variable("acc");
        var i = new Variable("i");
        return new("WhileLoop_ShiftAndXorInterleaved", [
            new("seed", new Block(
                new Assignment(acc, Wrap(1)),
                new Assignment(i, Wrap(0)))),
            new("shift and mix", new WhileLoop(
                new LessThan(i, Wrap(6)),
                new Block(
                    new Assignment(acc, new BitwiseXor(new ShiftLeft(acc, Wrap(1)), i)),
                    new Assignment(i, new Add(i, Wrap(1)))))),
        ], [acc, i], acc);
    }

    static AstParitySequence.SequenceCase Closure_CapturedLocalAcrossIterations() {
        var acc = new Variable("acc");
        var i = new Variable("i");
        var n = new Parameter("n", TypeReference.To<int>());
        return new("Closure_CapturedLocalAcrossIterations", [
            new("seed", new Assignment(acc, Wrap(0))),
            new("fold through the closure", new ForLoop(
                new Assignment(i, Wrap(1)),
                new LessThanOrEqual(i, Wrap(3)),
                new Assignment(i, new Add(i, Wrap(1))),
                new Assignment(acc, new Invoke(new Lambda([n], new Add(acc, n)), i)),
                DeclaredVariables: [i])),
        ], [acc], acc);
    }

    static AstParitySequence.SequenceCase String_BuiltInsideLoop() {
        var s = new Variable("s");
        var i = new Variable("i");
        return new("String_BuiltInsideLoop", [
            new("seed", new Assignment(s, Wrap(""))),
            new("append", new ForLoop(
                new Assignment(i, Wrap(0)),
                new LessThan(i, Wrap(4)),
                new Assignment(i, new Add(i, Wrap(1))),
                new Assignment(s, new Add(s, new Add(Wrap("x"), i))),
                DeclaredVariables: [i])),
        ], [s], s);
    }

    static AstParitySequence.SequenceCase MixedNumeric_PromotionAcrossAccumulatorOrder() {
        var acc = new Variable("acc");
        var a = new Variable("a");
        var b = new Variable("b");
        var d = new Variable("d");
        return new("MixedNumeric_PromotionAcrossAccumulatorOrder", [
            new("seed", new Block(
                new Assignment(acc, Wrap(0.0)),
                new Assignment(a, Wrap(100L)),
                new Assignment(b, Wrap(3)),
                new Assignment(d, Wrap(2.5)))),
            new("long and int product, then double", new Assignment(acc, new Add(new Multiply(a, b), d))),
            new("double first, then product", new Assignment(acc, new Subtract(acc, new Multiply(new Add(a, d), b)))),
        ], [acc, a, b, d], acc);
    }

    static AstParitySequence.SequenceCase IntOverflow_OrderChangesResult() {
        var acc = new Variable("acc");
        var max = new Variable("max");
        var one = new Variable("one");
        var two = new Variable("two");
        return new("IntOverflow_OrderChangesResult", [
            new("seed", new Block(
                new Assignment(acc, Wrap(0)),
                new Assignment(max, Wrap(int.MaxValue)),
                new Assignment(one, Wrap(1)),
                new Assignment(two, Wrap(2)))),
            new("widen before dividing", new Assignment(acc, new Divide(new Add(max, two), Wrap(2)))),
            new("divide before widening", new Assignment(acc, new Subtract(acc, new Add(new Divide(max, Wrap(2)), one)))),
        ], [acc, max, one, two], acc);
    }

    static AstParitySequence.SequenceCase IntDivision_TruncatesTowardZero() {
        var acc = new Variable("acc");
        var a = new Variable("a");
        var b = new Variable("b");
        return new("IntDivision_TruncatesTowardZero", [
            new("seed", new Block(
                new Assignment(acc, Wrap(0)),
                new Assignment(a, Wrap(7)),
                new Assignment(b, Wrap(2)))),
            new("seven halves", new Assignment(acc, new Divide(a, b))),
        ], [acc, a, b], acc);
    }

    static AstParitySequence.SequenceCase IntDivision_NegativeOperandsTruncateTowardZero() {
        var acc = new Variable("acc");
        var a = new Variable("a");
        var b = new Variable("b");
        return new("IntDivision_NegativeOperandsTruncateTowardZero", [
            new("seed", new Block(
                new Assignment(acc, Wrap(0)),
                new Assignment(a, Wrap(-7)),
                new Assignment(b, Wrap(2)))),
            new("negative seven halves", new Assignment(acc, new Divide(a, b))),
        ], [acc, a, b], acc);
    }
}