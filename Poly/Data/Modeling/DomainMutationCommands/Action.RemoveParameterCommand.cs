namespace Poly.Data.Modeling;

public sealed partial record Action {
    internal sealed record RemoveParameterCommand(Action Action, Property Parameter) : DomainMutationCommand {
        public override void Apply() => Action._parameters.Remove(Parameter);
        public override void Rollback() => Action._parameters.Add(Parameter);
        public override IEnumerable<Node> AffectedNodes => [Action, Parameter];
    }
}