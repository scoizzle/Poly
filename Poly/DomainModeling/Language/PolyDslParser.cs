using Poly.DomainModeling.Evolution;

namespace Poly.DomainModeling.Language;

/// <summary>
/// Facade over the two stages: <see cref="DslSyntaxTree.Parse"/> the text, then
/// <see cref="DomainResolver.Resolve"/> with the session's library meaning.
/// Producers that must load between the stages call those two types directly.
/// </summary>
public sealed class PolyDslParser {
    private readonly DslSyntaxTree _tree;
    private readonly DomainSession _session;

    public PolyDslParser(string text) : this(text, DomainSession.ForExtensions([])) {
    }

    public PolyDslParser(string text, DomainSession session) {
        ArgumentNullException.ThrowIfNull(session);
        _tree = DslSyntaxTree.Parse(text);
        _session = session;
    }

    /// <summary>
    /// Returns a list of <see cref="DomainChange"/> that, when applied via
    /// evolution, produces the declared domain.
    /// </summary>
    public List<DomainChange> Parse() => new DomainResolver(_tree, _session).Resolve();
}
