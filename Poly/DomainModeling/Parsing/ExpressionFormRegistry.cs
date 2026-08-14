using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// Cited-gap RD escape only (pack-host lock 13): the product expression parser is
/// recursive descent and cannot fold by grammar pattern name, so a pack form keeps
/// this hand-rolled hook until the engine folds by pattern. Must leave the cursor
/// unchanged when returning false. New product forms must NOT use this type.
/// </summary>
public interface IExpressionPrimaryForm {
    bool TryParse(IDslParseCursor cursor, DslExpressionParser expressions, out DomainExpression expression);
}

/// <summary>
/// Pre-IDomainLibrary bridge for pack extension (pack-1-4). A pack registers Grammar
/// <b>patterns</b> (<see cref="RegisterGrammarContributor"/>) on existing rules and a
/// <b>print mapping</b> (<see cref="RegisterPrintMapping"/>) mapping its IR back to a
/// (rule, pattern, fills) form, so parse and print share one Grammar seam.
/// <see cref="IExpressionPrimaryForm"/> is the cited-gap fold escape only — not the
/// product API; new product forms must not register here.
/// </summary>
public sealed class ExpressionFormRegistry {
    private readonly List<IExpressionPrimaryForm> _forms = [];
    private readonly List<Action<Grammar<DslToken, DslTokenKind>>> _grammarContributors = [];
    private readonly List<IExpressionPrintMapping> _printMappings = [];
    private readonly List<IBinaryExpressionFold> _binaryFolds = [];

    public ExpressionFormRegistry() {
    }

    public ExpressionFormRegistry(ExpressionFormRegistry source) {
        ArgumentNullException.ThrowIfNull(source);
        _forms.AddRange(source._forms);
        _grammarContributors.AddRange(source._grammarContributors);
        _printMappings.AddRange(source._printMappings);
        _binaryFolds.AddRange(source._binaryFolds);
    }

    public void Register(IExpressionPrimaryForm form) {
        ArgumentNullException.ThrowIfNull(form);
        _forms.Add(form);
    }

    /// <summary>Registers an IR → Grammar print mapping.</summary>
    public void RegisterPrintMapping(IExpressionPrintMapping mapping) {
        ArgumentNullException.ThrowIfNull(mapping);
        _printMappings.Add(mapping);
    }

    /// <summary>Grammar patterns for pack primaries (recognition + Matcher probes + print table).</summary>
    public void RegisterGrammarContributor(Action<Grammar<DslToken, DslTokenKind>> contribute) {
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

    public void ContributeGrammarPatterns(Grammar<DslToken, DslTokenKind> grammar) {
        ArgumentNullException.ThrowIfNull(grammar);
        foreach (var c in _grammarContributors)
            c(grammar);
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

    public bool TryParsePrimary(IDslParseCursor cursor, DslExpressionParser expressions, out DomainExpression expression) {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(expressions);
        foreach (var form in _forms) {
            if (form.TryParse(cursor, expressions, out expression!))
                return true;
        }
        expression = null!;
        return false;
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