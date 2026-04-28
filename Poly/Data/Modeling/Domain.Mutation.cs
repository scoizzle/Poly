using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public sealed partial record Domain {
    public sealed record MutationStep(string MutationName, System.Action Apply, System.Action Rollback);

    public Mutation CreateMutation(DomainModelAnalyzer? analyzer = null) => new Mutation(this, analyzer ?? new DomainModelAnalyzer());

    public sealed class Mutation {
        private readonly Domain _domain;
        private readonly DomainModelAnalyzer _analyzer;
        private readonly List<MutationStep> _steps = [];
        private readonly HashSet<Node> _analysisRoots = [];
        private bool _completed;

        internal Mutation(Domain domain, DomainModelAnalyzer analyzer) {
            ArgumentNullException.ThrowIfNull(domain);
            ArgumentNullException.ThrowIfNull(analyzer);

            _domain = domain;
            _analyzer = analyzer;
        }

        public Domain Domain => _domain;

        public Mutation SetDomainName(string name) => Track(Domain).AddStep(Domain.CreateSetNameMutation(Domain, name));

        public Mutation AddType(DomainType type) => Track(type).AddStep(Domain.CreateAddTypeMutation(Domain, type));

        public Mutation AddRelationship(Relationship relationship) => Track(relationship).AddStep(Domain.CreateAddRelationshipMutation(Domain, relationship));

        public Mutation SetRelationship(Relationship relationship, Entity source, Entity target, RelationshipCardinality cardinality, bool sourceOwnsTarget)
            => Track(relationship).AddStep(Relationship.CreateSetShapeMutation(relationship, source, target, cardinality, sourceOwnsTarget));

        public Mutation AddProperty(Entity entity, Property property) => Track(entity).Track(property).AddStep(Entity.CreateAddPropertyMutation(entity, property));

        public Mutation AddStage(Entity entity, Stage stage) => Track(entity).Track(stage).AddStep(Entity.CreateAddStageMutation(entity, stage));

        public Mutation AddPolicy(Entity entity, Policy policy) => Track(entity).Track(policy).AddStep(Entity.CreateAddPolicyMutation(entity, policy));

        public Mutation AddAction(Entity entity, Action action) => Track(entity).Track(action).AddStep(Entity.CreateAddActionMutation(entity, action));

        public Mutation AddEvent(Entity entity, Event @event) => Track(entity).Track(@event).AddStep(Entity.CreateAddEventMutation(entity, @event));

        public Mutation AddEntityRelationship(Entity entity, Relationship relationship) => Track(entity).Track(relationship).AddStep(Entity.CreateAddRelationshipMutation(entity, relationship));

        public Mutation AddPolicy(Stage stage, Policy policy) => Track(stage).Track(policy).AddStep(Stage.CreateAddPolicyMutation(stage, policy));

        public Mutation AddAction(Stage stage, Action action) => Track(stage).Track(action).AddStep(Stage.CreateAddActionMutation(stage, action));

        public Mutation AddRule(Policy policy, IPolicyRule rule) => Track(policy).AddStep(Policy.CreateAddRuleMutation(policy, rule));

        public Mutation AddConstraint(Property property, Constraint constraint) => Track(property).AddStep(Property.CreateAddConstraintMutation(property, constraint));

        public Mutation AddPolicy(Property property, Policy policy) => Track(property).Track(policy).AddStep(Property.CreateAddPolicyMutation(property, policy));

        public Mutation AddProperty(Event @event, Property property) => Track(@event).Track(property).AddStep(Event.CreateAddPropertyMutation(@event, property));

        public Mutation AddParameter(Action action, Property parameter) => Track(action).Track(parameter).AddStep(Action.CreateAddParameterMutation(action, parameter));

        public Mutation AddEffect(Action action, Effect effect) => Track(action).AddStep(Action.CreateAddEffectMutation(action, effect));

        internal AnalysisResult ExecuteValidatedMutation(string mutationName, System.Action apply, System.Action rollback) {
            ArgumentNullException.ThrowIfNull(mutationName);
            ArgumentNullException.ThrowIfNull(apply);
            ArgumentNullException.ThrowIfNull(rollback);

            return ExecuteValidatedMutation(new MutationStep(mutationName, apply, rollback));
        }

        internal AnalysisResult ExecuteValidatedMutation(MutationStep mutationStep) {
            AddStep(mutationStep);

            lock (Domain._mutationLock) {
                EnsureNotCompleted();
                return _analyzer.AnalyzeDomain(Domain);
            }
        }

        private Mutation AddStep(MutationStep step) {
            ArgumentNullException.ThrowIfNull(step);

            lock (Domain._mutationLock) {
                EnsureNotCompleted();
                _steps.Add(step);
            }

            return this;
        }

        public AnalysisResult Apply() {
            EnsureNotCompleted();

            lock (Domain._mutationLock) {
                EnsureNotCompleted();

                var appliedSteps = new Stack<MutationStep>();

                try {
                    foreach (var step in _steps) {
                        step.Apply();
                        appliedSteps.Push(step);
                    }

                    var analysis = _analyzer.AnalyzeDomain(Domain);
                    var trackedDiagnostics = AnalyzeTrackedRoots();
                    var diagnostics = trackedDiagnostics.Count == 0
                        ? analysis.Diagnostics
                        : [.. analysis.Diagnostics, .. trackedDiagnostics];

                    if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) {
                        RollbackAllAppliedSteps();
                        _completed = true;
                        throw new DomainMutationValidationException("MutationApply", diagnostics);
                    }

                    _completed = true;
                    return analysis;
                }
                catch {
                    RollbackAllAppliedSteps();
                    _steps.Clear();
                    _completed = true;
                    throw;
                }

                void RollbackAllAppliedSteps() {
                    foreach (var step in appliedSteps) {
                        step.Rollback();
                    }
                }
            }
        }

        private List<Diagnostic> AnalyzeTrackedRoots() {
            var diagnostics = new List<Diagnostic>();

            foreach (var root in _analysisRoots) {
                var analysis = _analyzer.Analyze(root);
                if (analysis.Diagnostics.Count > 0) {
                    diagnostics.AddRange(analysis.Diagnostics);
                }
            }

            return diagnostics;
        }

        private Mutation Track(Node root) {
            ArgumentNullException.ThrowIfNull(root);
            _analysisRoots.Add(root);
            return this;
        }

        private void EnsureNotCompleted() {
            if (_completed) {
                throw new InvalidOperationException("Mutation transaction has already completed.");
            }
        }
    }
}