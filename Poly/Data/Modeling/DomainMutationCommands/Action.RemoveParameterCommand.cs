namespace Poly.Data.Modeling;

public sealed partial record Action {
    internal sealed record RemoveParameterCommand(Action Action, Property Parameter) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Action._parameters, Parameter);
        public override void Rollback() => DomainMutationCollection.Restore(Action._parameters, Parameter, _index);
        public override IEnumerable<Node> AffectedNodes => [Action, Parameter];
    }
}