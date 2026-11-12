using System.Globalization;

using Poly.DomainModeling.Analysis;

namespace Poly.DomainModeling.Ontology.Constraints;

/// <summary>
/// What a constraint printer needs from the DSL printer, so a kind's text lives in its
/// <see cref="ConstraintKind"/> row without the Ontology layer referencing the printer.
/// </summary>
internal interface IConstraintPrintHost {
    string Expression(DomainExpression expression);

    string Literal(object? value);

    string EscapeString(string value);
}

/// <summary>
/// One constraint kind and its behaviour at every seam (merge, join, identity, validation,
/// description, DSL printing). Built only through <see cref="ConstraintKinds"/>; the
/// delegates are the single place a kind's rules live (#367 A-1).
/// </summary>
internal sealed class ConstraintKind {
    internal ConstraintKind(
        Type type,
        Func<Constraint, Constraint, ConstraintMergeResult> merge,
        Func<Constraint, Constraint, Constraint?> join,
        Func<Constraint, Constraint, bool> identical,
        Func<Constraint, object?, ConstraintVerdict> evaluate,
        Func<Constraint, string> describe,
        Func<Constraint, IConstraintPrintHost, string>? print,
        Func<Constraint, AbstractValue, ConstraintVerdict> decide,
        bool writeRelevant) {
        Type = type;
        Merge = merge;
        Join = join;
        Identical = identical;
        Evaluate = evaluate;
        Describe = describe;
        Print = print;
        Decide = decide;
        WriteRelevant = writeRelevant;
    }

    public Type Type { get; }

    /// <summary>Intersection (meet) of two constraints of this kind.</summary>
    public Func<Constraint, Constraint, ConstraintMergeResult> Merge { get; }

    /// <summary>Least upper bound of two constraints of this kind; null drops the axis.</summary>
    public Func<Constraint, Constraint, Constraint?> Join { get; }

    /// <summary>Structural sameness of two constraints of this kind.</summary>
    public Func<Constraint, Constraint, bool> Identical { get; }

    /// <summary>Three-valued check of a literal against this kind.</summary>
    public Func<Constraint, object?, ConstraintVerdict> Evaluate { get; }

    /// <summary>Human text for diagnostics.</summary>
    public Func<Constraint, string> Describe { get; }

    /// <summary>
    /// DSL text, or <c>null</c> when the kind has no DSL word (a required property is simply
    /// not marked optional, so the printer omits it).
    /// </summary>
    public Func<Constraint, IConstraintPrintHost, string>? Print { get; }

    /// <summary>
    /// Three-valued check of a non-literal (flow) value against this kind: the flow-side twin of
    /// <see cref="Evaluate"/>. Kinds that have no flow decision answer Undecided, never Satisfied.
    /// </summary>
    public Func<Constraint, AbstractValue, ConstraintVerdict> Decide { get; }

    /// <summary>
    /// True when a write of a non-literal value must be verified against this kind (the Q4
    /// unverified-write channel consults <see cref="Decide"/> for it). Range only in V-03: a
    /// widening kind sets this together with a real <see cref="Decide"/>.
    /// </summary>
    public bool WriteRelevant { get; }

    public bool PrintedInDsl => Print is not null;
}

/// <summary>
/// The single registry of constraint kinds. Adding a kind means adding one row here; a
/// constraint type with no row is Unmergeable, Undecided (DMCS003) and prints as
/// <c>?TypeName</c> — it never silently falls through to a default that decides.
/// </summary>
internal static class ConstraintKinds {
    private static readonly Dictionary<Type, ConstraintKind> ByType = Build();

    /// <summary>Every registered kind.</summary>
    public static IReadOnlyCollection<ConstraintKind> All => ByType.Values;

    /// <summary>The kind row for <paramref name="constraint"/>, or null when unregistered.</summary>
    public static ConstraintKind? Of(Constraint constraint) =>
        ByType.GetValueOrDefault(constraint.GetType());

    private static ConstraintKind Row<T>(
        Func<T, T, ConstraintMergeResult> merge,
        Func<T, T, Constraint?>? join,
        Func<T, T, bool> identical,
        Func<T, object?, ConstraintVerdict> evaluate,
        Func<T, string> describe,
        Func<T, IConstraintPrintHost, string>? print,
        Func<T, AbstractValue, ConstraintVerdict>? decide = null,
        bool writeRelevant = false) where T : Constraint =>
        new(
            typeof(T),
            (a, b) => merge((T)a, (T)b),
            join is null
                ? (a, b) => identical((T)a, (T)b) ? a : null
                : (a, b) => join((T)a, (T)b),
            (a, b) => identical((T)a, (T)b),
            (c, v) => evaluate((T)c, v),
            c => describe((T)c),
            print is null ? null : (c, host) => print((T)c, host),
            decide is null ? ConstraintValidation.Undecided : (c, v) => decide((T)c, v),
            writeRelevant);

    private static Dictionary<Type, ConstraintKind> Build() {
        var rows = new[] {
            Row<RangeConstraint>(
                merge: ConstraintMerge.MergeRangeResult,
                join: ConstraintMerge.JoinRange,
                identical: (x, y) => Equals(x.Minimum, y.Minimum) && Equals(x.Maximum, y.Maximum) && Equals(x.Intervals, y.Intervals),
                evaluate: (r, v) => ConstraintValidation.Of(ConstraintValidation.IsInRangeUnion(v, r)),
                describe: ConstraintValidation.FormatRange,
                // A union has no DSL word; it prints as its hull, a sound widening.
                print: (r, _) => $"range({Convert.ToString(r.Minimum, CultureInfo.InvariantCulture)}, {Convert.ToString(r.Maximum, CultureInfo.InvariantCulture)})",
                decide: WriteVerification.DecideRange,
                writeRelevant: true),
            Row<LengthConstraint>(
                merge: (a, b) => ConstraintMerge.MergeLength(a, b) is { } length
                    ? ConstraintMergeResult.Of(length)
                    : ConstraintMergeResult.Empty,
                join: (a, b) => new LengthConstraint(Math.Min(a.MinLength, b.MinLength), Math.Max(a.MaxLength, b.MaxLength)),
                identical: (x, y) => x.MinLength == y.MinLength && x.MaxLength == y.MaxLength,
                evaluate: (l, v) => ConstraintValidation.Of(ConstraintValidation.IsValidLength(v, l.MinLength, l.MaxLength)),
                describe: l => $"length({l.MinLength}, {l.MaxLength})",
                print: (l, _) => l.MinLength == l.MaxLength
                    ? $"length({l.MinLength})"
                    : $"length({l.MinLength}, {l.MaxLength})"),
            Row<RequiredConstraint>(
                merge: (_, _) => ConstraintMergeResult.Of(new RequiredConstraint()),
                join: (_, _) => new RequiredConstraint(),
                identical: (_, _) => true,
                evaluate: (_, v) => ConstraintValidation.Of(v is not null),
                describe: _ => "required (non-null)",
                print: null),
            Row<OptionalConstraint>(
                merge: (_, _) => ConstraintMergeResult.Of(new OptionalConstraint()),
                join: (_, _) => new OptionalConstraint(),
                identical: (_, _) => true,
                evaluate: (_, _) => ConstraintVerdict.Satisfied, // optional is the opt-out of requiredness; any value (incl. null) is legal
                describe: _ => "optional",
                print: (_, _) => "optional"),
            Row<EqualityConstraint>(
                merge: (a, b) => Equals(a.ExpectedValue, b.ExpectedValue)
                    ? ConstraintMergeResult.Of(a)
                    : ConstraintMergeResult.Empty,
                join: null,
                identical: (x, y) => Equals(x.ExpectedValue, y.ExpectedValue),
                evaluate: (eq, v) => ConstraintValidation.Of(Equals(v, eq.ExpectedValue)),
                describe: eq => $"== {eq.ExpectedValue}",
                print: (e, host) => $"equals({host.Literal(e.ExpectedValue)})"),
            Row<PatternConstraint>(
                // Two different patterns are a conjunction (a string can match both), not bottom:
                // not one pattern, so Unmergeable; AbstractValue keeps both (ConstraintMerge.Coexists).
                merge: (a, b) => string.Equals(a.Pattern, b.Pattern, StringComparison.Ordinal)
                    ? ConstraintMergeResult.Of(a)
                    : ConstraintMergeResult.Unmergeable,
                join: null,
                identical: (x, y) => string.Equals(x.Pattern, y.Pattern, StringComparison.Ordinal),
                evaluate: (p, v) => ConstraintValidation.Of(ConstraintValidation.IsValidPattern(v, p.Pattern)),
                describe: p => $"pattern({p.Pattern})",
                print: (p, host) => $"pattern(\"{host.EscapeString(p.Pattern)}\")"),
            Row<UniqueConstraint>(
                merge: (a, _) => ConstraintMergeResult.Of(a),
                join: null,
                identical: (_, _) => true,
                evaluate: (_, _) => ConstraintVerdict.Satisfied, // cannot validate statically; requires instance set
                describe: _ => "unique",
                print: (_, _) => "unique"),
            Row<DefaultValueConstraint>(
                merge: (a, b) => ConstraintMerge.SameDefault(a.Expression, b.Expression)
                    ? ConstraintMergeResult.Of(a)
                    : ConstraintMergeResult.Empty,
                join: null,
                identical: (x, y) => ConstraintMerge.SameDefault(x.Expression, y.Expression),
                evaluate: (_, _) => ConstraintVerdict.Satisfied, // default-expression does not constrain assignment
                describe: _ => "default(...)",
                print: (dv, host) => $"default({host.Expression(dv.Expression)})"),
            Row<RelationalConstraint>(
                // Same pair: intersect the ordering sets (none left = bottom). A different pair is an
                // independent fact: Unmergeable, kept beside it (ConstraintMerge.Coexists).
                merge: (a, b) => !string.Equals(a.Other, b.Other, StringComparison.Ordinal)
                    ? ConstraintMergeResult.Unmergeable
                    : (a.Orderings & b.Orderings) is var both and not RelationalOrdering.None
                        ? ConstraintMergeResult.Of(a with { Orderings = both })
                        : ConstraintMergeResult.Empty,
                // Same pair: union of the orderings; all three (no information) or a different pair drops the axis.
                join: (a, b) => string.Equals(a.Other, b.Other, StringComparison.Ordinal)
                    && (a.Orderings | b.Orderings) is var either and not RelationalOrdering.All
                        ? a with { Orderings = either }
                        : null,
                identical: (x, y) => string.Equals(x.Other, y.Other, StringComparison.Ordinal) && x.Orderings == y.Orderings,
                // A relation needs the other property's value; a lone literal cannot decide it.
                evaluate: (_, _) => ConstraintVerdict.Undecided,
                describe: r => $"{RelationalConstraint.Symbol(r.Orderings)} {r.Other}",
                print: null),
        };
        return rows.ToDictionary(r => r.Type);
    }
}