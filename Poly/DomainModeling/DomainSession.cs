using Poly.DomainModeling.Packs;
using Poly.DomainModeling.Packs.Temporal;
using Poly.DomainModeling.Parsing;
using Poly.Grammar;

namespace Poly.DomainModeling;

/// <summary>
/// Compilation unit: Domain facts plus the concepts its <see cref="Domain.Extensions"/>
/// load (language tables, folds, meaning, type maps, artifacts). Not an MCP session.
/// </summary>
public sealed class DomainSession {
    public Domain? Domain { get; }

    public IReadOnlyList<string> Extensions { get; }

    public Language<DslToken, DslTokenKind> Language { get; }

    public DomainParserInputs ParserInputs { get; }

    public DomainAnalysisInputs Analysis { get; }

    public ExpressionFoldTable Folds { get; }

    public ExpressionMeaning Meaning { get; }

    public IReadOnlyList<IArtifactContributor> Artifacts { get; }

    private DomainSession(
        Domain? domain,
        IReadOnlyList<string> extensions,
        Language<DslToken, DslTokenKind> language,
        DomainParserInputs parserInputs,
        DomainAnalysisInputs analysis,
        ExpressionFoldTable folds,
        ExpressionMeaning meaning,
        IReadOnlyList<IArtifactContributor>? artifacts = null) {
        Domain = domain;
        Extensions = extensions;
        Language = language;
        ParserInputs = parserInputs;
        Analysis = analysis;
        Folds = folds;
        Meaning = meaning;
        Artifacts = artifacts ?? [];
    }

    /// <summary>Loads libraries for an existing domain's extension ids.</summary>
    public static DomainSession Open(
        Domain domain,
        ExtensionCatalog? catalog = null,
        bool failOnUnknown = false) {
        ArgumentNullException.ThrowIfNull(domain);
        return Create(domain, domain.Extensions, catalog, failOnUnknown);
    }

    /// <summary>Peeks <c>uses</c> (or <paramref name="seed"/>) and loads those libraries.</summary>
    public static DomainSession ForSource(
        string poly,
        IReadOnlyList<string> seed,
        ExtensionCatalog? catalog = null,
        bool failOnUnknown = false) {
        ArgumentNullException.ThrowIfNull(poly);
        ArgumentNullException.ThrowIfNull(seed);
        var ids = DomainCompilation.PeekExtensions(poly);
        if (ids.Count == 0)
            ids = seed;
        return Create(domain: null, ids, catalog, failOnUnknown);
    }

    /// <summary>Session from explicit parser inputs (tests / one-off parse).</summary>
    public static DomainSession FromInputs(DomainParserInputs? inputs) {
        var parserInputs = inputs ?? DomainParserInputs.Empty;
        var language = DslGrammar.LanguageFor(parserInputs);
        var meaning = language.Grammar.TryGetPattern("expr-primary", "now", out _)
            ? ExtensionCatalog.Core.Language.Meaning
            : ExpressionMeaning.Empty;
        return new DomainSession(
            domain: null,
            extensions: [],
            language,
            parserInputs,
            DomainAnalysisInputs.Empty,
            FoldsFor(language.Grammar, parserInputs.ExpressionForms),
            meaning);
    }

    /// <summary>Keeps tables when <c>uses</c> is unchanged; reloads when it changes.</summary>
    public DomainSession WithDomain(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        if (SameExtensions(Extensions, domain.Extensions))
            return new DomainSession(domain, domain.Extensions, Language, ParserInputs, Analysis, Folds, Meaning, Artifacts);
        return Open(domain);
    }

    private static DomainSession Create(
        Domain? domain,
        IReadOnlyList<string> ids,
        ExtensionCatalog? catalog,
        bool failOnUnknown) {
        var host = (catalog ?? ExtensionCatalog.Core).ResolveHost(ids, failOnUnknown);
        var language = DslGrammar.LanguageFor(host.Parser);
        return new DomainSession(
            domain,
            ids,
            language,
            host.Parser,
            host.Analysis,
            FoldsFor(language.Grammar, host.Parser.ExpressionForms),
            host.Meaning,
            host.Artifacts);
    }

    private static ExpressionFoldTable FoldsFor(Grammar<DslToken, DslTokenKind> grammar, ExpressionFormRegistry forms) {
        var folds = ExpressionFoldTable.Core();
        if (grammar.TryGetPattern("expr-primary", "now", out _))
            TemporalExpressionPrintBinders.RegisterFolds(folds);
        forms.ContributeFolds(folds);
        return folds;
    }

    private static bool SameExtensions(IReadOnlyList<string> left, IReadOnlyList<string> right) {
        if (left.Count != right.Count)
            return false;
        for (var i = 0; i < left.Count; i++) {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}