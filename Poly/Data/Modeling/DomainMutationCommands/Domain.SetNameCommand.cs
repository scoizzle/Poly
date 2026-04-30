namespace Poly.Data.Modeling;

public sealed partial record Domain {
    internal sealed record SetNameCommand(Domain Target, string Name) : DomainMutationCommand {
        private readonly string _previous = Target.Name;
        public override void Apply() => Target.Name = Guard.ThrowIfNullOrEmpty(Name);
        public override void Rollback() => Target.Name = _previous;
        public override IEnumerable<Node> AffectedNodes => [Target];
    }
}