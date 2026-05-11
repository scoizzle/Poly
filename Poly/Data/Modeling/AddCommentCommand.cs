using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

/// <summary>
/// Mutation command to append a comment to a domain object.
/// </summary>
internal sealed record AddCommentCommand(DomainObject Target, string Comment) : DomainMutationCommand {
    private int? _index;
    public override void Apply() {
        _index = Target.Comments.Count;
        Target.AddComment(Comment);
    }
    public override void Rollback() {
        if (_index is int idx && idx < Target.Comments.Count)
            Target.RemoveCommentAt(idx);
    }
    public override IEnumerable<Node> AffectedNodes => [Target];
}