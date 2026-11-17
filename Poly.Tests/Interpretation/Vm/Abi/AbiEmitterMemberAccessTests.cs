using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm.Abi;
using Poly.Introspection;

namespace Poly.Tests.Interpretation.Vm.Abi;

/// <summary>
/// The dictionary-backed member read/write policy belongs to the VM emitter, not the
/// Introspection member (#942 I3). These pin the behaviour the moved policy carries:
/// coercion, declared defaults, the missing-key zero, and the static const field.
/// </summary>
public class AbiEmitterMemberAccessTests {
    [Test]
    public async Task AstProperty_EmitRead_CoercesIntToLong() {
        // When a domain property is Number (→ long CLR type) but the
        // dictionary stores an int, the emitted read coerces it to long.
        var typeNode = new TypeDefinitionNode(
            "Item", "Sample",
            Properties: [new PropertyDefinitionNode("Count", new PrimitiveTypeReference(PrimitiveType.Int64))]);

        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeNode);

        var prop = ctx.TypeDefinitions.GetTypeDefinition("Sample.Item")!.Properties.Single();
        var dict = new Dictionary<string, object?> { ["Count"] = 42 };
        var readExpr = AbiEmitter.EmitMemberRead(prop, Expression.Constant(dict));
        var lambda = Expression.Lambda<Func<object>>(readExpr!);
        var result = lambda.Compile()();

        await Assert.That(result).IsTypeOf<long>();
        await Assert.That((long)result!).IsEqualTo(42L);
    }

    [Test]
    public async Task AstProperty_EmitRead_UsesDefaultValueWhenKeyMissing() {
        var typeNode = new TypeDefinitionNode(
            "Item", "Sample",
            Properties: [new PropertyDefinitionNode("Count", new PrimitiveTypeReference(PrimitiveType.Int64),
                DefaultValue: new Constant(10L))]);

        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeNode);

        var prop = ctx.TypeDefinitions.GetTypeDefinition("Sample.Item")!.Properties.Single();
        var dict = new Dictionary<string, object?>(); // empty — no "Count" key
        var readExpr = AbiEmitter.EmitMemberRead(prop, Expression.Constant(dict));
        var lambda = Expression.Lambda<Func<object>>(readExpr!);
        var result = lambda.Compile()();

        await Assert.That(result).IsTypeOf<long>();
        await Assert.That((long)result!).IsEqualTo(10L);
    }

    [Test]
    public async Task AstProperty_EmitRead_MissingKeyWithoutDefault_ReadsZero() {
        var typeNode = new TypeDefinitionNode(
            "Item", "Sample",
            Properties: [new PropertyDefinitionNode("Count", new PrimitiveTypeReference(PrimitiveType.Int64))]);

        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeNode);

        var prop = ctx.TypeDefinitions.GetTypeDefinition("Sample.Item")!.Properties.Single();
        var dict = new Dictionary<string, object?>();
        var readExpr = AbiEmitter.EmitMemberRead(prop, Expression.Constant(dict));
        var lambda = Expression.Lambda<Func<object>>(readExpr!);
        var result = lambda.Compile()();

        await Assert.That(result).IsEqualTo(0L);
    }

    [Test]
    public async Task AstField_EmitRead_WritesAndReadsValue() {
        var typeNode = new TypeDefinitionNode(
            "Item", "Sample",
            Fields: [new FieldDefinitionNode("Tag", new PrimitiveTypeReference(PrimitiveType.Int64))]);

        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeNode);

        var field = ctx.TypeDefinitions.GetTypeDefinition("Sample.Item")!.Fields.Single();
        var dict = new Dictionary<string, object?> { ["Tag"] = 99L };
        var readExpr = AbiEmitter.EmitMemberRead(field, Expression.Constant(dict));
        var lambda = Expression.Lambda<Func<object>>(readExpr!);
        var result = lambda.Compile()();

        await Assert.That(result).IsTypeOf<long>();
        await Assert.That((long)result!).IsEqualTo(99L);
    }

    [Test]
    public async Task AstField_EmitWrite_ModifiesDictionary() {
        var typeNode = new TypeDefinitionNode(
            "Item", "Sample",
            Fields: [new FieldDefinitionNode("Tag", new PrimitiveTypeReference(PrimitiveType.Int64))]);

        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeNode);

        var field = ctx.TypeDefinitions.GetTypeDefinition("Sample.Item")!.Fields.Single();
        var dict = new Dictionary<string, object?>();
        var valueConst = Expression.Convert(Expression.Constant(77L), typeof(object));
        var writeExpr = AbiEmitter.EmitMemberWrite(field, Expression.Constant(dict), valueConst);
        var lambda = Expression.Lambda<Action>(writeExpr!);
        lambda.Compile()();

        await Assert.That(dict["Tag"]).IsEqualTo(77L);
    }

    [Test]
    public async Task AstField_EmitRead_CoercesIntToLong() {
        var typeNode = new TypeDefinitionNode(
            "Item", "Sample",
            Fields: [new FieldDefinitionNode("Score", new PrimitiveTypeReference(PrimitiveType.Int64))]);

        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeNode);

        var field = ctx.TypeDefinitions.GetTypeDefinition("Sample.Item")!.Fields.Single();
        var dict = new Dictionary<string, object?> { ["Score"] = 7 };
        var readExpr = AbiEmitter.EmitMemberRead(field, Expression.Constant(dict));
        var lambda = Expression.Lambda<Func<object>>(readExpr!);
        var result = lambda.Compile()();

        await Assert.That(result).IsTypeOf<long>();
        await Assert.That((long)result!).IsEqualTo(7L);
    }

    [Test]
    public async Task ClrDictionaryKeys_ReadsTheClrProperty() {
        // A CLR definition of the bag's own interface is not dictionary-backed storage.
        var definition = Poly.Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared
            .GetTypeDefinition(typeof(IDictionary<string, object>));
        var keys = definition.Properties.Single(property => property.Name == "Keys");
        var dict = new Dictionary<string, object> { ["a"] = 1L };
        var readExpr = AbiEmitter.EmitMemberRead(keys, Expression.Constant(dict, typeof(object)));
        var lambda = Expression.Lambda<Func<object>>(readExpr!);
        var result = lambda.Compile()();

        await Assert.That(((ICollection<string>)result).Single()).IsEqualTo("a");
    }

    [Test]
    public async Task AstField_EmitRead_StaticConstEnumMember_ReturnsNameWithoutInstance() {
        var typeNode = new TypeDefinitionNode(
            "PatronStatus",
            Fields: [
                new FieldDefinitionNode(
                    "Active",
                    new PrimitiveTypeReference(PrimitiveType.String),
                    DefaultValue: new Constant("Active"),
                    IsStatic: true,
                    IsConst: true)
            ],
            Kind: TypeKind.Enum);

        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeNode);

        var field = ctx.TypeDefinitions.GetTypeDefinition("PatronStatus")!.Fields.Single();
        var readExpr = AbiEmitter.EmitMemberRead(field, null);
        var lambda = Expression.Lambda<Func<object>>(readExpr!);
        var result = lambda.Compile()();

        await Assert.That(result).IsEqualTo("Active");
        await Assert.That(field.IsStatic).IsTrue();
        await Assert.That(field.Mutability.HasFlag(Mutability.CompileTimeConst)).IsTrue();
        await Assert.That(ctx.TypeDefinitions.GetTypeDefinition("PatronStatus")!.Kind)
            .IsEqualTo(TypeKind.Enum);
    }
}