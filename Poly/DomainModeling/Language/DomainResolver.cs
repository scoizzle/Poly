using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;
using Poly.Grammar;

using TokenKind = Poly.DomainModeling.Language.DslTokenKind;

namespace Poly.DomainModeling.Language;

/// <summary>
/// Applies the loaded session's meaning to a parsed <see cref="DslSyntaxTree"/>.
/// Structure/annotations: Matcher over <see cref="DslGrammar"/>.
/// Expressions: <see cref="DslExpressionParser"/> (product shapes + session concept folds).
/// Cursor mechanics (Current/Advance/Expect/MatchRule/Consume/Error) come from
/// <see cref="DslCursor"/> — the reader owns the committed position.
/// </summary>
public sealed partial class DomainResolver : DslCursor {
    private readonly DslExpressionParser _expressions;
    private int _entityIndex;
    private bool _primitivesAdded;

    // Accumulated changes for the current entity
    private string _currentEntityName = "";
    private readonly Dictionary<string, DomainExpression> _entityPolicies = new(StringComparer.Ordinal);

    // Pending requires that must be resolved after the full entity body is parsed
    private readonly List<PendingRequire> _pendingRequires = new();
    private readonly List<PendingStageRequire> _pendingStageRequires = new();

    // Pending navigation properties (N1 form) resolved after all entities are known
    private readonly List<PendingNav> _pendingNavs = new();

    // Entity names collected during parsing, for nav target resolution
    private readonly HashSet<string> _entityNames = new(StringComparer.Ordinal);

    // Enum type names, for distinguishing typed properties from nav lines
    private readonly HashSet<string> _enumTypeNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _valueTypeNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _primitiveTypeNames = new(StringComparer.Ordinal);

    // Property names per entity, for collision detection with navs
    private readonly Dictionary<string, HashSet<string>> _entityPropertyNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _entityStageNames = new(StringComparer.Ordinal);

    // Relationship names from N1 nav lines, for duplicate detection
    private readonly Dictionary<string, HashSet<string>> _relationshipNamesByEntity = new(StringComparer.Ordinal);

    // Q1′′′.5 / Q1'''''.2: Prevents recursive `Rel where ...` parsing inside a where body.
    // (Flag lives on DslParseCursorBase as _inWhereBody.)

    // Explicit parse inputs / annotation support
    private readonly AnnotationRegistry? _annotations;
    private readonly DomainSession _session;
    private readonly DslSyntaxTree _tree;

    // Contract names seeded from a `uses` id, and names a body block already declared.
    private readonly Dictionary<string, string> _seededContracts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _authoredContracts = new(StringComparer.Ordinal);

    private readonly record struct PendingRequire(
        string ActionName,
        string? StageName,
        string PolicyName,
        bool Negated);

    private readonly record struct PendingStageRequire(
        string StageName,
        string PolicyName,
        bool Negated,
        DslToken At);

    private readonly record struct PendingNav(
        string SourceEntityName,
        string PropertyName,
        string TargetTypeName,
        RelationshipCardinality Cardinality,
        bool SourceOwnsTarget,
        DslToken At);

    public DomainResolver(DslSyntaxTree tree, DomainSession session)
        : base(TokenReaderFor(tree), reader => new Matcher<DslToken, DslTokenKind>(session.Language.Grammar, reader)) {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(session);
        _tree = tree;
        _session = session;
        _annotations = session.Annotations;
        _expressions = new DslExpressionParser(this, session.ExpressionForms, session.Folds);
    }

    private static ListTokenReader<DslToken, DslTokenKind> TokenReaderFor(DslSyntaxTree tree) {
        ArgumentNullException.ThrowIfNull(tree);
        return new(tree.Tokens, kind => kind == DslTokenKind.EndOfFile);
    }

    private DomainExpression ParseExpression() =>
        DslExpressionFragment.RejectUnresolvedDuration(_expressions.ParseExpression());

    /// <summary>
    /// Resolves the document and returns a list of <see cref="DomainChange"/>
    /// that, when applied via evolution, produces the declared domain.
    /// </summary>
    public List<DomainChange> Resolve() =>
        // The single resolve point, so every facade that reaches the resolver — PolyDslParser,
        // DomainCompilation, PolyCompiler — parses on the shared stack (OQ-4 / Q17 condition 1).
        DslLimits.RunOnParseStack(ResolveOnParseStack);

    private List<DomainChange> ResolveOnParseStack() {
        var changes = new List<DomainChange>();

        // ── Domain header ─────────────────────────────────────
        Expect(TokenKind.Domain);
        var domainName = ExpectIdentifier(TokenKind.Identifier, "domain name");
        changes.Add(new SetDomainNameChange(domainName));

        // The reader owns the header shape (including an optional same-line pin); the cursor
        // only has to move past the tokens it consumed, so the two cannot disagree.
        for (var headerIndex = 2; headerIndex < _tree.HeaderEnd; headerIndex++)
            Advance();

        for (var useIndex = 0; useIndex < _tree.Uses.Count; useIndex++) {
            var extensionId = _tree.Uses[useIndex];
            var pin = useIndex < _tree.UsesVersions.Count ? _tree.UsesVersions[useIndex] : null;
            changes.Add(new AddDomainExtensionChange(extensionId));

            var contribution = _session.FindContractContribution(extensionId);
            if (contribution is not null) {
                _seededContracts[contribution.Surface.Name] = extensionId;
                changes.AddRange(contribution.ToChanges(pin));
            }
            else if (pin is not null) {
                throw ErrorAtUse(extensionId,
                    $"Extension '{extensionId}' pins surface version '{pin}' but contributed no contract.");
            }
        }

        // Every document carries the canonical built-in primitives, plus whichever primitives
        // the loaded libraries seed — an entity-free document included (#478).
        EnsurePrimitivesOnce(changes);

        // ── Enum type definitions + entity definitions ─────────
        // Parse entities and enum types in order; enum types must precede entities
        // that reference them for property type resolution.
        // Dispatch is matcher-driven over the "top" rule; unmodeled shapes get
        // the same targeted diagnostics ParseEntity would produce.
        while (Current.Kind == TokenKind.Identifier || Current.Kind == TokenKind.Public) {
            if (TryTakeVisibility(out var visibility)) {
                ParseMarkedTop(changes, visibility);
                continue;
            }
            if (IsFunctionDeclarationStart()) {
                ParseFunction(changes);
                continue;
            }
            switch (MatchRule("top")?.PatternName) {
                case "enum":
                    ParseEnumType(changes);
                    break;
                case "entity":
                    ParseEntity(changes);
                    break;
                default:
                    if (TryParseNamedTopLevel(changes))
                        break;
                    throw TopLevelRejection();
            }
        }

        // ── Resolve N1 navigation properties ───────────────────
        ResolvePendingNavs(changes);

        if (Current.Kind == TokenKind.Relationship)
            throw N2RelationshipNotSupported();

        Expect(TokenKind.EndOfFile);
        return changes;
    }

    /// <summary>Positions a header finding on the <c>uses</c> id token for <paramref name="extensionId"/>.</summary>
    private FormatException ErrorAtUse(string extensionId, string message) {
        for (var i = 2; i + 1 < _tree.Tokens.Count && i < _tree.HeaderEnd; i++) {
            if (_tree.Tokens[i].Kind != TokenKind.Uses
                || _tree.Tokens[i + 1].Kind != TokenKind.Identifier
                || !string.Equals(_tree.Tokens[i + 1].Text, extensionId, StringComparison.Ordinal))
                continue;
            var token = _tree.Tokens[i + 1];
            return GrammarError.Error(message, token.Line, token.Col);
        }
        return GrammarError.Error(message);
    }

    private void EnsurePrimitivesOnce(List<DomainChange> changes) {
        if (_primitivesAdded) return;
        _primitivesAdded = true;
        foreach (var change in CanonicalBuiltInTypeCatalog.CreateChanges())
            changes.Add(change);
        var adding = changes.OfType<AddDomainExtensionChange>()
            .Select(c => c.ExtensionId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (libraryId, name, semantics) in _session.PrimitiveSeeds) {
            if (adding.Contains(libraryId))
                continue;
            changes.Add(new AddPrimitiveTypeChange(name, semantics, []));
        }
        IndexPrimitiveNames(changes);
    }

    private void IndexPrimitiveNames(List<DomainChange> changes) {
        foreach (var def in CanonicalBuiltInTypeCatalog.Definitions)
            _primitiveTypeNames.Add(def.Name);
        foreach (var (_, name, _) in _session.PrimitiveSeeds)
            _primitiveTypeNames.Add(name);
        foreach (var add in changes.OfType<AddDomainExtensionChange>()) {
            if (!ExtensionCatalog.Core.Contains(add.ExtensionId))
                continue;
            foreach (var (name, _) in ExtensionCatalog.Core.Resolve(add.ExtensionId).PrimitiveSeeds)
                _primitiveTypeNames.Add(name);
        }
    }

    /// <summary>
    /// A property name is an identifier or a primitive type keyword (<c>Text</c>,
    /// <c>Number</c>, <c>Boolean</c>), the same words the entity declaration accepts.
    /// </summary>
    private string ExpectPropertyName() {
        if (Current.Kind == TokenKind.Identifier || DslGrammar.IsPrimitiveTypeKind(Current.Kind)) {
            var name = Current.Text;
            Advance();
            return name;
        }
        throw Error($"Expected property name, got '{Current.Text}'");
    }

    // ── Helpers ───────────────────────────────────────────────

    private List<string> ParseIdentifierList() {
        var list = new List<string>();
        list.Add(ExpectIdentifier(TokenKind.Identifier, "identifier"));
        while (Current.Kind == TokenKind.Comma) {
            Advance();
            list.Add(ExpectIdentifier(TokenKind.Identifier, "identifier"));
        }
        return list;
    }

    /// <summary>
    /// <c>required</c> is not a constraint. A property is required unless it is marked
    /// <c>optional</c>. A member whose name is <c>required</c> stays legal
    /// (<c>required: Text</c>).
    /// </summary>
    private void RejectRetiredRequiredWord() {
        if (Current.Kind == TokenKind.Identifier
            && string.Equals(Current.Text, "required", StringComparison.Ordinal)
            && !PeekIs(TokenKind.Colon)) {
            throw Error(
                "'required' is not a constraint. A property is required unless it is marked 'optional'.");
        }
    }

    private static readonly HashSet<string> _unsupportedKeywords = new(StringComparer.OrdinalIgnoreCase) {
        "actor", "schedule", "parallel", "for", "function"
    };
}