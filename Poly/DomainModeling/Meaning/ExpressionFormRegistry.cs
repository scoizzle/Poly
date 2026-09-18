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
    private readonly List<IBinaryExpressionFold> _binaryFolds = [];
    private readonly List<(string? Rule, string Pattern, Func<MatchResult<DslToken, DslTokenKind>, DomainExpression> Fold)> _folds = [];
    private readonly Dictionary<string, Func<DomainExpression>> _idents = new(StringComparer.Ordinal);

    public ExpressionFormRegistry() {
    }

    public ExpressionFormRegistry(ExpressionFormRegistry source) {
        ArgumentNullException.ThrowIfNull(source);
        _grammarContributors.AddRange(source._grammarContributors);
        _printMappings.AddRange(source._printMappings);
        _binaryFolds.AddRange(source._binaryFolds);
        _folds.AddRange(source._folds);
        foreach (var (name, fold) in source._idents)
            _idents[name] = fold;
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
    /// Registers a binary fold: given a parsed <c>left</c> and <c>right</c> operand and
    /// whether the operator was <c>+</c> (vs <c>-</c>), returns a replacement expression
    /// or null to keep the plain arithmetic node. Pack-owned binary specializations
    /// (e.g. temporal <c>Now - 12 days</c> → <c>DateOperation</c>) register here so the
    /// core parser never names pack IR.
    /// </summary>
    public void RegisterBinaryFold(IBinaryExpressionFold fold) {
        ArgumentNullException.ThrowIfNull(fold);
        _binaryFolds.Add(fold);
    }

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

    /// <summary>
    /// Applies registered binary folds in order; the first non-null result wins. Returns
    /// null when no fold claims the pair (caller keeps the plain arithmetic node).
    /// </summary>
    public DomainExpression? TryFoldBinary(DomainExpression left, DomainExpression right, bool isPlus) {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        foreach (var fold in _binaryFolds) {
            var result = fold.TryFold(left, right, isPlus);
            if (result is not null)
                return result;
        }
        return null;
    }

}

/// <summary>
/// Pack-owned binary expression fold (parse time). Given a parsed left/right operand and
/// whether the operator is <c>+</c>, returns a replacement expression or null to keep the
/// plain arithmetic node. Registered via <see cref="ExpressionFormRegistry.RegisterBinaryFold"/>.
/// </summary>
public interface IBinaryExpressionFold {
    DomainExpression? TryFold(DomainExpression left, DomainExpression right, bool isPlus);
}