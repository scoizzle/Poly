using Poly.DomainModeling.Ontology;

using AccessModifier = Poly.Introspection.AccessModifier;
using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// Notify dispatch: subscriber registration, registry fields, and cascade-depth guards.
public sealed partial class DomainToCSharpExporter {
    /// <summary>
    /// Appends subscription registration statements to the constructor body.
    /// For each subscription on this entity's stage, emits code that registers
    /// this instance as a subscriber on each related target entity.
    /// </summary>
    private static void AddSubscriberRegistrationNodes(
        List<SubscriptionInfo>? subscriberSubs,
        List<Node> bodyNodes) {
        if (subscriberSubs is { Count: > 0 }) {
            foreach (var group in subscriberSubs.GroupBy(s => s.Relationship.Name)) {
                var rel = group.First().Relationship;
                var pascalNavName = ToPascalCase(rel.Name);
                var isMany = rel.Cardinality is RelationshipCardinality.OneToMany
                             or RelationshipCardinality.ManyToMany;

                // Multiple subscriptions on the same relation+stage (any/all/Each) share
                // ONE registry list on the target — register once per stage, or the
                // subscriber is added N times and each handler fires N times.
                var perStage = group.GroupBy(s => s.StageName).Select(g => g.First());

                if (isMany) {
                    foreach (var info in perStage) {
                        var subVarName = "target";
                        bodyNodes.Add(new ForEachLoop(
                            new Variable(subVarName),
                            new Member(new ThisReference(), pascalNavName),
                            new Block([
                                new Invoke(
                                    new Member(
                                        new Variable(subVarName),
                                        SubscriberRegisterMethodName(info)),
                                    [new ThisReference()])
                            ])
                        ));
                    }
                }
                else {
                    // A singular navigation is optional (unlinked until a later link),
                    // so every per-stage registration shares ONE null guard: dereferencing
                    // this.{Nav} in the constructor would NRE when the target is absent.
                    var registrations = new List<Node>();
                    foreach (var info in perStage) {
                        registrations.Add(new Invoke(
                            new Member(
                                new Member(new ThisReference(), pascalNavName),
                                SubscriberRegisterMethodName(info)),
                            [new ThisReference()]));
                    }
                    bodyNodes.Add(new IfStatement(
                        new NotEqual(
                            new Member(new ThisReference(), pascalNavName),
                            new Constant(null!)),
                        new Block(registrations)));
                }
            }
        }
    }

    /// <summary>Printed notify-cascade bound. Nested calls past this throw instead of overflowing the stack.</summary>
    private const int MaxNotifyDepth = 16;

    /// <summary>
    /// Nested <c>Notify{Stage}Subscribers</c> calls past <see cref="MaxNotifyDepth"/>
    /// throw instead of overflowing the stack.
    /// </summary>
    private static void AddNotifyCascadeDepthGuard(
        Entity entity, List<FieldDefinitionNode> fields, List<MethodDefinitionNode> methods) {
        fields.Add(new FieldDefinitionNode(
            "_notifyDepth",
            new PrimitiveTypeReference(PrimType.Int32),
            AccessModifier: AccessModifier.Private));
        methods.Add(new MethodDefinitionNode(
            "EnterNotifyCascade",
            new TypeReference("void"),
            Parameters: [new Parameter("stageName", new PrimitiveTypeReference(PrimType.String))],
            Body: new Block([
                new IfStatement(
                    new GreaterThanOrEqual(
                        new Member(new ThisReference(), "_notifyDepth"),
                        new Constant(MaxNotifyDepth)),
                    new ThrowStatement(new New(
                        new NamedTypeReference("System.InvalidOperationException"),
                        new Invoke(
                            new Member(new PrimitiveTypeReference(PrimType.String), "Concat"),
                            new Constant($"Subscription cascade on entity '{entity.Name}' exceeded max depth ({MaxNotifyDepth}) while notifying '"),
                            new Parameter("stageName"),
                            new Constant("'."))))),
                new Assignment(
                    new Member(new ThisReference(), "_notifyDepth"),
                    new Syntactic.Add(
                        new Member(new ThisReference(), "_notifyDepth"),
                        new Constant(1)))
            ]),
            AccessModifier: AccessModifier.Private));
        methods.Add(new MethodDefinitionNode(
            "ExitNotifyCascade",
            new TypeReference("void"),
            Body: new Block([
                new Assignment(
                    new Member(new ThisReference(), "_notifyDepth"),
                    new Syntactic.Subtract(
                        new Member(new ThisReference(), "_notifyDepth"),
                        new Constant(1)))
            ]),
            AccessModifier: AccessModifier.Private));
    }
}