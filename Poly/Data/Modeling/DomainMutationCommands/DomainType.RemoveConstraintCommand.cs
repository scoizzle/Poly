using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling.TypeSystem;

public abstract partial record DomainType {
    internal sealed record RemoveConstraintCommand(DomainType Type, Constraint Constraint) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Type._constraints, Constraint);
        public override void Rollback() => DomainMutationCollection.Restore(Type._constraints, Constraint, _index);
        public override IEnumerable<Node> AffectedNodes => [Type];
    }
}