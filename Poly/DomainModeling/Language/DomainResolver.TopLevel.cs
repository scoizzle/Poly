using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.Grammar;

using TokenKind = Poly.DomainModeling.Language.DslTokenKind;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Language;

// DomainResolver (TopLevel) — top-level declarations: visibility, enum/value/function/contract/bind and the top-level rejection.

public sealed partial class DomainResolver {
    /// <summary>
    /// The optional visibility slot. Absent means <see cref="Visibility.Internal"/>.
    /// The grammar rule is the plug point for a later level.
    /// </summary>
    private bool TryTakeVisibility(out Visibility visibility) {
        var match = MatchRule("visibility");
        if (match is null) {
            visibility = Visibility.Internal;
            return false;
        }
        visibility = match.PatternName switch {
            "public" => Visibility.Public,
            _ => throw Error($"Unknown visibility '{match.PatternName}'."),
        };
        Consume(match);
        return true;
    }

    /// <summary><c>public entity Name</c> or <c>public value Name</c>. The trailing mark is not accepted.</summary>
    private void ParseMarkedTop(List<DomainChange> changes, Visibility visibility) {
        if (Current.Kind == TokenKind.Entity) {
            Advance();
            var name = ExpectIdentifier(TokenKind.Identifier, "entity name");
            ParseEntityAfterKind(changes, name, visibility);
            return;
        }
        if (DslGrammar.IsValueWord(Current)) {
            Advance();
            var name = ExpectIdentifier(TokenKind.Identifier, "value type name");
            FinishValueType(changes, name, visibility);
            return;
        }
        throw Error($"Expected 'entity' or 'value' after '{visibility.ToString().ToLowerInvariant()}', got '{Shown(Current)}'");
    }

    /// <summary>
    /// Fail-closed rejection for top-level shapes the grammar table does not
    /// model: unsupported keywords (e.g. 'actor') and the removed inheritance
    /// form ('Parent: Base entity'). Mirrors the diagnostics ParseEntity
    /// previously produced at the same position.
    /// </summary>
    private Exception TopLevelRejection() {
        if (PeekIs(TokenKind.Colon)) {
            var name = Shown(Current);
            var typeWord = Peek(2);
            if (typeWord.Kind == TokenKind.Identifier && _unsupportedKeywords.Contains(typeWord.Name())) {
                return new FormatException(
                    $"'{Shown(typeWord)}' is not supported in Phase 1a (use 'entity' instead)");
            }
            if (typeWord.Kind == TokenKind.Identifier
                && Peek(3).Kind == TokenKind.Entity) {
                return new FormatException(
                    $"Entity inheritance ('{name}: {Shown(typeWord)} entity') is no longer supported. " +
                    $"Define '{Shown(typeWord)}' properties directly on '{name}'.");
            }
        }
        return Error($"Expected 'entity', 'enum', 'value', 'contract', 'function', or 'bind' definition, got '{Shown(Current)}'");
    }

    /// <summary>
    /// Top-level <c>Name: value|contract|bind</c> — words stay identifiers so
    /// <c>action (value: Text)</c> does not collide with a reserved keyword.
    /// </summary>
    private bool TryParseNamedTopLevel(List<DomainChange> changes) {
        if (Current.Kind != TokenKind.Identifier || !PeekIs(TokenKind.Colon))
            return false;
        var kindTok = Peek(2);
        if (kindTok.Kind != TokenKind.Identifier)
            return false;
        switch (kindTok.Name()) {
            case "value":
                ParseValueType(changes);
                return true;
            case "contract":
                ParseContract(changes);
                return true;
            case "bind":
                ParseBind(changes);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Parses a top-level enum type declaration: <c>Name: enum { Member1, Member2 }</c>.
    /// Enum types must be declared before entities that reference them.
    /// </summary>
    private void ParseEnumType(List<DomainChange> changes) {
        var name = ExpectIdentifier(TokenKind.Identifier, "enum type name");
        Expect(TokenKind.Colon);
        Expect(TokenKind.Enum);

        var typeFacets = new List<Facet>();
        var typeTermKeywords = new HashSet<string>(StringComparer.Ordinal);
        while (TryParseRegisteredAnnotation(out var headerFacet)) {
            ValidateTermFacet(headerFacet, typeTermKeywords, $"enum '{name}'", allowTerm: false);
            typeFacets.Add(headerFacet);
        }

        Expect(TokenKind.LBrace);

        var members = new List<string>();
        var memberFacets = new Dictionary<string, IReadOnlyList<Facet>>(StringComparer.Ordinal);
        while (Current.Kind == TokenKind.Identifier) {
            var member = Current.Name();
            members.Add(member);
            Advance();

            // Per-member term metadata: `Red iri("...") term("red")`.
            var facets = new List<Facet>();
            var memberTermKeywords = new HashSet<string>(StringComparer.Ordinal);
            while (Current.Kind == TokenKind.Identifier && PeekIs(TokenKind.LParen)) {
                var keyword = Current.Name();
                if (!TryParseRegisteredAnnotation(out var facet)) {
                    throw Error(
                        $"Unknown or unregistered annotation '{keyword}' on enum member '{member}'. " +
                        "Enum members accept only iri, term, label, or description.");
                }
                if (facet is not Annotation annotation || !TermFacets.IsTermKeyword(annotation.Name)) {
                    throw Error(
                        $"Only term annotations (iri, term, label, description) are allowed " +
                        $"on enum member '{member}'.");
                }
                ValidateTermFacet(facet, memberTermKeywords, $"enum member '{member}'", allowTerm: true);
                facets.Add(facet);
            }
            if (facets.Count > 0)
                memberFacets[member] = facets;

            if (Current.Kind == TokenKind.Comma)
                Advance();
        }

        // Enum members must be identifiers — a member named after a DSL keyword
        // (e.g. `Number`, `Text`, `Create`, `In`, `Stage`, `Entry`) lexes as that
        // keyword token and would otherwise fail cryptically on the RBrace expect
        // below. A letter-led token here that is not a valid member terminator is
        // necessarily a reserved word (symbols like `,`/`:` fall through to the
        // normal RBrace error; EndOfFile has no text).
        if (Current.Kind is not (TokenKind.RBrace or TokenKind.Comma or TokenKind.EndOfFile)
            && DslTokenText.Word(Current) is { Length: > 0 } word
            && char.IsLetter(word[0])) {
            throw Error(
                $"Enum member '{word}' is a reserved keyword and cannot be used as an enum member. " +
                "Rename it (e.g. 'Number' → 'Numeric').");
        }

        Expect(TokenKind.RBrace);

        _enumTypeNames.Add(name);
        changes.Add(new AddEnumTypeChange(
            name,
            members,
            memberFacets.Count > 0 ? memberFacets : null,
            typeFacets.Count > 0 ? typeFacets : null));
    }

    /// <summary>
    /// <c>Name: value { Prop: Type … }</c>. Value types must be declared before
    /// entities that reference them.
    /// </summary>
    private void ParseValueType(List<DomainChange> changes) {
        var name = ExpectIdentifier(TokenKind.Identifier, "value type name");
        Expect(TokenKind.Colon);
        ExpectWord("value");
        FinishValueType(changes, name, Visibility.Internal);
    }

    /// <summary>
    /// A top-level <c>function</c> declaration starts with the word and is not the
    /// legacy <c>function: entity</c> name form. A malformed <c>function (…)</c>
    /// head still enters the parser so the error names the missing function name.
    /// </summary>
    private bool IsFunctionDeclarationStart() =>
        Current.Kind == TokenKind.Identifier
        && Current.TextEquals("function")
        && !PeekIs(TokenKind.Colon);

    /// <summary>
    /// <c>function Name(p: T, …): T = expr</c> — one expression body. Parameter names
    /// bind inside the body as <see cref="ParameterAccess"/>, so parse → print →
    /// parse stays lossless beyond the text.
    /// </summary>
    private void ParseFunction(List<DomainChange> changes) {
        Advance(); // 'function'
        if (Current.Kind != TokenKind.Identifier)
            throw Error($"Expected function name after 'function', got '{Shown(Current)}'");
        var name = Current.Name();
        Advance();

        Expect(TokenKind.LParen);
        var parameters = new List<Property>();
        while (Current.Kind != TokenKind.RParen) {
            var parameterName = ExpectIdentifier(TokenKind.Identifier, "parameter name");
            Expect(TokenKind.Colon);
            var parameterType = ParseTypeName();
            parameters.Add(new Property(parameterName, new DomainTypeReference(parameterType), []));
            if (Current.Kind == TokenKind.Comma) {
                Advance();
                if (Current.Kind == TokenKind.RParen)
                    throw Error($"Trailing comma in function '{name}' parameter list");
            }
            else if (Current.Kind != TokenKind.RParen) {
                throw Error($"Expected ',' or ')' in function '{name}' parameter list, got '{Shown(Current)}'");
            }
        }
        Expect(TokenKind.RParen);
        Expect(TokenKind.Colon);
        var returnType = new DomainTypeReference(ParseTypeName());
        if (Current.Kind != TokenKind.SingleEq)
            throw Error($"Expected '=' after function '{name}' return type, got '{Shown(Current)}'");
        Advance();

        var parameterNames = parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var body = new ParameterBinder(parameterNames).Route(ParseExpression());
        changes.Add(new AddFunctionChange(name, parameters, returnType, body));
    }

    /// <summary>Rewrites body identifiers that name a declared parameter to parameter access.</summary>
    private sealed class ParameterBinder(ISet<string> parameterNames) : DomainExpressionRewriteBase {
        protected override DomainExpression PropertyAccess(PropertyAccess e) =>
            parameterNames.Contains(e.Name) ? DomainExpression.Parameter(e.Name) : e;
    }

    private void FinishValueType(List<DomainChange> changes, string name, Visibility visibility) {
        var properties = ParseValueTypeProperties();
        _valueTypeNames.Add(name);
        changes.Add(new AddValueTypeChange(name, properties, visibility));
    }

    private IReadOnlyList<Property> ParseValueTypeProperties() {
        Expect(TokenKind.LBrace);
        var properties = new List<Property>();
        while (Current.Kind != TokenKind.RBrace && Current.Kind != TokenKind.EndOfFile) {
            var propName = ExpectPropertyName();
            Expect(TokenKind.Colon);
            string typeName;
            if (IsPrimitiveType(Current.Kind)) {
                typeName = Current.Name();
                Advance();
            }
            else {
                typeName = ExpectIdentifier(TokenKind.Identifier, "type name");
            }
            var constraints = new List<Constraint>();
            while (IsConstraint(Current.Kind)) {
                if (Current.Kind == TokenKind.Optional) {
                    throw Error(
                        "'optional' applies to entity properties; " +
                        "value-type members are contract payload fields and are always required.");
                }
                var c = ParseConstraint(typeName);
                if (c is not null)
                    constraints.Add(c);
            }
            RejectRetiredRequiredWord();
            properties.Add(new Property(propName, new DomainTypeReference(typeName), constraints));
        }
        Expect(TokenKind.RBrace);
        return properties;
    }

    /// <summary>
    /// <c>Name: contract external|internal source version { Endpoint: inbound|outbound operation|event Type }</c>
    /// </summary>
    private void ParseContract(List<DomainChange> changes) {
        var nameToken = Current;
        var name = ExpectIdentifier(TokenKind.Identifier, "contract name");
        if (_seededContracts.TryGetValue(name, out var seededBy))
            throw GrammarError.Error(
                $"Contract '{name}' is contributed by `uses {seededBy}`. A hand-written contract block beside the seeded surface is a duplicate.",
                nameToken.Line, nameToken.Col);
        if (!_authoredContracts.Add(name))
            throw GrammarError.Error(
                $"Contract '{name}' is already declared. One contract block per name.",
                nameToken.Line, nameToken.Col);
        Expect(TokenKind.Colon);
        ExpectWord("contract");

        ContractSourceKind sourceKind;
        if (Current.Kind == TokenKind.Identifier && Current.TextEquals("external")) {
            Advance();
            sourceKind = ContractSourceKind.ExternalProvider;
        }
        else if (Current.Kind == TokenKind.Identifier && Current.TextEquals("internal")) {
            Advance();
            sourceKind = ContractSourceKind.InternalDomain;
        }
        else {
            throw Error($"Expected 'external' or 'internal' after contract '{name}'");
        }

        string sourceId;
        if (Current.Kind == TokenKind.StringLiteral) {
            sourceId = Current.Unescape();
            Advance();
        }
        else {
            sourceId = ExpectIdentifier(TokenKind.Identifier, "contract source");
        }

        string version;
        if (Current.Kind == TokenKind.StringLiteral) {
            version = Current.Unescape();
            Advance();
        }
        else {
            version = ExpectIdentifier(TokenKind.Identifier, "contract version");
        }

        changes.Add(new AddImportedContractChange(name, sourceKind, sourceId, version));
        Expect(TokenKind.LBrace);

        while (Current.Kind != TokenKind.RBrace && Current.Kind != TokenKind.EndOfFile) {
            if (Current.Kind == TokenKind.Identifier
                && PeekIs(TokenKind.Colon)
                && Peek(2).Kind == TokenKind.Identifier
                && Peek(2).TextEquals("value")) {
                var vtName = ExpectIdentifier(TokenKind.Identifier, "value type name");
                Expect(TokenKind.Colon);
                ExpectWord("value");
                var properties = ParseValueTypeProperties();
                _valueTypeNames.Add(vtName);
                changes.Add(new AddContractValueTypeChange(name, new ValueType(vtName, properties, [])));
                continue;
            }

            var endpointName = ExpectIdentifier(TokenKind.Identifier, "endpoint name");
            Expect(TokenKind.Colon);

            ContractEndpointDirection direction;
            if (Current.Kind == TokenKind.Identifier && Current.TextEquals("inbound")) {
                Advance();
                direction = ContractEndpointDirection.Inbound;
            }
            else if (Current.Kind == TokenKind.Identifier && Current.TextEquals("outbound")) {
                Advance();
                direction = ContractEndpointDirection.Outbound;
            }
            else {
                throw Error($"Expected 'inbound' or 'outbound' on endpoint '{endpointName}'");
            }

            ContractEndpointKind kind;
            if (Current.Kind == TokenKind.Identifier && Current.TextEquals("operation")) {
                Advance();
                kind = ContractEndpointKind.Operation;
            }
            else if (Current.Kind == TokenKind.Identifier && Current.TextEquals("event")) {
                Advance();
                kind = ContractEndpointKind.Event;
            }
            else {
                throw Error($"Expected 'operation' or 'event' on endpoint '{endpointName}'");
            }

            string payloadType;
            if (IsPrimitiveType(Current.Kind)) {
                payloadType = Current.Name();
                Advance();
            }
            else {
                payloadType = ExpectIdentifier(TokenKind.Identifier, "payload type");
            }

            changes.Add(new AddContractEndpointChange(name,
                new ContractEndpoint(endpointName, kind, direction, new DomainTypeReference(payloadType))));
        }

        Expect(TokenKind.RBrace);
    }

    /// <summary>
    /// <c>Name: bind Contract Endpoint to Action param</c>
    /// </summary>
    private void ParseBind(List<DomainChange> changes) {
        var name = ExpectIdentifier(TokenKind.Identifier, "binding name");
        Expect(TokenKind.Colon);
        ExpectWord("bind");
        var contractName = ExpectIdentifier(TokenKind.Identifier, "contract name");
        var endpointName = ExpectIdentifier(TokenKind.Identifier, "endpoint name");
        Expect(TokenKind.To);
        var actionName = ExpectIdentifier(TokenKind.Identifier, "action name");
        var paramName = ExpectIdentifier(TokenKind.Identifier, "action parameter");
        changes.Add(new AddContractBindingChange(name, contractName, endpointName, actionName, paramName));
    }

    private void ExpectWord(string word) {
        if (Current.Kind != TokenKind.Identifier
            || !Current.TextEquals(word))
            throw Error($"Expected '{word}', got '{Shown(Current)}'");
        Advance();
    }
}