using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Effects;

namespace Poly.DomainModeling.Evolution;

/// <summary>
/// Entry point for analysis-gated evolution over an immutable Domain.
/// 
/// This is the thin layer that preserves the model evolution pattern (batch changes,
/// analysis gate, rich trace + original root on failure) while the underlying model
/// is immutable records. There is no explicit transaction/commit/rollback model.
/// On analysis failure the proposed new root is simply discarded; the caller retains
/// the original snapshot. Atomicity is free because of immutability.
/// </summary>
public sealed class DomainEvolution {
    private readonly Domain _current;

    public DomainEvolution(Domain current) {
        _current = current ?? throw new ArgumentNullException(nameof(current));
    }

    public Domain Current => _current;

    /// <summary>
    /// Applies a batch of changes against the current snapshot.
    /// Produces a proposed new root, runs analysis, and returns either a successful
    /// result with the new root or a rolled-back result containing the original root + diagnostics.
    /// </summary>
    public EvolutionResult Apply(IReadOnlyList<DomainChange> changes, AnalysisResult? priorAnalysis = null) {
        var start = DateTime.UtcNow;

        var (proposed, modifiedNodes, evalErrors) = ApplyChanges(_current, changes);

        var analysis = priorAnalysis is null
            ? DomainModelAnalyzer.Analyze(proposed)
            : DomainModelAnalyzer.Analyze(proposed, priorAnalysis, modifiedNodes);

        // Integrate change history as first-class Information diagnostics *immediately*
        // after analysis, before any access to .Diagnostics. This ensures the EVOLUTION_STEP
        // infos are present in the materialized diagnostic list for both success and rejection paths.
        // This is the unified model: step history lives in the standard diagnostic stream.
        {
            var diagnosticsDict = analysis.GetDiagnosticsDictionary();
            foreach (var change in changes) {
                var infoDiag = new Diagnostic(
                    proposed,
                    DiagnosticSeverity.Information,
                    change.GetDescription(),
                    "EVOLUTION_STEP");

                if (!diagnosticsDict.TryGetValue(proposed.Id, out var bucket)) {
                    bucket = new List<Diagnostic>();
                    diagnosticsDict[proposed.Id] = bucket;
                }
                bucket.Add(infoDiag);
            }
        }

        // Inject evalErrors (missing-target failures from RequireUpdate) into the
        // analysis diagnostic stream as first-class Error diagnostics so they appear
        // in FailureSummary, trace, and MCP responses.
        if (evalErrors.Count > 0) {
            var diagnosticsDict = analysis.GetDiagnosticsDictionary();
            foreach (var err in evalErrors) {
                if (!diagnosticsDict.TryGetValue(proposed.Id, out var bucket)) {
                    bucket = new List<Diagnostic>();
                    diagnosticsDict[proposed.Id] = bucket;
                }
                bucket.Add(new Diagnostic(proposed, DiagnosticSeverity.Error, err, "EVOLUTION_TARGET"));
            }
        }

        var hasErrors = analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        var hasStructuralFailure = analysis.HasStructuralFailure;
        var duration = DateTime.UtcNow - start;
        var trace = BuildTrace(changes, hasErrors || hasStructuralFailure, analysis, duration);

        // A structural failure means the model is invalid at a fundamental level.
        // Per current design, this is treated as a hard rejection.
        // We also reject on any other errors or missing-target failures.
        if (hasErrors || hasStructuralFailure)
            return EvolutionResult.RolledBack(_current, analysis, trace);

        return EvolutionResult.Success(proposed, analysis, trace);
    }

    /// <summary>
    /// Starts a fluent evolution builder for ergonomic batch construction.
    /// All changes collected through the builder still go through the single
    /// analysis gate when the final Apply() is called.
    /// </summary>
    public EvolutionBuilder Evolve() => new(this, _current);

    private (Domain Domain, IReadOnlyList<Node> ModifiedNodes, IReadOnlyList<string> Errors) ApplyChanges(
        Domain current, IReadOnlyList<DomainChange> changes) {
        if (changes.Count == 0)
            return (current, [], []);

        var context = new DomainMutationContext(current);

        foreach (var change in changes) {
            change.ApplyTo(context);
        }

        return (context.ToDomain(), context.ModifiedNodes, context.Errors);
    }

    private EvolutionTrace BuildTrace(
        IReadOnlyList<DomainChange> changes,
        bool proposalRejected,
        AnalysisResult analysis,
        TimeSpan duration) {
        var steps = changes
            .Select(c => new EvolutionStep(c.GetDescription()))
            .ToList();

        return new EvolutionTrace(
            steps,
            RolledBack: proposalRejected,
            Duration: duration,
            ErrorCount: analysis.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error),
            WarningCount: analysis.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning));
    }
}

/// <summary>
/// Lightweight fluent builder for accumulating changes before a single analysis-gated Apply.
/// This is the primary ergonomic surface for agents and future UI-driven evolution.
/// </summary>
public sealed class EvolutionBuilder {
    private readonly DomainEvolution _evolution;
    private readonly Domain _startingRoot;
    private readonly List<DomainChange> _changes = new();

    internal EvolutionBuilder(DomainEvolution evolution, Domain startingRoot) {
        _evolution = evolution;
        _startingRoot = startingRoot;
    }

    public EvolutionBuilder Apply(DomainChange change) {
        ArgumentNullException.ThrowIfNull(change);
        _changes.Add(change);
        return this;
    }

    // --- Minimal fluent helpers for the most common operations (MVP surface) ---

    public EvolutionBuilder AddEntity(string name) =>
        Apply(new AddEntityChange(name, []));

    public EvolutionBuilder AddValueType(string name) =>
        Apply(new AddValueTypeChange(name, []));

    public EvolutionBuilder AddValueType(string name, params Property[] properties) =>
        Apply(new AddValueTypeChange(name, properties));

    public EvolutionBuilder RemoveValueType(string name) =>
        Apply(new RemoveValueTypeChange(name));



    public EvolutionBuilder AddPrimitiveType(string name, TypeCategory typeCategory) =>
        Apply(new AddPrimitiveTypeChange(name, typeCategory, []));

    public EvolutionBuilder RemovePrimitiveType(string name) =>
        Apply(new RemovePrimitiveTypeChange(name));

    public EvolutionBuilder AddPropertyToEntity(string entityName, Property property) =>
        Apply(new AddPropertyToEntityChange(entityName, property));

    public EvolutionBuilder AddStage(string entityName, string name) =>
        Apply(new AddStageChange(entityName, name));

    // Stage hierarchy (parent/child) is not supported in the current DSL surface.

    public EvolutionBuilder AddStage(
        string entityName,
        string name,
        DomainExpression? guard = null,
        Effect[]? onEntryEffects = null,
        Effect[]? onExitEffects = null) {
        var b = AddStage(entityName, name);

        if (guard != null) {
            b = b.AddStageGuard(entityName, name, $"Guard_{name}", guard);
        }

        if (onEntryEffects != null) {
            foreach (var e in onEntryEffects)
                b = b.AddOnEntryEffect(entityName, name, e);
        }

        if (onExitEffects != null) {
            foreach (var e in onExitEffects)
                b = b.AddOnExitEffect(entityName, name, e);
        }

        return b;
    }

    public EvolutionBuilder AddAction(string entityName, string name) =>
        Apply(new AddActionChange(entityName, name));

    public EvolutionBuilder AddActionToStage(string entityName, string stageName, string name) =>
        Apply(new AddActionToStageChange(entityName, stageName, name));

    public EvolutionBuilder AddParameterToAction(string entityName, string actionName, Property parameter) =>
        Apply(new AddParameterToActionChange(entityName, actionName, parameter));

    public EvolutionBuilder AddActionWithParameters(string entityName, string name, params Property[] parameters) {
        var builder = AddAction(entityName, name);
        foreach (var p in parameters) {
            builder = builder.AddParameterToAction(entityName, name, p);
        }
        return builder;
    }

    public EvolutionBuilder AddActionWithResult(string entityName, string name, InvocationResult result) =>
        SetActionResult(entityName, name, result);

    public EvolutionBuilder AddAction(string entityName, string name, InvocationResult result, params Property[] parameters) {
        var b = AddAction(entityName, name);
        foreach (var p in parameters)
            b = b.AddParameterToAction(entityName, name, p);
        return b.SetActionResult(entityName, name, result);
    }

    /// <summary>
    /// Creates an action with parameters, result, and effects in one call. This is the most ergonomic way to define rich actions.
    /// </summary>
    public EvolutionBuilder AddAction(
        string entityName,
        string name,
        InvocationResult? result = null,
        Property[]? parameters = null,
        Effect[]? effects = null) {
        var b = AddAction(entityName, name);

        if (parameters != null) {
            foreach (var p in parameters)
                b = b.AddParameterToAction(entityName, name, p);
        }

        if (result != null) {
            b = b.SetActionResult(entityName, name, result);
        }

        if (effects != null) {
            foreach (var e in effects)
                b = b.AddEffectToAction(entityName, name, e);
        }

        return b;
    }



    public EvolutionBuilder AddEffectToAction(string entityName, string actionName, Effect effect) =>
        Apply(new AddEffectToActionChange(entityName, actionName, effect));

    /// <summary>
    /// Adds a CreateEntityInstance effect with property bindings. This is the common pattern for creating owned documents.
    /// </summary>
    public EvolutionBuilder AddCreateEffect(
        string entityName,
        string actionName,
        string typeName,
        params (string propertyName, DomainExpression expression)[] bindings) {
        var initializers = bindings
            .Select(b => new PropertyBinding(b.propertyName, b.expression))
            .ToList();

        var effect = new CreateEntityInstance(new DomainTypeReference(typeName), initializers);
        return AddEffectToAction(entityName, actionName, effect);
    }

    /// <summary>
    /// Adds a simple CreateEntityInstance effect with no initializers.
    /// </summary>
    public EvolutionBuilder AddCreateEffect(string entityName, string actionName, string typeName) =>
        AddEffectToAction(entityName, actionName, new CreateEntityInstance(new DomainTypeReference(typeName)));

    /// <summary>
    /// Adds a StageTransitionEffect to an action.
    /// </summary>
    public EvolutionBuilder AddStageTransitionEffect(string entityName, string actionName, string targetStageName) =>
        AddEffectToAction(entityName, actionName, new StageTransitionEffect(new StageReference(targetStageName)));

    public EvolutionBuilder AddPolicyToEntity(string entityName, Policy policy) =>
        Apply(new AddPolicyToEntityChange(entityName, policy));

    public EvolutionBuilder AddPolicyToEntity(string entityName, string policyName, DomainExpression expression) =>
        AddPolicyToEntity(entityName, new Policy(policyName, expression));

    public EvolutionBuilder AddPolicyToStage(string entityName, string stageName, Policy policy) =>
        Apply(new AddPolicyToStageChange(entityName, stageName, policy));

    public EvolutionBuilder AddPolicyToStage(string entityName, string stageName, string policyName, DomainExpression expression) =>
        AddPolicyToStage(entityName, stageName, new Policy(policyName, expression));

    public EvolutionBuilder AddPolicyToAction(string entityName, string actionName, Policy policy) =>
        Apply(new AddPolicyToActionChange(entityName, actionName, policy));

    public EvolutionBuilder AddPolicyToAction(string entityName, string actionName, string policyName, DomainExpression expression) =>
        AddPolicyToAction(entityName, actionName, new Policy(policyName, expression));

    // Convenience for common "add action with effect" pattern
    public EvolutionBuilder AddActionWithEffect(string entityName, string actionName, Effect effect) =>
        AddAction(entityName, actionName)
            .AddEffectToAction(entityName, actionName, effect);

    /// <summary>
    /// Adds a named stage guard (policy). Use this for important invariants.
    /// For quick unnamed guards, prefer AddPolicyToStage with an explicit name.
    /// </summary>
    public EvolutionBuilder AddStageGuard(string entityName, string stageName, string guardName, DomainExpression expression) =>
        AddPolicyToStage(entityName, stageName, guardName, expression);

    public EvolutionBuilder AddOnEntryEffect(string entityName, string stageName, Effect effect) =>
        Apply(new AddOnEntryEffectToStageChange(entityName, stageName, effect));

    public EvolutionBuilder AddOnExitEffect(string entityName, string stageName, Effect effect) =>
        Apply(new AddOnExitEffectToStageChange(entityName, stageName, effect));

    public EvolutionBuilder AddRelationship(
        string name,
        string sourceEntityName,
        string targetEntityName,
        RelationshipCardinality cardinality,
        bool sourceOwnsTarget = false) =>
        Apply(new AddRelationshipChange(
            name,
            new DomainTypeReference(sourceEntityName),
            new DomainTypeReference(targetEntityName),
            cardinality,
            [],
            sourceOwnsTarget));

    public EvolutionBuilder RemoveRelationship(string name) =>
        Apply(new RemoveRelationshipChange(name));

    public EvolutionBuilder AddPropertyToRelationship(string relationshipName, Property property) =>
        Apply(new AddPropertyToRelationshipChange(relationshipName, property));

    public EvolutionBuilder RemovePropertyFromRelationship(string relationshipName, string propertyName) =>
        Apply(new RemovePropertyFromRelationshipChange(relationshipName, propertyName));

    public EvolutionBuilder AddConstraintToProperty(string entityName, string propertyName, Constraint constraint) =>
        Apply(new AddConstraintToPropertyChange(entityName, propertyName, constraint));

    public EvolutionBuilder RemoveConstraintFromProperty(string entityName, string propertyName, Constraint constraint) =>
        Apply(new RemoveConstraintFromPropertyChange(entityName, propertyName, constraint));

    public EvolutionBuilder SetDomainName(string name) =>
        Apply(new SetDomainNameChange(name));

    public EvolutionBuilder AddConstraintToDomainType(string typeName, Constraint constraint) =>
        Apply(new AddConstraintToDomainTypeChange(typeName, constraint));

    public EvolutionBuilder RemoveConstraintFromDomainType(string typeName, Constraint constraint) =>
        Apply(new RemoveConstraintFromDomainTypeChange(typeName, constraint));

    // ── Facets ──────────────────────────────────────────────────────────────

    /// <summary>Adds a facet to a property on an entity.</summary>
    public EvolutionBuilder AddFacetToProperty(string entityName, string propertyName, Facet facet) =>
        Apply(new AddFacetToPropertyChange(entityName, propertyName, facet));

    /// <summary>Adds a facet to a DomainType (Entity, ValueType, PrimitiveType, EnumType).</summary>
    public EvolutionBuilder AddFacetToDomainType(string typeName, Facet facet) =>
        Apply(new AddFacetToDomainTypeChange(typeName, facet));

    public EvolutionBuilder ChangePropertyType(string entityName, string propertyName, DomainTypeReference newType) =>
        Apply(new ChangePropertyTypeChange(entityName, propertyName, newType));

    public EvolutionBuilder SetRelationshipShape(string relationshipName,
        DomainTypeReference? newSource = null,
        DomainTypeReference? newTarget = null,
        RelationshipCardinality? newCardinality = null) =>
        Apply(new SetRelationshipShapeChange(relationshipName, newSource, newTarget, newCardinality));

    public EvolutionBuilder SetPrimitiveTypeCategory(string typeName, TypeCategory category) =>
        Apply(new SetPrimitiveTypeCategoryChange(typeName, category));

    public EvolutionBuilder RemoveParameterFromAction(string entityName, string actionName, string parameterName) =>
        Apply(new RemoveParameterFromActionChange(entityName, actionName, parameterName));

    public EvolutionBuilder RemoveEffectFromAction(string entityName, string actionName, Effect effect) =>
        Apply(new RemoveEffectFromActionChange(entityName, actionName, effect));

    public EvolutionBuilder RemoveOnEntryEffectFromStage(string entityName, string stageName, Effect effect) =>
        Apply(new RemoveOnEntryEffectFromStageChange(entityName, stageName, effect));

    public EvolutionBuilder RemoveOnExitEffectFromStage(string entityName, string stageName, Effect effect) =>
        Apply(new RemoveOnExitEffectFromStageChange(entityName, stageName, effect));

    /// <summary>
    /// Adds a stage subscription to a stage on an entity.
    /// </summary>
    public EvolutionBuilder AddStageSubscription(string entityName, string stageName, StageSubscription subscription) =>
        Apply(new AddStageSubscriptionChange(entityName, stageName, subscription));

    /// <summary>
    /// Removes a stage subscription from a stage on an entity.
    /// </summary>
    public EvolutionBuilder RemoveStageSubscription(string entityName, string stageName, StageSubscription subscription) =>
        Apply(new RemoveStageSubscriptionChange(entityName, stageName, subscription));

    /// <summary>
    /// Adds a subscription to a stage using a builder configuration delegate.
    /// The builder does not accept bindings yet — the Configure overload will be added in Phase 1b.
    /// </summary>
    public EvolutionBuilder AddStageSubscription(string entityName, string stageName,
        string relationshipName, string targetStageName, StageSubscriptionQuantifier quantifier,
        IReadOnlyList<Effect> effects) =>
        AddStageSubscription(entityName, stageName,
            new StageSubscription(relationshipName, targetStageName, quantifier, effects));

    /// <summary>
    /// Convenience: adds a subscription with <see cref="StageSubscriptionQuantifier.Each"/> and a single target stage.
    /// </summary>
    public EvolutionBuilder AddStageSubscription(string entityName, string stageName,
        string relationshipName, string targetStageName,
        IReadOnlyList<Effect> effects) =>
        AddStageSubscription(entityName, stageName,
            new StageSubscription(relationshipName, targetStageName, effects));

    // ── Entity-level subscriptions ────────────────────────────────────────────

    /// <summary>
    /// Adds an entity-level subscription (fires regardless of the entity's current stage).
    /// </summary>
    public EvolutionBuilder AddEntitySubscription(string entityName, StageSubscription subscription) =>
        Apply(new AddEntitySubscriptionChange(entityName, subscription));

    /// <summary>
    /// Adds an entity-level subscription with explicit quantifier.
    /// </summary>
    public EvolutionBuilder AddEntitySubscription(string entityName,
        string relName, string targetStage, StageSubscriptionQuantifier quantifier, params Effect[] effects) =>
        AddEntitySubscription(entityName,
            new StageSubscription(relName, [targetStage], quantifier, effects));

    /// <summary>
    /// Adds an entity-level subscription with <see cref="StageSubscriptionQuantifier.Each"/>.
    /// </summary>
    public EvolutionBuilder AddEntitySubscription(string entityName,
        string relName, string targetStage, params Effect[] effects) =>
        AddEntitySubscription(entityName,
            new StageSubscription(relName, [targetStage], StageSubscriptionQuantifier.Each, effects));

    public EvolutionBuilder SetActionResult(string entityName, string actionName, InvocationResult result) =>
        Apply(new SetActionResultChange(entityName, actionName, result));

    // --- Remove methods for completeness of the public API ---

    public EvolutionBuilder RemoveEntity(string name) =>
        Apply(new RemoveEntityChange(name));

    public EvolutionBuilder RemovePropertyFromEntity(string entityName, string propertyName) =>
        Apply(new RemovePropertyFromEntityChange(entityName, propertyName));

    public EvolutionBuilder RemoveStage(string entityName, string name) =>
        Apply(new RemoveStageChange(entityName, name));

    public EvolutionBuilder RemoveAction(string entityName, string name) =>
        Apply(new RemoveActionChange(entityName, name));

    public EvolutionBuilder RemoveActionFromStage(string entityName, string stageName, string name) =>
        Apply(new RemoveActionFromStageChange(entityName, stageName, name));

    public EvolutionBuilder RemovePolicyFromEntity(string entityName, string policyName) =>
        Apply(new RemovePolicyFromEntityChange(entityName, policyName)); // Note: will need to add the change type if not present

    public EvolutionBuilder RemovePolicyFromStage(string entityName, string stageName, string policyName) =>
        Apply(new RemovePolicyFromStageChange(entityName, stageName, policyName));

    public EvolutionBuilder RemovePolicyFromAction(string entityName, string actionName, string policyName) =>
        Apply(new RemovePolicyFromActionChange(entityName, actionName, policyName));

    // --- Enum type builder methods ---

    public EvolutionBuilder AddEnumType(string name, params string[] memberNames) =>
        Apply(new AddEnumTypeChange(name, memberNames));

    // --- Relationship stage/policy builder methods ---

    public EvolutionBuilder AddStageToRelationship(string relationshipName, string stageName) {
        return Apply(new AddStageToRelationshipChange(relationshipName, new Stage(stageName, [], [], [], [])));
    }

    public EvolutionBuilder RemoveStageFromRelationship(string relationshipName, string stageName) =>
        Apply(new RemoveStageFromRelationshipChange(relationshipName, stageName));

    public EvolutionBuilder AddPolicyToRelationship(string relationshipName, Policy policy) =>
        Apply(new AddPolicyToRelationshipChange(relationshipName, policy));

    public EvolutionBuilder AddPolicyToRelationship(string relationshipName, string policyName, DomainExpression expression) =>
        AddPolicyToRelationship(relationshipName, new Policy(policyName, expression));

    public EvolutionBuilder RemovePolicyFromRelationship(string relationshipName, string policyName) =>
        Apply(new RemovePolicyFromRelationshipChange(relationshipName, policyName));

    /// <summary>
    /// Executes the accumulated changes through the analysis gate.
    /// Returns either a successful EvolutionResult with the new root,
    /// or a rejected proposal result (WasRolledBack = true) with the original root + diagnostics + trace.
    /// </summary>
    public EvolutionResult Apply(AnalysisResult? priorAnalysis = null)
        => _evolution.Apply(_changes, priorAnalysis);
}