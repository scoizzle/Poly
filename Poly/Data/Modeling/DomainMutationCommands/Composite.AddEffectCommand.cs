using Poly.Data.Modeling.Effects;

namespace Poly.Data.Modeling.Effects;

public sealed partial record Composite {
    internal sealed record AddEffectCommand(Composite Composite, Effect Effect) : DomainMutationCommand {
        public override void Apply() {
            if (!ReferenceEquals(Composite.Domain, Effect.Domain)) {
                throw new InvalidOperationException("Composite effect children must belong to the same domain.");
            }

            Composite._childEffects.Add(Effect);
        }

        public override void Rollback() => _ = DomainMutationCollection.RemoveAt(Composite._childEffects, Effect);

        public override IEnumerable<Node> AffectedNodes => [Composite, Effect];
    }
}