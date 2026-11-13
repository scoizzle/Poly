using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.Compile;

/// <summary>The outcome of one compile: every diagnostic, and the emitted files on success.</summary>
public sealed record CompileResult(
    string File,
    string? DomainName,
    IReadOnlyList<CompileDiagnostic> Diagnostics,
    IReadOnlyList<(string FileName, string Source)> Files,
    Domain? Domain,
    AnalysisResult? Analysis,
    DomainSession? Session,
    ArtifactCatalog? Catalog) {
    public bool Succeeded => !Diagnostics.Any(d => d.IsError);
}

/// <summary>
/// Drives <see cref="DomainSession"/> for one document: Header → Load → Parse → Evolve →
/// Analyze → Lower → Produce. It calls the session; it holds no analyzer or lowering of its
/// own. The libraries a document loads are the ones <see cref="LibrarySelection.Resolve"/> picks
/// from the caller's <see cref="SessionOptions"/>; the compiler never switches on an id.
/// </summary>
public sealed class PolyCompiler {
    private const string VmReportType = "vm-analysis-report";
    private const string CatalogReportType = "catalog-reference-report";

    private readonly SessionOptions _options;
    private readonly object _gate = new();
    private string? _sessionKey;
    private DomainSession? _session;

    /// <summary>
    /// A compiler over <paramref name="catalog"/>, selecting libraries as
    /// <paramref name="options"/> say. The catalog is the options' catalog; naming two is a
    /// caller error.
    /// </summary>
    public PolyCompiler(ExtensionCatalog catalog, SessionOptions options) {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);
        if (!ReferenceEquals(catalog, options.Catalog))
            throw new ArgumentException(
                "The catalog must be the one the session options resolve against.", nameof(catalog));
        _options = options with { Authoring = true };
    }

    /// <summary>The domain name when the document sets none. Defaults to <c>PolyDomain</c>.</summary>
    public string? DefaultDomainName { get; init; }

    /// <summary>
    /// Compiles <paramref name="text"/>. Writes nothing; <see cref="CompileResult.Files"/> is in memory.
    /// </summary>
    /// <param name="target">
    /// The host target, or null when the host names none. A target the catalog does not declare is
    /// <c>DSL0005</c>. It is a host fact: never read from the document's <c>uses</c> line.
    /// </param>
    public CompileResult Compile(
        string text, string file = "<input>", CancellationToken cancel = default, string? target = null) {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
            return DslLimits.RunOnParseStack(() => CompileCore(text, file, target, cancel, produce: true));
    }

    /// <summary>
    /// Header → Load → Parse → Evolve → Analyze, without Lower or Produce. For a host that keeps
    /// the analyzed domain (a workspace) and produces later through <see cref="Produce"/>.
    /// </summary>
    public CompileResult Analyze(
        string text, string file = "<input>", CancellationToken cancel = default, string? target = null) {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
            return DslLimits.RunOnParseStack(() => CompileCore(text, file, target, cancel, produce: false));
    }

    /// <summary>
    /// Lower and Produce for a domain a host already analyzed on <paramref name="session"/>. The
    /// fail-closed gate <see cref="Compile"/> runs: a refusal is a diagnostic, never an exception.
    /// </summary>
    public static CompileResult Produce(
        DomainSession session, Domain domain, AnalysisResult analysis, string file = "<domain>") {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);
        var build = new DiagnosticBuilder(file, "");
        var diags = new List<CompileDiagnostic>();
        var files = ProduceFiles(build, diags, session, domain, analysis);
        return new CompileResult(file, domain.Name, diags, files, domain, analysis, session, session.ArtifactCatalog);
    }

    private CompileResult CompileCore(
        string text, string file, string? target, CancellationToken cancel, bool produce) {
        var build = new DiagnosticBuilder(file, text);
        var diags = new List<CompileDiagnostic>();
        string? domainName = null;
        CompileResult Fail() => new(file, domainName, diags, [], null, null, null, null);

        // A host misconfiguration is independent of the document: report it first and alone.
        if (_options.Catalog.TargetError(target) is { } targetError) {
            diags.Add(build.Unplaced(Severities.Error, DiagnosticCodes.UnknownTarget, targetError));
            return Fail();
        }

        // A leading UTF-8 BOM is an encoding artifact the scanner skips; strip it here too, so a
        // BOM-only document reports the same empty-document error as an empty one rather than a
        // parse attempt on an invisible character.
        var body = text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
        if (string.IsNullOrWhiteSpace(body)) {
            diags.Add(build.Unplaced(Severities.Error, DiagnosticCodes.Parse, "DSL text is empty."));
            return Fail();
        }

        DslSyntaxTree tree;
        try {
            tree = DslSyntaxTree.Parse(text);
        }
        catch (GrammarException ex) {
            diags.Add(FromGrammar(build, ex));
            return Fail();
        }
        catch (FormatException ex) {
            diags.Add(HeaderFailure(build, text, ex));
            return Fail();
        }
        cancel.ThrowIfCancellationRequested();
        if (tree.Name.Length > 0)
            domainName = tree.Name;

        var listed = tree.Uses.Count > 0 ? tree.Uses : _options.ImplicitIds;
        if (!CheckExtensions(build, tree, listed, diags))
            return Fail();

        DomainSession session;
        List<DomainChange> changes;
        try {
            var requested = LibrarySelection.Resolve(tree.Uses, _options);
            // The resolver sees the ids the session loads, so the domain's extension facts, the
            // primitives they seed and their contract surfaces agree with the session whether
            // the document or the host named them.
            RejectDroppedPins(tree, requested);
            var selected = tree with { Uses = requested, UsesVersions = PinsFor(tree, requested) };
            session = SessionFor(selected, target);
            cancel.ThrowIfCancellationRequested();
            changes = DomainCompilation.Resolve(selected, session);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (GrammarException ex) {
            diags.Add(FromGrammar(build, ex));
            return Fail();
        }
        catch (FormatException ex) {
            diags.Add(build.Unplaced(Severities.Error, DiagnosticCodes.Parse, ex.Message));
            return Fail();
        }
        catch (Exception ex) {
            diags.Add(build.Unplaced(Severities.Error, DiagnosticCodes.Refused, ex.Message));
            return Fail();
        }

        if (changes.Count == 0) {
            diags.Add(build.Unplaced(Severities.Error, DiagnosticCodes.Parse, "No domain changes parsed from the DSL text."));
            return Fail();
        }

        domainName = changes.OfType<SetDomainNameChange>().FirstOrDefault()?.Name ?? DefaultDomainName ?? "PolyDomain";
        var seedDomain = new Domain(domainName, []);
        EvolutionResult outcome;
        try {
            outcome = new DomainEvolution(seedDomain).ApplyAndAnalyze(changes, session: session);
        }
        catch (Exception ex) {
            diags.Add(build.Unplaced(Severities.Error, DiagnosticCodes.Refused, $"Evolution failed: {ex.Message}"));
            return Fail();
        }
        cancel.ThrowIfCancellationRequested();

        var analysisDiagnostics = outcome.Analysis.Diagnostics;
        var root = outcome.Root;
        Domain? placing = root;
        if (!outcome.Succeeded) {
            // A rejected proposal returns the seed root, so the nodes analysis reported are not
            // under it. Re-run mutation and analysis on the proposal to get a root they are under.
            placing = null;
            var applied = new DomainEvolution(seedDomain).Apply(changes, session: session);
            if (applied.Succeeded) {
                var again = session.Analyze(applied.Root);
                if (again.Diagnostics.Count == analysisDiagnostics.Count) {
                    analysisDiagnostics = again.Diagnostics;
                    placing = applied.Root;
                }
            }
        }

        var outline = new Lazy<DslOutline>(() => DslOutline.Build(text));
        foreach (var diagnostic in analysisDiagnostics)
            diags.Add(PlaceAnalysis(build, outline, placing, domainName, diagnostic));
        foreach (var mutation in outcome.MutationErrors)
            diags.Add(build.Unplaced(Severities.Error, DiagnosticCodes.Refused, mutation, domainName));

        if (!outcome.Succeeded) {
            if (!diags.Any(d => d.IsError))
                diags.Add(build.Unplaced(Severities.Error, DiagnosticCodes.Refused, "Analysis rejected the domain.", domainName));
            return new CompileResult(file, domainName, diags, [], null, outcome.Analysis, session, null);
        }

        if (!produce)
            return new CompileResult(file, domainName, diags, [], outcome.Root, outcome.Analysis, session, null);

        // A contributor never runs over an analysis that already has errors (returned above).
        var files = ProduceFiles(build, diags, session, outcome.Root, outcome.Analysis);
        return new CompileResult(file, domainName, diags, files, outcome.Root, outcome.Analysis, session, session.ArtifactCatalog);
    }

    /// <summary>
    /// The fail-closed Lower/VM gate: Emit lowers, analyzes the module, and produces. Findings from
    /// the VM and catalog-reference reports join <paramref name="diags"/>; files come back only
    /// when nothing in <paramref name="diags"/> is an error.
    /// </summary>
    private static IReadOnlyList<(string FileName, string Source)> ProduceFiles(
        DiagnosticBuilder build,
        List<CompileDiagnostic> diags,
        DomainSession session,
        Domain domain,
        AnalysisResult analysis) {
        var before = session.ArtifactCatalog;
        IReadOnlyList<(string FileName, string Source)> files = [];
        Exception? refusal = null;
        try {
            files = session.Emit(domain, analysis).ToList();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            refusal = ex;
        }

        var catalog = session.ArtifactCatalog;
        var vmErrors = false;
        if (!ReferenceEquals(before, catalog)) {
            foreach (var finding in ReportOf(catalog, domain.Name, VmReportType)) {
                diags.Add(FromFinding(build, finding));
                vmErrors |= finding.Severity == DiagnosticSeverity.Error;
            }
            foreach (var finding in ReportOf(catalog, domain.Name, CatalogReportType))
                diags.Add(FromFinding(build, finding));
        }
        if (refusal is not null && !vmErrors)
            diags.Add(build.Unplaced(Severities.Error, DiagnosticCodes.Refused, refusal.Message, domain.Name));

        return refusal is null && !diags.Any(d => d.IsError) ? files : [];
    }

    private DomainSession SessionFor(DslSyntaxTree selected, string? target) {
        var ids = selected.Uses;
        var key = string.Join('\n', ids) + "\ntarget:" + (target ?? "");
        if (_session is not null && _sessionKey == key)
            return _session;
        var session = LibrarySelection.Load(ids, _options).WithTarget(target);
        _session = session;
        _sessionKey = key;
        return session;
    }

    /// <summary>
    /// Authored pins follow their id, not their position: selection may drop, replace or add ids,
    /// which shifts the ones after. A selected id the document did not write has no pin.
    /// </summary>
    private static List<string?> PinsFor(DslSyntaxTree tree, IReadOnlyList<string> ids) =>
        ids.Select(id => {
            var authored = tree.Uses.ToList().IndexOf(id);
            return authored >= 0 && authored < tree.UsesVersions.Count ? tree.UsesVersions[authored] : null;
        }).ToList();

    /// <summary>
    /// A pin on an authored id that selection dropped or replaced (generic <c>persistence</c>
    /// under a vendor) has no surface to pin: the resolver's own refusal, kept for the id it
    /// never sees.
    /// </summary>
    private static void RejectDroppedPins(DslSyntaxTree tree, IReadOnlyList<string> selected) {
        for (var i = 0; i < tree.Uses.Count && i < tree.UsesVersions.Count; i++) {
            if (tree.UsesVersions[i] is { } pin && !selected.Contains(tree.Uses[i], StringComparer.Ordinal))
                throw new FormatException(
                    $"Extension '{tree.Uses[i]}' pins surface version '{pin}' but contributed no contract.");
        }
    }

    private bool CheckExtensions(
        DiagnosticBuilder build, DslSyntaxTree tree, IReadOnlyList<string> ids, List<CompileDiagnostic> diags) {
        var ok = true;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids) {
            if (!seen.Add(id)) {
                diags.Add(ExtensionDiagnostic(build, tree, id, occurrence: 2, Severities.Error,
                    DiagnosticCodes.DuplicateExtension, $"Domain lists extension '{id}' more than once."));
                ok = false;
                continue;
            }
            if (!_options.Catalog.Contains(id)) {
                diags.Add(ExtensionDiagnostic(build, tree, id, occurrence: 1, Severities.Error,
                    DiagnosticCodes.UnknownExtension,
                    $"Unknown domain extension '{id}'. Record only ids this catalog can resolve."));
                ok = false;
            }
        }
        return ok;
    }

    private static CompileDiagnostic ExtensionDiagnostic(
        DiagnosticBuilder build, DslSyntaxTree tree, string id, int occurrence, string severity, string code, string message) {
        var count = 0;
        for (var i = 0; i + 1 < tree.Tokens.Count; i++) {
            if (tree.Tokens[i].Kind != DslTokenKind.Uses || tree.Tokens[i + 1].Text != id)
                continue;
            if (++count == occurrence) {
                var token = tree.Tokens[i + 1];
                return build.Span(token.Start, token.Length, severity, code, message, "");
            }
        }
        return build.Unplaced(severity, code, message, "");
    }

    private static CompileDiagnostic FromGrammar(DiagnosticBuilder build, GrammarException ex) {
        if (ex.Line < 1)
            return build.Unplaced(Severities.Error, DiagnosticCodes.Parse, ex.Detail);
        var offset = ex.Offset >= 0 ? ex.Offset : build.Lines.OffsetOf(ex.Line, ex.Column);
        var length = ex.Length > 0 ? ex.Length : build.WordLength(offset);
        return build.Span(offset, length, Severities.Error, DiagnosticCodes.Parse, ex.Detail, "");
    }

    /// <summary>
    /// A <see cref="FormatException"/> with no position came out of header parsing. Re-read the header
    /// tokens to tell a duplicate <c>uses</c> id from any other header problem.
    /// </summary>
    private static CompileDiagnostic HeaderFailure(DiagnosticBuilder build, string text, FormatException ex) {
        var reader = new DslTokenReader(text);
        var tokens = new List<DslToken>();
        while (true) {
            var token = reader.Peek();
            tokens.Add(token);
            if (token.Kind == DslTokenKind.EndOfFile)
                break;
            reader.Consume(1);
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 2; DslSyntaxTree.TryReadUse(tokens, i, out var id, out _, out var next); i = next) {
            if (!seen.Add(id)) {
                var token = tokens[i + 1];
                return build.Span(token.Start, token.Length, Severities.Error,
                    DiagnosticCodes.DuplicateExtension, ex.Message, "");
            }
        }
        return build.Unplaced(Severities.Error, DiagnosticCodes.Parse, ex.Message);
    }

    private static CompileDiagnostic PlaceAnalysis(
        DiagnosticBuilder build, Lazy<DslOutline> outline, Domain? root, string domainName, Diagnostic diagnostic) {
        var severity = SeverityOf(diagnostic.Severity);
        var code = diagnostic.Code ?? DiagnosticCodes.AnalysisUncoded;
        if (root is null)
            return build.Unplaced(severity, code, diagnostic.Message, "");
        var path = DomainElementPath.Resolve(root, diagnostic.Node);
        var node = SpanBridge.Locate(outline.Value, path);
        if (node is null)
            return build.Unplaced(severity, code, diagnostic.Message, path);
        var length = string.Equals(build.TextAt(node.StartOffset, node.Name.Length), node.Name, StringComparison.Ordinal)
            ? node.Name.Length
            : 1;
        return build.Span(node.StartOffset, length, severity, code, diagnostic.Message, path);
    }

    private static CompileDiagnostic FromFinding(DiagnosticBuilder build, AnalysisFinding finding) =>
        build.Unplaced(SeverityOf(finding.Severity), finding.Code ?? DiagnosticCodes.AnalysisUncoded,
            finding.Message, finding.ElementPath);

    private static IEnumerable<AnalysisFinding> ReportOf(ArtifactCatalog catalog, string domain, string type) =>
        catalog.Find(ArtifactId.Create([domain], type))?.Payload is AnalysisReport report ? report.Findings : [];

    private static string SeverityOf(DiagnosticSeverity severity) => severity switch {
        DiagnosticSeverity.Error => Severities.Error,
        DiagnosticSeverity.Warning => Severities.Warning,
        DiagnosticSeverity.Information => Severities.Information,
        _ => Severities.Hint,
    };
}