namespace Poly.Data.Modeling;

public sealed partial record Actor {
    internal sealed record SetSubjectPropertyCommand(Actor Actor, Property? Property) : DomainMutationCommand {
        private readonly Property? _previous = Actor.SubjectProperty;

        public override void Apply() => Actor.SubjectProperty = Property;
        public override void Rollback() => Actor.SubjectProperty = _previous;
        public override IEnumerable<Node> AffectedNodes => [Actor];
    }
}