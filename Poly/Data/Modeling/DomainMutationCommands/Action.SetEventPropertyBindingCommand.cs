using Poly.Data.Modeling.Effects;

namespace Poly.Data.Modeling;

public sealed partial record Action {
    internal sealed record SetEventPropertyBindingCommand(
        Action Action,
        PublishEvent Effect,
        string EventPropertyName,
        EventPropertyBindingSource Source) : DomainMutationCommand {
        private EventPropertyBindingSource? _previousSource;

        public override void Apply() {
            Effect._bindings.TryGetValue(EventPropertyName, out _previousSource);
            Effect._bindings[EventPropertyName] = Source;
        }

        public override void Rollback() {
            if (_previousSource is null)
                Effect._bindings.Remove(EventPropertyName);
            else
                Effect._bindings[EventPropertyName] = _previousSource;
        }

        public override IEnumerable<Node> AffectedNodes => [Action];
    }
}