using Microsoft.CodeAnalysis.CSharp;

using Poly.Interpretation.CSharp;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The writer fails closed: a node kind with no C# surface form throws, naming the
/// kind and its id, instead of printing the type name as source; and the kinds the
/// VM executes that have a real C# spelling round-trip as themselves.
/// </summary>
public class CSharpWriterFailClosedTests {
    [Test]
    public async Task UnknownKind_Throws_InsteadOfPrintingItsTypeName() {
        var node = new ParameterReference();
        var error = Assert.Throws<InvalidOperationException>(() => CSharpLanguage.Write(node));
        await Assert.That(error.Message).Contains(nameof(ParameterReference));
        await Assert.That(error.Message).Contains(node.Id.ToString());
    }

    [Test]
    public async Task StridedSetBits_IsRejected() {
        var node = new StridedSetBits(
            new Variable("bits"), new Constant(0L), new Constant(1L), new Constant(63L));
        var error = Assert.Throws<InvalidOperationException>(() => CSharpLanguage.Write(node));
        await Assert.That(error.Message).Contains(nameof(StridedSetBits));
        await Assert.That(error.Message).Contains(node.Id.ToString());
    }

    [Test]
    public async Task DeclaredNeverWritten_ReadLocal_FailsClosedInsteadOfPrintingObject() {
        // A local declared with no analysis type and no first assignment has no
        // faithful C# type; reading it would print `object` while the VM slot is a
        // long zero (#465). Refuse instead of printing the wrong shape.
        var v = new Variable("x");
        var block = new Block([v], [v]);
        var error = Assert.Throws<InvalidOperationException>(() => CSharpLanguage.Write(block));
        await Assert.That(error.Message).Contains(nameof(Variable)).Because(error.Message);
        await Assert.That(error.Message).Contains(v.Id.ToString()).Because(error.Message);
        await Assert.That(error.Message).Contains("no resolved type").Because(error.Message);
    }

    [Test]
    public async Task DeclaredNeverWritten_UnreadLocal_WriteReadIsIdempotent() {
        // An unread declared local is inert: its default initializer prints and the
        // printed source re-reads and re-prints identically.
        var v = new Variable("x");
        var block = new Block([new Constant(1L)], [v]);
        var printed = CSharpLanguage.Write(block);
        await Assert.That(printed).Contains("var x = default(object);");

        var source = $"class C {{ void M() {printed} }}";
        await Assert.That(SyntaxErrors(source)).IsEmpty();
        var written = CSharpLanguage.Write(CSharpLanguage.Read(source));
        var second = CSharpLanguage.Write(CSharpLanguage.Read(written));
        await Assert.That(second).IsEqualTo(written);
    }

    // The assignment sits in a nested block so the writer cannot fuse it into a
    // `var x = value;` declaration; the declaration then needs TypeNameForVariable,
    // and `1 + 2` is a value InferTypeNameFromValue returns null for (it handles
    // constants, new, new-array and casts only).
    private static Block NestedAssignment(Variable v) =>
        new([new Assignment(v, new Add(new Constant(1L), new Constant(2L)))]);

    [Test]
    public async Task DeclaredLocal_OnlyAssignedUninferableValue_PrintsBecauseDestinationIsNotARead() {
        var v = new Variable("x");
        var block = new Block([NestedAssignment(v)], [v]);

        var printed = CSharpLanguage.Write(block);

        await Assert.That(printed).Contains("var x = default(object);").Because(printed);
    }

    [Test]
    public async Task DeclaredLocal_AssignedUninferableThenRead_FailsClosed() {
        var v = new Variable("x");
        var block = new Block([NestedAssignment(v), v], [v]);

        var error = Assert.Throws<InvalidOperationException>(() => CSharpLanguage.Write(block));

        await Assert.That(error.Message).Contains("no resolved type").Because(error.Message);
        await Assert.That(error.Message).Contains(v.Id.ToString()).Because(error.Message);
    }

    [Test]
    public async Task NewArray_WriteRead_IsKindStable() {
        var printed = CSharpLanguage.Write(new NewArray(TypeReference.To<long>(), new Constant(4L)));
        await Assert.That(printed).IsEqualTo("new System.Int64[4L];");

        var source = $"class C {{ void M() {{ var a = {printed} }} }}";
        await Assert.That(SyntaxErrors(source)).IsEmpty();

        var unit = CSharpLanguage.Read(source);
        await Assert.That(Find<NewArray>(unit)).IsNotNull();
        await Assert.That(Find<TypeCast>(unit)).IsNull();
    }

    [Test]
    public async Task PopCount_PrintsItsOnlyFaithfulSpelling() {
        // System.Numerics.BitOperations.PopCount((ulong)x) is the kind's only faithful spelling.
        // The surface expresses it with a type receiver, so the printer emits the library call
        // with no grammar pattern keyed on the intrinsic.
        var printed = CSharpLanguage.Write(new PopCount(new Constant(7L)));
        await Assert.That(printed).IsEqualTo("_ = System.Numerics.BitOperations.PopCount((ulong)7L);");
        await Assert.That(SyntaxErrors($"class C {{ void M() {{ {printed} }} }}")).IsEmpty();
    }

    [Test]
    public async Task ForIncrement_StatementInsteadOfExpression_Rejects() {
        var loop = new ForLoop(new Constant(0), new Constant(true), new Block(), new Block());
        var error = Assert.Throws<InvalidOperationException>(() => CSharpLanguage.Write(loop));
        await Assert.That(error.Message).Contains(nameof(Block));
        await Assert.That(error.Message).Contains(loop.Increment!.Id.ToString());
    }

    /// <summary>Every kind with no writer arm reaches the same rejection.</summary>
    [Test]
    public async Task KindsWithoutWriterArm_Throw_InsteadOfPrintingTheirToString() {
        Node[] kinds = [
            new ParameterReference(),
            new AttributeNode("Obsolete", []),
            new PropertyGetterDefinitionNode(),
            new PropertySetterDefinitionNode(),
            new PropertyInitializerDefinitionNode(),
            new UnionTypeReference([TypeReference.To<long>()]),
            new ResolvedTypeReference(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(string))),
        ];
        foreach (var node in kinds) {
            var error = Assert.Throws<InvalidOperationException>(() => CSharpLanguage.Write(node));
            await Assert.That(error.Message).Contains(node.GetType().Name).Because(node.GetType().Name);
            await Assert.That(error.Message).Contains(node.Id.ToString()).Because(node.GetType().Name);
        }
    }

    private static IReadOnlyList<string> SyntaxErrors(string text) =>
        [.. CSharpSyntaxTree.ParseText(text)
            .GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => $"{d.Id} {d.GetMessage()}")];

    private static T? Find<T>(Node node) where T : Node {
        if (node is T match)
            return match;
        foreach (var child in node.Children) {
            if (child is null)
                continue;
            var found = Find<T>(child);
            if (found is not null)
                return found;
        }
        return null;
    }
}