using System.Diagnostics;

using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public sealed partial record Domain {
    public Mutation CreateMutation(DomainModelAnalyzer? analyzer = null) => new Mutation(this, analyzer ?? new DomainModelAnalyzer());

    public sealed class Mutation {
        private readonly Domain _domain;
        private readonly DomainModelAnalyzer _analyzer;
        private readonly List<DomainMutationCommand> _steps = [];
        private bool _completed;

        internal Mutation(Domain domain, DomainModelAnalyzer analyzer) {
            ArgumentNullException.ThrowIfNull(domain);
            ArgumentNullException.ThrowIfNull(analyzer);

            _domain = domain;
            _analyzer = analyzer;
        }

        public Domain Domain => _domain;

        // ── Domain ───────────────────────────────────────────────────────────

        public Mutation SetDomainName(string name) =>
            AddStep(new SetNameCommand(Domain, name));

        public Mutation AddType(DomainType type) =>
            AddStep(new AddTypeCommand(Domain, type));

        public Mutation RemoveType(DomainType type) =>
            AddStep(new RemoveTypeCommand(Domain, type));

        public Mutation AddRelationship(Relationship relationship) =>
            AddStep(new AddRelationshipCommand(Domain, relationship));

        public Mutation RemoveRelationship(Relationship relationship) =>
            AddStep(new RemoveRelationshipCommand(Domain, relationship));

        public Mutation AddImportedContract(ImportedContract contract) =>
            AddStep(new AddImportedContractCommand(Domain, contract));

        public Mutation RemoveImportedContract(ImportedContract contract) =>
            AddStep(new RemoveImportedContractCommand(Domain, contract));

        public Mutation AddContractBinding(ContractBinding binding) =>
            AddStep(new AddContractBindingCommand(Domain, binding));

        public Mutation RemoveContractBinding(ContractBinding binding) =>
            AddStep(new RemoveContractBindingCommand(Domain, binding));

        // ── Entity ───────────────────────────────────────────────────────────

        public Mutation AddProperty(Entity entity, Property property) =>
            AddStep(new Entity.AddPropertyCommand(entity, property));

        public Mutation RemoveProperty(Entity entity, Property property) =>
            AddStep(new Entity.RemovePropertyCommand(entity, property));

        public Mutation AddStage(Entity entity, Stage stage) =>
            AddStep(new Entity.AddStageCommand(entity, stage));

        public Mutation RemoveStage(Entity entity, Stage stage) =>
            AddStep(new Entity.RemoveStageCommand(entity, stage));

        public Mutation AddPolicy(Entity entity, Policy policy) =>
            AddStep(new Entity.AddPolicyCommand(entity, policy));

        public Mutation RemovePolicy(Entity entity, Policy policy) =>
            AddStep(new Entity.RemovePolicyCommand(entity, policy));

        public Mutation AddAction(Entity entity, Action action) =>
            AddStep(new Entity.AddActionCommand(entity, action));

        public Mutation RemoveAction(Entity entity, Action action) =>
            AddStep(new Entity.RemoveActionCommand(entity, action));

        public Mutation AddEvent(Entity entity, Event @event) =>
            AddStep(new Entity.AddEventCommand(entity, @event));

        public Mutation RemoveEvent(Entity entity, Event @event) =>
            AddStep(new Entity.RemoveEventCommand(entity, @event));

        public Mutation AddEventSubscription(Entity entity, EventSubscription subscription) =>
            AddStep(new Entity.AddEventSubscriptionCommand(entity, subscription));

        public Mutation RemoveEventSubscription(Entity entity, EventSubscription subscription) =>
            AddStep(new Entity.RemoveEventSubscriptionCommand(entity, subscription));

        public Mutation AddEntityRelationship(Entity entity, Relationship relationship) =>
            AddStep(new Entity.AddRelationshipRefCommand(entity, relationship));

        public Mutation RemoveEntityRelationship(Entity entity, Relationship relationship) =>
            AddStep(new Entity.RemoveRelationshipRefCommand(entity, relationship));

        // ── Relationship ─────────────────────────────────────────────────────

        public Mutation SetRelationship(Relationship relationship, Entity source, Entity target, RelationshipCardinality cardinality, bool sourceOwnsTarget) =>
            AddStep(new Relationship.SetShapeCommand(relationship, source, target, cardinality, sourceOwnsTarget));

        // ── Stage ────────────────────────────────────────────────────────────

        public Mutation AddPolicy(Stage stage, Policy policy) =>
            AddStep(new Stage.AddPolicyCommand(stage, policy));

        public Mutation RemovePolicy(Stage stage, Policy policy) =>
            AddStep(new Stage.RemovePolicyCommand(stage, policy));

        public Mutation AddAction(Stage stage, Action action) =>
            AddStep(new Stage.AddActionCommand(stage, action));

        public Mutation RemoveAction(Stage stage, Action action) =>
            AddStep(new Stage.RemoveActionCommand(stage, action));

        // ── Policy ───────────────────────────────────────────────────────────

        public Mutation AddRule(Policy policy, Rule rule) =>
            AddStep(new Policy.AddRuleCommand(policy, rule));

        public Mutation RemoveRule(Policy policy, Rule rule) =>
            AddStep(new Policy.RemoveRuleCommand(policy, rule));

        // ── Property ─────────────────────────────────────────────────────────

        public Mutation AddConstraint(Property property, Constraint constraint) =>
            AddStep(new Property.AddConstraintCommand(property, constraint));

        public Mutation RemoveConstraint(Property property, Constraint constraint) =>
            AddStep(new Property.RemoveConstraintCommand(property, constraint));

        public Mutation AddConstraint(DomainType type, Constraint constraint) =>
            AddStep(new DomainType.AddConstraintCommand(type, constraint));

        public Mutation RemoveConstraint(DomainType type, Constraint constraint) =>
            AddStep(new DomainType.RemoveConstraintCommand(type, constraint));

        public Mutation AddPolicy(Property property, Policy policy) =>
            AddStep(new Property.AddPolicyCommand(property, policy));

        public Mutation RemovePolicy(Property property, Policy policy) =>
            AddStep(new Property.RemovePolicyCommand(property, policy));

        // ── Event ────────────────────────────────────────────────────────────

        public Mutation AddProperty(Event @event, Property property) =>
            AddStep(new Event.AddPropertyCommand(@event, property));

        public Mutation RemoveProperty(Event @event, Property property) =>
            AddStep(new Event.RemovePropertyCommand(@event, property));

        // ── Action ───────────────────────────────────────────────────────────

        public Mutation AddParameter(Action action, Property parameter) =>
            AddStep(new Action.AddParameterCommand(action, parameter));

        public Mutation RemoveParameter(Action action, Property parameter) =>
            AddStep(new Action.RemoveParameterCommand(action, parameter));

        public Mutation AddEffect(Action action, Effect effect) =>
            AddStep(new Action.AddEffectCommand(action, effect));

        public Mutation RemoveEffect(Action action, Effect effect) =>
            AddStep(new Action.RemoveEffectCommand(action, effect));

        public Mutation SetEventPropertyBinding(Action action, PublishEvent effect, string propertyName, EventPropertyBindingSource source) {
            effect._bindings.TryGetValue(propertyName, out var previous);
            return AddStep(new Action.SetEventPropertyBindingCommand(action, effect, propertyName, source, previous));
        }

        public Mutation AddEventSubscriptionCorrelation(EventSubscription subscription, EventCorrelationBinding binding) =>
            AddStep(new EventSubscription.AddCorrelationBindingCommand(subscription, binding));

        public Mutation RemoveEventSubscriptionCorrelation(EventSubscription subscription, EventCorrelationBinding binding) =>
            AddStep(new EventSubscription.RemoveCorrelationBindingCommand(subscription, binding));

        public Mutation SetEventSubscriptionAudience(EventSubscription subscription, EventSubscriptionAudience audience) =>
            AddStep(new EventSubscription.SetAudienceCommand(subscription, audience, subscription.Audience));

        public Mutation SetEventSubscriptionRoutingMode(EventSubscription subscription, EventSubscriptionRoutingMode routingMode) =>
            AddStep(new EventSubscription.SetRoutingModeCommand(subscription, routingMode, subscription._routingMode));

        public Mutation SetEventSubscriptionEventParameter(EventSubscription subscription, string eventParameterName) =>
            AddStep(new EventSubscription.SetEventParameterNameCommand(subscription, eventParameterName, subscription._eventParameterName));

        public Mutation SetActionTrigger(Action action, ActionTrigger trigger) =>
            AddStep(new Action.SetTriggerCommand(action, trigger, action._trigger));

        public Mutation SetEventHandlerTrigger(Action action, Event eventType, string eventParameterName) =>
            SetActionTrigger(action, new ActionTrigger.EventHandler(eventType, eventParameterName));

        public Mutation SetCommandTrigger(Action action) =>
            SetActionTrigger(action, ActionTrigger.Default);

        public Mutation AddPolicy(Action action, Policy policy) =>
            AddStep(new Action.AddPolicyCommand(action, policy));

        public Mutation RemovePolicy(Action action, Policy policy) =>
            AddStep(new Action.RemovePolicyCommand(action, policy));

        public Mutation AddContractEndpoint(ImportedContract contract, ContractEndpoint endpoint) =>
            AddStep(new ImportedContract.AddEndpointCommand(contract, endpoint));

        public Mutation RemoveContractEndpoint(ImportedContract contract, ContractEndpoint endpoint) =>
            AddStep(new ImportedContract.RemoveEndpointCommand(contract, endpoint));

        public Mutation AddContractFieldMap(ContractBinding binding, ContractFieldMap map) =>
            AddStep(new ContractBinding.AddFieldMapCommand(binding, map));

        public Mutation RemoveContractFieldMap(ContractBinding binding, ContractFieldMap map) =>
            AddStep(new ContractBinding.RemoveFieldMapCommand(binding, map));

        // ── Actor ────────────────────────────────────────────────────────────

        public Mutation SetActorSubjectProperty(Actor actor, Property? property) =>
            AddStep(new Actor.SetSubjectPropertyCommand(actor, property));

        public Mutation SetActorRoleClaimType(Actor actor, string? roleClaimType) =>
            AddStep(new Actor.SetRoleClaimTypeCommand(actor, roleClaimType));

        public Mutation AddActorClaimMapping(Actor actor, ActorClaimMapping mapping) =>
            AddStep(new Actor.AddClaimMappingCommand(actor, mapping));

        public Mutation RemoveActorClaimMapping(Actor actor, ActorClaimMapping mapping) =>
            AddStep(new Actor.RemoveClaimMappingCommand(actor, mapping));

        // ── Comments ──────────────────────────────────────────────────────
        public Mutation AddComment(DomainObject target, string comment) {
            AddStep(new AddCommentCommand(target, comment));
            return this;
        }

        // ── Execution ────────────────────────────────────────────────────────

        public AnalysisResult Apply(AnalysisResult? preMutationAnalysis = null) {
            return ApplyWithTrace(preMutationAnalysis).Analysis;
        }

        public DomainMutationExecutionResult ApplyWithTrace(AnalysisResult? preMutationAnalysis = null) {
            EnsureNotCompleted();

            lock (Domain._mutationLock) {
                EnsureNotCompleted();

                var appliedSteps = new Stack<DomainMutationCommand>();
                var stepTraces = new List<DomainMutationStepTrace>(_steps.Count);
                var started = Stopwatch.GetTimestamp();
                var rolledBack = false;

                try {
                    foreach (var step in _steps) {
                        step.Apply();
                        appliedSteps.Push(step);
                        stepTraces.Add(new DomainMutationStepTrace(
                            step.GetType().Name,
                            step.AffectedNodes.Select(static node => node.Id).Distinct().ToArray()));
                    }

                    var affectedNodes = _steps.SelectMany(static step => step.AffectedNodes).Distinct().ToArray();
                    var affectedNodeIds = affectedNodes.Select(static node => node.Id).Distinct().ToArray();

                    var analysis = preMutationAnalysis is null
                        ? _analyzer.Analyze(Domain)
                        : _analyzer.Analyze(Domain, preMutationAnalysis, affectedNodes);

                    if (analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) {
                        Rollback(appliedSteps);
                        rolledBack = true;
                    }

                    _completed = true;
                    var trace = new DomainMutationTrace(
                        stepTraces,
                        affectedNodeIds,
                        stepTraces.Count,
                        rolledBack,
                        !rolledBack,
                        Stopwatch.GetElapsedTime(started),
                        analysis.Diagnostics.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                        analysis.Diagnostics.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning));

                    return new DomainMutationExecutionResult(analysis, trace);
                }
                catch {
                    Rollback(appliedSteps);
                    _steps.Clear();
                    _completed = true;
                    throw;
                }
            }
        }

        private Mutation AddStep(DomainMutationCommand command) {
            ArgumentNullException.ThrowIfNull(command);

            lock (Domain._mutationLock) {
                EnsureNotCompleted();
                _steps.Add(command);
            }

            return this;
        }

        private void EnsureNotCompleted() {
            if (_completed) {
                throw new InvalidOperationException("Mutation transaction has already completed.");
            }
        }

        private static void Rollback(Stack<DomainMutationCommand> applied) {
            foreach (var step in applied) {
                step.Rollback();
            }
        }
    }
}