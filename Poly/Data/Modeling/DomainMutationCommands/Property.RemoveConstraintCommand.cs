using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public sealed partial record Property {
    internal sealed record RemoveConstraintCommand(Property Property, Constraint Constraint) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Property._constraints, Constraint);
        public override void Rollback() => DomainMutationCollection.Restore(Property._constraints, Constraint, _index);
        public override IEnumerable<Node> AffectedNodes => [Property];
    }
}