using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation;
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
                var typeRef = MapDomainTypeToAstNode(ep.Type);
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
                Body: new Block([]))
        };
        // Empty bodies: analysis resolves Member(entity, action/policy) as ITypeMethod.
        // VM does not inline them; InvokeNamed / generated C# owns the implementation.
        var methodNames = new HashSet<string>(StringComparer.Ordinal) { "Notify" };
        foreach (var action in EnumerateTypeDefActions(entity)) {
            if (!methodNames.Add(action.Name))
                continue;
            methods.Add(new MethodDefinitionNode(
                action.Name,
                TypeReference.To<DomainResult>(),
                Parameters: [.. action.Parameters.Select(p =>
                    new Parameter(p.Name, MapDomainTypeToAstNode(p.Type)))],
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
    /// IDictionary read of a OneToOne nav property: the linked target, or
    /// <c>null</c> when unlinked so the lowered guard can return
    /// <c>DomainResult.Failure</c> instead of NRE. More than one link is
    /// fail-closed (singular invoke).
    /// </summary>
    internal bool TryGetOneToOneNavigation(string key, out object? value) {
        value = null;
        Relationship? match = null;
        foreach (var nav in NavigationsFor(Entity, Domain)) {
            if (nav.Cardinality is not RelationshipCardinality.OneToOne)
                continue;
            if (!string.Equals(DomainToCSharpExporter.ToPascalCase(nav.Name), key, StringComparison.Ordinal))
                continue;
            match = nav;
            break;
        }
        if (match is null)
            return false;

        if (Store is null || Domain is null) {
            value = null;
            return true;
        }

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
    /// IDictionary read of a collection nav (OneToMany / ManyToMany): all linked
    /// targets (empty list when unlinked — foreach zero-match, not NRE).
    /// For-invoke analysis still requires OneToMany; this matches lowering's
    /// collection-nav predicate so a ManyToMany member read is not a miss.
    /// </summary>
    internal bool TryGetCollectionNavigation(string key, out object? value) {
        value = null;
        Relationship? match = null;
        foreach (var nav in NavigationsFor(Entity, Domain)) {
            if (nav.Cardinality is not (RelationshipCardinality.OneToMany
                or RelationshipCardinality.ManyToMany))
                continue;
            if (!string.Equals(DomainToCSharpExporter.ToPascalCase(nav.Name), key, StringComparison.Ordinal))
                continue;
            match = nav;
            break;
        }
        if (match is null)
            return false;

        if (Store is null || Domain is null) {
            value = new List<DomainEntityInstance>();
            return true;
        }

        value = Store.GetRelatedInstances(match.Name, this)
            .Where(t => string.Equals(t.Entity.Name, match.Target.TypeName, StringComparison.Ordinal))
            .ToList();
        return true;
    }

    /// <summary>
    /// Outbound links only (this instance as relationship source → targets).
    /// Reverse-side navigate is rejected (matches DMEFF007).
    /// </summary>
    private IReadOnlyList<DomainEntityInstance> GetOutboundRelatedInstances(string relationshipName) {
        if (Domain is null)
            throw new InvalidOperationException(
                $"Cannot resolve relationship '{relationshipName}' without a domain.");

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
        // Catalog/RLM miss with analysis present is a genuine not-found — fail closed.
        // ResolveSourceRelationshipOrThrow also reports the precise cause when the
        // relationship exists on a different source entity (reverse-side invoke).
        var relationship = ResolveSourceRelationshipOrThrow(relationshipName,
            $"Relationship '{relationshipName}' not found in domain '{Domain.Name}'.");

        if (relationship.Cardinality is not (RelationshipCardinality.OneToOne or RelationshipCardinality.OneToMany))
            throw new InvalidOperationException(
                $"Cross-entity invoke on '{relationshipName}' ({relationship.Cardinality}) is not supported yet. " +
                "Use OneToOne or OneToMany from the source.");

        if (Store is null)
            throw new InvalidOperationException(
                "Cannot resolve relationship target without a DomainInstanceStore. " +
                "Call store.Add(instance) first.");

        // Source → target only (do not walk reverse links).
        return Store.GetRelatedInstances(relationshipName, this)
            .Where(t => string.Equals(t.Entity.Name, relationship.Target.TypeName, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Returns linked target instances for a cross-entity invoke, optionally
    /// filtered by a predicate expression evaluated against each target's bag.
    /// </summary>
    private IReadOnlyList<DomainEntityInstance> GetRelatedTargets(
        string relationshipName, DomainExpression? filter) {
        var all = GetOutboundRelatedInstances(relationshipName);
        if (filter is null || all.Count == 0) return all;

        var result = new List<DomainEntityInstance>();
        foreach (var t in all) {
            var loweringPass = new DomainExpressionLoweringPass(new LoweringContext(new Parameter("entity")));
            var lowered = loweringPass.Lower(filter,
                new Parameter("entity", new TypeReference(t.Entity.Name)));
            var compiled = Interpreter.Compile(lowered, t._typeDefAnalyzer);
            using var exec = Interpreter.Execute(compiled,
                s => s.SetArgs(new object?[] { t }));
            if (exec.Result.GetValue<bool>())
                result.Add(t);
        }
        return result;
    }

    // ── Collection quantifier preprocessing ──────────────────────────

    /// <summary>
    /// Walks an expression tree and evaluates collection quantifier nodes
    /// (AnyExpr/AllExpr/NoneExpr/CountExpr) and to-one
    /// <see cref="RelationshipNavigation"/> against the current store,
    /// replacing them with literal results. Non-store nodes are
    /// returned unchanged (or with preprocessed children for composites).
    ///
    /// <para>Fail-closed: missing <see cref="Store"/>, missing relationship
    /// metadata, or missing outbound link throws
    /// <see cref="InvalidOperationException"/> — no soft pass-through to bag
    /// <c>Member</c> chains (no vacuous true/false).</para>
    /// </summary>
    private DomainExpression PreprocessQuantifiers(DomainExpression expr) =>
        QuantifierRewrite.Route(expr);

    /// <summary>
    /// Store-aware quantifier / path-prefix / Rel-exists preprocessing (coh-d1 —
    /// leaf override on the shared <see cref="DomainExpressionRewriteBase"/>;
    /// composites recurse in the base). Fail-closed: missing store/relationship
    /// metadata or missing outbound links throw (no vacuous true/false).
    /// </summary>
    private sealed class QuantifierPreprocessRewrite(DomainEntityInstance instance)
        : DomainExpressionRewriteBase {
        private readonly DomainEntityInstance _instance = instance;

        protected override DomainExpression AnyExpr(AnyExpr e) =>
            DomainExpression.Literal(_instance.EvaluateAnyExpr(e));
        protected override DomainExpression AllExpr(AllExpr e) =>
            DomainExpression.Literal(_instance.EvaluateAllExpr(e));
        protected override DomainExpression NoneExpr(NoneExpr e) =>
            DomainExpression.Literal(_instance.EvaluateNoneExpr(e));
        protected override DomainExpression CountExpr(CountExpr e) =>
            DomainExpression.Literal(_instance.EvaluateCountExpr(e));

        protected override DomainExpression RelationshipNavigation(RelationshipNavigation r) {
            // Singular path-prefix (to-one hops). Nested navs (loan book Title) hop
            // on the linked target — not re-routed on the original instance (P2).
            // Use quantifiers (any/all) for collections — never pick targets[0] silently.
            var result = EvaluatePathPrefixChain(r, _instance);
            return DomainExpression.Literal(result);
        }

        /// <summary>
        /// Walk to-one path-prefix hops; evaluate the leaf expression on the final bag.
        /// </summary>
        private static object? EvaluatePathPrefixChain(
            RelationshipNavigation r, DomainEntityInstance source) {
            // Action-parameter roots (ConvertLead's `lead Name`) live in the bag,
            // not the store.
            if (source.TryGetRaw(r.RelationshipName, out var bag)
                && bag is DomainEntityInstance paramHop) {
                if (r.TargetProperty is RelationshipNavigation nestedParam)
                    return EvaluatePathPrefixChain(nestedParam, paramHop);
                var paramPass = new DomainExpressionLoweringPass(new LoweringContext(new Parameter("entity")));
                var paramLowered = paramPass.Lower(r.TargetProperty,
                    new Parameter("entity", new TypeReference(paramHop.Entity.Name)));
                var paramCompiled = Interpreter.Compile(paramLowered, paramHop._typeDefAnalyzer);
                using var paramExec = Interpreter.Execute(paramCompiled,
                    s => s.SetArgs(new object?[] { paramHop }));
                return BoxPathPrefixLeaf(r.TargetProperty, paramExec.Result.GetValue<object>());
            }

            var targets = source.GetOutboundRelatedInstances(r.RelationshipName);
            if (targets.Count == 0)
                throw new InvalidOperationException(
                    $"No linked instances found for relationship '{r.RelationshipName}' on entity '{source.Entity.Name}'.");
            if (targets.Count > 1)
                throw new InvalidOperationException(
                    $"Path-prefix on relationship '{r.RelationshipName}' requires exactly one linked target " +
                    $"(found {targets.Count} on entity '{source.Entity.Name}'). Use any/all quantifiers for collections.");

            var hop = targets[0];
            if (r.TargetProperty is RelationshipNavigation nested)
                return EvaluatePathPrefixChain(nested, hop);

            // Leaf (comparison, property, etc.) on the final hop instance.
            var pass = new DomainExpressionLoweringPass(new LoweringContext(new Parameter("entity")));
            var lowered = pass.Lower(r.TargetProperty,
                new Parameter("entity", new TypeReference(hop.Entity.Name)));
            var compiled = Interpreter.Compile(lowered, hop._typeDefAnalyzer);
            using var exec = Interpreter.Execute(compiled,
                s => s.SetArgs(new object?[] { hop }));
            // VM bools are long 0/1 on the stack. Boxing them as Int64 made
            // `require not` of a path-prefix comparison compile as Not(Int64).
            return BoxPathPrefixLeaf(r.TargetProperty, exec.Result.GetValue<object>());
        }

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

        protected override DomainExpression Exists(Exists e) {
            // Rel exists: store outbound-link presence when Target is a relationship name.
            // Fail closed without store (GetOutboundRelatedInstances). Empty links → false.
            // Non-relationship targets keep bag-null lowering.
            if (_instance.TryEvaluateRelationshipPresence(e.Target, out var present))
                return DomainExpression.Literal(present);
            return base.Exists(e);
        }

        protected override DomainExpression NotExists(NotExists e) {
            if (_instance.TryEvaluateRelationshipPresence(e.Target, out var present))
                return DomainExpression.Literal(!present);
            return base.NotExists(e);
        }
    }

    /// <summary>
    /// When <paramref name="target"/> is a bare relationship name on this entity as source,
    /// evaluates store-linked presence (count &gt; 0). Returns false from <c>out present</c>
    /// with return false when the target is not an outbound relationship (caller uses bag path).
    /// </summary>
    private bool TryEvaluateRelationshipPresence(DomainExpression target, out bool present) {
        present = false;
        if (target is not PropertyAccess pa)
            return false;
        if (Domain is null)
            return false;

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
        if (!analysis.TryGetRelationship(Domain, Entity.Name, pa.Name, out var relationship) || relationship is null)
            return false;
        if (!string.Equals(relationship.Source.TypeName, Entity.Name, StringComparison.Ordinal))
            return false;

        // Outbound relationship: require store; empty links are false (not throw).
        var targets = GetOutboundRelatedInstances(pa.Name);
        present = targets.Count > 0;
        return true;
    }

    private bool EvaluateAnyExpr(AnyExpr a) {
        var targets = GetOutboundRelatedInstances(a.RelationshipName);
        foreach (var t in targets) {
            if (EvaluateBodyOnTarget(a.Body, t))
                return true;
        }
        return false;
    }

    private bool EvaluateAllExpr(AllExpr a) {
        var targets = GetOutboundRelatedInstances(a.RelationshipName);
        if (targets.Count == 0) return false; // no vacuous all
        foreach (var t in targets) {
            if (!EvaluateBodyOnTarget(a.Body, t))
                return false;
        }
        return true;
    }

    private bool EvaluateNoneExpr(NoneExpr n) {
        return !EvaluateAnyExpr(new AnyExpr(n.RelationshipName, n.Body));
    }

    private long EvaluateCountExpr(CountExpr c) {
        var targets = GetOutboundRelatedInstances(c.RelationshipName);
        if (c.Body is null) return targets.Count;

        long count = 0;
        foreach (var t in targets) {
            if (EvaluateBodyOnTarget(c.Body, t))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Lowers, compiles, and executes a body expression against a target
    /// instance's property bag and type definition. Returns the boolean
    /// result. This is the same pattern used by <see cref="GetRelatedTargets"/>.
    /// </summary>
    private static bool EvaluateBodyOnTarget(DomainExpression body, DomainEntityInstance target) {
        var pass = new DomainExpressionLoweringPass(new LoweringContext(new Parameter("entity")));
        var lowered = pass.Lower(body,
            new Parameter("entity", new TypeReference(target.Entity.Name)));
        var compiled = Interpreter.Compile(lowered, target._typeDefAnalyzer);
        using var exec = Interpreter.Execute(compiled,
            s => s.SetArgs(new object?[] { target }));
        return exec.Result.GetValue<bool>();
    }

    /// <summary>
    /// Maps a domain type reference (e.g. "Text", "Number") to an AST type
    /// reference node suitable for <see cref="PropertyDefinitionNode"/>.
    /// </summary>
    private static Node MapDomainTypeToAstNode(DomainTypeReference domainType) {
        var typeName = domainType.TypeName;
        return typeName switch {
            "Text" => new PrimitiveTypeReference(Prim.String),
            "Number" => new PrimitiveTypeReference(Prim.Int64),
            "Int" => new PrimitiveTypeReference(Prim.Int64),
            "Boolean" => new PrimitiveTypeReference(Prim.Boolean),
            "Bool" => new PrimitiveTypeReference(Prim.Boolean),
            "DateTime" => new PrimitiveTypeReference(Prim.DateTime),
            "Timestamp" => new PrimitiveTypeReference(Prim.DateTime),
            "Date" => new PrimitiveTypeReference(Prim.DateOnly),
            "DateOnly" => new PrimitiveTypeReference(Prim.DateOnly),
            "Time" => new PrimitiveTypeReference(Prim.TimeOnly),
            "TimeOnly" => new PrimitiveTypeReference(Prim.TimeOnly),
            "Duration" => new PrimitiveTypeReference(Prim.TimeSpan),
            "TimeSpan" => new PrimitiveTypeReference(Prim.TimeSpan),
            "Uuid" => new PrimitiveTypeReference(Prim.Guid),
            "Guid" => new PrimitiveTypeReference(Prim.Guid),
            "Decimal" => new PrimitiveTypeReference(Prim.Decimal),
            "Float" => new PrimitiveTypeReference(Prim.Float64),
            "Double" => new PrimitiveTypeReference(Prim.Float64),
            _ => new PrimitiveTypeReference(Prim.Structure)
        };
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
}