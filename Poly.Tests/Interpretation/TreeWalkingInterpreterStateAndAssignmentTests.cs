using System.Collections.Generic;

using Poly.Interpretation.TreeWalking;
using Poly.Syntax;
using Poly.Syntax.Nodes;

namespace Poly.Tests.Interpretation;

public class TreeWalkingInterpreterStateAndAssignmentTests {
    private sealed class WritableObject {
        public int Number { get; set; }
    }

    private sealed class ReadOnlyObject {
        public int ReadOnlyValue { get; } = 42;
    }

    [Test]
    public async Task Evaluate_UsesInitialVariablesForVariableLookup() {
        var walker = new TreeWalkingInterpreter();
        var x = new Variable("x");
        var ast = new Block([x], [x]);  // explicit block var decl for scope validator

        var result = walker.Evaluate(ast, new Dictionary<string, object?> {
            ["x"] = 123
        });

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(123);
    }

    [Test]
    public async Task Evaluate_AssignmentOverridesSeededVariable() {
        var walker = new TreeWalkingInterpreter();
        var x = new Variable("x");
        var ast = new Block([
            new Assignment(x, new Constant(99)),
            x
        ], [x]);  // explicit decl for scope

        var result = walker.Evaluate(ast, new Dictionary<string, object?> {
            ["x"] = 10
        });

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(99);
    }

    [Test]
    public async Task Evaluate_ParameterDefaultIsUsedWhenMissingFromInitialVariables() {
        var walker = new TreeWalkingInterpreter();
        var ast = new Parameter("count", null, new Constant(44));

        var result = walker.Evaluate(ast);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(44);
    }

    [Test]
    public async Task Assignment_ToMember_WritesValue() {
        var walker = new TreeWalkingInterpreter();
        var owner = new WritableObject { Number = 1 };
        var ownerParameter = new Parameter("owner", TypeReference.To<WritableObject>());
        var ast = new Block([
            new Assignment(new Member(ownerParameter, nameof(WritableObject.Number)), new Constant(42)),
            new Member(ownerParameter, nameof(WritableObject.Number))
        ]);

        var result = walker.Evaluate(ast, new Dictionary<string, object?> {
            ["owner"] = owner
        });

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(42);
        await Assert.That(owner.Number).IsEqualTo(42);
    }

    [Test]
    public async Task Assignment_ToIndex_WritesValueForList() {
        var walker = new TreeWalkingInterpreter();
        var itemsParameter = new Parameter("items", TypeReference.To<List<int>>());
        var list = new List<int> { 1, 2, 3 };
        var ast = new Block([
            new Assignment(new IndexAccess(itemsParameter, new Constant(1)), new Constant(99)),
            new IndexAccess(itemsParameter, new Constant(1))
        ]);

        var result = walker.Evaluate(ast, new Dictionary<string, object?> {
            ["items"] = list
        });

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(99);
        await Assert.That(list[1]).IsEqualTo(99);
    }

    [Test]
    public async Task Assignment_ToMember_OnNullTarget_ThrowsInvalidOperationException() {
        var walker = new TreeWalkingInterpreter();
        var target = new Parameter("target", TypeReference.To<WritableObject>());
        var ast = new Assignment(new Member(target, nameof(WritableObject.Number)), new Constant(1));

        var ex = await Assert.That(() => walker.Evaluate(ast, new Dictionary<string, object?> {
            ["target"] = null
        })).Throws<InvalidOperationException>();

        await Assert.That(ex!.Message).Contains("null target");
    }

    [Test]
    public async Task Assignment_ToIndex_NullTarget_ThrowsInvalidOperationException() {
        var walker = new TreeWalkingInterpreter();
        var target = new Parameter("target", TypeReference.To<List<int>>());
        var ast = new Assignment(new IndexAccess(target, new Constant(0)), new Constant(1));

        var ex = await Assert.That(() => walker.Evaluate(ast, new Dictionary<string, object?> {
            ["target"] = null
        })).Throws<InvalidOperationException>();

        await Assert.That(ex!.Message).Contains("null target");
    }

    [Test]
    public async Task Assignment_ToNonWritableMember_ThrowsInvalidOperationException() {
        var walker = new TreeWalkingInterpreter();
        var owner = new ReadOnlyObject();
        var ownerParameter = new Parameter("owner", TypeReference.To<ReadOnlyObject>());
        var ast = new Assignment(new Member(ownerParameter, nameof(ReadOnlyObject.ReadOnlyValue)), new Constant(100));

        var ex = await Assert.That(() => walker.Evaluate(ast, new Dictionary<string, object?> {
            ["owner"] = owner
        })).Throws<InvalidOperationException>();

        await Assert.That(ex!.Message).Contains("not writable");
    }
}