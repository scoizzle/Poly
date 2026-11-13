using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// Concept bindings on product expression shapes: folds (existing tokens → IR)
/// and print mappings. Not a place to add new language productions.
/// </summary>
public sealed class ExpressionFormRegistry {
    private readonly List<System.Action<GrammarBuilder<DslToken, DslTokenKind>>> _grammarContributors = [];
    private readonly List<IExpressionPrintMapping> _printMappings = [];
    private readonly List<(DslTokenKind Op, Func<DomainExpression, DomainExpression, DomainExpression?> Claim)> _binaryClaims = [];
    private readonly List<(string? Rule, string Pattern, Func<MatchResult<DslToken, DslTokenKind>, DomainExpression> Fold)> _folds = [];
    private readonly Dictionary<string, Func<DomainExpression>> _idents = new(StringComparer.Ordinal);
    private Func<long, string, DomainExpression>? _durationClaim;

    public ExpressionFormRegistry() {
    }

    public ExpressionFormRegistry(ExpressionFormRegistry source) {
        ArgumentNullException.ThrowIfNull(source);
        _grammarContributors.AddRange(source._grammarContributors);
        _printMappings.AddRange(source._printMappings);
        _binaryClaims.AddRange(source._binaryClaims);
        _folds.AddRange(source._folds);
        foreach (var (name, fold) in source._idents)
            _idents[name] = fold;
        _durationClaim = source._durationClaim;
    }

    /// <summary>Fold for a pattern name in every rule that uses it.</summary>
    public void RegisterFold(string pattern, Func<MatchResult<DslToken, DslTokenKind>, DomainExpression> fold) {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(fold);
        _folds.Add((null, pattern, fold));
    }

    /// <summary>Fold for one (rule, pattern). Prefer <see cref="RegisterFold(string, Func{MatchResult{DslToken, DslTokenKind}, DomainExpression})"/>.</summary>
    public void RegisterFold(string rule, string pattern, Func<MatchResult<DslToken, DslTokenKind>, DomainExpression> fold) {
        ArgumentException.ThrowIfNullOrWhiteSpace(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(fold);
        _folds.Add((rule, pattern, fold));
    }

    /// <summary>Claims an identifier spelling (e.g. <c>Now</c>) as library IR.</summary>
    public void RegisterIdent(string text, Func<DomainExpression> fold) {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(fold);
        if (!_idents.TryAdd(text, fold))
            throw new InvalidOperationException($"An ident fold for '{text}' is already registered.");
    }

    /// <summary>Claims a parsed duration spelling. One claim per registry.</summary>
    public void RegisterDurationClaim(Func<long, string, DomainExpression> claim) {
        ArgumentNullException.ThrowIfNull(claim);
        if (_durationClaim is not null)
            throw new InvalidOperationException("A duration claim is already registered.");
        _durationClaim = claim;
    }

    /// <summary>Applies the duration claim, or returns false when none is registered.</summary>
    public bool TryClaimDuration(long amount, string unit, out DomainExpression expression) {
        if (_durationClaim is null) {
            expression = null!;
            return false;
        }
        expression = _durationClaim(amount, unit);
        return true;
    }

    public bool TryRewriteIdent(string text, out DomainExpression expression) {
        if (_idents.TryGetValue(text, out var fold)) {
            expression = fold();
            return true;
        }
        expression = null!;
        return false;
    }

    public void ContributeFolds(ExpressionFoldTable table) {
        ArgumentNullException.ThrowIfNull(table);
        foreach (var (rule, pattern, fold) in _folds) {
            if (rule is null)
                table.Register(pattern, fold);
            else
                table.Register(rule, pattern, fold);
        }
    }

    /// <summary>Registers an IR → Grammar print mapping.</summary>
    public void RegisterPrintMapping(IExpressionPrintMapping mapping) {
        ArgumentNullException.ThrowIfNull(mapping);
        _printMappings.Add(mapping);
    }

    /// <summary>Patterns on existing expression rules (e.g. ident / number+ident).</summary>
    public void RegisterGrammarContributor(System.Action<GrammarBuilder<DslToken, DslTokenKind>> contribute) {
        ArgumentNullException.ThrowIfNull(contribute);
        _grammarContributors.Add(contribute);
    }

    /// <summary>
    /// Registers a library claim on a binary operator's row of the operator table: given the parsed operands it returns the
    /// replacement expression, or null to keep the row's plain node. Claims run in registration order and the first
    /// non-null result wins. A pack-owned specialization (temporal <c>Now - 12 days</c> → <c>DateOperation</c>) registers here
    /// so the core parser never names pack IR. Throws when <paramref name="op"/> has no binary row. Register before the session
    /// builds: a table snapshots the claims when <see cref="DslOperatorTable.Core"/> builds it, and a later registration is ignored.
    /// The claim applies to the plain row of <paramref name="op"/>, not to its follow-word variants (<c>is</c> versus <c>is not</c>).
    /// </summary>
    public void RegisterBinaryClaim(DslTokenKind op, Func<DomainExpression, DomainExpression, DomainExpression?> claim) {
        ArgumentNullException.ThrowIfNull(claim);
        DslOperatorTable.RequireCoreBinaryRow(op);
        _binaryClaims.Add((op, claim));
    }

    /// <summary>One combined claim per operator, in registration order, for <see cref="DslOperatorTable.Core"/>.</summary>
    internal IReadOnlyDictionary<DslTokenKind, Func<DomainExpression, DomainExpression, DomainExpression?>> BinaryClaims() =>
        _binaryClaims
            .GroupBy(c => c.Op)
            .ToDictionary(
                g => g.Key,
                g => {
                    var claims = g.Select(c => c.Claim).ToArray();
                    return new Func<DomainExpression, DomainExpression, DomainExpression?>((l, r) => {
                        foreach (var claim in claims) {
                            if (claim(l, r) is { } result)
                                return result;
                        }
                        return null;
                    });
                });

    internal bool HasGrammarContributors => _grammarContributors.Count > 0;

    public void ContributeGrammarPatterns(GrammarBuilder<DslToken, DslTokenKind> builder) {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var c in _grammarContributors)
            c(builder);
    }

    /// <summary>Registers this library's print mappings (before core mappings).</summary>
    public void ContributePrintMappings(ExpressionPrintRegistry registry) {
        ArgumentNullException.ThrowIfNull(registry);
        foreach (var mapping in _printMappings)
            registry.Register(mapping);
    }
}