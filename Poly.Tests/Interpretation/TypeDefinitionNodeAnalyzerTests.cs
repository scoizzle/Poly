using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

public class TypeDefinitionNodeAnalyzerTests {
    [Test]
    public async Task Analyze_TypeDefinitionNode_MapsAstMembersAndRelationships() {
        var baseType = new TypeDefinitionNode("Base", "Sample");
        var interfaceType = new TypeDefinitionNode("IWidget", "Sample");
        var subjectType = new TypeDefinitionNode(
            "Widget",
            "Sample",
            Properties: [
                new PropertyDefinitionNode(
                    "Item",
                    new PrimitiveTypeReference(PrimitiveType.Int32),
                    IndexParameters: [new Parameter("index", new PrimitiveTypeReference(PrimitiveType.Int32))])
            ],
            Methods: [
                new MethodDefinitionNode(
                    "Format",
                    new PrimitiveTypeReference(PrimitiveType.String),
                    Parameters: [
                        new Parameter("value", new PrimitiveTypeReference(PrimitiveType.String)),
                        new Parameter("count", new PrimitiveTypeReference(PrimitiveType.Int32), new Constant(3))
                    ])
            ],
            BaseType: new NamedTypeReference("Base", "Sample"),
            Interfaces: [new NamedTypeReference("IWidget", "Sample")],
            GenericParameters: [new Parameter("T")]);

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .Build()
            .Analyze(new Block([baseType, interfaceType, subjectType]));
        var resolvedType = analysis.GetMetadata<TypeDefinitionMetadata>(subjectType)?.TypeDefinition;

        await Assert.That(resolvedType).IsNotNull();
        await Assert.That(resolvedType!.BaseType?.FullName).IsEqualTo("Sample.Base");
        await Assert.That(resolvedType.Interfaces.Select(static type => type.FullName).ToArray()).IsEquivalentTo(new[] { "Sample.IWidget" });

        var genericParameter = resolvedType.GenericParameters.Single();
        await Assert.That(genericParameter.Name).IsEqualTo("T");
        await Assert.That(genericParameter.Position).IsEqualTo(0);
        await Assert.That(genericParameter.IsOptional).IsFalse();

        var indexer = resolvedType.Properties.Single();
        var indexParameter = indexer.Parameters!.Single();
        await Assert.That(indexParameter.Name).IsEqualTo("index");
        await Assert.That(indexParameter.Position).IsEqualTo(0);
        await Assert.That(indexParameter.ParameterTypeDefinition.GetRuntimeType()).IsEqualTo(typeof(int));

        var method = resolvedType.Methods.Single();
        var methodParameters = method.Parameters.ToArray();
        await Assert.That(methodParameters.Length).IsEqualTo(2);
        await Assert.That(methodParameters[0].Name).IsEqualTo("value");
        await Assert.That(methodParameters[0].ParameterTypeDefinition.GetRuntimeType()).IsEqualTo(typeof(string));
        await Assert.That(methodParameters[1].Name).IsEqualTo("count");
        await Assert.That(methodParameters[1].IsOptional).IsTrue();
        await Assert.That(methodParameters[1].DefaultValue).IsEqualTo(3);
        await Assert.That(methodParameters[1].ParameterTypeDefinition.GetRuntimeType()).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task Analyze_GenericTypeParameter_ResolvesAsMemberType() {
        var box = new TypeDefinitionNode(
            "Box",
            "Sample",
            Properties: [
                new PropertyDefinitionNode("Value", new NamedTypeReference("T"))
            ],
            Methods: [
                new MethodDefinitionNode(
                    "Wrap",
                    new NamedTypeReference("Box", TypeArguments: [new NamedTypeReference("T")]),
                    Parameters: [new Parameter("value", new NamedTypeReference("T"))],
                    IsStatic: true)
            ],
            GenericParameters: [new Parameter("T")]);

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .Build()
            .Analyze(box);
        var resolvedType = analysis.GetMetadata<TypeDefinitionMetadata>(box)?.TypeDefinition;

        await Assert.That(resolvedType).IsNotNull();
        var valueType = resolvedType!.Properties.Single().MemberTypeDefinition;
        await Assert.That(valueType.Name).IsEqualTo("T");
        await Assert.That(valueType.GetRuntimeType()).IsEqualTo(typeof(object));

        var wrap = resolvedType.Methods.Single();
        await Assert.That(wrap.Parameters.Single().ParameterTypeDefinition.Name).IsEqualTo("T");
        await Assert.That(wrap.MemberTypeDefinition.Name).IsEqualTo("Box");
    }

    [Test]
    public async Task Analyze_NamedTypeReference_WithTypeArguments_ClosesCollectionOfAstType() {
        var widget = new TypeDefinitionNode("Widget", "Sample");
        var holder = new TypeDefinitionNode(
            "Holder",
            "Sample",
            Properties: [
                new PropertyDefinitionNode(
                    "Items",
                    new NamedTypeReference("IEnumerable", TypeArguments: [new NamedTypeReference("Widget")]))
            ]);

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .Build()
            .Analyze(new Block([widget, holder]));
        var resolvedHolder = analysis.GetMetadata<TypeDefinitionMetadata>(holder)?.TypeDefinition;
        var resolvedWidget = analysis.GetMetadata<TypeDefinitionMetadata>(widget)?.TypeDefinition;

        await Assert.That(resolvedHolder).IsNotNull();
        await Assert.That(resolvedWidget).IsNotNull();
        var itemsType = resolvedHolder!.Properties.Single().MemberTypeDefinition;
        await Assert.That(itemsType.TypeCategory.IsCollection).IsTrue();
        await Assert.That(itemsType.GetElementType()).IsEqualTo(resolvedWidget);
    }

    [Test]
    public async Task Analyze_NamedTypeReference_ShortName_ResolvesNamespacedPeer() {
        var orderState = new TypeDefinitionNode("OrderState", "Hotel");
        var stay = new TypeDefinitionNode(
            "Stay",
            Properties: [
                new PropertyDefinitionNode("State", new NamedTypeReference("OrderState"))
            ]);

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .Build()
            .Analyze(new Block([stay, orderState]));
        var resolvedStay = analysis.GetMetadata<TypeDefinitionMetadata>(stay)?.TypeDefinition;

        await Assert.That(resolvedStay).IsNotNull();
        var stateType = resolvedStay!.Properties.Single().MemberTypeDefinition;
        await Assert.That(stateType.FullName).IsEqualTo("Hotel.OrderState");
    }

    [Test]
    public async Task Analyze_UnknownNamedType_MemberResolve_Throws() {
        var holder = new TypeDefinitionNode(
            "Holder",
            Properties: [
                new PropertyDefinitionNode("Missing", new NamedTypeReference("DoesNotExist"))
            ]);

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .Build()
            .Analyze(holder);
        var resolved = analysis.GetMetadata<TypeDefinitionMetadata>(holder)?.TypeDefinition;

        await Assert.That(resolved).IsNotNull();
        await Assert.That(() => resolved!.Properties.Single().MemberTypeDefinition)
            .Throws<InvalidOperationException>()
            .WithMessageContaining("DoesNotExist");
    }

    [Test]
    public async Task Analyze_TypeDefinitionNode_WithUnionProperty_ResolvesToExpectedRuntimeType() {
        var sameTypeUnion = new TypeDefinitionNode(
            "SameTypeUnionHolder",
            "Sample",
            Properties: [
                new PropertyDefinitionNode(
                    "Value",
                    new UnionTypeReference([
                        new PrimitiveTypeReference(PrimitiveType.Int32),
                        new PrimitiveTypeReference(PrimitiveType.Int32)
                    ]))
            ]);

        var mixedTypeUnion = new TypeDefinitionNode(
            "MixedTypeUnionHolder",
            "Sample",
            Properties: [
                new PropertyDefinitionNode(
                    "Value",
                    new UnionTypeReference([
                        new PrimitiveTypeReference(PrimitiveType.Int32),
                        new PrimitiveTypeReference(PrimitiveType.String)
                    ]))
            ]);

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .Build()
            .Analyze(new Block([sameTypeUnion, mixedTypeUnion]));

        var sameTypeDefinition = analysis.GetMetadata<TypeDefinitionMetadata>(sameTypeUnion)?.TypeDefinition;
        var mixedTypeDefinition = analysis.GetMetadata<TypeDefinitionMetadata>(mixedTypeUnion)?.TypeDefinition;

        await Assert.That(sameTypeDefinition).IsNotNull();
        await Assert.That(mixedTypeDefinition).IsNotNull();

        var samePropertyType = sameTypeDefinition!.Properties.Single().MemberTypeDefinition.GetRuntimeType();
        var mixedPropertyType = mixedTypeDefinition!.Properties.Single().MemberTypeDefinition.GetRuntimeType();

        await Assert.That(samePropertyType).IsEqualTo(typeof(int));
        await Assert.That(mixedPropertyType).IsEqualTo(typeof(object));
    }

    [Test]
    public async Task Analyze_DerivedType_InheritsMembersMostDerivedFirst() {
        var baseType = new TypeDefinitionNode(
            "BaseWidget",
            "Sample",
            Properties: [
                new PropertyDefinitionNode("BaseOnly", new PrimitiveTypeReference(PrimitiveType.String)),
                new PropertyDefinitionNode("Shared", new PrimitiveTypeReference(PrimitiveType.String))
            ],
            Methods: [
                new MethodDefinitionNode("BaseMethod", new PrimitiveTypeReference(PrimitiveType.String)),
                new MethodDefinitionNode("SharedMethod", new PrimitiveTypeReference(PrimitiveType.String))
            ],
            Fields: [
                new FieldDefinitionNode("BaseField", new PrimitiveTypeReference(PrimitiveType.String)),
                new FieldDefinitionNode("SharedField", new PrimitiveTypeReference(PrimitiveType.String))
            ]);
        var subjectType = new TypeDefinitionNode(
            "Widget",
            "Sample",
            Properties: [
                new PropertyDefinitionNode("Shared", new PrimitiveTypeReference(PrimitiveType.String))
            ],
            Methods: [
                new MethodDefinitionNode("SharedMethod", new PrimitiveTypeReference(PrimitiveType.String))
            ],
            Fields: [
                new FieldDefinitionNode("SharedField", new PrimitiveTypeReference(PrimitiveType.String))
            ],
            BaseType: new NamedTypeReference("BaseWidget", "Sample"));

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .Build()
            .Analyze(new Block([baseType, subjectType]));
        var resolvedType = analysis.GetMetadata<TypeDefinitionMetadata>(subjectType)?.TypeDefinition;

        await Assert.That(resolvedType).IsNotNull();
        await Assert.That(resolvedType!.Properties.WithName("BaseOnly").Single().DeclaringTypeDefinition.FullName).IsEqualTo("Sample.BaseWidget");
        await Assert.That(resolvedType.Methods.WithName("BaseMethod").Single().DeclaringTypeDefinition.FullName).IsEqualTo("Sample.BaseWidget");
        await Assert.That(resolvedType.Fields.WithName("BaseField").Single().DeclaringTypeDefinition.FullName).IsEqualTo("Sample.BaseWidget");

        await Assert.That(resolvedType.Properties.WithName("Shared").Single().DeclaringTypeDefinition.FullName).IsEqualTo("Sample.Widget");
        await Assert.That(resolvedType.Methods.WithName("SharedMethod").Single().DeclaringTypeDefinition.FullName).IsEqualTo("Sample.Widget");
        await Assert.That(resolvedType.Fields.WithName("SharedField").Single().DeclaringTypeDefinition.FullName).IsEqualTo("Sample.Widget");
    }

    [Test]
    public async Task Analyze_RecordWithPrimaryConstructor_SynthesizesConstructorAndProperties() {
        var subjectType = new TypeDefinitionNode(
            "WidgetCreated",
            "Sample",
            PrimaryConstructorParameters: [
                new Parameter("Name", new PrimitiveTypeReference(PrimitiveType.String)),
                new Parameter("Version", new PrimitiveTypeReference(PrimitiveType.Int32))
            ],
            Semantics: TypeDefinitionSemantics.ImmutableValue);

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .Build()
            .Analyze(subjectType);
        var resolvedType = analysis.GetMetadata<TypeDefinitionMetadata>(subjectType)?.TypeDefinition;

        await Assert.That(resolvedType).IsNotNull();
        await Assert.That(resolvedType!.Constructors.Single().Parameters.Select(static p => p.Name).ToArray())
            .IsEquivalentTo(new[] { "Name", "Version" });
        await Assert.That(resolvedType.Properties.Select(static property => property.Name).ToArray())
            .IsEquivalentTo(new[] { "Name", "Version" });
    }

    // ── Phase 2: coercion, defaults, field read/write ─────────────

    [Test]
    public async Task AstProperty_EmitRead_CoercesIntToLong() {
        // When a domain property is Number (→ long CLR type) but the
        // dictionary stores an int, EmitRead should coerce it to long.
        var typeNode = new TypeDefinitionNode(
            "Item", "Sample",
            Properties: [new PropertyDefinitionNode("Count", new PrimitiveTypeReference(PrimitiveType.Int64))]);

        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeNode);

        var prop = analyzer.GetTypeDefinition("Sample.Item")!.Properties.Single();
        var dict = new Dictionary<string, object?> { ["Count"] = 42 };
        var readExpr = prop.EmitRead(Expression.Constant(dict));
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

        var prop = analyzer.GetTypeDefinition("Sample.Item")!.Properties.Single();
        var dict = new Dictionary<string, object?>(); // empty — no "Count" key
        var readExpr = prop.EmitRead(Expression.Constant(dict));
        var lambda = Expression.Lambda<Func<object>>(readExpr!);
        var result = lambda.Compile()();

        await Assert.That(result).IsTypeOf<long>();
        await Assert.That((long)result!).IsEqualTo(10L);
    }

    [Test]
    public async Task AstProperty_EmitRead_ReturnsMissingValueWhenNoDefaultAndKeyMissing() {
        var typeNode = new TypeDefinitionNode(
            "Item", "Sample",
            Properties: [new PropertyDefinitionNode("Count", new PrimitiveTypeReference(PrimitiveType.Int64))]);

        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeNode);

        var prop = analyzer.GetTypeDefinition("Sample.Item")!.Properties.Single();
        var dict = new Dictionary<string, object?>();
        var readExpr = prop.EmitRead(Expression.Constant(dict));
        var lambda = Expression.Lambda<Func<object>>(readExpr!);
        var result = lambda.Compile()();

        await Assert.That(result).IsEqualTo(System.Reflection.Missing.Value);
    }

    [Test]
    public async Task AstField_EmitRead_WritesAndReadsValue() {
        var typeNode = new TypeDefinitionNode(
            "Item", "Sample",
            Fields: [new FieldDefinitionNode("Tag", new PrimitiveTypeReference(PrimitiveType.Int64))]);

        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeNode);

        var field = analyzer.GetTypeDefinition("Sample.Item")!.Fields.Single();
        var dict = new Dictionary<string, object?> { ["Tag"] = 99L };
        var readExpr = field.EmitRead(Expression.Constant(dict));
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

        var field = analyzer.GetTypeDefinition("Sample.Item")!.Fields.Single();
        var dict = new Dictionary<string, object?>();
        var valueConst = Expression.Convert(Expression.Constant(77L), typeof(object));
        var writeExpr = field.EmitWrite(Expression.Constant(dict), valueConst);
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

        var field = analyzer.GetTypeDefinition("Sample.Item")!.Fields.Single();
        var dict = new Dictionary<string, object?> { ["Score"] = 7 };
        var readExpr = field.EmitRead(Expression.Constant(dict));
        var lambda = Expression.Lambda<Func<object>>(readExpr!);
        var result = lambda.Compile()();

        await Assert.That(result).IsTypeOf<long>();
        await Assert.That((long)result!).IsEqualTo(7L);
    }
}