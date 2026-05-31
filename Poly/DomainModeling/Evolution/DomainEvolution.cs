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
    private readonly DomainModelAnalyzer _analyzer;

    public DomainEvolution(Domain current, DomainModelAnalyzer? analyzer = null) {
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _analyzer = analyzer ?? new DomainModelAnalyzer(AnalysisOptions.StopOnStructuralErrors);
    }

    public Domain Current => _current;

    /// <summary>
    /// Applies a batch of changes against the current snapshot.
    /// Produces a proposed new root, runs analysis, and returns either a successful
    /// result with the new root or a rolled-back result containing the original root + diagnostics.
    /// </summary>
    public EvolutionResult Apply(IReadOnlyList<DomainChange> changes, AnalysisResult? priorAnalysis = null) {
        Domain proposed = ApplyChanges(_current, changes);

        var analysis = priorAnalysis is null
            ? Analyze(proposed)
            : Analyze(proposed, priorAnalysis, GetAffectedNodes(changes));

        var hasErrors = analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        var hasStructuralFailure = analysis.HasStructuralFailure;
        var trace = BuildTrace(changes, hasErrors || hasStructuralFailure, analysis);

        // A structural failure means the model is invalid at a fundamental level.
        // Per current design, this is treated as a hard rejection.
        // We also reject on any other errors.
        return (hasErrors || hasStructuralFailure)
            ? EvolutionResult.RolledBack(_current, analysis, trace)
            : EvolutionResult.Success(proposed, analysis, trace);
    }

    /// <summary>
    /// Starts a fluent evolution builder for ergonomic batch construction.
    /// All changes collected through the builder still go through the single
    /// analysis gate when the final Apply() is called.
    /// </summary>
    public EvolutionBuilder Evolve() => new(this, _current);

    // --- Internal analysis hooks (will be refined for incremental support) ---

    internal AnalysisResult Analyze(Domain domain)
        => _analyzer.Analyze(domain);

    internal AnalysisResult Analyze(Domain domain, AnalysisResult prior, IEnumerable<Node> affected)
        => _analyzer.Analyze(domain, prior, affected);

    private Domain ApplyChanges(Domain current, IReadOnlyList<DomainChange> changes) {
        if (changes.Count == 0)
            return current;

        var context = new DomainMutationContext(current);

        foreach (var change in changes) {
            change.ApplyTo(context);
        }

        return context.ToDomain();
    }

    private IReadOnlyList<Node> GetAffectedNodes(IReadOnlyList<DomainChange> changes) {
        // MVP: return empty for now. Full population (yielding the actual modified Node instances)
        // can be added once we have richer change handling and need true incremental analysis.
        // The analysis gate still runs correctly (full or prior+empty).
        return Array.Empty<Node>();
    }

    private EvolutionTrace BuildTrace(
        IReadOnlyList<DomainChange> changes,
        bool proposalRejected,
        AnalysisResult analysis) {
        var steps = changes
            .Select(c => new EvolutionStep(c.GetDescription(), Array.Empty<string>()))
            .ToList();

        return new EvolutionTrace(
            steps,
            AffectedNodeIds: Array.Empty<string>(),
            RolledBack: proposalRejected,
            Duration: TimeSpan.Zero,
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

    public EvolutionBuilder AddEvent(string name) =>
        Apply(new AddEventChange(name, []));

    public EvolutionBuilder AddEvent(string name, params Property[] properties) =>
        Apply(new AddEventChange(name, properties));

    public EvolutionBuilder RemoveEvent(string name) =>
        Apply(new RemoveEventChange(name));

    public EvolutionBuilder AddEventReferenceToEntity(string entityName, string eventName) =>
        Apply(new AddEventReferenceToEntityChange(entityName, new DomainTypeReference(eventName)));

    public EvolutionBuilder RemoveEventReferenceFromEntity(string entityName, string eventName) =>
        Apply(new RemoveEventReferenceFromEntityChange(entityName, eventName));

    public EvolutionBuilder AddPropertyToEntity(string entityName, Property property) =>
        Apply(new AddPropertyToEntityChange(entityName, property));

    public EvolutionBuilder AddStage(string entityName, string name) =>
        Apply(new AddStageChange(entityName, name));

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

    /// <summary>
    /// Common pattern: Add an action that publishes an event (with optional parameters and result).
    /// </summary>
    public EvolutionBuilder AddActionThatPublishesEvent(
        string entityName,
        string actionName,
        string eventName,
        InvocationResult? result = null,
        Property[]? parameters = null) {
        var b = AddAction(entityName, actionName, result, parameters);
        return b.AddPublishEventEffect(entityName, actionName, eventName);
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
    /// Adds a PublishEventEffect with optional property bindings.
    /// </summary>
    public EvolutionBuilder AddPublishEventEffect(
        string entityName,
        string actionName,
        string eventName,
        params (string propertyName, DomainExpression expression)[] bindings) {
        var initializers = bindings
            .Select(b => new PropertyBinding(b.propertyName, b.expression))
            .ToList();

        var effect = new PublishEventEffect(new DomainTypeReference(eventName), initializers);
        return AddEffectToAction(entityName, actionName, effect);
    }

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
        RelationshipCardinality cardinality) =>
        Apply(new AddRelationshipChange(
            name,
            new DomainTypeReference(sourceEntityName),
            new DomainTypeReference(targetEntityName),
            cardinality,
            []));

    public EvolutionBuilder RemoveRelationship(string name) =>
        Apply(new RemoveRelationshipChange(name));

    public EvolutionBuilder RemoveParameterFromAction(string entityName, string actionName, string parameterName) =>
        Apply(new RemoveParameterFromActionChange(entityName, actionName, parameterName));

    public EvolutionBuilder RemoveEffectFromAction(string entityName, string actionName, Effect effect) =>
        Apply(new RemoveEffectFromActionChange(entityName, actionName, effect));

    public EvolutionBuilder RemoveOnEntryEffectFromStage(string entityName, string stageName, Effect effect) =>
        Apply(new RemoveOnEntryEffectFromStageChange(entityName, stageName, effect));

    public EvolutionBuilder RemoveOnExitEffectFromStage(string entityName, string stageName, Effect effect) =>
        Apply(new RemoveOnExitEffectFromStageChange(entityName, stageName, effect));

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

    public EvolutionBuilder RemovePolicyFromEntity(string entityName, string policyName) =>
        Apply(new RemovePolicyFromEntityChange(entityName, policyName)); // Note: will need to add the change type if not present

    public EvolutionBuilder RemovePolicyFromStage(string entityName, string stageName, string policyName) =>
        Apply(new RemovePolicyFromStageChange(entityName, stageName, policyName));

    public EvolutionBuilder RemovePolicyFromAction(string entityName, string actionName, string policyName) =>
        Apply(new RemovePolicyFromActionChange(entityName, actionName, policyName));

    /// <summary>
    /// Executes the accumulated changes through the analysis gate.
    /// Returns either a successful EvolutionResult with the new root,
    /// or a rejected proposal result (WasRolledBack = true) with the original root + diagnostics + trace.
    /// </summary>
    public EvolutionResult Apply(AnalysisResult? priorAnalysis = null)
        => _evolution.Apply(_changes, priorAnalysis);
}