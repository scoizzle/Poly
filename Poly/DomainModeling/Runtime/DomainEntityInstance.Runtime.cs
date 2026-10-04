using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.Analysis.Semantics;

using Action = Poly.DomainModeling.Ontology.Action;
using Prim = Poly.Introspection.PrimitiveType;

namespace Poly.DomainModeling.Runtime;

public sealed partial record DomainEntityInstance {
    /// <summary>
    /// Returns all property names, values, and the current stage for debugging.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Snapshot() {
        if (!_values.ContainsKey(CurrentStageBagKey))
            return _values.AsReadOnly();
        var schemaHasStage = Entity.Properties.Any(p =>
            string.Equals(p.Name, CurrentStageBagKey, StringComparison.Ordinal));
        if (schemaHasStage)
            return _values.AsReadOnly();
        var copy = new Dictionary<string, object?>(_values, StringComparer.Ordinal);
        copy.Remove(CurrentStageBagKey);
        return copy;
    }

    // ── Private helpers ─────────────────────────────────────────

    /// <summary>
    /// Standalone (<see cref="Domain"/> null) action resolve: structural stage then
    /// entity actions with SA fallthrough (empty stage-copy → entity action).
    /// Parameters are ignored in the empty-copy predicate (same as catalog path).
    /// </summary>
    private Action? ResolveStandaloneAction(string actionName) {
        Action? stageAction = null;
        if (CurrentStage is not null) {
            var currentStageRef = Entity.Stages
                .FirstOrDefault(s => string.Equals(s.Name, CurrentStage, StringComparison.Ordinal));
            stageAction = currentStageRef?.Actions
                .FirstOrDefault(a => string.Equals(a.Name, actionName, StringComparison.Ordinal));
        }

        var entityAction = Entity.Actions
            .FirstOrDefault(a => string.Equals(a.Name, actionName, StringComparison.Ordinal));

        if (stageAction is not null
            && stageAction.Effects.Count == 0
            && stageAction.Policies.Count == 0
            && entityAction is not null)
            return entityAction;

        return stageAction ?? entityAction;
    }

    /// <summary>
    /// Builds a dictionary-backed type definition for <paramref name="entityName"/>
    /// with the given schema properties (and optional extra action parameters).
    /// </summary>
    private static TypeDefinitionNodeAnalyzer BuildTypeDefAnalyzer(
        Entity entity,
        IEnumerable<Property>? extraProperties = null,
        Domain? domain = null) {
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, BuildTypeDefNode(entity, extraProperties, domain));
        if (domain is not null) {
            foreach (var other in domain.Types.OfType<Entity>()) {
                if (string.Equals(other.Name, entity.Name, StringComparison.Ordinal))
                    continue;
                analyzer.Analyze(ctx, BuildTypeDefNode(other, extraProperties: null, domain));
            }
        }
        return analyzer;
    }

    private static TypeDefinitionNode BuildTypeDefNode(
        Entity entity,
        IEnumerable<Property>? extraProperties,
        Domain? domain) {
        var propDefs = new List<PropertyDefinitionNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddProps(IEnumerable<Property> source) {
            foreach (var ep in source) {
                if (!seen.Add(ep.Name))
                    continue;
                var typeRef = MapDomainTypeToAstNode(ep.Type, domain);
                propDefs.Add(new PropertyDefinitionNode(ep.Name, typeRef,
                    Getter: new PropertyGetterDefinitionNode()));
            }
        }

        AddProps(entity.Properties);
        if (extraProperties is not null)
            AddProps(extraProperties);
        if (seen.Add("CurrentStage")) {
            propDefs.Add(new PropertyDefinitionNode(
                "CurrentStage",
                new PrimitiveTypeReference(Prim.String),
                Getter: new PropertyGetterDefinitionNode()));
        }

        foreach (var nav in NavigationsFor(entity, domain)) {
            var pascal = DomainToCSharpExporter.ToPascalCase(nav.Name);
            if (!seen.Add(pascal))
                continue;
            if (nav.Cardinality is RelationshipCardinality.OneToOne) {
                propDefs.Add(new PropertyDefinitionNode(
                    pascal,
                    new TypeReference(nav.Target.TypeName),
                    DefaultValue: new Constant(null!),
                    Getter: new PropertyGetterDefinitionNode()));
            }
            else if (nav.Cardinality is RelationshipCardinality.OneToMany
                or RelationshipCardinality.ManyToMany) {
                propDefs.Add(new PropertyDefinitionNode(
                    pascal,
                    new CollectionTypeReference(new TypeReference(nav.Target.TypeName)),
                    Getter: new PropertyGetterDefinitionNode()));
            }
        }

        var methods = new List<MethodDefinitionNode> {
            new MethodDefinitionNode(
                "Notify",
                new TypeReference("void"),
                Parameters: [new Parameter("stageName",
                    new PrimitiveTypeReference(Prim.String))],
                Body: new Block([])),
            new MethodDefinitionNode(
                "Notify",
                new TypeReference("void"),
                Parameters: [
                    new Parameter("stageName", new PrimitiveTypeReference(Prim.String)),
                    new Parameter("previousStageName", new PrimitiveTypeReference(Prim.String))
                ],
                Body: new Block([])),
            new MethodDefinitionNode(
                "EnsureUnique",
                TypeReference.To<DomainResult>(),
                Parameters: [
                    new Parameter("propertyName", TypeReference.To<string>()),
                    new Parameter("value", TypeReference.To<object>())
                ],
                Body: new Block([]))
        };
        // Empty bodies: analysis resolves Member(entity, action/policy) as ITypeMethod.
        // VM does not inline them; InvokeNamed / generated C# owns the implementation.
        var methodNames = new HashSet<string>(StringComparer.Ordinal) {
            "Notify", "EnsureUnique",
            "ExistsRelated", "GetRelatedOne", "LinkRelated"
        };
        // Printed trees call Notify{Stage}Subscribers(previousStage); InvokeNamed
        // dispatches those names to Notify(stage, previousStage).
        foreach (var stage in entity.Stages) {
            var notifySubscribers = $"Notify{stage.Name}Subscribers";
            if (!methodNames.Add(notifySubscribers))
                continue;
            methods.Add(new MethodDefinitionNode(
                notifySubscribers,
                new TypeReference("void"),
                Parameters: [new Parameter("previousStage",
                    new PrimitiveTypeReference(Prim.String))],
                Body: new Block([])));
        }
        // Runtime factories for mixed if+create. Dictionary slot plus pair
        // overloads so Invoke types as DomainResult (IsSuccess resolves).
        // Generated C# may still call Stay.Create inside the factory that binds this job.
        var str = TypeReference.To<string>();
        var i64 = TypeReference.To<long>();
        var boolean = TypeReference.To<bool>();
        var obj = TypeReference.To<object>();
        Node[] valueTypes = [i64, str, boolean, obj];
        foreach (var vt in new Node[] { i64, str, boolean }) {
            methods.Add(new MethodDefinitionNode(
                "EnsureUnique",
                TypeReference.To<DomainResult>(),
                Parameters: [
                    new Parameter("propertyName", str),
                    new Parameter("value", vt)
                ],
                Body: new Block([])));
        }
        methods.Add(new MethodDefinitionNode(
            "LinkRelated",
            new TypeReference("void"),
            Parameters: [
                new Parameter("relationshipName", str),
                new Parameter("target", obj)
            ],
            Body: new Block([])));
        methods.Add(new MethodDefinitionNode(
            "ExistsRelated",
            boolean,
            Parameters: [new Parameter("relationshipName", str)],
            Body: new Block([])));
        methods.Add(new MethodDefinitionNode(
            "GetRelatedOne",
            obj,
            Parameters: [new Parameter("relationshipName", str)],
            Body: new Block([])));
        foreach (var factory in new[] { "Create", "CreateIn", "ProbeCreate" }) {
            methods.Add(new MethodDefinitionNode(
                factory,
                TypeReference.To<DomainResult>(),
                Parameters: [
                    new Parameter("name", str),
                    new Parameter("values", TypeReference.To<Dictionary<string, object?>>())
                ],
                Body: new Block([])));
            methodNames.Add(factory);
            methods.Add(new MethodDefinitionNode(
                factory,
                TypeReference.To<DomainResult>(),
                Parameters: [new Parameter("name", str)],
                Body: new Block([])));
            for (var t = 0; t < valueTypes.Length; t++) {
                methods.Add(new MethodDefinitionNode(
                    factory,
                    TypeReference.To<DomainResult>(),
                    Parameters: [
                        new Parameter("name", str),
                        new Parameter("p0", str),
                        new Parameter("v0", valueTypes[t])
                    ],
                    Body: new Block([])));
            }
            for (var pairs = 2; pairs <= 16; pairs++) {
                var ps = new List<Parameter> { new Parameter("name", str) };
                for (var i = 0; i < pairs; i++) {
                    ps.Add(new Parameter($"p{i}", str));
                    ps.Add(new Parameter($"v{i}", obj));
                }
                methods.Add(new MethodDefinitionNode(
                    factory,
                    TypeReference.To<DomainResult>(),
                    Parameters: ps,
                    Body: new Block([])));
            }
        }
        foreach (var action in EnumerateTypeDefActions(entity)) {
            if (!methodNames.Add(action.Name))
                continue;
            methods.Add(new MethodDefinitionNode(
                action.Name,
                TypeReference.To<DomainResult>(),
                Parameters: [.. action.Parameters.Select(p =>
                    new Parameter(p.Name, MapDomainTypeToAstNode(p.Type, domain)))],
                Body: new Block([])));
        }
        foreach (var policy in EnumerateTypeDefPolicies(entity)) {
            if (!methodNames.Add(policy.Name))
                continue;
            methods.Add(new MethodDefinitionNode(
                policy.Name,
                new PrimitiveTypeReference(Prim.Boolean),
                Body: new Block([])));
        }

        return new TypeDefinitionNode(
            Name: entity.Name,
            Properties: [.. propDefs],
            Methods: [.. methods],
            Namespace: null);
    }

    /// <summary>
    /// Type provider that includes entity properties plus the current action's
    /// parameters so bag-injected args resolve as members during effect compile.
    /// </summary>
    private TypeDefinitionNodeAnalyzer BuildActionScopedTypeDefAnalyzer(
        Action action) =>
        BuildTypeDefAnalyzer(Entity, action.Parameters, Domain);

    /// <summary>
    /// Source-entity navigations. Prefer the domain's copy of the entity
    /// (tests often pass a pre-redistribution entity plus a Domain that
    /// already owns the navs).
    /// </summary>
    private static IEnumerable<Relationship> NavigationsFor(Entity entity, Domain? domain) {
        if (domain is not null) {
            var live = domain.Types.OfType<Entity>()
                .FirstOrDefault(e => string.Equals(e.Name, entity.Name, StringComparison.Ordinal));
            if (live is not null)
                return live.Navigations;
        }
        return entity.Navigations;
    }

    /// <summary>
    /// Actions on This: entity-level plus every stage's actions. Same set the
    /// C# export emits as methods. Empty-body stubs only — do not inline bodies.
    /// </summary>
    private static IEnumerable<Action> EnumerateTypeDefActions(Entity entity) {
        foreach (var action in entity.Actions)
            yield return action;
        foreach (var stage in entity.Stages) {
            foreach (var action in stage.Actions)
                yield return action;
        }
    }

    private static IEnumerable<Policy> EnumerateTypeDefPolicies(Entity entity) {
        foreach (var policy in entity.Policies)
            yield return policy;
        foreach (var stage in entity.Stages) {
            foreach (var policy in stage.Policies)
                yield return policy;
        }
        // Action-level require guards emit this.PolicyName() in the module body
        // (BuildActionBodyWithGuards). Stub the same names so named-module execute
        // type-checks; InvokeAction still evaluates guards via EvaluatePolicy first.
        foreach (var action in EnumerateTypeDefActions(entity)) {
            foreach (var policy in action.Policies) {
                var name = policy.Name.StartsWith("not_", StringComparison.Ordinal)
                    ? policy.Name[4..]
                    : policy.Name;
                yield return string.Equals(name, policy.Name, StringComparison.Ordinal)
                    ? policy
                    : policy with { Name = name };
            }
        }
    }

    /// <summary>
    /// Resolves an outbound relationship by (this entity, name). Relationship identity
    /// is (source entity, name). Defense-in-depth for a source-scoped miss: when the
    /// name exists on a different source entity, report the precise cause instead of
    /// a generic not-found.
    /// </summary>
    private Relationship ResolveSourceRelationshipOrThrow(string relationshipName, string notFoundMessage) {
        if (Domain is null)
            throw new InvalidOperationException(notFoundMessage);

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
        var rlm = analysis.GetRelationshipLookup(Domain);
        if (rlm is null)
            throw new InvalidOperationException(notFoundMessage);

        if (rlm.TryGetRelationship(Entity.Name, relationshipName, out var relationship))
            return relationship;

        var elsewhere = rlm.FindByNameAcrossSources(relationshipName).ToList();
        if (elsewhere.Count > 0)
            throw new InvalidOperationException(
                $"Entity '{Entity.Name}' is not the source of relationship '{relationshipName}'. " +
                $"Declared on: {string.Join(", ", elsewhere.Select(r => r.Source.TypeName))}.");
        throw new InvalidOperationException(notFoundMessage);
    }

    /// <summary>
    /// Matches a OneToOne nav by generated member name. Does not read the store,
    /// so <see cref="IDictionary{TKey,TValue}.ContainsKey"/> never throws.
    /// </summary>
    internal Relationship? MatchOneToOneNavigation(string key) {
        foreach (var nav in NavigationsFor(Entity, Domain)) {
            if (nav.Cardinality is not RelationshipCardinality.OneToOne)
                continue;
            if (!string.Equals(DomainToCSharpExporter.ToPascalCase(nav.Name), key, StringComparison.Ordinal))
                continue;
            return nav;
        }
        return null;
    }

    /// <summary>
    /// IDictionary read of a OneToOne nav property: the linked target, or
    /// <c>null</c> when unlinked so the lowered guard can return
    /// <c>DomainResult.Failure</c> instead of NRE. More than one link is
    /// fail-closed (singular invoke). Without a store, throws — same as
    /// collection reads and path-prefix / <c>Rel exists</c> policies.
    /// </summary>
    internal bool TryGetOneToOneNavigation(string key, out object? value) {
        value = null;
        var match = MatchOneToOneNavigation(key);
        if (match is null)
            return false;

        if (Store is null || Domain is null)
            throw new InvalidOperationException(
                "Cannot resolve relationship target without a DomainInstanceStore. " +
                "Call store.Add(instance) first.");

        var related = Store.GetRelatedInstances(match.Name, this)
            .Where(t => string.Equals(t.Entity.Name, match.Target.TypeName, StringComparison.Ordinal))
            .ToList();
        if (related.Count > 1)
            throw new InvalidOperationException(
                $"Relationship '{match.Name}' has {related.Count} linked instances; " +
                "singular cross-entity invoke requires exactly one target.");
        value = related.Count == 0 ? null : related[0];
        return true;
    }

    /// <summary>
    /// Matches a collection nav (OneToMany / ManyToMany) by generated member name.
    /// Does not read the store, so ContainsKey never throws. For-invoke analysis
    /// still requires OneToMany; this matches lowering's collection-nav predicate
    /// so a ManyToMany member read is not a miss.
    /// </summary>
    internal Relationship? MatchCollectionNavigation(string key) {
        foreach (var nav in NavigationsFor(Entity, Domain)) {
            if (nav.Cardinality is not (RelationshipCardinality.OneToMany
                or RelationshipCardinality.ManyToMany))
                continue;
            if (!string.Equals(DomainToCSharpExporter.ToPascalCase(nav.Name), key, StringComparison.Ordinal))
                continue;
            return nav;
        }
        return null;
    }

    /// <summary>
    /// IDictionary read of a collection nav (OneToMany / ManyToMany): the outbound
    /// linked targets of <paramref name="navigation"/> (empty list when unlinked, so
    /// a foreach sees zero items). Throws without a store or domain.
    /// </summary>
    internal List<DomainEntityInstance> ReadLinkedTargets(Relationship navigation) {
        if (Store is null || Domain is null)
            throw new InvalidOperationException(
                "Cannot resolve relationship target without a DomainInstanceStore. " +
                "Call store.Add(instance) first.");

        return Store.GetLinkedTargets(navigation.Name, this)
            .Where(t => string.Equals(t.Entity.Name, navigation.Target.TypeName, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// VM bools are long 0/1 on the stack. Boxing them as Int64 made
    /// <c>require not</c> of a path-prefix comparison compile as Not(Int64).
    /// </summary>
    private static object? BoxPathPrefixLeaf(DomainExpression leaf, object? boxed) {
        if (leaf is not (Ontology.Comparison or Ontology.And or Ontology.Or or Ontology.Not
            or Ontology.Exists or Ontology.NotExists
            or Ontology.AnyExpr or Ontology.AllExpr or Ontology.NoneExpr))
            return boxed;
        return boxed switch {
            bool b => b,
            long l => l != 0L,
            int i => i != 0,
            _ => boxed
        };
    }

    /// <summary>
    /// Maps a domain type reference (e.g. "Text", "Number") to an AST type
    /// reference node suitable for <see cref="PropertyDefinitionNode"/>.
    /// </summary>
    private static Node MapDomainTypeToAstNode(DomainTypeReference domainType, Domain? domain) {
        var typeName = domainType.TypeName;
        if (domain?.Types.OfType<Entity>().Any(e =>
                string.Equals(e.Name, typeName, StringComparison.Ordinal)) == true)
            return new TypeReference(typeName);
        var clr = RuntimeAnalysisCache.ClrTypeName(domain, typeName);
        if (DomainTypeMapping.TryPrimitiveType(clr, out var prim))
            return new PrimitiveTypeReference(prim);
        return new PrimitiveTypeReference(Prim.Structure);
    }
}

/// <summary>
/// Result of calling an action on a <see cref="DomainEntityInstance"/>.
/// </summary>
public sealed record ActionInvocationResult {
    private ActionInvocationResult() { }

    /// <summary>The action name that was called.</summary>
    public string ActionName { get; private init; } = "";

    /// <summary>Whether the action call succeeded (all guards passed).</summary>
    public bool Succeeded { get; private init; }

    /// <summary>Names of guard policies that failed, if any.</summary>
    public IReadOnlyList<string> FailedGuards { get; private init; } = [];

    /// <summary>The new stage after the action, if a transition occurred.</summary>
    public string? NewStage { get; private init; }

    /// <summary>Error message for not-found action.</summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>
    /// When the action declared <c>-&gt; EntityType</c> and succeeded, the created
    /// instance of that type from this invoke (product vertical). Null for void actions.
    /// </summary>
    public DomainEntityInstance? ResultInstance { get; private init; }

    /// <summary>Declared return type name when a result instance is present (or missing-return error).</summary>
    public string? ResultTypeName { get; private init; }

    internal static ActionInvocationResult Ok(
        string actionName,
        string? newStage,
        DomainEntityInstance? resultInstance = null,
        string? resultTypeName = null) => new() {
            ActionName = actionName,
            Succeeded = true,
            NewStage = newStage,
            ResultInstance = resultInstance,
            ResultTypeName = resultTypeName ?? resultInstance?.Entity.Name
        };

    internal static ActionInvocationResult MissingReturn(string actionName, string expectedType) => new() {
        ActionName = actionName,
        Succeeded = false,
        ResultTypeName = expectedType,
        ErrorMessage =
            $"Action '{actionName}' declared return type '{expectedType}' but no create/create-in " +
            "produced an instance of that type during this invoke."
    };

    internal static ActionInvocationResult Blocked(string actionName, List<string> failures) => new() {
        ActionName = actionName,
        Succeeded = false,
        FailedGuards = failures.AsReadOnly()
    };

    internal static ActionInvocationResult Missing(string entityName, string actionName) => new() {
        ActionName = actionName,
        Succeeded = false,
        ErrorMessage = $"Action '{actionName}' not found on entity '{entityName}'."
    };

    internal static ActionInvocationResult StageRequired(string entityName, string actionName, string stageName) => new() {
        ActionName = actionName,
        Succeeded = false,
        ErrorMessage = $"Action '{actionName}' exists on entity '{entityName}' but is only available in stage '{stageName}'."
    };

    internal static ActionInvocationResult InvokeDepthExceeded(string actionName, int maxDepth) => new() {
        ActionName = actionName,
        Succeeded = false,
        ErrorMessage =
            $"Action invoke depth exceeded (max {maxDepth}) while calling '{actionName}'. " +
            "Possible recursive invoke cycle (e.g. action → invoke self, or OnEntry → invoke → transition loops)."
    };

    internal static ActionInvocationResult InvalidArguments(string actionName, string message) => new() {
        ActionName = actionName,
        Succeeded = false,
        ErrorMessage = message
    };

    /// <summary>
    /// Module require Failure that carries both the ONE-TREE Failure string and
    /// FailedGuards (policy names) so harness oracles and export agree.
    /// </summary>
    internal static ActionInvocationResult RequireFailure(
        string actionName, string message, IReadOnlyList<string> failedGuards) => new() {
            ActionName = actionName,
            Succeeded = false,
            ErrorMessage = message,
            FailedGuards = failedGuards
        };
}