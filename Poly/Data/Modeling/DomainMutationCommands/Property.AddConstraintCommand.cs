using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public sealed partial record Property {
    internal sealed record AddConstraintCommand(Property Property, Constraint Constraint) : DomainMutationCommand {
        public override void Apply() => Property._constraints.Add(Constraint);
        public override void Rollback() => Property._constraints.Remove(Constraint);
        public override IEnumerable<Node> AffectedNodes => [Property];
    }
}