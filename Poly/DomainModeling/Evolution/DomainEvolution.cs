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
        var start = DateTime.UtcNow;

        var proposed = ApplyChanges(_current, changes);

        var analysis = priorAnalysis is null
            ? Analyze(proposed)
            : Analyze(proposed, priorAnalysis, GetAffectedNodes(proposed, changes));

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

        var hasErrors = analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        var hasStructuralFailure = analysis.HasStructuralFailure;
        var duration = DateTime.UtcNow - start;
        var trace = BuildTrace(changes, hasErrors || hasStructuralFailure, analysis, duration);

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

    private IReadOnlyList<Node> GetAffectedNodes(Domain proposed, IReadOnlyList<DomainChange> changes) {
        var nodes = new List<Node>();

        foreach (var change in changes) {
            switch (change) {
                case AddEntityChange c: AddType(c.Name); break;
                case RemoveEntityChange c: break; // removed — not in proposed
                case AddPropertyToEntityChange c: AddEntity(c.EntityName); break;
                case RemovePropertyFromEntityChange c: AddEntity(c.EntityName); break;
                case AddStageChange c: AddEntity(c.EntityName); break;
                case RemoveStageChange c: AddEntity(c.EntityName); break;
                case AddActionChange c: AddEntity(c.EntityName); break;
                case RemoveActionChange c: AddEntity(c.EntityName); break;
                case AddActionToStageChange c: AddEntity(c.EntityName); break;
                case RemoveActionFromStageChange c: AddEntity(c.EntityName); break;
                case AddEffectToActionChange c: AddEntity(c.EntityName); break;
                case RemoveEffectFromActionChange c: AddEntity(c.EntityName); break;
                case AddPolicyToEntityChange c: AddEntity(c.EntityName); break;
                case RemovePolicyFromEntityChange c: AddEntity(c.EntityName); break;
                case AddPolicyToStageChange c: AddEntity(c.EntityName); break;
                case RemovePolicyFromStageChange c: AddEntity(c.EntityName); break;
                case AddPolicyToActionChange c: AddEntity(c.EntityName); break;
                case RemovePolicyFromActionChange c: AddEntity(c.EntityName); break;
                case AddParameterToActionChange c: AddEntity(c.EntityName); break;
                case RemoveParameterFromActionChange c: AddEntity(c.EntityName); break;
                case SetActionResultChange c: AddEntity(c.EntityName); break;
                case AddOnEntryEffectToStageChange c: AddEntity(c.EntityName); break;
                case RemoveOnEntryEffectFromStageChange c: AddEntity(c.EntityName); break;
                case AddOnExitEffectToStageChange c: AddEntity(c.EntityName); break;
                case RemoveOnExitEffectFromStageChange c: AddEntity(c.EntityName); break;
                case AddEventReferenceToEntityChange c: AddEntity(c.EntityName); break;
                case RemoveEventReferenceFromEntityChange c: AddEntity(c.EntityName); break;
                case AddPrimitiveTypeChange c: AddType(c.Name); break;
                case RemovePrimitiveTypeChange c: break;
                case AddValueTypeChange c: AddType(c.Name); break;
                case RemoveValueTypeChange c: break;
                case AddEventChange c: AddType(c.Name); break;
                case RemoveEventChange c: break;
                case AddRelationshipChange c: break; // not in type tree
                case RemoveRelationshipChange c: break;
                case AddPropertyToRelationshipChange c: break;
                case RemovePropertyFromRelationshipChange c: break;
                case AddConstraintToPropertyChange c: AddEntity(c.EntityName); break;
                case RemoveConstraintFromPropertyChange c: AddEntity(c.EntityName); break;
                case AddConstraintToDomainTypeChange c: AddType(c.TypeName); break;
                case RemoveConstraintFromDomainTypeChange c: AddType(c.TypeName); break;
                case AddPropertyToEventChange c: AddType(c.EventName); break;
                case RemovePropertyFromEventChange c: AddType(c.EventName); break;
                case ChangePropertyTypeChange c: AddEntity(c.EntityName); break;
                case SetRelationshipShapeChange c: break;
                case SetPrimitiveTypeCategoryChange c: AddType(c.TypeName); break;
                case SetDomainNameChange: nodes.Add(proposed); break;
            }
        }

        return nodes;

        void AddEntity(string entityName) {
            var e = proposed.Types.OfType<Entity>().FirstOrDefault(et =>
                string.Equals(et.Name, entityName, StringComparison.Ordinal));
            if (e is not null) nodes.Add(e);
        }

        void AddType(string typeName) {
            var t = proposed.Types.FirstOrDefault(tp =>
                string.Equals(tp.Name, typeName, StringComparison.Ordinal));
            if (t is not null) nodes.Add(t);
        }
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

    public EvolutionBuilder AddEvent(string name) =>
        Apply(new AddEventChange(name, []));

    public EvolutionBuilder AddEvent(string name, params Property[] properties) =>
        Apply(new AddEventChange(name, properties));

    public EvolutionBuilder RemoveEvent(string name) =>
        Apply(new RemoveEventChange(name));

    /// <summary>
    /// Common pattern: Define an event and immediately attach it to an entity.
    /// </summary>
    public EvolutionBuilder AddEventToEntity(string entityName, string eventName, params Property[] properties) {
        var b = AddEvent(eventName, properties);
        return b.AddEventReferenceToEntity(entityName, eventName);
    }

    public EvolutionBuilder AddEventReferenceToEntity(string entityName, string eventName) =>
        Apply(new AddEventReferenceToEntityChange(entityName, new DomainTypeReference(eventName)));

    public EvolutionBuilder RemoveEventReferenceFromEntity(string entityName, string eventName) =>
        Apply(new RemoveEventReferenceFromEntityChange(entityName, eventName));

    public EvolutionBuilder AddPrimitiveType(string name, TypeCategory typeCategory) =>
        Apply(new AddPrimitiveTypeChange(name, typeCategory, []));

    public EvolutionBuilder RemovePrimitiveType(string name) =>
        Apply(new RemovePrimitiveTypeChange(name));

    public EvolutionBuilder AddPropertyToEntity(string entityName, Property property) =>
        Apply(new AddPropertyToEntityChange(entityName, property));

    public EvolutionBuilder AddStage(string entityName, string name) =>
        Apply(new AddStageChange(entityName, name));

    public EvolutionBuilder AddStage(string entityName, string name, string parentName) =>
        Apply(new AddStageChange(entityName, name, new StageReference(parentName)));

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
    /// Adds a PublishEventEffect with optional property bindings to an action.
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

    /// <summary>
    /// Adds a simple PublishEventEffect (no bindings) to an action.
    /// </summary>
    public EvolutionBuilder AddPublishEventEffect(string entityName, string actionName, string eventName) =>
        AddEffectToAction(entityName, actionName, new PublishEventEffect(new DomainTypeReference(eventName), []));

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
        RelationshipCardinality cardinality) =>
        Apply(new AddRelationshipChange(
            name,
            new DomainTypeReference(sourceEntityName),
            new DomainTypeReference(targetEntityName),
            cardinality,
            []));

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

    public EvolutionBuilder AddPropertyToEvent(string eventName, Property property) =>
        Apply(new AddPropertyToEventChange(eventName, property));

    public EvolutionBuilder RemovePropertyFromEvent(string eventName, string propertyName) =>
        Apply(new RemovePropertyFromEventChange(eventName, propertyName));

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

    /// <summary>
    /// Executes the accumulated changes through the analysis gate.
    /// Returns either a successful EvolutionResult with the new root,
    /// or a rejected proposal result (WasRolledBack = true) with the original root + diagnostics + trace.
    /// </summary>
    public EvolutionResult Apply(AnalysisResult? priorAnalysis = null)
        => _evolution.Apply(_changes, priorAnalysis);
}