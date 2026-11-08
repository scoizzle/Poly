using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// pack-3b-1: <see cref="InternalDomainProducer"/> projects the <c>public</c> surface of a
/// loaded <see cref="Domain"/> — name-only public entities, public actions as outbound
/// operations, and the public value types/enums a public signature reaches. Unmarked
/// members are absent, and a public action on an internal entity is never exported
/// (<c>DMCON003</c> refuses that domain).
/// </summary>
public sealed class InternalDomainProducerTests {
    private static Domain Parse(string dsl, bool analyze = true) {
        var changes = new PolyDslParser(dsl).Parse();
        if (analyze) {
            var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
                .ApplyAndAnalyze(changes);
            if (!result.Succeeded)
                throw new InvalidOperationException(result.FailureSummary ?? "analysis failed");
            return result.Root!;
        }

        var applied = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!applied.Succeeded)
            throw new InvalidOperationException(applied.FailureSummary ?? "mutation failed");
        return applied.Root;
    }

    private static ImportedContract Produce(Domain domain) =>
        new InternalDomainProducer().Produce(domain);

    private const string BillingDsl = """
        domain billing

        public value ChargeRequest {
          Amount: Number
          Currency: Text
        }

        public entity Ledger {
          public action Charge (request: ChargeRequest) {
          }
        }
        """;

    [Test]
    public async Task Produce_PublicSingletonAction_IsAnEndpoint() {
        var contract = Produce(Parse(BillingDsl));

        await Assert.That(contract.Name).IsEqualTo("Billing");
        await Assert.That(contract.SourceKind).IsEqualTo(ContractSourceKind.InternalDomain);
        await Assert.That(contract.SourceIdentifier).IsEqualTo("billing");
        await Assert.That(contract.Version).IsEqualTo("v1");
        var charge = contract.Endpoints.Single(e => e.Name == "Charge");
        await Assert.That(charge.SourceName).IsEqualTo("Charge");
        await Assert.That(charge.Kind).IsEqualTo(ContractEndpointKind.Operation);
        await Assert.That(charge.Direction).IsEqualTo(ContractEndpointDirection.Outbound);
        await Assert.That(charge.PayloadType.TypeName).IsEqualTo("ChargeRequest");
    }

    [Test]
    public async Task Produce_UnmarkedAction_IsNotAnEndpoint() {
        var contract = Produce(Parse("""
            domain billing

            public entity Ledger {
              Total: Number
              Charge: action (request: Text) {
              }
            }
            """));

        await Assert.That(contract.Endpoints).IsEmpty();
    }

    [Test]
    public async Task Produce_PublicActionOnInternalEntity_IsNotAnEndpoint() {
        // DMCON003 refuses this domain; the producer must not project through an
        // internal owner even when called on an unanalyzed model.
        var contract = Produce(Parse("""
            domain billing

            Ledger: entity {
              public action Charge (request: Text) {
              }
            }
            """, analyze: false));

        await Assert.That(contract.Endpoints).IsEmpty();
        await Assert.That(contract.Entities).IsEmpty();
    }

    [Test]
    public async Task Produce_PublicEntityName_IsOnContract_PropertiesAndStagesAreNot() {
        var contract = Produce(Parse("""
            domain billing

            public entity Ledger {
              Total: Number
              Active: stage {
                public action Post (note: Text) {
                }
              }
            }
            """));

        await Assert.That(contract.Entities).Contains("Ledger");
        await Assert.That(contract.Types.Select(t => t.Name)).DoesNotContain("Ledger");
        await Assert.That(contract.Endpoints.Select(e => e.Name)).Contains("Post");
        await Assert.That(contract.Children.OfType<Stage>()).IsEmpty();
        await Assert.That(contract.Children.OfType<Property>()).IsEmpty();
    }

    [Test]
    public async Task Produce_InternalEntityName_IsAbsent() {
        var contract = Produce(Parse("""
            domain billing

            Ledger: entity {
              Total: Number
            }
            """));

        await Assert.That(contract.Entities).IsEmpty();
    }

    [Test]
    public async Task Produce_PublicValueTypeReachableFromSignature_IsIncluded() {
        var contract = Produce(Parse(BillingDsl));

        var request = contract.Types.Single(t => t.Name == "ChargeRequest");
        await Assert.That(request.Properties.Count).IsEqualTo(2);
        await Assert.That(request.Properties.Select(p => p.Name)).Contains("Amount");
        await Assert.That(request.Properties.Select(p => p.Name)).Contains("Currency");
    }

    [Test]
    public async Task Produce_PublicValueTypeNotOnSignature_IsAbsent() {
        var contract = Produce(Parse("""
            domain billing

            public value Orphan {
              Note: Text
            }

            public entity Ledger {
              public action Charge (request: Text) {
              }
            }
            """));

        await Assert.That(contract.Types.Select(t => t.Name)).DoesNotContain("Orphan");
    }

    [Test]
    public async Task Produce_InternalValueTypeOnSignature_IsAbsent() {
        var domain = Parse("""
            domain billing

            public value Money {
              Amount: Number
            }

            public entity Ledger {
              public action Charge (request: Money) {
              }
            }
            """);
        var internalized = domain with {
            Types = domain.Types
                .Select(t => t is ValueType { Name: "Money" } money
                    ? money with { Visibility = Visibility.Internal }
                    : t)
                .ToList()
        };

        var contract = Produce(internalized);

        await Assert.That(contract.Types.Select(t => t.Name)).DoesNotContain("Money");
    }

    [Test]
    public async Task Produce_EnumPayload_IsOnContract() {
        var contract = Produce(Parse("""
            domain billing

            Color: enum {
              Red,
              Green,
              Blue,
            }

            public entity Ledger {
              public action Paint (color: Color) {
              }
            }
            """));

        var color = contract.Enums.Single(e => e.Name == "Color");
        await Assert.That(color.MemberNames).Contains("Red");
        await Assert.That(color.MemberNames).Contains("Green");
        await Assert.That(contract.Types.Select(t => t.Name)).DoesNotContain("Color");
    }

    [Test]
    public async Task Produce_UnmarkedStageAction_IsNotAnEndpoint() {
        var contract = Produce(Parse("""
            domain billing

            public entity Ledger {
              Active: stage {
                Post: action {
                }
              }
            }
            """));

        await Assert.That(contract.Endpoints).IsEmpty();
    }

    [Test]
    public async Task Produce_PublicStageAction_IsAnEndpoint_WithStagePreconditions() {
        var contract = Produce(Parse("""
            domain billing

            public value LedgerLine {
              Amount: Number
            }

            public entity Ledger {
              Total: Number
              LowQty: policy { Total <= 80 }
              Active: stage {
                public action Post (line: LedgerLine)
                  require LowQty
                {
                }
              }
            }
            """));

        var post = contract.Endpoints.Single(e => e.Name == "Post");
        await Assert.That(post.SourceName).IsEqualTo("Post");
        await Assert.That(post.PayloadType.TypeName).IsEqualTo("LedgerLine");
        await Assert.That(post.Preconditions.Select(p => p.Name)).Contains("LowQty");
    }

    /// <summary>
    /// #431 item 1: an entity action and a same-named stage shell are one action. The
    /// export takes the union of the public marks instead of reading the shell's copy order.
    /// </summary>
    [Test]
    public async Task Produce_EntityActionAndPublicStageShell_OneEndpoint() {
        var contract = Produce(Parse("""
            domain billing

            public value Money {
              Amount: Number
            }

            public entity Ledger {
              Total: Number
              Charge: action (request: Money) {
              }
              Open: stage {
                public action Charge {
                }
              }
            }
            """));

        var charge = contract.Endpoints.Single(e => e.Name == "Charge");
        await Assert.That(contract.Endpoints.Count).IsEqualTo(1);
        await Assert.That(charge.SourceName).IsEqualTo("Charge");
        await Assert.That(charge.PayloadType.TypeName).IsEqualTo("Money");
    }

    /// <summary>
    /// The domain language rejects duplicate action names (<c>DMSTR001</c>), so overloads are
    /// built directly here. The export is defined before the authoring slice exists.
    /// </summary>
    [Test]
    public async Task Produce_TwoPublicOverloads_TwoEndpointsWithSuffixes_AndSourceName() {
        var domain = OverloadedDomain();
        var contract = Produce(domain);

        await Assert.That(contract.Endpoints.Select(e => e.Name))
            .Contains("Charge__Money")
            .And.Contains("Charge__Key");
        var money = contract.Endpoints.Single(e => e.Name == "Charge__Money");
        var key = contract.Endpoints.Single(e => e.Name == "Charge__Key");
        await Assert.That(money.SourceName).IsEqualTo("Charge");
        await Assert.That(key.SourceName).IsEqualTo("Charge");
        await Assert.That(money.PayloadType.TypeName).IsEqualTo("Money");
        await Assert.That(key.PayloadType.TypeName).IsEqualTo("Key");
    }

    [Test]
    public async Task Produce_InternalOverload_DoesNotSuffixTheSinglePublicSignature() {
        var domain = OverloadedDomain(keyVisibility: Visibility.Internal);
        var contract = Produce(domain);

        var charge = contract.Endpoints.Single();
        await Assert.That(charge.Name).IsEqualTo("Charge");
        await Assert.That(charge.SourceName).IsEqualTo("Charge");
    }

    [Test]
    public async Task Produce_SameNameDifferentEntities_Throws() {
        var domain = Parse("""
            domain billing

            public value Money {
              Amount: Number
            }

            public entity Ledger {
              public action Charge (request: Money) {
              }
            }

            public entity Audit {
              public action Charge (request: Money) {
              }
            }
            """);

        await Assert.That(() => Produce(domain))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Charge");
    }

    [Test]
    public async Task Produce_MultiParameterPublicAction_Throws() {
        var domain = Parse("""
            domain billing

            public entity Ledger {
              public action Bad (a: Number, b: Text) {
              }
            }
            """);

        await Assert.That(() => Produce(domain))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Bad");
    }

    [Test]
    public async Task Produce_NoParameterPublicAction_Throws() {
        var domain = Parse("""
            domain billing

            public entity Ledger {
              public action Nop {
              }
            }
            """);

        await Assert.That(() => Produce(domain))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Nop");
    }

    [Test]
    public async Task Produce_UnmarkedMultiParameterAction_IsIgnored() {
        var contract = Produce(Parse("""
            domain billing

            public entity Ledger {
              Bad: action (a: Number, b: Text) {
              }
            }
            """));

        await Assert.That(contract.Endpoints).IsEmpty();
    }

    [Test]
    public async Task Produce_EmptyDomain_ProducesEmptyContract() {
        var contract = Produce(DomainFactory.Create("empty"));

        await Assert.That(contract.Name).IsEqualTo("Empty");
        await Assert.That(contract.Types).IsEmpty();
        await Assert.That(contract.Enums).IsEmpty();
        await Assert.That(contract.Entities).IsEmpty();
        await Assert.That(contract.Endpoints).IsEmpty();
    }

    private static Domain OverloadedDomain(Visibility keyVisibility = Visibility.Public) {
        var money = new ValueType("Money",
            [new Property("Amount", new DomainTypeReference("Number"), [])], []);
        var key = new ValueType("Key",
            [new Property("Value", new DomainTypeReference("Text"), [])], []);
        var chargeMoney = new Action("Charge", InvocationResult.Void,
            [new Property("amount", new DomainTypeReference("Money"), [])], [], []) {
            Visibility = Visibility.Public
        };
        var chargeKey = new Action("Charge", InvocationResult.Void,
            [new Property("key", new DomainTypeReference("Key"), [])], [], []) {
            Visibility = keyVisibility
        };
        var order = new Entity("Order", [], [chargeMoney, chargeKey], [], []) {
            Visibility = Visibility.Public
        };
        return new Domain("billing", [money, key, order]);
    }
}