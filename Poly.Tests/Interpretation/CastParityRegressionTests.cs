using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>
/// #374 regressions found by the seeded generator: a cast of a compound operand lost its brackets in the
/// printed C#, and a widening cast under a conditional branch returned the double's raw bits in the VM.
/// Each repro runs through the Roslyn differential, so a regression prints interpreter vs printed value.
/// </summary>
public class CastParityRegressionTests {
    static Block Program(IReadOnlyList<(Variable Var, object Value)> locals, Node body) {
        List<Node> statements = [.. locals.Select(l => (Node)new Assignment(l.Var, new Constant(l.Value))), body];
        return new Block(statements, [.. locals.Select(l => l.Var)]);
    }

    [Test]
    public async Task Cast_OfCompoundOperand_PrintsBracketsAndAgreesWithTheVm() {
        // seeds 374 #67, #88, #91: Cast<Long>(Add(Add(i2,i1),Mul(i1,i2))) printed '(System.Int64)i2 + i1 + i1 * i2'.
        var i1 = new Variable("i1");
        var i2 = new Variable("i2");
        var body = new TypeCast(new Add(new Add(i2, i1), new Multiply(i1, i2)), TypeReference.To<long>());

        var row = AstParity.Compare("cast-compound", Program([(i1, int.MaxValue), (i2, 2)], body));

        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.Source!).Contains("(System.Int64)(");
    }

    [Test]
    public async Task Cast_OfEveryLowerPrecedenceOperand_IsBracketed() {
        var i1 = new Variable("i1");
        var b1 = new Variable("b1");
        var operands = new Node[] {
            new Add(i1, i1), new Subtract(i1, i1), new Multiply(i1, i1), new Divide(i1, i1),
            new Conditional(b1, i1, i1),
        };
        foreach (var operand in operands) {
            var row = AstParity.Compare("cast-" + operand.GetType().Name,
                Program([(i1, 3), (b1, true)], new TypeCast(operand, TypeReference.To<long>())));
            await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
            await Assert.That(row.Source!).Contains("(System.Int64)(");
        }
    }

    [Test]
    public async Task Conditional_WithWideningCastBranch_YieldsTheValueNotTheBits() {
        // seeds 374 #58, #97: Conditional(b, d, Cast<Double>(i)) returned the double's raw bit pattern.
        var b1 = new Variable("b1");
        var d1 = new Variable("d1");
        var i1 = new Variable("i1");
        var body = new Conditional(b1, d1, new TypeCast(i1, TypeReference.To<double>()));

        var row = AstParity.Compare("cond-cast",
            Program([(b1, false), (d1, 1.5), (i1, 3)], body));

        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("double:3");
    }

    [Test]
    public async Task Coalesce_NullableDoubleLeft_WithWideningCastRight_YieldsTheValueNotTheBits() {
        // A nullable-double left takes the Nullable<> coalesce arm; the cast right arm is an IEEE word.
        var n = new Parameter("n", TypeReference.To<double?>());
        var i = new Parameter("i", TypeReference.To<int>());
        var lambda = new Lambda([n, i], new Coalesce(n, new TypeCast(i, TypeReference.To<double>())));

        var row = AstParity.Compare("coalesce-cast-right", lambda, null, 3);

        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("double:3");
    }

    [Test]
    public async Task Cast_OfNullConstantToNullableDouble_AssignedToLocal_StaysNull() {
        var n = new Variable("n");
        var body = new Block([new Assignment(n, new TypeCast(new Constant(null), TypeReference.To<double?>())), n], [n]);
        var row = AstParity.Compare("null-cast-local", body);
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("null");
    }

    [Test]
    public async Task Cast_OfNullConstantToNullableDouble_Coalesced_TakesTheRightArm() {
        var n = new Variable("n");
        var body = new Block([new Assignment(n, new TypeCast(new Constant(null), TypeReference.To<double?>())), new Coalesce(n, new Constant(2.5))], [n]);
        var row = AstParity.Compare("null-cast-coalesce", body);
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("double:2.5");
    }

    [Test]
    public async Task Cast_OfNullConstantToNullableDouble_CoalescedDirectly_TakesTheRightArm() {
        var body = new Coalesce(new TypeCast(new Constant(null), TypeReference.To<double?>()), new Constant(2.5));
        var row = AstParity.Compare("null-cast-direct-coalesce", body);
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("double:2.5");
    }

    [Test]
    public async Task Cast_OfNullConstantToNullableInt_EqualsNull() {
        var n = new Variable("n");
        var body = new Block([new Assignment(n, new TypeCast(new Constant(null), TypeReference.To<int?>())), new Equal(n, new Constant(null))], [n]);
        var row = AstParity.Compare("null-cast-int-eq-null", body);
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("bool:true");
    }

    [Test]
    public async Task Cast_OfNullConstantToNullableInt_Coalesced_TakesTheRightArm() {
        var n = new Variable("n");
        var body = new Block([new Assignment(n, new TypeCast(new Constant(null), TypeReference.To<int?>())), new Coalesce(n, new Constant(7))], [n]);
        var row = AstParity.Compare("null-cast-int-coalesce", body);
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
    }

    [Test]
    public async Task Cast_OfValueToNullableDouble_AssignedToLocal_StaysPresent() {
        var n = new Variable("n");
        var body = new Block([new Assignment(n, new TypeCast(new Constant(0.0), TypeReference.To<double?>())), new Coalesce(n, new Constant(2.5))], [n]);
        var row = AstParity.Compare("value-cast-present", body);
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("double:0");
    }

    public sealed class NullHost {
        public object? Nothing() => null;
        public object? Something() => 0.0;
    }

    [Test]
    public async Task Cast_OfObjectParameterHoldingNull_ToNullableDouble_Coalesced_TakesTheRightArm() {
        var o = new Parameter("o", TypeReference.To<object>());
        var lambda = new Lambda([o], new Coalesce(new TypeCast(o, TypeReference.To<double?>()), new Constant(2.5)));
        var row = AstParity.Compare("nonconst-null-cast-param", lambda, new object?[] { null });
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("double:2.5");
    }

    [Test]
    public async Task Cast_OfObjectParameterHoldingNull_ToNullableDouble_AssignedToLocal_StaysNull() {
        var o = new Parameter("o", TypeReference.To<object>());
        var n = new Variable("n");
        var lambda = new Lambda([o], new Block([new Assignment(n, new TypeCast(o, TypeReference.To<double?>())), n], [n]));
        var row = AstParity.Compare("nonconst-null-cast-param-local", lambda, new object?[] { null });
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("null");
    }

    [Test]
    public async Task Cast_OfObjectParameterHoldingNull_ToNullableInt_EqualsNull() {
        var o = new Parameter("o", TypeReference.To<object>());
        var n = new Variable("n");
        var lambda = new Lambda([o], new Block([new Assignment(n, new TypeCast(o, TypeReference.To<int?>())), new Equal(n, new Constant(null))], [n]));
        var row = AstParity.Compare("nonconst-null-cast-int-eq", lambda, new object?[] { null });
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("bool:true");
    }

    [Test]
    public async Task Cast_OfConditionalOfNullObjects_ToNullableDouble_Coalesced_TakesTheRightArm() {
        var o = new Parameter("o", TypeReference.To<object>());
        var b = new Parameter("b", TypeReference.To<bool>());
        var lambda = new Lambda([o, b],
            new Coalesce(new TypeCast(new Conditional(b, o, o), TypeReference.To<double?>()), new Constant(2.5)));
        var row = AstParity.Compare("nonconst-null-cast-cond", lambda, null, true);
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("double:2.5");
    }

    [Test]
    public async Task Cast_OfNullReturningCall_ToNullableDouble_Coalesced_TakesTheRightArm() {
        var h = new Parameter("h", TypeReference.To<NullHost>());
        var lambda = new Lambda([h],
            new Coalesce(new TypeCast(new Invoke(new Member(h, "Nothing")), TypeReference.To<double?>()), new Constant(2.5)));
        var row = AstParity.Compare("nonconst-null-cast-call", lambda, new NullHost());
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("double:2.5");
    }

    [Test]
    public async Task Cast_OfNullReturningCall_ToNullableDouble_AssignedToLocal_StaysNull() {
        var h = new Parameter("h", TypeReference.To<NullHost>());
        var n = new Variable("n");
        var lambda = new Lambda([h], new Block([new Assignment(n, new TypeCast(new Invoke(new Member(h, "Nothing")), TypeReference.To<double?>())), n], [n]));
        var row = AstParity.Compare("nonconst-null-cast-call-local", lambda, new NullHost());
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("null");
    }

    [Test]
    public async Task Cast_OfBoxedZeroDoubleCall_ToNullableDouble_StaysPresent() {
        var h = new Parameter("h", TypeReference.To<NullHost>());
        var lambda = new Lambda([h],
            new Coalesce(new TypeCast(new Invoke(new Member(h, "Something")), TypeReference.To<double?>()), new Constant(2.5)));
        var row = AstParity.Compare("nonconst-value-cast-call", lambda, new NullHost());
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("double:0");
    }

    [Test]
    public async Task CoalesceAcrossSuspend_NullableDoubleLeft_WithWideningCastRight_YieldsTheValueNotTheBits() {
        var n = new Parameter("n", TypeReference.To<double?>());
        var node = new Coalesce(
            n,
            new Block([
                new SuspendNode(new Constant(0L), "mid"),
                new TypeCast(new Constant(3), TypeReference.To<double>()),
            ]));
        var program = Interpreter.Compile(node);

        using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { null }));
        await Assert.That(exec.IsSuspended).IsTrue();
        using var resumed = exec.Resume();
        await Assert.That(resumed.Result.GetValue<double>()).IsEqualTo(3.0);
    }

    [Test]
    public async Task CoalesceAcrossSuspend_NullableDoubleLeft_WithWideningCastRight_LeftPresentSkipsRight() {
        var n = new Parameter("n", TypeReference.To<double?>());
        var node = new Coalesce(
            n,
            new Block([
                new SuspendNode(new Constant(0L), "mid"),
                new TypeCast(new Constant(3), TypeReference.To<double>()),
            ]));
        var program = Interpreter.Compile(node);

        using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { 1.5 }));
        await Assert.That(exec.IsSuspended).IsFalse();
        await Assert.That(exec.Result.GetValue<double>()).IsEqualTo(1.5);
    }

    [Test]
    public async Task SeedFour_Index28_PrintedCSharp_AgreesWithTheVm() {
        // Formerly an unminimised exclusion: the VM read a double conditional arm as raw bits
        // (-9.2e18 vs -2.5). DecodeIeeeWordArm fixed it; the shape is still emitted, so it stays covered.
        // The generator has since drifted: no (seed, count, depth) reaches the recorded program, so it is
        // rebuilt by hand: Add(Sub(Cond(Not(b1),Cond(b1,d1,d1),Neg(d1)),C(2.5)),Cond(Lt(..),d1,Cond(..))), d1=5E-324.
        foreach (var b in new[] { true, false })
            foreach (var rhs in new[] { 1.0, double.Epsilon }) {
                var b1 = new Variable("b1");
                var d1 = new Variable("d1");
                var body = new Add(
                    new Subtract(
                        new Conditional(new Not(b1), new Conditional(b1, d1, d1), new UnaryMinus(d1)),
                        new Constant(2.5)),
                    new Conditional(new LessThan(d1, new Constant(rhs)), d1, new Conditional(b1, d1, d1)));
                var row = AstParity.Compare($"seed4-28-b{b}-rhs{rhs}",
                    Program([(b1, b), (d1, double.Epsilon)], body));
                await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
                await Assert.That(row.Vm).IsEqualTo(ParityDisposal.Executed);
            }
    }

    [Test]
    public async Task SeedFour_FirstHundredFifty_PrintedCSharp_AgreesWithTheVm() {
        var disagreements = AstShapeGenerator.Generate(4, 150)
            .Select(c => AstParity.Compare(c.Label, c.Program))
            .Where(r => !r.Agrees)
            .Select(r => r.Describe())
            .ToList();
        await Assert.That(disagreements).IsEmpty().Because(string.Join("\n\n", disagreements.Take(3)));
    }

    [Test]
    public async Task NullAssignedWithoutCast_ToNullableDouble_IsNull() {
        var n = new Parameter("n", TypeReference.To<double?>());
        var lambda = new Lambda([n], new Coalesce(n, new Constant(2.5)));
        var row = AstParity.Compare("null-param-coalesce", lambda, new object?[] { null });
        await Assert.That(row.Agrees).IsTrue().Because(row.Describe());
        await Assert.That(row.VmOutcome).IsEqualTo("double:2.5");
    }
}