using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// Registry of pack-provided annotation syntax handlers. Populated by packs at
/// registration time and consumed by <see cref="Parsing.PolyDslParser"/> (for
/// keyword validation) and <see cref="Parsing.DomainDslPrinter"/> (for facet printing).
/// </summary>
public sealed class AnnotationRegistry {
    private readonly Dictionary<string, IAnnotationSyntax> _syntaxByKeyword = new(StringComparer.Ordinal);
    private readonly List<System.Action<GrammarBuilder<DslToken, DslTokenKind>>> _grammarContributors = [];

    public AnnotationRegistry() {
    }

    public AnnotationRegistry(AnnotationRegistry source) {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var pair in source._syntaxByKeyword) {
            _syntaxByKeyword[pair.Key] = pair.Value;
        }
        _grammarContributors.AddRange(source._grammarContributors);
    }

    /// <summary>Registers a syntax handler. Throws on duplicate keyword.</summary>
    public void Register(IAnnotationSyntax syntax) {
        ArgumentNullException.ThrowIfNull(syntax);
        if (string.IsNullOrWhiteSpace(syntax.Keyword))
            throw new ArgumentException("Annotation keyword must be non-empty.", nameof(syntax));

        if (!_syntaxByKeyword.TryAdd(syntax.Keyword, syntax))
            throw new InvalidOperationException(
                $"Annotation keyword '{syntax.Keyword}' is already registered.");
    }

    /// <summary>
    /// GI-5: register extra grammar patterns (e.g. non-literal annotation args).
    /// Invoked when building the product <see cref="DslGrammar"/> for a parse session.
    /// </summary>
    public void RegisterGrammarContributor(System.Action<GrammarBuilder<DslToken, DslTokenKind>> contribute) {
        ArgumentNullException.ThrowIfNull(contribute);
        _grammarContributors.Add(contribute);
    }

    /// <summary>Applies all pack grammar contributors to <paramref name="builder"/>.</summary>
    public void ContributePatterns(GrammarBuilder<DslToken, DslTokenKind> builder) {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var contribute in _grammarContributors)
            contribute(builder);
    }

    /// <summary>True when the given keyword has a registered handler.</summary>
    public bool CanAccept(string keyword) =>
        _syntaxByKeyword.ContainsKey(keyword);

    /// <summary>
    /// Prints a facet if a handler is registered for it. Returns null otherwise.
    /// Generic <see cref="Annotation"/> values only use the handler registered for
    /// <see cref="Annotation.Name"/> — never another pack's printer.
    /// </summary>
    public string? TryPrint(Facet facet) {
        ArgumentNullException.ThrowIfNull(facet);

        if (facet is Annotation ann) {
            if (_syntaxByKeyword.TryGetValue(ann.Name, out var handler)
                && handler.TryPrint(ann, out var text)) {
                return text;
            }
            return null;
        }

        foreach (var syntax in _syntaxByKeyword.Values) {
            if (syntax.TryPrint(facet, out var text))
                return text;
        }
        return null;
    }

    internal IReadOnlyCollection<IAnnotationSyntax> GetRegisteredSyntaxes() =>
        _syntaxByKeyword.Values.ToArray();
}