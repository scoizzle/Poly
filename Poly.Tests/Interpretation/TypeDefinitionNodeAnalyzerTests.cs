using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

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
                    TypeReference.To(typeof(int)),
                    IndexParameters: [new Parameter("index", TypeReference.To(typeof(int)))])
            ],
            Methods: [
                new MethodDefinitionNode(
                    "Format",
                    TypeReference.To(typeof(string)),
                    Parameters: [
                        new Parameter("value", TypeReference.To(typeof(string))),
                        new Parameter("count", TypeReference.To(typeof(int)), new Constant(3))
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

        var method = resolvedType.Methods.WithName("Format").Single();
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

        var wrap = resolvedType.Methods.WithName("Wrap").Single();
        await Assert.That(wrap.Parameters.Single().ParameterTypeDefinition.Name).IsEqualTo("T");
        await Assert.That(wrap.MemberTypeDefinition.Name).IsEqualTo("Box");
    }

    [Test]
    public async Task Analyze_ClosedGenericStaticMember_SubstitutesTypeArgument() {
        var widget = new TypeDefinitionNode("Widget", "Sample");
        var box = new TypeDefinitionNode(
            "Box",
            "Sample",
            Methods: [
                new MethodDefinitionNode(
                    "Wrap",
                    new NamedTypeReference("Box", TypeArguments: [new NamedTypeReference("T")]),
                    Parameters: [new Parameter("value", new NamedTypeReference("T"))],
                    IsStatic: true)
            ],
            GenericParameters: [new Parameter("T")]);
        var holder = new TypeDefinitionNode(
            "Holder",
            "Sample",
            Properties: [
                new PropertyDefinitionNode(
                    "Closed",
                    new NamedTypeReference("Box", TypeArguments: [new NamedTypeReference("Widget", "Sample")]))
            ]);

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .Build()
            .Analyze(new Block([widget, box, holder]));
        var closed = analysis.GetMetadata<TypeDefinitionMetadata>(holder)?.TypeDefinition?
            .Properties.Single().MemberTypeDefinition;

        await Assert.That(closed).IsNotNull();
        var wrap = closed!.Methods.WithName("Wrap").Single();
        await Assert.That(wrap.Parameters.Single().ParameterTypeDefinition.Name).IsEqualTo("Widget");
        await Assert.That(wrap.Parameters.Single().ParameterTypeDefinition.FullName).IsEqualTo("Sample.Widget");
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
        await Assert.That(itemsType.GetElementType(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared)).IsEqualTo(resolvedWidget);
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
                        TypeReference.To(typeof(int)),
                        TypeReference.To(typeof(int))
                    ]))
            ]);

        var mixedTypeUnion = new TypeDefinitionNode(
            "MixedTypeUnionHolder",
            "Sample",
            Properties: [
                new PropertyDefinitionNode(
                    "Value",
                    new UnionTypeReference([
                        TypeReference.To(typeof(int)),
                        TypeReference.To(typeof(string))
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
                new PropertyDefinitionNode("BaseOnly", TypeReference.To(typeof(string))),
                new PropertyDefinitionNode("Shared", TypeReference.To(typeof(string)))
            ],
            Methods: [
                new MethodDefinitionNode("BaseMethod", TypeReference.To(typeof(string))),
                new MethodDefinitionNode("SharedMethod", TypeReference.To(typeof(string)))
            ],
            Fields: [
                new FieldDefinitionNode("BaseField", TypeReference.To(typeof(string))),
                new FieldDefinitionNode("SharedField", TypeReference.To(typeof(string)))
            ]);
        var subjectType = new TypeDefinitionNode(
            "Widget",
            "Sample",
            Properties: [
                new PropertyDefinitionNode("Shared", TypeReference.To(typeof(string)))
            ],
            Methods: [
                new MethodDefinitionNode("SharedMethod", TypeReference.To(typeof(string)))
            ],
            Fields: [
                new FieldDefinitionNode("SharedField", TypeReference.To(typeof(string)))
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
                new Parameter("Name", TypeReference.To(typeof(string))),
                new Parameter("Version", TypeReference.To(typeof(int)))
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

    [Test]
    public async Task Analyze_OptionalAndMapPropertyTypes_Resolve() {
        var typeNode = new TypeDefinitionNode(
            "Bag",
            "Sample",
            Properties: [
                new PropertyDefinitionNode(
                    "Maybe",
                    new OptionalTypeReference(TypeReference.To(typeof(int)))),
                new PropertyDefinitionNode(
                    "Dict",
                    new MapTypeReference(
                        TypeReference.To(typeof(string)),
                        TypeReference.To(typeof(int)))),
            ]);
        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new TypeDefinitionNodeAnalyzer())
            .Build()
            .Analyze(typeNode);
        var td = analysis.GetMetadata<TypeDefinitionMetadata>(typeNode)?.TypeDefinition;
        await Assert.That(td).IsNotNull();
        var maybe = td!.Properties.Single(p => p.Name == "Maybe");
        var dict = td.Properties.Single(p => p.Name == "Dict");
        await Assert.That(maybe.MemberTypeDefinition.GetRuntimeType()).IsEqualTo(typeof(int?));
        await Assert.That(dict.MemberTypeDefinition.GetRuntimeType())
            .IsEqualTo(typeof(Dictionary<string, int>));
    }

    [Test]
    public async Task Analyze_DomainResultAndDomainResultOfT_BothResolveByNameAndArity() {
        var plain = new TypeDefinitionNode(
            "DomainResult",
            Methods: [
                new MethodDefinitionNode(
                    "Success",
                    new NamedTypeReference("DomainResult"),
                    IsStatic: true)
            ]);
        var generic = new TypeDefinitionNode(
            "DomainResult",
            Methods: [
                new MethodDefinitionNode(
                    "Success",
                    new NamedTypeReference("DomainResult", TypeArguments: [new NamedTypeReference("T")]),
                    Parameters: [new Parameter("value", new NamedTypeReference("T"))],
                    IsStatic: true)
            ],
            GenericParameters: [new Parameter("T")]);
        var holder = new TypeDefinitionNode(
            "Holder",
            Properties: [
                new PropertyDefinitionNode("Plain", new NamedTypeReference("DomainResult")),
                new PropertyDefinitionNode(
                    "Closed",
                    new NamedTypeReference("DomainResult", TypeArguments: [TypeReference.To(typeof(string))]))
            ]);

        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, new Block([plain, generic, holder]));

        var plainDef = ctx.TypeSystem.Resolve("DomainResult");
        var genericDef = ctx.TypeSystem.Resolve(AstTypeRegistry.Key("DomainResult", 1));
        await Assert.That(plainDef).IsNotNull();
        await Assert.That(genericDef).IsNotNull();
        await Assert.That(plainDef).IsNotSameReferenceAs(genericDef);
        await Assert.That(plainDef!.GenericParameters.Any()).IsFalse();
        await Assert.That(genericDef!.GenericParameters.Count()).IsEqualTo(1);
        await Assert.That(plainDef.Methods.WithName("Success").Single().Parameters.Any()).IsFalse();
        await Assert.That(genericDef.Methods.WithName("Success").Single().Parameters.Count()).IsEqualTo(1);

        var resolvedHolder = ctx.GetMetadata<TypeDefinitionMetadata>(holder)?.TypeDefinition;
        await Assert.That(resolvedHolder).IsNotNull();
        var plainProp = resolvedHolder!.Properties.Single(p => p.Name == "Plain").MemberTypeDefinition;
        var closedProp = resolvedHolder.Properties.Single(p => p.Name == "Closed").MemberTypeDefinition;
        await Assert.That(plainProp).IsSameReferenceAs(plainDef);
        // The open definition is unchanged: its Success parameter is still the T placeholder.
        await Assert.That(genericDef.Methods.WithName("Success").Single().Parameters.Single().ParameterTypeDefinition.Name).IsEqualTo("T");
        // The closed view substitutes T; it is a distinct resolution product, not the open definition.
        await Assert.That(closedProp).IsNotSameReferenceAs(genericDef);
        await Assert.That(closedProp.Methods.WithName("Success").Single().Parameters.Single().ParameterTypeDefinition.GetRuntimeType()).IsEqualTo(typeof(string));
    }
}