using System.Text.RegularExpressions;

using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The shape of the strings a <c>pattern(...)</c> constraint can accept: a length
/// envelope in UTF-16 code units (the unit <c>string.Length</c> and
/// <c>ConstraintValidation.IsValidLength</c> use), whether the pattern is valid,
/// and whether it is anchored at both ends.
/// </summary>
/// <remarks>
/// Validity is delegated to .NET (<c>new Regex</c>); the hand scanner only ever
/// rounds outward or bails to an unboundable envelope, so a wrong shape cannot
/// invent a contradiction. An end anchor follows L-20: a trailing unescaped
/// <c>$</c> is absolute (<c>\z</c>), so <c>^[A-Z]{1,3}$</c> is at most 3, not 4 —
/// #681:165's "+1 for a trailing newline" reading is stale after L-20.
/// </remarks>
internal sealed record PatternShape(
    bool IsValid,
    string? Error,
    int MinLength,
    int? MaxLength,
    bool Anchored) {
    /// <summary>Recursion budget: <c>poly_define</c> patterns must never overflow the stack.</summary>
    private const int MaxDepth = 64;

    /// <summary>
    /// Scans <paramref name="pattern"/> into a sound envelope. Returns an unboundable
    /// shape (<c>MinLength 0</c>, <c>MaxLength null</c>, <c>Anchored false</c>) for any
    /// construct the scanner does not model; an invalid regex keeps <see cref="IsValid"/>
    /// false so it stays V-13's diagnostic, not a length contradiction.
    /// </summary>
    internal static PatternShape Of(string pattern) {
        ArgumentNullException.ThrowIfNull(pattern);

        try {
            _ = new Regex(pattern);
        }
        catch (RegexParseException ex) {
            return new PatternShape(false, ex.Message, 0, null, false);
        }

        var scan = new Scanner(pattern).Scan();
        if (scan is not { } result) return new PatternShape(true, null, 0, null, false);

        var anchored = result.StartAnchored && result.EndAnchored;
        return new PatternShape(
            true,
            null,
            (int)result.Min,
            anchored && result.Max is { } max ? (int)max : null,
            anchored);
    }

    /// <summary>
    /// True when this pattern's envelope and <paramref name="length"/> cannot both hold.
    /// An invalid regex is never a contradiction here (it is a separate diagnostic).
    /// </summary>
    internal bool ContradictsLength(LengthConstraint length) {
        ArgumentNullException.ThrowIfNull(length);
        if (!IsValid) return false;
        if (MaxLength is { } max && max < length.MinLength) return true;
        return MinLength > length.MaxLength;
    }

    private readonly record struct Envelope(long Min, long? Max);

    private readonly record struct ScanResult(
        long Min,
        long? Max,
        bool StartAnchored,
        bool EndAnchored);

    /// <summary>
    /// The one-pass scanner. Every metacharacter has an arm; anything unmodelled bails.
    /// Min/max are <c>long</c> internally and clamped at each step so they cannot overflow.
    /// </summary>
    private sealed class Scanner(string pattern) {
        private readonly string _s = pattern;
        private int _i;
        private bool _endAnchored;

        internal ScanResult? Scan() {
            var startAnchored = false;
            if (_s.Length > 0 && _s[0] == '^') {
                if (IsQuantifierAt(1)) return null; // A3: a quantified anchor bails
                _i = 1;
                startAnchored = true;
            }
            else if (_s.Length >= 2 && _s[0] == '\\' && _s[1] == 'A') {
                if (IsQuantifierAt(2)) return null;
                _i = 2;
                startAnchored = true;
            }

            var envelope = ParseAlternation(depth: 0, close: null);
            if (envelope is not { } body) return null;
            return new ScanResult(body.Min, body.Max, startAnchored, _endAnchored);
        }

        private Envelope? ParseAlternation(int depth, char? close) {
            Envelope? union = null;
            while (true) {
                var sequence = ParseSequence(depth, close);
                if (sequence is null) return null;
                union = union is { } seen ? Union(seen, sequence.Value) : sequence;

                if (_i >= _s.Length) return close is null ? union : null;
                var c = _s[_i];
                if (c == '|') {
                    if (close is null) return null; // top-level alternation is unboundable
                    _i++;
                    continue;
                }
                if (close is not null && c == close) {
                    _i++;
                    return union;
                }
                return null;
            }
        }

        private Envelope? ParseSequence(int depth, char? close) {
            Envelope? sequence = null;
            while (_i < _s.Length) {
                var c = _s[_i];
                if (c == '|' || (close is not null && c == close)) break;

                var atom = ParseAtom(depth);
                if (atom is not { } value) return null;
                var quantified = ApplyQuantifier(value);
                if (quantified is not { } piece) return null;
                sequence = sequence is { } seen ? Concatenate(seen, piece) : piece;
            }
            return sequence ?? new Envelope(0, 0);
        }

        private Envelope? ParseAtom(int depth) {
            var c = _s[_i];
            switch (c) {
                case '$':
                    // A2: the scanner and L-20's backslash-parity rule must agree, and the
                    // anchor must be the final token (A3 covers a following quantifier).
                    if (depth != 0
                        || _i != _s.Length - 1
                        || !IsUnescapedTrailingDollar(_s)) {
                        return null;
                    }
                    _i++;
                    _endAnchored = true;
                    return new Envelope(0, 0);

                case '^':
                    return null; // a start anchor is only the pattern's first token

                case '\\':
                    return ParseEscape(depth);

                case '[':
                    return ParseClass();

                case '(':
                    return ParseGroup(depth);

                case '.':
                    _i++;
                    return new Envelope(1, 1);

                case '*':
                case '+':
                case '?':
                    return null; // a quantifier with no atom: unmodelled here (A3 for anchors)

                default:
                    // Literals, including a stray '{', '}', ']' that was not a quantifier.
                    _i++;
                    return new Envelope(1, 1);
            }
        }

        private Envelope? ParseEscape(int depth) {
            if (_i + 1 >= _s.Length) return null;
            var escape = _s[_i + 1];

            if (depth == 0 && escape == 'z') {
                // Absolute end, only as the final token (a following quantifier bails).
                if (_i + 2 != _s.Length) return null;
                _i += 2;
                _endAnchored = true;
                return new Envelope(0, 0);
            }

            // A1: the modelled set only. Everything else — \A \b \B \G \Z \k \x \u \c
            // \0 \a \f \v \e \p \P and any unknown escape — bails.
            if (!IsModelledEscape(escape)) return null;

            _i += 2;
            return new Envelope(1, 1);
        }

        private Envelope? ParseClass() {
            var j = _i + 1;
            if (j < _s.Length && _s[j] == '^') j++;
            if (j >= _s.Length || _s[j] == ']') return null; // a class starting with ']'

            while (j < _s.Length) {
                var c = _s[j];
                if (c == '\\') {
                    if (j + 1 >= _s.Length || !IsModelledEscape(_s[j + 1])) return null;
                    j += 2;
                    continue;
                }
                if (c == '[') return null; // class subtraction or a nested '[' — unmodelled
                if (c == ']') {
                    _i = j + 1;
                    return new Envelope(1, 1);
                }
                j++;
            }
            return null; // unclosed class
        }

        private Envelope? ParseGroup(int depth) {
            var j = _i + 1;
            if (j < _s.Length && _s[j] == '?') {
                // Only '(?:' is modelled: lookaround, inline options, named and atomic
                // groups all bail.
                if (j + 1 >= _s.Length || _s[j + 1] != ':') return null;
                j += 2;
            }
            if (depth + 1 > MaxDepth) return null;

            _i = j;
            return ParseAlternation(depth + 1, ')');
        }

        private Envelope? ApplyQuantifier(Envelope atom) {
            if (_i >= _s.Length) return atom;

            long quantifierMin;
            long? quantifierMax;
            switch (_s[_i]) {
                case '*':
                    quantifierMin = 0;
                    quantifierMax = null;
                    _i++;
                    break;
                case '+':
                    quantifierMin = 1;
                    quantifierMax = null;
                    _i++;
                    break;
                case '?':
                    quantifierMin = 0;
                    quantifierMax = 1;
                    _i++;
                    break;
                case '{' when TryReadBraceQuantifier(out var lower, out var upper):
                    quantifierMin = lower;
                    quantifierMax = upper;
                    break;
                default:
                    return atom;
            }

            if (_i < _s.Length && _s[_i] == '?') _i++; // a lazy suffix changes nothing
            return Repeat(atom, quantifierMin, quantifierMax);
        }

        private bool TryReadBraceQuantifier(out long min, out long? max) {
            min = 0;
            max = 0;

            var j = _i + 1;
            if (j >= _s.Length || !IsAsciiDigit(_s[j])) return false;

            var lowerStart = j;
            while (j < _s.Length && IsAsciiDigit(_s[j])) j++;
            if (!long.TryParse(_s.AsSpan(lowerStart, j - lowerStart), out var lower)) return false;

            if (j < _s.Length && _s[j] == ',') {
                j++;
                var upperStart = j;
                while (j < _s.Length && IsAsciiDigit(_s[j])) j++;
                if (j >= _s.Length || _s[j] != '}') return false;
                if (j == upperStart) max = null;
                else if (long.TryParse(_s.AsSpan(upperStart, j - upperStart), out var upper)) max = upper;
                else return false;
            }
            else if (j < _s.Length && _s[j] == '}') {
                max = lower;
            }
            else {
                return false;
            }

            if (max is { } ceiling && lower > ceiling) return false;

            _i = j + 1;
            min = lower;
            return true;
        }

        private bool IsQuantifierAt(int index) {
            if (index >= _s.Length) return false;
            var c = _s[index];
            if (c is '*' or '+' or '?') return true;
            if (c != '{') return false;

            var j = index + 1;
            if (j >= _s.Length || !IsAsciiDigit(_s[j])) return false;
            while (j < _s.Length && IsAsciiDigit(_s[j])) j++;
            if (j < _s.Length && _s[j] == ',') {
                j++;
                while (j < _s.Length && IsAsciiDigit(_s[j])) j++;
            }
            return j < _s.Length && _s[j] == '}';
        }

        private static bool IsModelledEscape(char escape) => escape is
            '\\' or '.' or '*' or '+' or '?' or '(' or ')' or '[' or ']' or '{' or '}'
            or '|' or '^' or '$' or '-' or '/' or '"' or '\''
            or 'n' or 'r' or 't'
            or 'd' or 'D' or 'w' or 'W' or 's' or 'S';

        private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

        /// <summary>L-20's parity rule: an odd backslash run escapes the trailing <c>$</c>.</summary>
        private static bool IsUnescapedTrailingDollar(string pattern) {
            if (pattern.Length == 0 || pattern[^1] != '$') return false;
            var backslashes = 0;
            for (var i = pattern.Length - 2; i >= 0 && pattern[i] == '\\'; i--) backslashes++;
            return backslashes % 2 == 0;
        }

        private static Envelope Union(Envelope left, Envelope right) =>
            new(
                Math.Min(left.Min, right.Min),
                left.Max is { } a && right.Max is { } b ? Math.Max(a, b) : null);

        private static Envelope Concatenate(Envelope left, Envelope right) {
            var min = (Int128)left.Min + right.Min;
            Int128? max = left.Max is { } a && right.Max is { } b ? (Int128)a + b : null;
            return new(
                min > int.MaxValue ? int.MaxValue : (long)min,
                max is null || max > int.MaxValue ? null : (long)max);
        }

        private static Envelope Repeat(Envelope atom, long min, long? max) {
            if (max == 0) return new Envelope(0, 0); // (a*){0} is still empty
            var lower = (Int128)atom.Min * min;
            Int128? upper = atom.Max is { } a && max is { } b ? (Int128)a * b : null;
            return new(
                lower > int.MaxValue ? int.MaxValue : (long)lower,
                upper is null || upper > int.MaxValue ? null : (long)upper);
        }
    }
}