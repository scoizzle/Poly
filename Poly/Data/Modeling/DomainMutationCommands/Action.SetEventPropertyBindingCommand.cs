using Poly.Data.Modeling.Effects;

namespace Poly.Data.Modeling;

public sealed partial record Action {
    internal sealed record SetEventPropertyBindingCommand(
        Action Action,
        PublishEvent Effect,
        string EventPropertyName,
        EventPropertyBindingSource Source,
        EventPropertyBindingSource? PreviousSource) : DomainMutationCommand {

        public override void Apply() => Effect._bindings[EventPropertyName] = Source;

        public override void Rollback() {
            if (PreviousSource is null)
                Effect._bindings.Remove(EventPropertyName);
            else
                Effect._bindings[EventPropertyName] = PreviousSource;
        }

        public override IEnumerable<Node> AffectedNodes => [Action];
    }
}