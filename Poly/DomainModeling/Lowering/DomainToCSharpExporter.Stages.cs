using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

using AccessModifier = Poly.Introspection.AccessModifier;
using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;
using TypeKind = Poly.Introspection.TypeKind;

namespace Poly.DomainModeling.Lowering;

// Stages: the stage enum, CurrentStage membership, and the automatic-transition loop guard.
public sealed partial class DomainToCSharpExporter {
    /// <summary>
    /// Enumeration type with a static const string field per member, defaulted to the member name.
    /// </summary>
    internal static TypeDefinitionNode BuildEnumTypeDef(string name, IEnumerable<string> memberNames) {
        var fields = memberNames.Select(memberName => new FieldDefinitionNode(
            memberName,
            new PrimitiveTypeReference(PrimType.String),
            DefaultValue: new Constant(memberName),
            IsStatic: true,
            IsConst: true,
            AccessModifier: AccessModifier.Public
        )).ToList();
        return new TypeDefinitionNode(
            name,
            Fields: fields,
            Kind: TypeKind.Enum,
            Semantics: Syntactic.TypeDefinitionSemantics.MutableReference
        );
    }

    /// <summary>
    /// Each stage entered at most once per trigger (action or subscription); a second
    /// entry throws instead of recursing.
    /// </summary>
    private static void AddAutomaticTransitionLoopGuard(
        Entity entity, List<FieldDefinitionNode> fields, List<MethodDefinitionNode> methods) {
        if (!EffectHelpers.HasAutomaticTransitions(entity))
            return;
        fields.Add(new FieldDefinitionNode(
            "_automaticStageChain",
            new OptionalTypeReference(
                new NamedTypeReference(
                    "HashSet",
                    TypeArguments: [new PrimitiveTypeReference(PrimType.String)])),
            AccessModifier: AccessModifier.Private));
        methods.Add(new MethodDefinitionNode(
            "NoteAutomaticStage",
            new TypeReference("void"),
            Parameters: [new Parameter("stageName", new PrimitiveTypeReference(PrimType.String))],
            Body: new Block([
                new IfStatement(
                    new Equal(
                        new Invoke(new Member(new Member(new ThisReference(), "CurrentStage"), "ToString")),
                        new Parameter("stageName")),
                    new Return()),
                new IfStatement(
                    new Equal(
                        new Member(new ThisReference(), "_automaticStageChain"),
                        new Constant(null!)),
                    new Assignment(
                        new Member(new ThisReference(), "_automaticStageChain"),
                        new New(new NamedTypeReference(
                            "HashSet",
                            TypeArguments: [new PrimitiveTypeReference(PrimType.String)])))),
                new IfStatement(
                    new Syntactic.Not(new Invoke(
                        new Member(
                            new Member(new ThisReference(), "_automaticStageChain"),
                            "Add"),
                        new Parameter("stageName"))),
                    new ThrowStatement(new New(
                        new NamedTypeReference("System.InvalidOperationException"),
                        new Invoke(
                            new Member(new PrimitiveTypeReference(PrimType.String), "Concat"),
                            new Constant($"Automatic stage transition loop on entity '{entity.Name}': stage '"),
                            new Parameter("stageName"),
                            new Constant("' was already entered in this chain.")))))
            ]),
            AccessModifier: AccessModifier.Private));
        methods.Add(new MethodDefinitionNode(
            "ClearAutomaticStageChain",
            new TypeReference("void"),
            Body: new Block([
                new Assignment(
                    new Member(new ThisReference(), "_automaticStageChain"),
                    new Constant(null!))]),
            AccessModifier: AccessModifier.Private));
    }
}