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
    public async Task PopCount_IsRejected_UntilQualifiedStaticCallsResolve() {
        // PopCount's only faithful spelling is System.Numerics.BitOperations.PopCount((ulong)x).
        // The reader roots that chain in a Variable, so a round trip loses the kind and cannot
        // execute. The grammar must not carry a bespoke pattern for one library intrinsic;
        // reject until IS1c gives the surface a real qualified-type receiver.
        await Assert.That(() => CSharpLanguage.Write(new PopCount(new Constant(7L))))
            .Throws<InvalidOperationException>()
            .WithMessageContaining(nameof(PopCount));
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