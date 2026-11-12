using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.Compile;

/// <summary>
/// Optional host rewrites of the extension id lists a compile uses. The compiler never
/// switches on an id itself; a host that needs to (for example a legacy facade) says so here.
/// </summary>
public sealed class CompileHooks {
    /// <summary>Rewrites the ids the session loads (document <c>uses</c> or the seed).</summary>
    public Func<IReadOnlyList<string>, IReadOnlyList<string>>? SessionExtensions { get; init; }

    /// <summary>Rewrites the ids stamped on the <see cref="Domain"/>.</summary>
    public Func<IReadOnlyList<string>, IReadOnlyList<string>>? DomainExtensions { get; init; }

    /// <summary>The domain name when the document sets none. Defaults to <c>PolyDomain</c>.</summary>
    public string? DefaultDomainName { get; init; }
}

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
/// own. Extension ids come from the caller's <see cref="ExtensionCatalog"/> and a named seed.
/// </summary>
public sealed class PolyCompiler {
    private const string VmReportType = "vm-analysis-report";
    private const string CatalogReportType = "catalog-reference-report";

    private readonly SessionOptions _options;
    private readonly CompileHooks? _hooks;
    private readonly object _gate = new();
    private string? _sessionKey;
    private DomainSession? _session;

    public PolyCompiler(ExtensionCatalog catalog, string seed = "authoring", CompileHooks? hooks = null)
        : this(catalog, ExtensionCatalog.Seed(seed), hooks) { }

    public PolyCompiler(ExtensionCatalog catalog, IReadOnlyList<string> seedIds, CompileHooks? hooks = null) {
        ArgumentNullException.ThrowIfNull(seedIds);
        _options = new SessionOptions(
            catalog ?? throw new ArgumentNullException(nameof(catalog)), seedIds) { Authoring = true };
        _hooks = hooks;
    }

    /// <summary>
    /// Compiles <paramref name="text"/>. Writes nothing; <see cref="CompileResult.Files"/> is in memory.
    /// </summary>
    /// <param name="target">
    /// The host target (<c>posix</c>, <c>windows</c>), or null when the host names none. It is a
    /// host fact: never read from the document's <c>uses</c> line.
    /// </param>
    public CompileResult Compile(
        string text, string file = "<input>", CancellationToken cancel = default, string? target = null) {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
            return CompileCore(text, file, target, cancel, produce: true);
    }

    /// <summary>
    /// Header → Load → Parse → Evolve → Analyze, without Lower or Produce. For a host that keeps
    /// the analyzed domain (a workspace) and produces later through <see cref="Produce"/>.
    /// </summary>
    public CompileResult Analyze(
        string text, string file = "<input>", CancellationToken cancel = default, string? target = null) {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
            return CompileCore(text, file, target, cancel, produce: false);
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

        var requested = tree.Uses.Count > 0 ? tree.Uses : _options.ImplicitIds;
        var sessionIds = _hooks?.SessionExtensions?.Invoke(requested) ?? requested;
        if (!CheckExtensions(build, tree, sessionIds, diags))
            return Fail();

        DomainSession session;
        List<DomainChange> changes;
        try {
            session = SessionFor(tree, sessionIds, target);
            cancel.ThrowIfCancellationRequested();
            changes = DomainCompilation.WithSeed(
                DomainCompilation.Resolve(tree, session), _options.ImplicitIds, session).ToList();
            if (_hooks?.DomainExtensions is { } rewrite)
                changes = RewriteExtensions(changes, rewrite);
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

        domainName = changes.OfType<SetDomainNameChange>().FirstOrDefault()?.Name ?? _hooks?.DefaultDomainName ?? "PolyDomain";
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

    private DomainSession SessionFor(DslSyntaxTree tree, IReadOnlyList<string> ids, string? target) {
        var key = string.Join('\n', ids) + "\ntarget:" + (target ?? "");
        if (_session is not null && _sessionKey == key)
            return _session;
        // A session-extension hook may rewrite the ids. Keep an authored pin only where the
        // id at that position is unchanged; a rewritten id has no authored surface pin.
        var versions = ids.Select((id, index) =>
            index < tree.Uses.Count
            && index < tree.UsesVersions.Count
            && string.Equals(tree.Uses[index], id, StringComparison.Ordinal)
                ? tree.UsesVersions[index]
                : null).ToList();
        var session = LibrarySelection.Load(tree with { Uses = ids, UsesVersions = versions }, _options)
            .WithTarget(target);
        _session = session;
        _sessionKey = key;
        return session;
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
                return build.Span(token.Offset, token.Length, severity, code, message, "");
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
                return build.Span(token.Offset, token.Length, Severities.Error,
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

    /// <summary>
    /// Replaces the document's extension facts with the rewritten list, placed where the first
    /// original fact was. Other changes keep their order.
    /// </summary>
    private static List<DomainChange> RewriteExtensions(
        List<DomainChange> changes, Func<IReadOnlyList<string>, IReadOnlyList<string>> rewrite) {
        var original = changes.OfType<AddDomainExtensionChange>().Select(c => c.ExtensionId).ToList();
        if (original.Count == 0)
            return changes;
        var rewritten = rewrite(original);
        var result = new List<DomainChange>(changes.Count);
        var placed = false;
        foreach (var change in changes) {
            if (change is not AddDomainExtensionChange) {
                result.Add(change);
                continue;
            }
            if (placed)
                continue;
            placed = true;
            result.AddRange(rewritten.Select(id => new AddDomainExtensionChange(id)));
        }
        return result;
    }
}