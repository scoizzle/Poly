using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Ontology.Contract;

using Poly.Mcp.Tools;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// #418 (epic #427, spike #413 §3): the surface fingerprint is the public signature's
/// identity. It covers endpoints, payload shapes, public entity names, enum members, public
/// property constraints, public action preconditions, and the reserved <c>accepts</c>
/// component. It never covers implementation AST, retries, or internal constraints.
/// </summary>
public sealed class ContractSurfaceFingerprintTests {
    private static ImportedContract Surface(
        IReadOnlyList<ContractEndpoint>? endpoints = null,
        IReadOnlyList<string>? entities = null,
        IReadOnlyList<ValueType>? types = null,
        IReadOnlyList<EnumType>? enums = null) =>
        new("Billing", ContractSourceKind.InternalDomain, "billing", "v1", endpoints ?? []) {
            Entities = entities ?? [],
            Types = types ?? [],
            Enums = enums ?? []
        };

    private static ContractEndpoint Endpoint(
        string name,
        string payload = "Money",
        string? sourceName = null,
        IReadOnlyList<Policy>? preconditions = null) =>
        new(name, ContractEndpointKind.Operation, ContractEndpointDirection.Outbound,
            new DomainTypeReference(payload)) {
            SourceName = sourceName ?? name,
            Preconditions = preconditions ?? []
        };

    private static ValueType Money(params Constraint[] constraints) =>
        new("Money", [new Property("Amount", new DomainTypeReference("Number"), constraints)], []);

    private static bool IsLowercaseHex(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    [Test]
    public async Task AddingEndpoint_ChangesFingerprint() {
        var one = Surface([Endpoint("Charge")]).Fingerprint;
        var two = Surface([Endpoint("Charge"), Endpoint("Refund")]).Fingerprint;

        await Assert.That(two).IsNotEqualTo(one);
    }

    [Test]
    public async Task DifferentPayload_ChangesFingerprint() {
        var a = Surface([Endpoint("Charge", "Money")]).Fingerprint;
        var b = Surface([Endpoint("Charge", "Key")]).Fingerprint;

        await Assert.That(b).IsNotEqualTo(a);
    }

    [Test]
    public async Task AddingValueTypeField_ChangesFingerprint() {
        var withAmount = new ValueType("Money",
            [new Property("Amount", new DomainTypeReference("Number"), [])], []);
        var withCurrency = new ValueType("Money",
            [
                new Property("Amount", new DomainTypeReference("Number"), []),
                new Property("Currency", new DomainTypeReference("Text"), []),
            ], []);

        var a = Surface([Endpoint("Charge")], types: [withAmount]).Fingerprint;
        var b = Surface([Endpoint("Charge")], types: [withCurrency]).Fingerprint;

        await Assert.That(b).IsNotEqualTo(a);
    }

    [Test]
    public async Task AddingPublicPropertyConstraint_ChangesFingerprint() {
        var plain = Surface([Endpoint("Charge")], types: [Money()]).Fingerprint;
        var bounded = Surface([Endpoint("Charge")], types: [Money(new RangeConstraint(0, 100))]).Fingerprint;

        await Assert.That(bounded).IsNotEqualTo(plain);
    }

    [Test]
    public async Task AddingPrecondition_ChangesFingerprint() {
        var plain = Surface([Endpoint("Charge")]).Fingerprint;
        var guarded = Surface([
            Endpoint("Charge", preconditions: [new Policy("Positive", DomainExpression.Property("Amount"))])
        ]).Fingerprint;

        await Assert.That(guarded).IsNotEqualTo(plain);
    }

    [Test]
    public async Task AddingEntityName_ChangesFingerprint() {
        var without = Surface([Endpoint("Charge")]).Fingerprint;
        var with = Surface([Endpoint("Charge")], entities: ["Order"]).Fingerprint;

        await Assert.That(with).IsNotEqualTo(without);
    }

    [Test]
    public async Task AddingEnumMember_ChangesFingerprint() {
        var two = Surface([Endpoint("Paint", "Color")],
            enums: [new EnumType("Color", ["Red", "Green"], [])]).Fingerprint;
        var three = Surface([Endpoint("Paint", "Color")],
            enums: [new EnumType("Color", ["Red", "Green", "Blue"], [])]).Fingerprint;

        await Assert.That(three).IsNotEqualTo(two);
    }

    /// <summary>
    /// The exported name carries the overload suffix; the digest is keyed by the original
    /// action name plus the payload, so renaming a suffix spelling does not move the pin.
    /// </summary>
    [Test]
    public async Task ChangingSuffixSpelling_DoesNotChangeFingerprint() {
        var plain = Surface([Endpoint("Charge", "Money", sourceName: "Charge")]).Fingerprint;
        var suffixed = Surface([Endpoint("Charge__Money", "Money", sourceName: "Charge")]).Fingerprint;

        await Assert.That(suffixed).IsEqualTo(plain);
    }

    [Test]
    public async Task ReorderingSurface_DoesNotChangeFingerprint() {
        var money = Money(new RangeConstraint(0, 100));
        var key = new ValueType("Key", [new Property("Value", new DomainTypeReference("Text"), [])], []);

        var forward = Surface(
            [Endpoint("Charge", "Money"), Endpoint("Refund", "Key")],
            entities: ["Audit", "Order"],
            types: [money, key]).Fingerprint;

        var reversed = Surface(
            [Endpoint("Refund", "Key"), Endpoint("Charge", "Money")],
            entities: ["Order", "Audit"],
            types: [key, money]).Fingerprint;

        await Assert.That(reversed).IsEqualTo(forward);
    }

    [Test]
    public async Task EmptySurface_IsStable() {
        var first = Surface().Fingerprint;
        var second = Surface().Fingerprint;

        await Assert.That(IsLowercaseHex(first)).IsTrue();
        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    public async Task Fingerprint_IsLowercaseHexSha256_AndNotTheN7CountString() {
        var domain = DomainFactory.Create("billing", b => b.AddEntity("Ledger"));
        var contract = new InternalDomainProducer().Produce(domain);

        await Assert.That(IsLowercaseHex(contract.Fingerprint)).IsTrue();
        await Assert.That(contract.Fingerprint).IsNotEqualTo(EvolveTool.GetFingerprint(domain));
    }

    /// <summary>
    /// A public surface describes what a caller may use, not what the body does: two
    /// domains with the same export and different effects fingerprint the same.
    /// </summary>
    [Test]
    public async Task RequireNot_IsAPublicPrecondition_AndChangesFingerprint() {
        const string Plain = """
            domain billing

            public value Money {
              Amount: Number
            }

            public entity Order {
              Total: Number
              Positive: policy { Total > 0 }
              public action Charge (request: Money) {
              }
            }
            """;

        const string Negated = """
            domain billing

            public value Money {
              Amount: Number
            }

            public entity Order {
              Total: Number
              Positive: policy { Total > 0 }
              public action Charge (request: Money)
                require not Positive
              {
              }
            }
            """;

        var plain = Produce(Plain);
        var negated = Produce(Negated);

        await Assert.That(negated.Endpoints.Single().Preconditions.Select(p => p.Name)).Contains("not_Positive");
        await Assert.That(negated.Fingerprint).IsNotEqualTo(plain.Fingerprint);
    }

    [Test]
    public async Task SameSurface_DifferentImplementation_SameFingerprint() {
        var empty = Produce("""
            domain billing

            public value Money {
              Amount: Number
            }

            public entity Order {
              Total: Number
              public action Charge (request: Money) {
              }
            }
            """);

        var withEffects = Produce("""
            domain billing

            public value Money {
              Amount: Number
            }

            public entity Order {
              Total: Number
              public action Charge (request: Money) {
                assign Total to Total + 1
              }
            }
            """);

        await Assert.That(withEffects.Fingerprint).IsEqualTo(empty.Fingerprint);
    }

    [Test]
    public async Task ChangingOnlyAnInternalConstraint_DoesNotChangeFingerprint() {
        var plain = Produce("""
            domain billing

            public entity Order {
              Total: Number
            }
            """);

        var bounded = Produce("""
            domain billing

            public entity Order {
              Total: Number range(0, 100)
            }
            """);

        await Assert.That(bounded.Fingerprint).IsEqualTo(plain.Fingerprint);
    }

    private static ImportedContract Produce(string dsl) {
        var applied = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(dsl).Parse());
        if (!applied.Succeeded)
            throw new InvalidOperationException(applied.FailureSummary ?? "mutation failed");
        return new InternalDomainProducer().Produce(applied.Root);
    }
}