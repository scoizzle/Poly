using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Queries;
using Poly.Tests.TestHelpers;

using Entity = Poly.DomainModeling.Ontology.Entity;
using EnumType = Poly.DomainModeling.Ontology.EnumType;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Queries;

/// <summary>
/// M-03 (#689): the one element-path grammar. <see cref="ElementPath"/> parses, <see cref="ElementPathResolver"/>
/// resolves against a <see cref="Domain"/>; every outcome is a typed ref, not-found or ambiguity.
/// </summary>
internal sealed class ElementPathTests {
    private const string ClinicDsl = """
        domain Clinic

        Kind: enum { A, B }

        Money: value {
          Amount: Decimal
          Currency: Text
        }

        Customer: entity {
          Name: Text
        }

        Appt: entity {
          SlotNo: Number
          Note: Text optional
          customer: Customer
          Adult: policy { SlotNo > 0 }
          Book: action require Adult { assign Note to "booked" }
          Open: stage require Adult {
            Go: action { transition to Done }
          }
          Review: stage {
            Go: action { transition to Done }
            Check: action { transition to Done }
          }
          Done: stage { }
        }

        function Add(a: Number, b: Number): Number = a + b
        """;

    private static (Domain Domain, AnalysisResult Analysis) Clinic() => EvolvedDomain.FromDsl(ClinicDsl);

    private static ElementResolution Resolve(Domain domain, string select) =>
        ElementPathResolver.Resolve(domain, ElementPath.Parse(select));

    private static Poly.DomainModeling.Ontology.Action Act(string name, params Policy[] policies) =>
        new(name, InvocationResult.Void, [], [], policies);

    private static Policy Pol(string name) =>
        new(name, DomainExpression.GreaterThanOrEqual(DomainExpression.Property("Age"), DomainExpression.Literal(18)));

    // ── Parse ──────────────────────────────────────────────────

    [Test]
    public async Task Parse_DottedPath_SplitsIntoSegments() {
        var path = ElementPath.Parse("Appt.Open.Go");

        await Assert.That(path.Segments).IsEquivalentTo(new[] { "Appt", "Open", "Go" });
        await Assert.That(path.Canonical).IsEqualTo("Appt.Open.Go");
    }

    [Test]
    public async Task Parse_SlashPath_EqualsTheDottedForm() {
        await Assert.That(ElementPath.Parse("Clinic/Appt/Open/Go").Segments)
            .IsEquivalentTo(ElementPath.Parse("Clinic.Appt.Open.Go").Segments);
    }

    [Test]
    public async Task Parse_WhitespaceAroundSegments_IsTrimmed() {
        await Assert.That(ElementPath.Parse(" Appt . SlotNo ").Segments).IsEquivalentTo(new[] { "Appt", "SlotNo" });
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("A..B")]
    [Arguments("A.")]
    [Arguments(".A")]
    [Arguments("A/ /B")]
    public async Task TryParse_EmptySegment_ReturnsAPositionedError(string text) {
        var ok = ElementPath.TryParse(text, out var path, out var error);

        await Assert.That(ok).IsFalse();
        await Assert.That(path).IsNull();
        await Assert.That(error).IsNotNull();
        await Assert.That(error!).Contains("position");
    }

    [Test]
    public async Task TryParse_Null_ReturnsAnError() {
        var ok = ElementPath.TryParse(null, out _, out var error);

        await Assert.That(ok).IsFalse();
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task Parse_Malformed_ThrowsFormatException() {
        await Assert.That(() => ElementPath.Parse("A..B")).Throws<FormatException>();
    }

    [Test]
    public async Task Parse_AcceptsEveryPathDslOutlineProducesForTheSameDocument() {
        var outline = DslOutline.Build(ClinicDsl);
        var paths = outline.All().Select(n => n.Path).Where(p => p.Length > 0).Distinct().ToList();

        await Assert.That(paths).Contains("Appt.Open.Go");
        foreach (var outlinePath in paths) {
            var ok = ElementPath.TryParse(outlinePath, out var parsed, out var error);
            await Assert.That(ok).IsTrue().Because($"'{outlinePath}': {error}");
            await Assert.That(parsed!.Canonical).IsEqualTo(outlinePath);
        }
    }

    // ── Resolve: every kind ────────────────────────────────────

    [Test]
    [Arguments("Clinic", ElementKind.Domain)]
    [Arguments("Appt", ElementKind.Entity)]
    [Arguments("Kind", ElementKind.Enum)]
    [Arguments("Money", ElementKind.ValueType)]
    [Arguments("Add", ElementKind.Function)]
    [Arguments("Appt.SlotNo", ElementKind.Property)]
    [Arguments("Appt.customer", ElementKind.Navigation)]
    [Arguments("Appt.Book", ElementKind.EntityAction)]
    [Arguments("Appt.Open", ElementKind.Stage)]
    [Arguments("Appt.Adult", ElementKind.Policy)]
    [Arguments("Appt.Open.Go", ElementKind.StageAction)]
    [Arguments("Appt.Review.Check", ElementKind.StageAction)]
    [Arguments("Appt.Check", ElementKind.StageAction)]
    [Arguments("Kind.A", ElementKind.EnumMember)]
    [Arguments("Clinic/Appt/Open/Go", ElementKind.StageAction)]
    [Arguments("Clinic.Appt.SlotNo", ElementKind.Property)]
    public async Task Resolve_EachKind_ReturnsARefOfThatKind(string select, ElementKind kind) {
        var (domain, _) = Clinic();

        var resolution = Resolve(domain, select);

        await Assert.That(resolution).IsTypeOf<ElementRef>().Because(Describe(resolution));
        await Assert.That(((ElementRef)resolution).Kind).IsEqualTo(kind);
    }

    [Test]
    public async Task Resolve_StageAction_NamesEntityStageAndCanonicalPath() {
        var (domain, _) = Clinic();

        var viaStage = (ElementRef)Resolve(domain, "Appt.Review.Check");
        var bare = (ElementRef)Resolve(domain, "Appt.Check");

        await Assert.That(viaStage.Entity).IsEqualTo("Appt");
        await Assert.That(viaStage.Stage).IsEqualTo("Review");
        await Assert.That(viaStage.Name).IsEqualTo("Check");
        await Assert.That(viaStage.Canonical).IsEqualTo("Appt.Review.Check");
        await Assert.That(viaStage.AnalysisPath).IsEqualTo("Clinic/Appt/Review/Check");
        await Assert.That(bare.Canonical).IsEqualTo("Appt.Review.Check");
        await Assert.That(ReferenceEquals(bare.Node, viaStage.Node)).IsTrue();
    }

    [Test]
    public async Task Resolve_EveryKindWithANode_AnalysisPathEqualsDomainElementPath() {
        var (domain, _) = Clinic();
        var selects = new[] {
            "Appt", "Kind", "Money", "Add", "Appt.SlotNo", "Appt.customer", "Appt.Book",
            "Appt.Open", "Appt.Adult", "Appt.Open.Go", "Appt.Review.Check",
        };

        foreach (var select in selects) {
            var resolved = (ElementRef)Resolve(domain, select);
            await Assert.That(resolved.Node).IsNotNull().Because(select);
            await Assert.That(resolved.AnalysisPath).IsEqualTo(DomainElementPath.Resolve(domain, resolved.Node!)).Because(select);
        }

        var domainRef = (ElementRef)Resolve(domain, "Clinic");
        await Assert.That(domainRef.AnalysisPath).IsEqualTo("Clinic");
        var member = (ElementRef)Resolve(domain, "Kind.A");
        await Assert.That(member.Node).IsNull();
        await Assert.That(member.AnalysisPath).IsEqualTo("Clinic/Kind/A");
    }

    [Test]
    public async Task Resolve_RoundTrip_AnalysisPathAndCanonicalResolveToTheSameElement() {
        var (domain, _) = Clinic();
        var selects = new[] {
            "Appt", "Kind", "Money", "Add", "Appt.SlotNo", "Appt.customer", "Appt.Book",
            "Appt.Open", "Appt.Adult", "Appt.Open.Go", "Appt.Review.Check", "Kind.A",
        };

        foreach (var select in selects) {
            var original = (ElementRef)Resolve(domain, select);
            var viaAnalysisPath = Resolve(domain, original.AnalysisPath);
            var viaCanonical = Resolve(domain, original.Canonical);

            await Assert.That(viaAnalysisPath).IsTypeOf<ElementRef>().Because($"{select} -> {original.AnalysisPath}: {Describe(viaAnalysisPath)}");
            await Assert.That(((ElementRef)viaAnalysisPath).AnalysisPath).IsEqualTo(original.AnalysisPath);
            await Assert.That(viaCanonical).IsTypeOf<ElementRef>().Because($"{select} -> {original.Canonical}: {Describe(viaCanonical)}");
            await Assert.That(((ElementRef)viaCanonical).AnalysisPath).IsEqualTo(original.AnalysisPath);
        }
    }

    // ── Resolve: domain prefix ─────────────────────────────────

    [Test]
    public async Task Resolve_EntityNamedLikeTheDomain_StillResolvesItsMember() {
        var shop = new Entity("Shop", [new Property("Price", new DomainTypeReference("Number"), [])], [], [], []);
        var domain = DomainTestFactory.Create("Shop", [shop], []);

        var resolved = Resolve(domain, "Shop.Price");

        await Assert.That(resolved).IsTypeOf<ElementRef>().Because(Describe(resolved));
        await Assert.That(((ElementRef)resolved).Kind).IsEqualTo(ElementKind.Property);
        await Assert.That(((ElementRef)resolved).Canonical).IsEqualTo("Shop.Price");
    }

    [Test]
    public async Task Resolve_DomainPrefixReadingAndAsWrittenReadingBothResolve_IsAmbiguous() {
        var shop = new Entity("Shop", [new Property("Price", new DomainTypeReference("Number"), [])], [], [], []);
        var price = new Entity("Price", [], [], [], []);
        var domain = DomainTestFactory.Create("Shop", [shop, price], []);

        var resolved = Resolve(domain, "Shop.Price");

        await Assert.That(resolved).IsTypeOf<ElementAmbiguous>().Because(Describe(resolved));
        var matches = ((ElementAmbiguous)resolved).Matches.Select(m => m.Canonical).ToList();
        await Assert.That(matches).IsEquivalentTo(new[] { "Shop.Price", "Price" });
    }

    [Test]
    public async Task Resolve_SingleSegmentEqualToTheDomainName_IsTheDomainUnlessATypeHasThatName() {
        var asDomain = DomainTestFactory.Create("Shop", [new Entity("Item", [], [], [], [])], []);
        var asEntity = DomainTestFactory.Create("Shop", [new Entity("Shop", [], [], [], [])], []);

        await Assert.That(((ElementRef)Resolve(asDomain, "Shop")).Kind).IsEqualTo(ElementKind.Domain);
        await Assert.That(((ElementRef)Resolve(asEntity, "Shop")).Kind).IsEqualTo(ElementKind.Entity);
    }

    // ── Resolve: precedence, collisions, policies ──────────────

    [Test]
    public async Task Resolve_EntityActionAndSameNamedStageAction_BareIsAmbiguous_EachMatchSelectsItself() {
        var entityAction = Act("Submit");
        var stageAction = Act("Submit");
        var draft = new Stage("Draft", [stageAction], [], [], []);
        var order = new Entity("Order", [], [entityAction], [], [draft]);
        var domain = DomainTestFactory.Create("Shop", [order], []);

        var bare = Resolve(domain, "Order.Submit");

        await Assert.That(bare).IsTypeOf<ElementAmbiguous>().Because(Describe(bare));
        var matches = ((ElementAmbiguous)bare).Matches;
        await Assert.That(matches.Select(m => m.Kind).ToList())
            .IsEquivalentTo(new[] { ElementKind.EntityAction, ElementKind.StageAction });
        foreach (var match in matches) {
            var viaAnalysisPath = Resolve(domain, match.AnalysisPath);
            await Assert.That(viaAnalysisPath).IsTypeOf<ElementRef>().Because(match.AnalysisPath + ": " + Describe(viaAnalysisPath));
            await Assert.That(ReferenceEquals(((ElementRef)viaAnalysisPath).Node, match.Node)).IsTrue().Because(match.AnalysisPath);
        }

        var staged = (ElementRef)Resolve(domain, "Order.Draft.Submit");
        await Assert.That(staged.Kind).IsEqualTo(ElementKind.StageAction);
        await Assert.That(ReferenceEquals(staged.Node, stageAction)).IsTrue();
        var exact = (ElementRef)Resolve(domain, "Shop/Order/Submit");
        await Assert.That(exact.Kind).IsEqualTo(ElementKind.EntityAction);
        await Assert.That(ReferenceEquals(exact.Node, entityAction)).IsTrue();
    }

    [Test]
    public async Task Resolve_BareStageActionDeclaredByTwoStages_IsAmbiguousListingEachStage() {
        var (domain, _) = Clinic();

        var resolved = Resolve(domain, "Appt.Go");

        await Assert.That(resolved).IsTypeOf<ElementAmbiguous>().Because(Describe(resolved));
        await Assert.That(((ElementAmbiguous)resolved).Matches.Select(m => m.Canonical).ToList())
            .IsEquivalentTo(new[] { "Appt.Open.Go", "Appt.Review.Go" });
    }

    [Test]
    public async Task Resolve_StageSharingANameWithAProperty_IsAmbiguous() {
        var gadget = new Entity("Gadget",
            [new Property("Flag", new DomainTypeReference("Text"), [])], [], [],
            [new Stage("Flag", [], [], [], [])]);
        var domain = DomainTestFactory.Create("Shop", [gadget], []);

        var resolved = Resolve(domain, "Gadget.Flag");

        await Assert.That(resolved).IsTypeOf<ElementAmbiguous>().Because(Describe(resolved));
        await Assert.That(((ElementAmbiguous)resolved).Matches.Select(m => m.Kind).ToList())
            .IsEquivalentTo(new[] { ElementKind.Property, ElementKind.Stage });
    }

    [Test]
    public async Task Resolve_FunctionSharingANameWithAType_IsAmbiguous() {
        var gadget = new Entity("Gadget", [], [], [], []);
        var domain = DomainTestFactory.Create("Shop", [gadget], []) with {
            Functions = [new DomainFunction("Gadget", [], new DomainTypeReference("Number"), DomainExpression.Literal(1))],
        };

        var resolved = Resolve(domain, "Gadget");

        await Assert.That(resolved).IsTypeOf<ElementAmbiguous>().Because(Describe(resolved));
    }

    [Test]
    public async Task Resolve_PolicyDefinedOnTheEntityAndRequiredByAnAction_ResolvesToTheDefinition() {
        var defined = Pol("Adult");
        var used = defined with { };
        // A `require Adult` use shares the definition's Expression reference (NameScopes.PolicyDefinitions).
        var book = Act("Book", used);
        var gadget = new Entity("Gadget", [], [book], [defined], []);
        var domain = DomainTestFactory.Create("Shop", [gadget], []);

        var resolved = Resolve(domain, "Gadget.Adult");

        await Assert.That(resolved).IsTypeOf<ElementRef>().Because(Describe(resolved));
        await Assert.That(ReferenceEquals(((ElementRef)resolved).Node, defined)).IsTrue();
        await Assert.That(((ElementRef)resolved).Canonical).IsEqualTo("Gadget.Adult");
    }

    [Test]
    public async Task Resolve_NegatedPolicyUse_IsNotAnElement() {
        var defined = Pol("Adult");
        var negated = Pol("not_Adult");
        var gadget = new Entity("Gadget", [], [Act("Book", negated)], [defined], []);
        var domain = DomainTestFactory.Create("Shop", [gadget], []);

        var resolved = Resolve(domain, "Gadget.not_Adult");

        await Assert.That(resolved).IsTypeOf<ElementNotFound>().Because(Describe(resolved));
    }

    [Test]
    public async Task Resolve_PolicyDefinedOnlyOnAStage_ResolvesBareAndWithTheStageSegment() {
        var onlyHere = Pol("OnlyHere");
        var stage = new Stage("Draft", [], [onlyHere], [], []);
        var gadget = new Entity("Gadget", [], [], [], [stage]);
        var domain = DomainTestFactory.Create("Shop", [gadget], []);

        var bare = (ElementRef)Resolve(domain, "Gadget.OnlyHere");
        var staged = (ElementRef)Resolve(domain, "Gadget.Draft.OnlyHere");

        await Assert.That(bare.Kind).IsEqualTo(ElementKind.Policy);
        await Assert.That(ReferenceEquals(bare.Node, onlyHere)).IsTrue();
        await Assert.That(ReferenceEquals(staged.Node, onlyHere)).IsTrue();
        await Assert.That(staged.Canonical).IsEqualTo("Gadget.Draft.OnlyHere");
    }

    [Test]
    public async Task Resolve_PolicyDefinedOnlyOnAnAction_ResolvesBare() {
        var onlyHere = Pol("OnlyHere");
        var gadget = new Entity("Gadget", [], [Act("Book", onlyHere)], [], []);
        var domain = DomainTestFactory.Create("Shop", [gadget], []);

        var bare = (ElementRef)Resolve(domain, "Gadget.OnlyHere");

        await Assert.That(bare.Kind).IsEqualTo(ElementKind.Policy);
        await Assert.That(ReferenceEquals(bare.Node, onlyHere)).IsTrue();
    }

    [Test]
    public async Task Resolve_SameNameStageActionAndEntityMember_BareNameIsTheEntityMember() {
        var gadget = new Entity("Gadget",
            [new Property("Go", new DomainTypeReference("Text"), [])], [], [],
            [new Stage("Draft", [Act("Go")], [], [], [])]);
        var domain = DomainTestFactory.Create("Shop", [gadget], []);

        var bare = (ElementRef)Resolve(domain, "Gadget.Go");
        var staged = (ElementRef)Resolve(domain, "Gadget.Draft.Go");

        await Assert.That(bare.Kind).IsEqualTo(ElementKind.Property);
        await Assert.That(staged.Kind).IsEqualTo(ElementKind.StageAction);
    }

    [Test]
    public async Task Resolve_IsCaseSensitive() {
        var (domain, _) = Clinic();

        var resolved = Resolve(domain, "appt.slotno");

        await Assert.That(resolved).IsTypeOf<ElementNotFound>().Because(Describe(resolved));
        await Assert.That(((ElementNotFound)resolved).Reason).IsEqualTo(NotFoundReason.Unknown);
        await Assert.That(((ElementNotFound)resolved).Candidates).Contains("Appt");
    }

    // ── Resolve: not found, not addressable ────────────────────

    [Test]
    public async Task Resolve_UnknownMember_IsNotFoundWithSameEntityCandidatesAndAPartial() {
        var (domain, _) = Clinic();

        var resolved = Resolve(domain, "Appt.SlotNp");

        await Assert.That(resolved).IsTypeOf<ElementNotFound>().Because(Describe(resolved));
        var notFound = (ElementNotFound)resolved;
        await Assert.That(notFound.Reason).IsEqualTo(NotFoundReason.Unknown);
        await Assert.That(notFound.Candidates).Contains("Appt.SlotNo");
        await Assert.That(notFound.Partial!.Kind).IsEqualTo(ElementKind.Entity);
        await Assert.That(notFound.Message).Contains("Appt").And.Contains("SlotNp");
    }

    [Test]
    public async Task Resolve_UnknownTopLevelName_ListsTheNearTypes() {
        var (domain, _) = Clinic();

        var resolved = Resolve(domain, "Aptt");

        var notFound = (ElementNotFound)resolved;
        await Assert.That(notFound.Reason).IsEqualTo(NotFoundReason.Unknown);
        await Assert.That(notFound.Candidates).Contains("Appt");
        await Assert.That(notFound.Partial).IsNull();
    }

    [Test]
    public async Task Resolve_MemberNameThatExistsUnderAStage_IsOfferedAsACandidate() {
        var (domain, _) = Clinic();

        var resolved = Resolve(domain, "Appt.Chek");

        await Assert.That(((ElementNotFound)resolved).Candidates).Contains("Appt.Review.Check");
    }

    [Test]
    public async Task Resolve_Candidates_AreCappedAtFiveAndDeterministic() {
        var props = Enumerable.Range(0, 12)
            .Select(i => new Property($"Field{(char)('a' + i)}", new DomainTypeReference("Text"), []))
            .ToList();
        var domain = DomainTestFactory.Create("Shop", [new Entity("Wide", props, [], [], [])], []);

        var first = (ElementNotFound)Resolve(domain, "Wide.Fielx");
        var second = (ElementNotFound)Resolve(domain, "Wide.Fielx");

        await Assert.That(first.Candidates.Count).IsEqualTo(5);
        await Assert.That(first.Candidates).IsEquivalentTo(second.Candidates);
    }

    [Test]
    public async Task Resolve_VeryLongSegment_IsNotScoredAndDoesNotThrow() {
        var (domain, _) = Clinic();

        var resolved = Resolve(domain, "Appt." + new string('x', 200));

        await Assert.That(resolved).IsTypeOf<ElementNotFound>();
        await Assert.That(((ElementNotFound)resolved).Candidates).IsEmpty();
    }

    [Test]
    [Arguments("Money.Amount")]
    [Arguments("Appt.customer.Name")]
    [Arguments("Add.a")]
    [Arguments("Kind.A.x")]
    [Arguments("Appt.Open.Go.x")]
    [Arguments("Appt.Book.x")]
    public async Task Resolve_UnaddressableShapes_AreNotAddressableWithAPartial(string select) {
        var (domain, _) = Clinic();

        var resolved = Resolve(domain, select);

        await Assert.That(resolved).IsTypeOf<ElementNotFound>().Because(Describe(resolved));
        var notFound = (ElementNotFound)resolved;
        await Assert.That(notFound.Reason).IsEqualTo(NotFoundReason.NotAddressable);
        await Assert.That(notFound.Partial).IsNotNull();
    }

    [Test]
    public async Task Resolve_AnAbsentEntityAndASubpath_IsUnknownNotNotAddressable() {
        var (domain, _) = Clinic();

        var resolved = Resolve(domain, "Nope.Open.Go");

        await Assert.That(((ElementNotFound)resolved).Reason).IsEqualTo(NotFoundReason.Unknown);
    }

    [Test]
    [Arguments("Clinic.Aptt")]
    [Arguments("Clinic/appt")]
    public async Task Resolve_DomainPrefixedUnknownEntity_IsUnknownWithCandidates(string select) {
        var (domain, _) = Clinic();

        var resolved = Resolve(domain, select);

        await Assert.That(resolved).IsTypeOf<ElementNotFound>().Because(Describe(resolved));
        var notFound = (ElementNotFound)resolved;
        await Assert.That(notFound.Reason).IsEqualTo(NotFoundReason.Unknown);
        await Assert.That(notFound.Candidates).Contains("Appt");
    }

    [Test]
    public async Task Resolve_UnknownNameNearAPrimitive_DoesNotSuggestThePrimitive() {
        var (domain, _) = Clinic();

        var resolved = (ElementNotFound)Resolve(domain, "Nmber");

        await Assert.That(resolved.Candidates).DoesNotContain("Number");
    }

    [Test]
    public async Task Resolve_APrimitiveName_IsNotAddressable() {
        var (domain, _) = Clinic();

        var resolved = Resolve(domain, "Number");

        await Assert.That(resolved).IsTypeOf<ElementNotFound>().Because(Describe(resolved));
        await Assert.That(((ElementNotFound)resolved).Reason).IsEqualTo(NotFoundReason.NotAddressable);
    }

    [Test]
    public async Task Resolve_ActionScopedPolicy_AnalysisPathResolvesBackToTheSameNode() {
        var onlyHere = Pol("OnlyHere");
        var gadget = new Entity("Gadget", [], [Act("Book", onlyHere)], [], []);
        var domain = DomainTestFactory.Create("Shop", [gadget], []);

        var bare = (ElementRef)Resolve(domain, "Gadget.OnlyHere");
        var back = Resolve(domain, bare.AnalysisPath);

        await Assert.That(back).IsTypeOf<ElementRef>().Because(bare.AnalysisPath + ": " + Describe(back));
        await Assert.That(ReferenceEquals(((ElementRef)back).Node, onlyHere)).IsTrue();
    }

    // ── Twin grammar parity (A7) ───────────────────────────────

    [Test]
    [Arguments("Appt")]
    [Arguments("Appt.SlotNo")]
    [Arguments("Appt.Book")]
    [Arguments("Appt.Adult")]
    [Arguments("Appt.Open")]
    [Arguments("Appt.Check")]
    [Arguments("Clinic.Appt.SlotNo")]
    [Arguments("Clinic/Appt/Book")]
    [Arguments("Appt.Nope")]
    [Arguments("Nope")]
    [Arguments("Nope.SlotNo")]
    public async Task Resolve_AgreesWithTheVerificationSelectRuleOnTheSharedSubset(string select) {
        var (domain, analysis) = Clinic();

        var resolvedElement = Resolve(domain, select) is ElementRef;
        var verificationNamesIt = analysis.GetVerification(domain, select).Verdict != CheckVerdict.Unknown;

        await Assert.That(resolvedElement).IsEqualTo(verificationNamesIt);
    }

    [Test]
    [Arguments("Kind", true, false)]
    [Arguments("Money", true, false)]
    [Arguments("Add", true, false)]
    [Arguments("Appt.customer", true, false)]
    [Arguments("Kind.A", true, false)]
    [Arguments("Appt.Open.Nope", false, true)]
    public async Task Resolve_KnownDivergencesFromTheVerificationSelectRule(string select, bool resolverResolves, bool verificationNamesIt) {
        var (domain, analysis) = Clinic();

        await Assert.That(Resolve(domain, select) is ElementRef).IsEqualTo(resolverResolves);
        await Assert.That(analysis.GetVerification(domain, select).Verdict != CheckVerdict.Unknown).IsEqualTo(verificationNamesIt);
    }

    private static string Describe(ElementResolution resolution) => resolution switch {
        ElementRef r => $"ref {r.Kind} {r.Canonical}",
        ElementNotFound n => $"not found ({n.Reason}) {n.Message} candidates=[{string.Join(", ", n.Candidates)}]",
        ElementAmbiguous a => $"ambiguous [{string.Join(", ", a.Matches.Select(m => m.Canonical))}]",
        _ => "?",
    };
}