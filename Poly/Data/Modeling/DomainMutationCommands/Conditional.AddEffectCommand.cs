using Poly.Data.Modeling.Effects;

namespace Poly.Data.Modeling.Effects;

public sealed partial record Conditional {
    internal sealed record AddEffectCommand(Conditional Conditional, Effect Effect) : DomainMutationCommand {
        public override void Apply() {
            if (!ReferenceEquals(Conditional.Domain, Effect.Domain)) {
                throw new InvalidOperationException("Conditional effect children must belong to the same domain.");
            }

            Conditional._childEffects.Add(Effect);
        }

        public override void Rollback() => _ = DomainMutationCollection.RemoveAt(Conditional._childEffects, Effect);

        public override IEnumerable<Node> AffectedNodes => [Conditional, Effect];
    }
}