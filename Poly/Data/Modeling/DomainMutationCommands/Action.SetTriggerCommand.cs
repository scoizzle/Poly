namespace Poly.Data.Modeling;

public sealed partial record Action {
    internal sealed record SetTriggerCommand(Action Action, ActionTrigger Trigger, ActionTrigger PreviousTrigger) : DomainMutationCommand {
        public override void Apply() => Action._trigger = Trigger;

        public override void Rollback() => Action._trigger = PreviousTrigger;

        public override IEnumerable<Node> AffectedNodes => [Action];
    }
}