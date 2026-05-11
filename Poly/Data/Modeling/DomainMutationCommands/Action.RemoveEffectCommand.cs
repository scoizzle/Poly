using Poly.Data.Modeling.Effects;
namespace Poly.Data.Modeling;

public sealed partial record Action {
    internal sealed record RemoveEffectCommand(Action Action, Effect Effect) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Action._effects, Effect);
        public override void Rollback() => DomainMutationCollection.Restore(Action._effects, Effect, _index);
        public override IEnumerable<Node> AffectedNodes => [Action];
    }
}