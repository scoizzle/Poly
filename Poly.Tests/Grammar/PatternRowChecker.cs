using System.Text.RegularExpressions;

using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// One surface the change-protocol check runs over: its table, and the reader's own
/// tokenize step (no parallel tokenizer code).
/// </summary>
/// <param name="Name">The surface key the allow-list and examples use (<c>dsl</c>, <c>csharp</c>).</param>
/// <param name="Grammar">The table the rows are checked against.</param>
/// <param name="Tokenize">The reader's tokenizer over a printed text.</param>
/// <param name="ReportsSuppliedContent">True when every print on this surface reports how many
/// positional content values it supplied (<c>Printer.Render</c>, which the C# writer prints through); the slot-count rule
/// (A1) then compares that count with the row's slots.</param>
internal sealed record PatternRowSurface<TToken, TTokenKind>(
    string Name,
    Grammar<TToken, TTokenKind> Grammar,
    Func<string, ITokenReader<TToken, TTokenKind>> Tokenize,
    bool ReportsSuppliedContent)
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct;

/// <summary>Which rule of the check a violation breaks.</summary>
internal enum PatternRowCheck {
    /// <summary>The allow-list or example table is malformed (schema, key, owner, duplicate).</summary>
    Schema,
    /// <summary>The printed pattern is not a row of the table.</summary>
    UnknownRow,
    /// <summary>A1: a named hole is missing, or a supplied fill names no hole of the row.</summary>
    Holes,
    /// <summary>A1: the caller supplied a different number of content values than the row has slots.</summary>
    Arity,
    /// <summary>A2: the printed text does not re-read as the same rule and pattern to the end of input.</summary>
    ReadBack,
    /// <summary>A3a: a row of the table is never printed and carries no unobserved entry.</summary>
    Unobserved,
    /// <summary>A3b: an optional or repeated slot of an observed row is empty in every observation.</summary>
    SlotNeverFilled,
    /// <summary>A3c: a reader-head row has no example, or an example does not select its row.</summary>
    Example,
    /// <summary>An allow-list entry whose condition does not fire.</summary>
    Stale,
}

/// <summary>One finding of the check.</summary>
internal sealed record PatternRowViolation(PatternRowCheck Check, string Rule, string? Pattern, string Message) {
    public override string ToString() => $"{Check} {Rule}/{Pattern ?? "*"}: {Message}";
}

/// <summary>
/// The grammar change-protocol check (G-23): every observed print of pattern <c>p</c> of rule
/// <c>R</c> is checked against the table it was printed from. Pure over its inputs: it reads the
/// grammar it is given and the observations it is given, and never a static writer.
///
/// <list type="bullet">
/// <item><b>A1, holes and slots.</b> A row's holes are its named <see cref="Value{TToken,TTokenKind}"/>
/// elements, plus each labelled <see cref="MatchPredicate{TToken,TTokenKind}"/> whose label the caller
/// supplied (that is the printer's rule). Every named value must be supplied, and a supplied key must name
/// a hole. A labelled predicate the caller did not fill is a positional slot, so the hole check is circular
/// for predicate labels: a missing predicate fill is caught by A2 only. The slot count comes from a
/// counting probe on the real <see cref="Printer{TToken,TTokenKind}"/>; when the surface reports supplied
/// content, the count supplied must equal it (strict: a spare value fails).</item>
/// <item><b>A2, read-back.</b> The printed text is tokenized by the reader's own tokenizer and matched
/// against rule <c>R</c> of the full grammar; the top match must be pattern <c>p</c> and consume every token
/// before end of input, except that a row ending in a <see cref="Slot{TToken,TTokenKind}"/> stops at the
/// Slot (the host reads the fill that follows).</item>
/// <item><b>A3, coverage.</b> (a) every row is observed or carries an unobserved entry; (b) every
/// optional or repeated slot of an observed row is non-empty in some observation, or carries an
/// <see cref="PatternRowEntryKind.AlwaysNull"/> entry keyed by its slot index; (c) every reader-head and
/// reader-only row has an example that the matcher selects, and every example names such a row.</item>
/// </list>
///
/// Every allow-list entry is checked both ways: an entry whose condition does not fire is a
/// <see cref="PatternRowCheck.Stale"/> violation.
/// </summary>
internal static class PatternRowChecker {
    /// <summary>What a top-level pattern element is to the printer.</summary>
    internal enum ElementRole {
        /// <summary>Emits fixed text (or nothing): <c>MatchKind</c>, <c>NotFollowedBy</c>, <c>Word</c>.</summary>
        Fixed,
        /// <summary>A named value or a Slot: always a hole.</summary>
        Hole,
        /// <summary>A labelled predicate: a hole when the caller supplies its label, else a slot.</summary>
        PredicateLabel,
        /// <summary>A required content slot (unnamed value, unlabelled predicate, <c>Any</c>, <c>Ref</c>,
        /// <c>LeftAssoc</c>, <c>Balanced</c>).</summary>
        Slot,
        /// <summary>An optional content slot (<c>Optional</c>, <c>Repeat</c>): A3b applies.</summary>
        OptionalSlot,
    }

    /// <summary>
    /// Classifies one top-level element. An element type this switch does not name throws, so a new
    /// element kind cannot pass the check by being skipped.
    /// </summary>
    internal static ElementRole Classify<TToken, TTokenKind>(IPatternElement<TToken, TTokenKind> element)
        where TToken : struct, IToken<TTokenKind>
        where TTokenKind : struct => element switch {
            MatchKind<TToken, TTokenKind> => ElementRole.Fixed,
            NotFollowedBy<TToken, TTokenKind> => ElementRole.Fixed,
            Word<TToken, TTokenKind> => ElementRole.Fixed,
            Value<TToken, TTokenKind> { Name: { Length: > 0 } } => ElementRole.Hole,
            Slot<TToken, TTokenKind> => ElementRole.Hole,
            MatchPredicate<TToken, TTokenKind> { Label: { Length: > 0 } } => ElementRole.PredicateLabel,
            Value<TToken, TTokenKind> => ElementRole.Slot,
            MatchPredicate<TToken, TTokenKind> => ElementRole.Slot,
            Any<TToken, TTokenKind> => ElementRole.Slot,
            Ref<TToken, TTokenKind> => ElementRole.Slot,
            LeftAssoc<TToken, TTokenKind> => ElementRole.Slot,
            Balanced<TToken, TTokenKind> => ElementRole.Slot,
            Optional<TToken, TTokenKind> => ElementRole.OptionalSlot,
            Repeat<TToken, TTokenKind> => ElementRole.OptionalSlot,
            _ => throw new InvalidOperationException(
                $"The change-protocol check does not know pattern element '{element.GetType().Name}'."),
        };

    /// <summary>Runs every rule of the check over one surface and returns the violations.</summary>
    public static IReadOnlyList<PatternRowViolation> Check<TToken, TTokenKind>(
        PatternRowSurface<TToken, TTokenKind> surface,
        IReadOnlyList<PrintObservation> observations,
        IReadOnlyList<PatternRowEntry> allowList,
        IReadOnlyList<PatternRowExample> examples)
        where TToken : struct, IToken<TTokenKind>
        where TTokenKind : struct {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(allowList);
        ArgumentNullException.ThrowIfNull(examples);
        return new Run<TToken, TTokenKind>(surface).Check(observations, allowList, examples);
    }

    /// <summary>
    /// The floor rule: every named row exists in the table (a renamed floor row fails loudly) and is
    /// observed at least once.
    /// </summary>
    public static IReadOnlyList<PatternRowViolation> CheckFloor<TToken, TTokenKind>(
        PatternRowSurface<TToken, TTokenKind> surface,
        IReadOnlyList<PrintObservation> observations,
        IReadOnlyList<(string Rule, string Pattern)> floor)
        where TToken : struct, IToken<TTokenKind>
        where TTokenKind : struct {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(floor);
        var violations = new List<PatternRowViolation>();
        foreach (var (rule, pattern) in floor) {
            if (!surface.Grammar.HasRule(rule) || !surface.Grammar.TryGetPattern(rule, pattern, out _)) {
                violations.Add(new(PatternRowCheck.Unobserved, rule, pattern,
                    $"the {surface.Name} floor names a row that is not a row of the table: rename or remove it in PatternRowFloors"));
                continue;
            }
            if (!observations.Any(o => o.Rule == rule && o.Pattern == pattern))
                violations.Add(new(PatternRowCheck.Unobserved, rule, pattern, $"the {surface.Name} floor row is never observed"));
        }
        return violations;
    }

    /// <summary>
    /// The refusal rule: every refusal the drive met is expected (same input, same first line with node ids
    /// shown as <c>…</c>), and every expected refusal still occurs. Each expected entry has a well-formed owner.
    /// </summary>
    public static IReadOnlyList<string> CheckRefusals(
        IReadOnlyList<PatternRowRefusal> refusals,
        IReadOnlyList<PatternRowExpectedRefusal> expected) {
        ArgumentNullException.ThrowIfNull(refusals);
        ArgumentNullException.ThrowIfNull(expected);
        var violations = new List<string>();
        foreach (var entry in expected.Where(e => !OwnerFormat.IsMatch(e.Owner)))
            violations.Add($"expected refusal '{entry.Input}' has a malformed owner '{entry.Owner}'");

        var met = refusals.Select(r => (r.Input, FirstLine: RefusalFirstLine(r.Message))).ToList();
        foreach (var (input, firstLine) in met.Where(m => !expected.Any(e => e.Input == m.Input && e.FirstLine == m.FirstLine)))
            violations.Add($"the drive refused '{input}' ('{firstLine}'), which ExpectedRefusals does not list");
        foreach (var entry in expected.Where(e => !met.Any(m => m.Input == e.Input && m.FirstLine == e.FirstLine)))
            violations.Add($"expected refusal '{entry.Input}' ('{entry.FirstLine}') no longer occurs: the entry is stale");
        return violations;
    }

    /// <summary>The first line of a refusal message with node ids shown as <c>…</c>, so the text is stable across runs.</summary>
    private static string RefusalFirstLine(string message) =>
        NodeIdText.Replace(message.Split('\n')[0].TrimEnd('\r'), "(NodeId …)");

    private static readonly Regex NodeIdText = new(@"\(NodeId [0-9a-f]+\)", RegexOptions.CultureInvariant);

    /// <summary>An owner is a removing row (optionally with its issue), an issue, or a permanent justification.</summary>
    private static readonly Regex OwnerFormat = new(
        @"^(?:[A-Z]-\d+[a-z]?(?: \(#\d+\))?|#\d+|none: permanent — \S.*)$", RegexOptions.CultureInvariant);

    private const string PermanentOwnerPrefix = "none: permanent";

    private static string Shown(string text) => text.Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>A sink for the slot probe: the probe counts content callbacks and keeps no text.</summary>
    private sealed class DiscardWriter<TTokenKind> : ITokenWriter<TTokenKind> where TTokenKind : struct {
        public void Write(TTokenKind kind) { }

        public void Write(TTokenKind kind, string text) { }

        public string ToText() => "";
    }

    /// <summary>A violation that an asymmetry or defect entry may exempt, with the observation it came from.</summary>
    private sealed record Candidate(PatternRowViolation Violation, PrintObservation Observation);

    /// <summary>One run of the check over one surface.</summary>
    private sealed class Run<TToken, TTokenKind>(PatternRowSurface<TToken, TTokenKind> surface)
        where TToken : struct, IToken<TTokenKind>
        where TTokenKind : struct {
        private readonly Grammar<TToken, TTokenKind> _grammar = surface.Grammar;
        private readonly List<PatternRowViolation> _violations = [];
        private readonly Printer<TToken, TTokenKind> _probe =
            new(surface.Grammar, _ => "", () => new DiscardWriter<TTokenKind>());

        public IReadOnlyList<PatternRowViolation> Check(
            IReadOnlyList<PrintObservation> observations,
            IReadOnlyList<PatternRowEntry> allowList,
            IReadOnlyList<PatternRowExample> examples) {
            var entries = ValidEntries(allowList);
            var unobservedCover = UnobservedCover(entries);

            if (observations.Count == 0)
                _violations.Add(new(PatternRowCheck.Unobserved, "*", null, $"the surface '{surface.Name}' has zero observations"));

            var observed = new HashSet<(string Rule, string Pattern)>();
            var optionalSlots = new Dictionary<(string Rule, string Pattern), SortedSet<int>>();
            var filledSlots = new Dictionary<(string Rule, string Pattern), HashSet<int>>();
            var candidates = new List<Candidate>();

            foreach (var observation in observations) {
                if (observation.Rule is not { Length: > 0 } rule || !_grammar.HasRule(rule)
                    || !_grammar.TryGetPattern(rule, observation.Pattern, out var row)) {
                    _violations.Add(new(PatternRowCheck.UnknownRow, observation.Rule ?? "?", observation.Pattern,
                        $"'{Shown(observation.Text)}' was printed from a pattern that is not a row of the table"));
                    continue;
                }
                var key = (rule, observation.Pattern);
                observed.Add(key);
                var slots = CheckObservation(rule, row, observation, candidates);
                var optional = optionalSlots.TryGetValue(key, out var known) ? known : optionalSlots[key] = [];
                var filled = filledSlots.TryGetValue(key, out var seen) ? seen : filledSlots[key] = [];
                for (var i = 0; i < slots.Count; i++) {
                    if (slots[i] != ElementRole.OptionalSlot)
                        continue;
                    optional.Add(i);
                    if (i < observation.ContentEmitted.Count && observation.ContentEmitted[i])
                        filled.Add(i);
                }
            }

            ApplyExemptions(entries, candidates);
            CheckRowCoverage(unobservedCover, observed);
            CheckSlotCoverage(entries, optionalSlots, filledSlots);
            CheckExamples(entries, examples);
            return _violations;
        }

        // ── A1 and A2, per observation ───────────────────────────────────────

        /// <summary>Checks one observation of a known row; returns the row's slot roles in printer order.</summary>
        private List<ElementRole> CheckObservation(
            string rule, Pattern<TToken, TTokenKind> row, PrintObservation observation, List<Candidate> candidates) {
            var pattern = row.Name;
            var supplied = new HashSet<string>(observation.FillKeys, StringComparer.Ordinal);
            var requiredHoles = new List<string>();
            var predicateLabels = new HashSet<string>(StringComparer.Ordinal);
            var slots = new List<ElementRole>();
            foreach (var element in row.Elements) {
                switch (Classify(element)) {
                    case ElementRole.Fixed:
                        break;
                    case ElementRole.Hole:
                        requiredHoles.Add(HoleName(element));
                        break;
                    case ElementRole.PredicateLabel: {
                            var label = ((MatchPredicate<TToken, TTokenKind>)element).Label;
                            predicateLabels.Add(label);
                            if (!supplied.Contains(label))
                                slots.Add(ElementRole.Slot);
                            break;
                        }
                    case ElementRole.Slot:
                        slots.Add(ElementRole.Slot);
                        break;
                    case ElementRole.OptionalSlot:
                        slots.Add(ElementRole.OptionalSlot);
                        break;
                }
            }

            void Report(PatternRowCheck check, string message) =>
                candidates.Add(new(new(check, rule, pattern, $"'{Shown(observation.Text)}' {message}"), observation));

            foreach (var hole in requiredHoles.Where(hole => !supplied.Contains(hole)))
                Report(PatternRowCheck.Holes, $"has no fill for the named hole '{hole}'");
            foreach (var key in observation.FillKeys.Where(key => !requiredHoles.Contains(key) && !predicateLabels.Contains(key)))
                Report(PatternRowCheck.Holes, $"supplies the fill '{key}', which names no hole of the row");

            var probeFills = requiredHoles.Concat(predicateLabels.Where(supplied.Contains))
                .ToDictionary(name => name, _ => "x", StringComparer.Ordinal);
            var slotCount = ProbeSlotCount(rule, pattern, probeFills);
            if (slotCount != slots.Count)
                throw new InvalidOperationException(
                    $"The printer visits {slotCount} content slots of {rule}/{pattern}; the classification names {slots.Count}.");

            if (observation.ContentEmitted.Count != slotCount)
                Report(PatternRowCheck.Arity,
                    $"was observed with {observation.ContentEmitted.Count} content slots; the printer visits {slotCount}");
            if (surface.ReportsSuppliedContent) {
                if (observation.SuppliedContentCount is not { } count)
                    Report(PatternRowCheck.Arity, "does not report how many content values its caller supplied");
                else if (count != slotCount)
                    Report(PatternRowCheck.Arity, $"supplies {count} content values; the row has {slotCount} content slots");
            }

            if (ReadBack(rule, pattern, observation.Text) is { } failure)
                Report(PatternRowCheck.ReadBack, failure);
            return slots;
        }

        private static string HoleName(IPatternElement<TToken, TTokenKind> element) => element switch {
            Value<TToken, TTokenKind> value => value.Name!,
            Slot<TToken, TTokenKind> slot => slot.Name,
            _ => throw new InvalidOperationException($"'{element.GetType().Name}' is not a hole."),
        };

        /// <summary>The number of content callbacks the real printer makes for this row and these fills.</summary>
        private int ProbeSlotCount(string rule, string pattern, IReadOnlyDictionary<string, string> fills) {
            var count = 0;
            _probe.PrintInto(new DiscardWriter<TTokenKind>(), rule, pattern, _ => count++, fills);
            return count;
        }

        /// <summary>
        /// Re-reads <paramref name="text"/> as <paramref name="rule"/> through the full grammar and the
        /// reader's tokenizer. Null when the top match is <paramref name="pattern"/> and consumes every
        /// token before end of input; otherwise why not.
        /// </summary>
        private string? ReadBack(string rule, string pattern, string text) {
            ITokenReader<TToken, TTokenKind> reader;
            var tokens = 0;
            try {
                reader = surface.Tokenize(text);
                while (!reader.EndOfStream(reader.Peek(tokens).Kind))
                    tokens++;
            }
            catch (FormatException ex) {
                return $"does not tokenize: {ex.Message}";
            }
            var match = new Matcher<TToken, TTokenKind>(_grammar, reader).TryMatch(rule);
            if (match is null)
                return $"does not re-read as rule '{rule}'";
            if (!string.Equals(match.PatternName, pattern, StringComparison.Ordinal))
                return $"re-reads as {rule}/{match.PatternName}";
            // A row that ends in a Slot stops there: the host reads what follows (the slot's fill), so the
            // row re-reads its head and the rest of the text is the fill's.
            if (match.Consumed != tokens && match.Pattern?.Elements is not [.., Slot<TToken, TTokenKind>])
                return $"re-reads {match.Consumed} of its {tokens} tokens as {rule}/{pattern}";
            return null;
        }

        // ── the allow-list ───────────────────────────────────────────────────

        /// <summary>Reports malformed entries (schema) and returns the well-formed ones.</summary>
        private List<PatternRowEntry> ValidEntries(IReadOnlyList<PatternRowEntry> allowList) {
            var valid = new List<PatternRowEntry>();
            var keys = new HashSet<(PatternRowEntryKind, string, string?, int?, string?)>();
            foreach (var entry in allowList) {
                var problem = SchemaProblem(entry);
                if (problem is null && !keys.Add((entry.Kind, entry.Rule, entry.Pattern, entry.Slot, entry.Witness)))
                    problem = "is listed twice";
                if (problem is not null) {
                    _violations.Add(new(PatternRowCheck.Schema, entry.Rule, entry.Pattern, $"{entry} {problem}"));
                    continue;
                }
                valid.Add(entry);
            }
            return valid;
        }

        private string? SchemaProblem(PatternRowEntry entry) {
            if (!string.Equals(entry.Surface, surface.Name, StringComparison.Ordinal))
                return $"is for the surface '{entry.Surface}', not '{surface.Name}'";
            if (string.IsNullOrWhiteSpace(entry.Reason))
                return "has no reason";
            if (entry.Owner is null || !OwnerFormat.IsMatch(entry.Owner))
                return $"has a malformed owner '{entry.Owner}' (a row such as G-24 or G-23b (#848), an issue #nnn, or 'none: permanent — why')";
            if (!_grammar.HasRule(entry.Rule))
                return "names a rule that is not in the table";
            if (entry.Pattern is { } pattern && !_grammar.TryGetPattern(entry.Rule, pattern, out _))
                return "names a row that is not in the table";

            if (entry.IsUnobserved) {
                if ((entry.Pattern is null) == (entry.RowCount is null))
                    return "must name exactly one of a pattern or a pinned row count";
                if (entry.RowCount is { } rows && rows != _grammar.GetPatterns(entry.Rule).Count)
                    return $"pins {rows} rows; the rule has {_grammar.GetPatterns(entry.Rule).Count}";
                if (entry.Slot is not null || entry.Witness is not null)
                    return "is an unobserved entry and takes no slot or witness";
                return null;
            }

            if (entry.Pattern is null || entry.RowCount is not null)
                return "must name one row";
            return entry.Kind switch {
                PatternRowEntryKind.AlwaysNull when entry.Slot is not >= 0 => "must name a content-slot index",
                PatternRowEntryKind.AlwaysNull when entry.Witness is not null => "takes no witness",
                PatternRowEntryKind.Defect when string.IsNullOrEmpty(entry.Witness) => "must name a witness text",
                PatternRowEntryKind.Defect when entry.Owner.StartsWith(PermanentOwnerPrefix, StringComparison.Ordinal) =>
                    "is a defect and needs the row that fixes it as its owner",
                PatternRowEntryKind.Defect or PatternRowEntryKind.PrefixOnly or PatternRowEntryKind.ContextOnly
                    or PatternRowEntryKind.RowOwnsText when entry.Slot is not null => "takes no slot",
                _ => null,
            };
        }

        /// <summary>Each row an unobserved entry covers, with the entry. A row covered twice is a schema error.</summary>
        private Dictionary<(string Rule, string Pattern), PatternRowEntry> UnobservedCover(List<PatternRowEntry> entries) {
            var cover = new Dictionary<(string Rule, string Pattern), PatternRowEntry>();
            foreach (var entry in entries.Where(e => e.IsUnobserved)) {
                var rows = entry.Pattern is { } pattern
                    ? [pattern]
                    : _grammar.GetPatterns(entry.Rule).Select(row => row.Name);
                foreach (var row in rows) {
                    if (!cover.TryAdd((entry.Rule, row), entry))
                        _violations.Add(new(PatternRowCheck.Schema, entry.Rule, row,
                            $"{entry} covers a row {cover[(entry.Rule, row)]} already covers"));
                }
            }
            return cover;
        }

        /// <summary>Applies asymmetry and defect entries to the per-observation violations; unused entries are stale.</summary>
        private void ApplyExemptions(List<PatternRowEntry> entries, List<Candidate> candidates) {
            var exempting = entries.Where(e => e.IsAsymmetry || e.Kind == PatternRowEntryKind.Defect).ToList();
            var used = new HashSet<PatternRowEntry>();
            foreach (var candidate in candidates) {
                var exempt = exempting.FirstOrDefault(entry => Exempts(entry, candidate));
                if (exempt is null)
                    _violations.Add(candidate.Violation);
                else
                    used.Add(exempt);
            }
            foreach (var entry in exempting.Where(e => !used.Contains(e)))
                _violations.Add(new(PatternRowCheck.Stale, entry.Rule, entry.Pattern,
                    $"{entry} exempts nothing: no observation of the row fails the rule it names"));
        }

        private static bool Exempts(PatternRowEntry entry, Candidate candidate) {
            var violation = candidate.Violation;
            if (!string.Equals(entry.Rule, violation.Rule, StringComparison.Ordinal)
                || !string.Equals(entry.Pattern, violation.Pattern, StringComparison.Ordinal))
                return false;
            if (entry.Witness is { } witness && !candidate.Observation.Text.Contains(witness, StringComparison.Ordinal))
                return false;
            // Asymmetry and defect entries exempt read-back only: a hole or slot-count failure on the
            // same observation is still reported.
            return violation.Check == PatternRowCheck.ReadBack;
        }

        // ── A3 coverage ──────────────────────────────────────────────────────

        private void CheckRowCoverage(
            Dictionary<(string Rule, string Pattern), PatternRowEntry> cover,
            HashSet<(string Rule, string Pattern)> observed) {
            foreach (var rule in _grammar.KnownRules.Order(StringComparer.Ordinal)) {
                foreach (var row in _grammar.GetPatterns(rule)) {
                    var key = (rule, row.Name);
                    var isObserved = observed.Contains(key);
                    var entry = cover.GetValueOrDefault(key);
                    if (isObserved && entry is not null)
                        _violations.Add(new(PatternRowCheck.Stale, rule, row.Name,
                            $"{entry} says the row is never printed, but it is observed"));
                    else if (!isObserved && entry is null)
                        _violations.Add(new(PatternRowCheck.Unobserved, rule, row.Name,
                            "is never printed and carries no unobserved entry: add an observation to the drive, or an entry with its reason and owner"));
                }
            }
        }

        private void CheckSlotCoverage(
            List<PatternRowEntry> entries,
            Dictionary<(string Rule, string Pattern), SortedSet<int>> optionalSlots,
            Dictionary<(string Rule, string Pattern), HashSet<int>> filledSlots) {
            var alwaysNull = entries.Where(e => e.Kind == PatternRowEntryKind.AlwaysNull).ToList();
            var used = new HashSet<PatternRowEntry>();
            foreach (var ((rule, pattern), optional) in optionalSlots) {
                foreach (var slot in optional.Where(slot => !filledSlots[(rule, pattern)].Contains(slot))) {
                    var entry = alwaysNull.FirstOrDefault(e => e.Rule == rule && e.Pattern == pattern && e.Slot == slot);
                    if (entry is not null) {
                        used.Add(entry);
                        continue;
                    }
                    _violations.Add(new(PatternRowCheck.SlotNeverFilled, rule, pattern,
                        $"optional content slot {slot} is empty in every observation: add an observation that fills it, or an AlwaysNull entry"));
                }
            }
            foreach (var entry in alwaysNull.Where(e => !used.Contains(e)))
                _violations.Add(new(PatternRowCheck.Stale, entry.Rule, entry.Pattern,
                    $"{entry} names a slot that is not an always-empty optional slot of an observed row"));
        }

        private void CheckExamples(List<PatternRowEntry> entries, IReadOnlyList<PatternRowExample> examples) {
            var heads = new HashSet<(string Rule, string Pattern)>();
            // Reader heads and reader-only rows are read but never printed through the table, so an
            // example the matcher must select stands in for the observation (it is what finds a shadowed row).
            foreach (var entry in entries.Where(e => e.Kind is PatternRowEntryKind.ReaderHead or PatternRowEntryKind.ReaderOnly)) {
                if (entry.Pattern is { } pattern)
                    heads.Add((entry.Rule, pattern));
                else
                    heads.UnionWith(_grammar.GetPatterns(entry.Rule).Select(row => (entry.Rule, row.Name)));
            }

            var exampled = new HashSet<(string Rule, string Pattern)>();
            foreach (var example in examples) {
                void Report(string message) =>
                    _violations.Add(new(PatternRowCheck.Example, example.Rule, example.Pattern, $"example '{Shown(example.Text)}' {message}"));

                if (!string.Equals(example.Surface, surface.Name, StringComparison.Ordinal)) {
                    Report($"is for the surface '{example.Surface}', not '{surface.Name}'");
                    continue;
                }
                if (!_grammar.HasRule(example.Rule) || !_grammar.TryGetPattern(example.Rule, example.Pattern, out _)) {
                    Report("names a row that is not in the table");
                    continue;
                }
                if (!heads.Contains((example.Rule, example.Pattern))) {
                    Report("is for a row that is neither a reader head nor reader-only: delete the example, or the row's entry is missing");
                    continue;
                }
                exampled.Add((example.Rule, example.Pattern));
                if (ReadBack(example.Rule, example.Pattern, example.Text) is { } failure)
                    Report(failure);
            }

            foreach (var (rule, pattern) in heads.Where(head => !exampled.Contains(head)).Order())
                _violations.Add(new(PatternRowCheck.Example, rule, pattern, "is a reader-head or reader-only row with no example"));
        }
    }
}