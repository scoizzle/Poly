using System.Globalization;

using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>
/// pack-3a: temporal print binders + grammar patterns. The temporal pack registers
/// <c>Now</c>/<c>Today</c>/<c>DateOperation</c> print binders so export_dsl re-emits
/// the library spellings (<c>Now - 12 Days</c>, <c>DueDate + 14 Days</c>, <c>Today - 3 Months</c>)
/// and apply_dsl re-folds them into the same IR. Patterns register on both primary
/// rules (with and without <c>not</c>) so the Grammar matcher recognizes the pack
/// surface; the <c>date-operation</c> rule is the DateOperation print table.
/// </summary>
public static class TemporalExpressionPrintBinders {
    /// <summary>Registers Now/Today/Duration folds on the session fold table.</summary>
    public static void RegisterFolds(ExpressionFoldTable table) {
        ArgumentNullException.ThrowIfNull(table);
        foreach (var rule in new[] { "expr-primary", "expr-primary-no-not" }) {
            table.Register(rule, "now", _ => new Now());
            table.Register(rule, "today", _ => new Today());
            table.Register(rule, "duration", FoldDuration);
        }
    }

    private static DomainExpression FoldDuration(MatchResult<DslToken, DslTokenKind> match) {
        if (!match.Captures.TryGetValue("amount", out var amounts) || amounts.Count == 0
            || !match.Captures.TryGetValue("unit", out var units) || units.Count == 0)
            throw new InvalidOperationException("Duration match is missing amount/unit captures.");
        if (!long.TryParse(amounts[0].Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
            throw new InvalidOperationException($"Duration amount '{amounts[0].Text}' is not an integer.");
        if (!DurationForm.TryGetUnit(units[0].Text, out var unit))
            throw new InvalidOperationException($"Unknown duration unit '{units[0].Text}'.");
        return new Duration(amount, unit);
    }

    /// <summary>Registers the temporal print binders on a pack's expression forms.</summary>
    public static void Register(ExpressionFormRegistry forms) {
        ArgumentNullException.ThrowIfNull(forms);
        forms.RegisterPrintMapping(new NowBinder());
        forms.RegisterPrintMapping(new TodayBinder());
        forms.RegisterPrintMapping(new DateOperationBinder());
    }

    /// <summary>
    /// Contributes the temporal patterns: clock primaries + duration on both primary
    /// rules, and the DateOperation spell rule. Recognition only — folding stays the
    /// RD parse forms' job (pack-host lock 13, cited gap).
    /// </summary>
    public static void ContributeGrammarPatterns(GrammarBuilder<DslToken, DslTokenKind> g) {
        ArgumentNullException.ThrowIfNull(g);
        foreach (var rule in new[] { "expr-primary", "expr-primary-no-not" }) {
            g.Define(rule)
                .Pattern("now", priority: 1).Predicate(IsNowIdentifier, "now").Commit()
                .Pattern("today", priority: 1).Predicate(IsTodayIdentifier, "today").Commit()
                .Pattern("duration").Value(DslTokenKind.Number, "amount").Predicate(IsDurationUnitToken, "unit").Commit();
        }

        g.Define("date-operation")
            .Pattern("add")
                .Ref("expr-primary").Kind(DslTokenKind.Plus)
                .Value(DslTokenKind.Number, "amount").Predicate(IsDurationUnitToken, "unit")
                .Commit()
            .Pattern("sub")
                .Ref("expr-primary").Kind(DslTokenKind.Minus)
                .Value(DslTokenKind.Number, "amount").Predicate(IsDurationUnitToken, "unit")
                .Commit();
    }

    internal static bool IsNowIdentifier(DslToken t) =>
        t.Kind == DslTokenKind.Identifier
        && string.Equals(t.Text, "Now", StringComparison.Ordinal);

    internal static bool IsTodayIdentifier(DslToken t) =>
        t.Kind == DslTokenKind.Identifier
        && string.Equals(t.Text, "Today", StringComparison.Ordinal);

    internal static bool IsDurationUnitToken(DslToken t) =>
        t.Kind == DslTokenKind.Identifier && DurationForm.TryGetUnit(t.Text, out _);

    /// <summary>Prints <see cref="Now"/> as the <c>Now</c> primary.</summary>
    private sealed class NowBinder : IExpressionPrintMapping {
        public Type ExpressionType => typeof(Now);

        public bool TryMap(DomainExpression expression, out PrintMapping binding) {
            if (expression is not Now) {
                binding = default;
                return false;
            }
            binding = new PrintMapping(
                "expr-primary",
                "now",
                NamedFills: new Dictionary<string, string>(StringComparer.Ordinal) { ["now"] = "Now" });
            return true;
        }
    }

    /// <summary>Prints <see cref="Today"/> as the <c>Today</c> primary.</summary>
    private sealed class TodayBinder : IExpressionPrintMapping {
        public Type ExpressionType => typeof(Today);

        public bool TryMap(DomainExpression expression, out PrintMapping binding) {
            if (expression is not Today) {
                binding = default;
                return false;
            }
            binding = new PrintMapping(
                "expr-primary",
                "today",
                NamedFills: new Dictionary<string, string>(StringComparer.Ordinal) { ["today"] = "Today" });
            return true;
        }
    }

    /// <summary>
    /// Prints <see cref="DateOperation"/> as <c>&lt;date&gt; + &lt;n&gt; days/months</c>
    /// (negative offset selects the <c>-</c> pattern so reparse folds identically).
    /// </summary>
    private sealed class DateOperationBinder : IExpressionPrintMapping {
        public Type ExpressionType => typeof(DateOperation);

        public bool TryMap(DomainExpression expression, out PrintMapping binding) {
            if (expression is not DateOperation dOp) {
                binding = default;
                return false;
            }

            var offset = dOp.Offset is Literal lit
                ? Convert.ToInt64(lit.Value, CultureInfo.InvariantCulture)
                : throw new InvalidOperationException(
                    $"Cannot print DateOperation: offset is '{dOp.Offset.GetType().Name}', not a literal.");
            var isSubtract = offset < 0;
            var amount = isSubtract ? -offset : offset;
            var unit = dOp.Kind switch {
                DateOperationKind.AddDays => "Days",
                DateOperationKind.AddMonths => "Months",
                _ => throw new InvalidOperationException(
                    $"Cannot print DateOperation: kind '{dOp.Kind}' has no DSL spelling."),
            };

            var pattern = isSubtract ? "sub" : "add";
            binding = new PrintMapping(
                "date-operation",
                pattern,
                Fill: ctx => EmitDate(ctx, dOp.Date),
                NamedFills: new Dictionary<string, string>(StringComparer.Ordinal) {
                    ["amount"] = amount.ToString(CultureInfo.InvariantCulture),
                    ["unit"] = unit,
                });
            return true;
        }

        private static void EmitDate(PrintContext<DslToken, DslTokenKind> ctx, DomainExpression date) {
            switch (date) {
                case Now:
                    ctx.Emit("Now");
                    return;
                case Today:
                    ctx.Emit("Today");
                    return;
                case PropertyAccess p:
                    ctx.Emit(p.Name);
                    return;
                default:
                    throw new InvalidOperationException(
                        $"Cannot print DateOperation date '{date.GetType().Name}': no DSL spelling.");
            }
        }
    }
}