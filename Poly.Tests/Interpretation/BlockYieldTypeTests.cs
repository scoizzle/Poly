using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>
/// A block used as a value yields its tail expression's type. When that tail is a
/// variable declared in an enclosing block but first assigned inside this one, the
/// assignment is the only type source and must resolve before the block's yield.
/// </summary>
public class BlockYieldTypeTests {
    private static Node Program(Variable iter, Variable iterTail, Variable total, Variable totalTail) {
        Node pixel = new Block([
            new Assignment(iter, new Constant(0L)),
            new Assignment(iter, new Add(iter, new Constant(1L))),
            iterTail
        ]);
        return new Block([
            new Assignment(total, new Constant(0L)),
            new Assignment(total, new Add(total, pixel)),
            totalTail
        ], [iter, total]);
    }

    [Test]
    public async Task BlockTail_CapturedVariableAssignedInside_ResolvesToItsType() {
        var iter = new Variable("iter");
        var total = new Variable("total");
        Node node = Program(iter, iter, total, total);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task BlockTail_AlreadyTypedDeclaration_IsNotOverwritten() {
        var x = new Variable("x");
        var xWrite = new Variable("x");
        var xTail = new Variable("x");
        Node inner = new Block([
            new Assignment(xWrite, new Constant(2L)),
            xTail
        ]);
        Node node = new Block([
            new Assignment(x, new Constant(1.5)),
            inner,
            x
        ], [x]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        await Assert.That(exec.GetValue<double>()).IsEqualTo(2.0);
    }

    [Test]
    public async Task BlockTail_AssignmentNestedInStatement_ResolvesToItsType() {
        var iter = new Variable("iter");
        var total = new Variable("total");
        Node pixel = new Block([
            new IfStatement(new Constant(true), new Block([
                new Assignment(new Variable("iter"), new Constant(1L))
            ])),
            new Variable("iter")
        ]);
        Node node = new Block([
            new Assignment(total, new Constant(0L)),
            new Assignment(new Variable("total"), new Add(new Variable("total"), pixel)),
            new Variable("total")
        ], [iter, total]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task BlockTail_BoundUse_ResolvesThroughItsDeclaration() {
        var iter = new Variable("iter");
        var total = new Variable("total");
        var iterTail = new Variable("iter");
        var totalTail = new Variable("total");
        var iterUse = new Variable("iter");
        var totalUse = new Variable("total");
        Node pixel = new Block([
            new Assignment(iterUse, new Constant(0L)),
            new Assignment(new Variable("iter"), new Add(new Variable("iter"), new Constant(1L))),
            iterTail
        ]);
        Node node = new Block([
            new Assignment(total, new Constant(0L)),
            new Assignment(totalUse, new Add(totalUse, pixel)),
            totalTail
        ], [iter, total]);

        using var exec = Interpreter.Execute(Interpreter.Compile(node));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }
}