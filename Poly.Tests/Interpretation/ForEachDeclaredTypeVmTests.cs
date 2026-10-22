using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>
/// A declared <c>foreach</c> element type converts each item: the loop variable takes the
/// declared type and the element is converted per item (slot-space for scalars). These run
/// at the AST level because the console surface cannot yet populate an array — array index
/// reads and writes are broken (Object[] casts to Int64[]), so a console snippet can only
/// iterate a zero-filled array.
/// </summary>
public class ForEachDeclaredTypeVmTests {
    [Test]
    public async Task ForEachDeclaredDouble_OverLongArray_ConvertsEachElement() {
        // Divide by an integer constant so the loop variable's type decides: as doubles
        // 1/2 + 3/2 = 0.5 + 1.5 = 2.0; if the declared type were ignored (long) it is 0 + 1 = 1.
        var d = new Variable("d");
        var sum = new Variable("sum");
        var node = new Block([
            new Assignment(sum, new Constant(0.0)),
            new ForEachLoop(d, new Constant(new long[] { 1L, 3L }),
                new Assignment(sum, new Add(sum, new Divide(d, new Constant(2L)))),
                DeclaredType: TypeReference.To<double>()),
            sum
        ], [sum]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<double>()).IsEqualTo(2.0);
    }

    [Test]
    public async Task ForEachDeclaredInt_OverLongArray_FailsClosed() {
        var d = new Variable("d");
        var sum = new Variable("sum");
        var node = new Block([
            new Assignment(sum, new Constant(0L)),
            new ForEachLoop(d, new Constant(new long[] { 1L, 3L }),
                new Assignment(sum, new Add(sum, new Divide(d, new Constant(2L)))),
                DeclaredType: TypeReference.To<int>()),
            sum
        ], [sum]);
        // C# has no implicit long-to-int conversion for the loop variable.
        await Assert.That(() => Interpreter.Compile(node)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ForEachDeclaredType_NotConvertible_FailsClosed() {
        var d = new Variable("d");
        var node = new Block([
            new ForEachLoop(d, new Constant(new long[] { 1L }), new Assignment(d, d),
                DeclaredType: TypeReference.To<string>())
        ]);
        await Assert.That(() => Interpreter.Compile(node)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ForEachDeclaredDecimal_FailsClosed() {
        // A decimal is a heap handle, not a slot scalar: converting an element into it needs
        // box-per-item, which the emitter does not do, so the pair fails closed.
        var d = new Variable("d");
        var node = new Block([
            new ForEachLoop(d, new Constant(new long[] { 1L }), new Assignment(d, d),
                DeclaredType: TypeReference.To<decimal>())
        ]);
        await Assert.That(() => Interpreter.Compile(node)).Throws<InvalidOperationException>();
    }
}