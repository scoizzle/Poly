using Poly.Data.Modeling.Effects;
namespace Poly.Data.Modeling;

public sealed partial record Action {
    internal sealed record RemoveEffectCommand(Action Action, Effect Effect) : DomainMutationCommand {
        public override void Apply() => Action._effects.Remove(Effect);
        public override void Rollback() => Action._effects.Add(Effect);
        public override IEnumerable<Node> AffectedNodes => [Action];
    }
}