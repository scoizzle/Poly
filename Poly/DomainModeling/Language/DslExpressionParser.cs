using System.Globalization;

using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.DomainModeling.Language;

/// <summary>
/// Table-driven expression parser over the cursor. One driver loop and an explicit frame stack (a local of
/// each <see cref="ParseExpression"/> call, never a field) replace the old recursive fold, so nesting depth is a
/// counted limit (<see cref="DslLimits.MaxExpressionDepth"/>) with a positioned error and never a stack or matcher
/// budget. Precedence and associativity come from <see cref="DslOperatorTable"/>; the matcher recognizes one atom at
/// a time (<c>expr-primary</c>) and the driver supplies every sub-expression.
/// </summary>
public sealed class DslExpressionParser {
    private readonly IDslParseCursor _c;
    private readonly ExpressionFormRegistry _forms;
    private readonly ExpressionFoldTable _folds;
    private readonly DslOperatorTable _operators;

    public DslExpressionParser(
        IDslParseCursor cursor,
        ExpressionFormRegistry? forms = null,
        ExpressionFoldTable? folds = null,
        DslOperatorTable? operators = null) {
        _c = cursor ?? throw new ArgumentNullException(nameof(cursor));
        _forms = forms ?? new ExpressionFormRegistry();
        _folds = folds ?? ExpressionFoldTable.Core();
        _operators = operators ?? DslOperatorTable.Core(_forms);
    }

    public DomainExpression ParseExpression() => new Run(this).Parse();

    // A token in a message: end of input is named, any other token shows its text.
    private static string Describe(DslToken token) =>
        token.Kind == DslTokenKind.EndOfFile ? "end of input" : DslTokenText.Shown(token);

    private static DslToken CaptureToken(MatchResult<DslToken, DslTokenKind> match, string name) =>
        match.Captures.TryGetValue(name, out var tokens) && tokens.Count > 0
            ? tokens[0]
            : throw new InvalidOperationException($"Match '{match.PatternName}' is missing capture '{name}'.");

    // Every capture read as a name is a word: the rows capture it with an Identifier value or an
    // identifier predicate (rel, prop, hop, quant, unit).
    private static string CaptureText(MatchResult<DslToken, DslTokenKind> match, string name) =>
        CaptureToken(match, name).Name();

    private DomainExpression RewriteIdent(DomainExpression expression) =>
        expression is PropertyAccess pa && _forms.TryRewriteIdent(pa.Name, out var claimed)
            ? claimed
            : expression;

    private static DomainExpression WrapPath(List<string> names, DomainExpression inner) {
        for (var i = names.Count - 2; i >= 0; i--)
            inner = DomainExpression.RelationshipNav(names[i], inner);
        return inner;
    }

    private static DomainExpression Compare(DslTokenKind op, DomainExpression left, DomainExpression right) =>
        op switch {
            DslTokenKind.Is or DslTokenKind.Eq => DomainExpression.Equal(left, right),
            DslTokenKind.Neq => DomainExpression.NotEqual(left, right),
            DslTokenKind.Gt => DomainExpression.GreaterThan(left, right),
            DslTokenKind.Gte => DomainExpression.GreaterThanOrEqual(left, right),
            DslTokenKind.Lt => DomainExpression.LessThan(left, right),
            DslTokenKind.Lte => DomainExpression.LessThanOrEqual(left, right),
            _ => throw new InvalidOperationException($"Unknown comparison operator '{op}'"),
        };

    private enum FrameKind { Top, Level, Prefix, Group, Call, Match, Where, PathCmp }

    private enum ArmPhase { Condition, Value }

    private sealed class Frame(FrameKind kind) {
        public FrameKind Kind { get; } = kind;
        public bool SavedInWhere;
        public DslToken Token;

        // Level
        public int Level;
        public DomainExpression? Acc;
        public int AccHeight;
        public BinaryRow? Op;
        public DslToken OpToken;
        public bool Counted;

        // Prefix
        public PrefixRow? Prefix;

        // Call / Match / Where / PathCmp
        public int MaxHeight;
        public string Name = "";
        public string Quant = "";
        public List<string>? Names;
        public DslTokenKind CompareKind;
        public bool NotEqual;
        public List<DomainExpression>? Args;
        public List<DslToken>? Tokens;
        public List<(int Start, int End)>? Segments;
        public int Index;
        public int Arrow;
        public bool IsElse;
        public ArmPhase Phase;
        public DomainExpression? Pending;
        public List<MatchArm>? Arms;
        public DomainExpression? ElseValue;
    }

    private sealed class Run(DslExpressionParser parser) {
        private readonly DslExpressionParser _p = parser;
        private readonly IDslParseCursor _c = parser._c;
        private readonly DslOperatorTable _t = parser._operators;
        private readonly List<Frame> _stack = [];
        private int _hard;
        private bool _inWhere;
        private DslToken _last;
        private DomainExpression _value = null!;
        private int _height;
        private bool _descend = true;
        private int _target;

        public DomainExpression Parse() {
            _stack.Add(new Frame(FrameKind.Top));
            while (true) {
                if (_descend) {
                    Descend();
                    continue;
                }

                var frame = _stack[^1];
                switch (frame.Kind) {
                    case FrameKind.Top:
                        RejectUnknownDurationUnit();
                        return _value;
                    case FrameKind.Level: OnLevel(frame); break;
                    case FrameKind.Prefix: OnPrefix(frame); break;
                    case FrameKind.Group: OnGroup(frame); break;
                    case FrameKind.Call: OnCall(frame); break;
                    case FrameKind.Match: OnMatch(frame); break;
                    case FrameKind.Where: OnWhere(frame); break;
                    case FrameKind.PathCmp: OnPathCmp(frame); break;
                }
            }
        }

        // ── depth ─────────────────────────────────────────────────────────────────────

        private static GrammarException DepthError(DslToken at) =>
            new(DslLimits.DepthDetail, at.Line, at.Col, at.Start, at.Length);

        /// <summary>A node of relative height <paramref name="height"/> under the enclosing levels counted so far.</summary>
        private void CheckNode(DslToken at, int height) {
            if (_hard + height > DslLimits.MaxExpressionDepth)
                throw DepthError(at);
        }

        private DslToken Take() {
            var token = _c.Current;
            _c.Advance();
            _last = token;
            return token;
        }

        private void Return(DomainExpression value, int height) {
            _value = value;
            _height = height;
            _descend = false;
        }

        private void Push(Frame frame) {
            _stack.Add(frame);
        }

        private Frame Pop() {
            var frame = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            return frame;
        }

        private void DescendTo(int level) {
            _target = level;
            _descend = true;
        }

        // ── descending: one step at the current token ─────────────────────────────────

        private void Descend() {
            if (_target < _t.Levels.Count) {
                if (_t.TryPrefix(_c.Current.Kind, out var row) && _t.HeadLevel(row) == _target) {
                    StartPrefix(row, _c.Current);
                    return;
                }

                Push(new Frame(FrameKind.Level) { Level = _target });
                _target++;
                return;
            }

            Primary();
        }

        private void StartPrefix(PrefixRow row, DslToken first) {
            CheckNode(first, 1);
            var token = Take();
            Push(new Frame(FrameKind.Prefix) { Prefix = row, Token = token });
            _hard++;
            DescendTo(_t.LevelAtOrAbove(row.OperandPrecedence));
        }

        private void Primary() {
            var match = _c.MatchRule("expr-primary")
                ?? throw _c.Error($"Expected expression, got '{Describe(_c.Current)}'");
            var pattern = match.PatternName;
            if (pattern is not "group" and not "not") {
                // Session folds override pattern payload (library Now vs core ident-shaped clock).
                if (_p._folds.TryFold(match.RuleName, match, out var folded)) {
                    Leaf(match, _p.RewriteIdent(folded));
                    return;
                }

                if (match.TryFoldExpression(out var bound)) {
                    Leaf(match, _p.RewriteIdent(bound));
                    return;
                }
            }

            switch (pattern) {
                case "group": StartGroup(); break;
                case "not":
                    StartPrefix(
                        _t.TryPrefix(match.Tokens[0].Kind, out var row)
                            ? row
                            : throw new InvalidOperationException($"No prefix row for '{match.Tokens[0].Kind}'."),
                        match.Tokens[0]);
                    break;
                case "duration": Leaf(match, FoldUnresolvedDuration(match)); break;
                case "match": StartMatch(match); break;
                case "call": StartCall(match); break;
                case "exists":
                    Leaf(match, DomainExpression.Exists(DomainExpression.Property(CaptureText(match, "rel"))));
                    break;
                case "count-bare":
                    Leaf(match, DomainExpression.Count(CaptureText(match, "rel"), null));
                    break;
                case "sum-prop":
                    Leaf(match, DomainExpression.Sum(CaptureText(match, "rel"), CaptureText(match, "prop")));
                    break;
                case "where-nav":
                case "quant-where": StartWhere(match); break;
                case "path-is-not": StartPathCmp(match, notEqual: true); break;
                case "path-cmp": StartPathCmp(match, notEqual: false); break;
                case "path":
                    var names = PathNames(match);
                    Leaf(match, WrapPath(names, DomainExpression.Property(names[^1])));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Cannot fold primary '{match.RuleName}/{pattern}'.");
            }
        }

        private void Leaf(MatchResult<DslToken, DslTokenKind> match, DomainExpression value) {
            _c.Consume(match);
            _last = match.Tokens[^1];
            Return(value, 0);
        }

        private static List<string> PathNames(MatchResult<DslToken, DslTokenKind> match) {
            var names = new List<string> { CaptureText(match, "rel"), CaptureText(match, "prop") };
            foreach (var hop in match.Children)
                names.Add(CaptureText(hop, "hop"));
            return names;
        }

        private DomainExpression FoldUnresolvedDuration(MatchResult<DslToken, DslTokenKind> match) {
            // The duration row captures 'amount' with a Number value.
            var amountToken = CaptureToken(match, "amount");
            if (!long.TryParse(amountToken.Span, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
                throw _c.Error($"Duration amount '{DslTokenText.Shown(amountToken)}' is not an integer.");
            return new UnresolvedDuration(amount, CaptureText(match, "unit"));
        }

        /// <summary>
        /// A duration is <c>Number</c> + a known unit, so a number followed on the same line by a word that is not a unit
        /// fails closed here with the legal spellings. Runs at the end of the top-level expression and of every group,
        /// argument and arm sub-expression. Exempt: <c>Identifier :</c> and a same-line <c>function</c> declaration.
        /// </summary>
        private void RejectUnknownDurationUnit() {
            if (_last.Kind != DslTokenKind.Number) return;
            var current = _c.Current;
            if (current.Kind != DslTokenKind.Identifier) return;
            if (current.Line != _last.Line) return;
            if (_c.PeekIs(DslTokenKind.Colon)) return;
            if (DslGrammar.IsFunctionDeclarationWord(current)) return;
            if (DslGrammar.IsDurationUnitToken(current)) return;
            throw _c.Error(
                $"Unknown duration unit '{DslTokenText.Shown(current)}'. Legal units: {DurationUnits.LegalUnits}.");
        }

        // ── chain levels ──────────────────────────────────────────────────────────────

        private void OnLevel(Frame f) {
            if (f.Acc is null) {
                f.Acc = _value;
                f.AccHeight = _height;
            }
            else {
                if (f.Counted) {
                    _hard--;
                    f.Counted = false;
                }

                f.Acc = f.Op!.Fold(f.Acc, _value);
                f.AccHeight = Math.Max(f.AccHeight, _height) + 1;
                CheckNode(f.OpToken, f.AccHeight);
                if (f.Op.Assoc == Assoc.None) {
                    FinishLevel(f);
                    return;
                }
            }

            if (_t.TryBinary(f.Level, _c.Current, () => _c.Peek(1), out var row, out var tokens)) {
                f.OpToken = _c.Current;
                for (var i = 0; i < tokens; i++)
                    Take();
                f.Op = row;
                CheckNode(f.OpToken, f.AccHeight + 1);
                f.Counted = true;
                _hard++;
                DescendTo(f.Level + 1);
                return;
            }

            FinishLevel(f);
        }

        private void FinishLevel(Frame f) {
            Pop();
            Return(f.Acc!, f.AccHeight);
        }

        // ── prefix, group ─────────────────────────────────────────────────────────────

        private void OnPrefix(Frame f) {
            Pop();
            _hard--;
            var height = _height + 1;
            CheckNode(f.Token, height);
            Return(f.Prefix!.Fold(_value), height);
        }

        private void StartGroup() {
            var open = _c.Current;
            CheckNode(open, 1);
            Take();
            if (_c.Current.Kind == DslTokenKind.RParen)
                throw _c.Error("Expected expression inside parentheses");
            Push(new Frame(FrameKind.Group) { Token = open, SavedInWhere = _inWhere });
            _inWhere = false;
            _hard++;
            DescendTo(0);
        }

        private void OnGroup(Frame f) {
            RejectUnknownDurationUnit();
            var current = _c.Current;
            if (current.Kind != DslTokenKind.RParen)
                throw current.Kind == DslTokenKind.EndOfFile
                    ? _c.Error("Expected ')' to close the group")
                    : _c.Error($"Unexpected '{Describe(current)}' in group");
            Take();
            Pop();
            _hard--;
            _inWhere = f.SavedInWhere;
            var height = _height + 1;
            CheckNode(f.Token, height);
            Return(_value, height);
        }

        // ── where bodies and path comparisons ─────────────────────────────────────────

        private void StartWhere(MatchResult<DslToken, DslTokenKind> match) {
            if (_inWhere)
                throw _c.Error("Nested 'where' is not allowed. Use parentheses for grouped conditions instead.");
            var first = match.Tokens[0];
            CheckNode(first, 1);
            var frame = new Frame(FrameKind.Where) {
                Token = first,
                SavedInWhere = _inWhere,
                Name = CaptureText(match, "rel"),
                Quant = match.PatternName == "quant-where" ? CaptureText(match, "quant").ToLowerInvariant() : "",
            };
            for (var i = 0; i < match.Tokens.Count; i++)
                Take();
            Push(frame);
            _inWhere = true;
            _hard++;
            DescendTo(_t.LevelOf(DslTokenKind.And));
        }

        private void OnWhere(Frame f) {
            Pop();
            _hard--;
            _inWhere = f.SavedInWhere;
            var height = _height + 1;
            CheckNode(f.Token, height);
            var body = _value;
            DomainExpression result = f.Quant switch {
                "" => DomainExpression.RelationshipNav(f.Name, body),
                "any" => DomainExpression.Any(f.Name, body),
                "all" => DomainExpression.All(f.Name, body),
                "none" => DomainExpression.None(f.Name, body),
                "count" => DomainExpression.Count(f.Name, body),
                "sum" => throw _c.Error("sum does not take where; write sum Rel Prop"),
                _ => throw _c.Error($"Unknown quantifier '{f.Quant}'"),
            };
            Return(result, height);
        }

        private void StartPathCmp(MatchResult<DslToken, DslTokenKind> match, bool notEqual) {
            var first = match.Tokens[0];
            CheckNode(first, 1);
            var frame = new Frame(FrameKind.PathCmp) {
                Token = first,
                Names = PathNames(match),
                NotEqual = notEqual,
                CompareKind = notEqual ? DslTokenKind.Neq : match.Captures["op"][0].Kind,
            };
            for (var i = 0; i < match.Tokens.Count; i++)
                Take();
            Push(frame);
            _hard++;
            DescendTo(_t.Levels.Count);
        }

        private void OnPathCmp(Frame f) {
            Pop();
            _hard--;
            var height = _height + 1;
            CheckNode(f.Token, height);
            var names = f.Names!;
            DomainExpression inner = f.NotEqual
                ? DomainExpression.NotEqual(DomainExpression.Property(names[^1]), _value)
                : Compare(f.CompareKind, DomainExpression.Property(names[^1]), _value);
            Return(WrapPath(names, inner), height);
        }

        // ── calls ─────────────────────────────────────────────────────────────────────

        private void StartCall(MatchResult<DslToken, DslTokenKind> match) {
            var name = match.Tokens[0];
            CheckNode(name, 1);
            Take();
            Take();
            Push(new Frame(FrameKind.Call) {
                Token = name,
                // The call row's name is an identifier (IsPathHopIdent).
                Name = name.Name(),
                Args = [],
                SavedInWhere = _inWhere,
            });
            _inWhere = false;
            _hard++;
            StartArgument(_stack[^1], first: true);
        }

        private void StartArgument(Frame f, bool first) {
            var current = _c.Current;
            if (first && current.Kind == DslTokenKind.RParen) {
                FinishCall(f);
                return;
            }

            if (current.Kind is DslTokenKind.Comma or DslTokenKind.RParen)
                throw _c.Error(
                    $"Expected an argument in call to '{f.Name}' before '{Describe(current)}' (line {current.Line}, col {current.Col})");
            DescendTo(0);
        }

        private void OnCall(Frame f) {
            RejectUnknownDurationUnit();
            f.MaxHeight = Math.Max(f.MaxHeight, _height);
            f.Args!.Add(_value);
            var current = _c.Current;
            if (current.Kind == DslTokenKind.Comma) {
                Take();
                StartArgument(f, first: false);
                return;
            }

            if (current.Kind == DslTokenKind.RParen) {
                FinishCall(f);
                return;
            }

            throw _c.Error($"Unexpected '{Describe(current)}' in call argument");
        }

        private void FinishCall(Frame f) {
            Take();
            Pop();
            _hard--;
            _inWhere = f.SavedInWhere;
            var height = f.MaxHeight + 1;
            CheckNode(f.Token, height);
            Return(DomainExpression.Call(f.Name, f.Args!, f.Token.Line, f.Token.Col), height);
        }

        // ── match ─────────────────────────────────────────────────────────────────────

        /// <summary>A parse error positioned at <paramref name="at"/>, the token the fault is on.</summary>
        private static Exception ErrorAt(DslToken at, string message) => GrammarError.Error(message, at.Line, at.Col);

        private static string At(DslToken t) => $" (line {t.Line}, col {t.Col})";

        private static bool IsOpen(DslTokenKind k) => k is DslTokenKind.LParen or DslTokenKind.LBrace or DslTokenKind.LBracket;

        private static bool IsClose(DslTokenKind k) => k is DslTokenKind.RParen or DslTokenKind.RBrace or DslTokenKind.RBracket;

        /// <summary>
        /// <c>match { cond -&gt; value, ..., else -&gt; value }</c>: the balanced braces are split at top-level commas; every
        /// segment malformation is a positioned error before any arm is parsed. The arms themselves are parsed in the stream.
        /// </summary>
        private void StartMatch(MatchResult<DslToken, DslTokenKind> match) {
            var keyword = match.Tokens[0];
            CheckNode(keyword, 1);
            var tokens = match.Tokens.ToList();
            var closing = tokens[^1];
            var segments = new List<(int Start, int End)>();
            var start = 2;
            var depth = 0;
            for (var i = 2; i < tokens.Count - 1; i++) {
                var token = tokens[i];
                if (IsOpen(token.Kind)) depth++;
                else if (IsClose(token.Kind)) depth--;
                if (depth == 0 && token.Kind == DslTokenKind.Comma) {
                    if (i == start)
                        throw ErrorAt(token, $"Expected an arm in 'match' before '{DslTokenText.Shown(token)}'{At(token)}");
                    segments.Add((start, i));
                    start = i + 1;
                }
            }

            if (start < tokens.Count - 1) segments.Add((start, tokens.Count - 1));
            else if (segments.Count > 0)
                throw ErrorAt(closing, $"Expected an arm in 'match' after the trailing ','{At(closing)}");

            if (segments.Count == 0)
                throw ErrorAt(keyword, $"'match' needs at least an 'else' arm (else -> value){At(keyword)}");

            var elseSegments = segments.Where(sg => tokens[sg.Start].Kind == DslTokenKind.Else).ToList();
            if (elseSegments.Count > 1)
                throw ErrorAt(tokens[elseSegments[1].Start], $"'match' has more than one 'else' arm{At(tokens[elseSegments[1].Start])}");

            Take();
            Take();
            var frame = new Frame(FrameKind.Match) {
                Token = keyword,
                Tokens = tokens,
                Segments = segments,
                Arms = [],
                SavedInWhere = _inWhere,
            };
            Push(frame);
            _inWhere = false;
            _hard++;
            StartArm(frame);
        }

        private void StartArm(Frame f) {
            var tokens = f.Tokens!;
            var segments = f.Segments!;
            if (f.Index == segments.Count) {
                if (f.ElseValue is null)
                    throw ErrorAt(f.Token, $"'match' requires a final 'else' arm (else -> value){At(f.Token)}");
                Take();
                Pop();
                _hard--;
                _inWhere = f.SavedInWhere;
                var height = f.MaxHeight + 1;
                CheckNode(f.Token, height);
                Return(DomainExpression.Match(f.Arms!, f.ElseValue, f.Token.Line, f.Token.Col), height);
                return;
            }

            var (start, end) = segments[f.Index];
            var first = tokens[start];
            var arrow = -1;
            var depth = 0;
            for (var j = start; j < end; j++) {
                var k = tokens[j].Kind;
                if (IsOpen(k)) depth++;
                else if (IsClose(k)) depth--;
                else if (depth == 0 && k == DslTokenKind.Arrow) { arrow = j; break; }
            }

            if (arrow < 0)
                throw ErrorAt(first, $"Expected '->' in 'match' arm starting at '{DslTokenText.Shown(first)}'{At(first)}");
            var isElse = arrow - start == 1 && first.Kind == DslTokenKind.Else;
            if (arrow == start)
                throw ErrorAt(tokens[arrow], $"Expected a condition before '->' in 'match'{At(tokens[arrow])}");
            if (arrow == end - 1)
                throw ErrorAt(tokens[arrow], $"Expected a value after '->' in 'match'{At(tokens[arrow])}");
            if (isElse) {
                if (f.ElseValue is not null)
                    throw ErrorAt(first, $"'match' has more than one 'else' arm{At(first)}");
                if (f.Index != segments.Count - 1)
                    throw ErrorAt(first, $"'else' must be the last arm of a 'match'{At(first)}");
            }
            else {
                for (var j = start; j < arrow; j++) {
                    if (tokens[j].Kind == DslTokenKind.Else)
                        throw ErrorAt(first, $"'else' cannot appear inside a 'match' condition{At(first)}");
                }
            }

            f.Arrow = arrow;
            f.IsElse = isElse;
            if (isElse) {
                Take();
                Take();
                f.Phase = ArmPhase.Value;
            }
            else {
                f.Phase = ArmPhase.Condition;
            }

            DescendTo(0);
        }

        private void OnMatch(Frame f) {
            RejectUnknownDurationUnit();
            f.MaxHeight = Math.Max(f.MaxHeight, _height);
            var tokens = f.Tokens!;
            var current = _c.Current;
            if (f.Phase == ArmPhase.Condition) {
                if (current.Start != tokens[f.Arrow].Start)
                    throw _c.Error($"Unexpected '{Describe(current)}' in match condition");
                f.Pending = _value;
                Take();
                f.Phase = ArmPhase.Value;
                DescendTo(0);
                return;
            }

            var end = f.Segments![f.Index].End;
            if (current.Start != tokens[end].Start)
                throw _c.Error($"Unexpected '{Describe(current)}' in match value");
            if (f.IsElse)
                f.ElseValue = _value;
            else
                f.Arms!.Add(new MatchArm(f.Pending!, _value));
            if (tokens[end].Kind == DslTokenKind.Comma)
                Take();
            f.Index++;
            StartArm(f);
        }
    }
}