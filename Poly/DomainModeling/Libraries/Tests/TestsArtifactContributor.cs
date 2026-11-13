using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Libraries.Tests;

/// <summary>
/// <c>uses tests</c>: one <c>test-tree</c> plus one <c>test-file</c> per entity, catalog-only
/// (never appended to Emit's file list).
/// <para>
/// The suite is a totality and regression suite, not a correctness suite. It reads the domain facts
/// (<see cref="Entity.Stages"/>, <see cref="Stage.Actions"/>) and binds the generated CLR the module
/// prints; it holds no stage-transition semantics of its own and evaluates nothing. Today it asserts
/// one fact per entity and one per refused action: <c>Create</c> with neutral arguments lands in the
/// initial stage, and an action no initial-stage declaration covers is refused there with a failure
/// naming the action. It never asserts that a declared action succeeds.
/// </para>
/// <para>
/// Output is a pure function of the domain: facts are read in declaration order, no clock, no random,
/// no ambient state. An entity whose Create or action arguments cannot be neutral-defaulted gets a
/// <see cref="TestUnsupported"/> outcome naming the member, never an empty file.
/// </para>
/// </summary>
public sealed class TestsArtifactContributor : IArtifactContributor {
    public IReadOnlyList<Artifact> Contribute(Domain domain, AnalysisResult analysis, ArtifactCatalog catalog) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(catalog);

        var entities = domain.Types.OfType<Entity>().ToList();
        if (entities.Count == 0)
            return [];

        catalog.DeclareType(TestTree.Type, mayPointAt: []);
        catalog.DeclareType(TestFile.Type, mayPointAt: [DomainSession.EntityType, TestTree.Type]);
        catalog.DeclareType(TestUnsupported.Type, mayPointAt: [DomainSession.EntityType]);

        var producer = nameof(TestsArtifactContributor);
        var artifacts = new List<Artifact>();
        foreach (var entity in entities) {
            var entityId = ArtifactId.Create([domain.Name, entity.Name], DomainSession.EntityType);
            if (Unsupported(entity) is { } reason) {
                artifacts.Add(TestUnsupported.Create(domain, entity.Name, reason, producer, [entityId]));
                continue;
            }
            var unit = BuildUnit(domain, entity);
            var tree = TestTree.Create(domain, entity.Name, unit, producer);
            artifacts.Add(tree);
            artifacts.Add(TestFile.Create(
                domain, entity.Name, new CSharpWriter().Write(unit), producer, [entityId, tree.Descriptor.Id]));
        }
        return artifacts;
    }

    private const string TypeText = "Text";
    private const string TypeNumber = "Number";

    /// <summary>Why no suite can be generated for <paramref name="entity"/>, or null when one can.</summary>
    private static string? Unsupported(Entity entity) {
        if (entity.Stages.Count == 0)
            return $"{entity.Name}: no stages, so there is no initial stage to assert.";
        if (entity.Navigations.Count > 0)
            return $"{entity.Name}.{entity.Navigations[0].Name}: navigations have no neutral Create argument.";
        foreach (var property in entity.Properties)
            if (NeutralArgument(entity.Name, property) is { Error: { } error })
                return error;
        foreach (var action in RefusedActions(entity))
            foreach (var parameter in action.Parameters)
                if (NeutralArgument($"{entity.Name}.{action.Name}", parameter) is { Error: { } error })
                    return error;
        return null;
    }

    /// <summary>
    /// A neutral literal for a property or parameter, or the reason it has none. Only unconstrained
    /// Text and Number have one: any constraint could refuse the literal, and the suite never predicts.
    /// </summary>
    private static (Node? Literal, string? Error) NeutralArgument(string owner, Property property) {
        if (property.Constraints.Count > 0)
            return (null, $"{owner}.{property.Name}: constrained properties have no neutral argument.");
        return property.Type.TypeName switch {
            TypeText => (new Constant("x"), null),
            TypeNumber => (new Constant(0L), null),
            var other => (null, $"{owner}.{property.Name}: type '{other}' has no neutral argument."),
        };
    }

    private static Node[] Arguments(string owner, IEnumerable<Property> properties) =>
        [.. properties.Select(p => NeutralArgument(owner, p).Literal!)];

    /// <summary>
    /// Actions declared on some later stage and not on the initial stage or the entity, in stage then
    /// declaration order, once per name. The initial stage is the designated one, else the first declared.
    /// </summary>
    private static List<Action> RefusedActions(Entity entity) {
        // Every stage but the initial one is "later". The filter below compares *references*, so two
        // stages that happen to hold equal values are still told apart — a record's value equality
        // would drop a later stage that mirrors the initial.
        var initial = entity.InitialStage()!;
        var covered = initial.Actions.Select(a => a.Name)
            .Concat(entity.Actions.Select(a => a.Name))
            .ToHashSet(StringComparer.Ordinal);
        var refused = new List<Action>();
        foreach (var action in entity.Stages.Where(s => !ReferenceEquals(s, initial)).SelectMany(s => s.Actions))
            if (covered.Add(action.Name))
                refused.Add(action);
        return refused;
    }

    private static CompilationUnitNode BuildUnit(Domain domain, Entity entity) {
        var initial = entity.InitialStage()!;
        var create = new Invoke(
            new Member(new TypeReference(DomainSession.QualifyForHost(domain, entity.Name)), "Create"),
            Arguments(entity.Name, entity.Properties));
        var fresh = new Member(create, "Value");

        var methods = new List<MethodDefinitionNode> {
            Test(
                "Create_WithNeutralArguments_StartsInInitialStage",
                Check(new Member(fresh, "CurrentStage"), "IsEqualTo",
                    new Member(new TypeReference($"{entity.Name}Stage"), initial.Name))),
        };
        foreach (var action in RefusedActions(entity)) {
            var call = new Invoke(
                new Member(fresh, action.Name),
                Arguments($"{entity.Name}.{action.Name}", action.Parameters));
            methods.Add(Test(
                $"{action.Name}_From{initial.Name}_IsRefused",
                Check(new Member(call, "IsSuccess"), "IsFalse"),
                Check(new Member(call, "ErrorMessage"), "Contains", new Constant($"'{action.Name}'"))));
        }

        return new CompilationUnitNode(
            Usings: ["System.Threading.Tasks", DomainSession.CSharpNamespace(domain)],
            Namespace: null,
            Types: [new TypeDefinitionNode($"{entity.Name}Tests", Methods: methods)],
            TopLevelStatements: null);
    }

    private static MethodDefinitionNode Test(string name, params Node[] statements) =>
        new(name, new TypeReference("Task"), Body: new Block(statements), IsAsync: true) {
            Attributes = [new AttributeNode("Test", [])],
        };

    private static Node Check(Node actual, string assertion, params Node[] arguments) =>
        new Await(new Invoke(
            new Member(new Invoke(new Member(new TypeReference("Assert"), "That"), actual), assertion),
            arguments));
}