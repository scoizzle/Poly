using System.Globalization;

using Poly.DomainModeling.Parsing;
using Poly.Grammar;

namespace Poly.DomainModeling.Packs.Temporal;

/// <summary>
/// pack-3a: temporal print binders + grammar patterns. The temporal pack registers
/// <c>Now</c>/<c>Today</c>/<c>DateOperation</c> print binders so export_dsl re-emits
/// the library spellings (<c>Now - 12 Days</c>, <c>DueDate + 14 Days</c>, <c>Today - 3 Months</c>)
/// and apply_dsl re-folds them into the same IR. Patterns register on both primary
/// rules (with and without <c>not</c>) so the Grammar matcher recognizes the pack
/// surface; the <c>date-operation</c> rule is the DateOperation print table.
/// </summary>
public static class TemporalExpressionPrintBinders {
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
    public static void ContributeGrammarPatterns(Grammar<DslToken, DslTokenKind> g) {
        ArgumentNullException.ThrowIfNull(g);
        foreach (var rule in new[] { "expr-primary", "expr-primary-no-not" }) {
            g.Define(rule)
                .Pattern("now").Predicate(IsNowIdentifier, "now-identifier").Optional(DslTokenKind.Comma).Commit();
            g.Define(rule)
                .Pattern("today").Predicate(IsTodayIdentifier, "today-identifier").Optional(DslTokenKind.Comma).Commit();
            g.Define(rule)
                .Pattern("duration").Value(DslTokenKind.Number).Value(DslTokenKind.Identifier).Commit();
        }

        g.Define("date-operation")
            .Pattern("add")
                .Ref("expr-primary").Kind(DslTokenKind.Plus)
                .Value(DslTokenKind.Number).Value(DslTokenKind.Identifier)
                .Commit()
            .Pattern("sub")
                .Ref("expr-primary").Kind(DslTokenKind.Minus)
                .Value(DslTokenKind.Number).Value(DslTokenKind.Identifier)
                .Commit();
    }

    internal static bool IsNowIdentifier(DslToken t) =>
        t.Kind == DslTokenKind.Identifier
        && string.Equals(t.Text, "Now", StringComparison.Ordinal);

    internal static bool IsTodayIdentifier(DslToken t) =>
        t.Kind == DslTokenKind.Identifier
        && string.Equals(t.Text, "Today", StringComparison.Ordinal);

    /// <summary>Prints <see cref="Now"/> as the <c>Now</c> primary.</summary>
    private sealed class NowBinder : IExpressionPrintMapping {
        public Type ExpressionType => typeof(Now);

        public bool TryMap(DomainExpression expression, out PrintMapping binding) {
            if (expression is not Now) {
                binding = default;
                return false;
            }
            var at = 0;
            binding = new PrintMapping("expr-primary", "now", ctx => {
                if (at++ == 0)
                    ctx.Emit("Now");
            });
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
            var at = 0;
            binding = new PrintMapping("expr-primary", "today", ctx => {
                if (at++ == 0)
                    ctx.Emit("Today");
            });
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
            var at = 0;
            binding = new PrintMapping("date-operation", pattern, ctx => {
                switch (at++) {
                    case 0:
                        EmitDate(ctx, dOp.Date);
                        return;
                    case 1:
                        ctx.Emit(amount.ToString(CultureInfo.InvariantCulture));
                        return;
                    case 2:
                        ctx.Emit(unit);
                        return;
                }
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