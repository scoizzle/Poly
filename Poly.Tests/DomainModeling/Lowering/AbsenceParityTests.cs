using Poly.DomainModeling;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

using SN = Poly.Ast.Nodes;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-06 (#683): one target-type adapter owns every value-to-target-type decision in
/// lowering, so the shapes the DSL accepts simulate and print the same way. Each row
/// is a value the domain type-checks but lowering used to mistranslate, drop, or
/// leave untyped: an enum member in a create initializer (#485), an omitted enum
/// default (#494), an omitted scalar before a written slot (#495), bare and
/// string <c>match</c> arms assigned to an enum property (#498, #499), and an
/// integral default for a Decimal (#511).
/// </summary>
public class AbsenceParityTests {
    [Test]
    public async Task CreateIn_WhenEnumMemberInitializer_SimAndCSharpAgree() {
        var outcomes = await ParityScenario.FromDsl("""
            domain Ledger
            Kind: enum { Debit, Credit }
            Entry: entity {
              Kind: Kind
            }
            Acct: entity {
              Name: Text
              entries: many Entry
              Dep: action { create in entries { Kind: Credit } }
            }
            """, "ParityEnumMemberCreateIn")
            .AssertAgree(side => {
                side.Create("Acct", ("Name", "Ada"));
                side.Invoke("Dep");
                if (side.Outcomes[^1].Success)
                    side.UseLinked("entries");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["Kind"]).IsEqualTo("Credit");
    }

    [Test]
    public async Task CreateIn_WhenEnumDefaultOmitted_AppliesDefault() {
        var outcomes = await ParityScenario.FromDsl("""
            domain Ledger
            Kind: enum { A, B }
            Sighting: entity {
              V: Kind default(A)
              N: Number
            }
            Acct: entity {
              Name: Text
              seen: many Sighting
              Add: action { create in seen { N: 1 } }
            }
            """, "ParityEnumDefaultOmitted")
            .AssertAgree(side => {
                side.Create("Acct", ("Name", "Ada"));
                side.Invoke("Add");
                if (side.Outcomes[^1].Success)
                    side.UseLinked("seen");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["V"]).IsEqualTo("A");
    }

    [Test]
    public async Task CreateIn_WhenEnumFromParameterHoleBeforeWritten_Agrees() {
        // #495 verbatim: the hole (N) sorts before the written slot (V), so lowering
        // must pass a typed null and then the caller's enum value.
        var outcomes = await ParityScenario.FromDsl("""
            domain Ledger
            Kind: enum { A, B }
            Sighting: entity {
              V: Kind default(A)
              N: Text optional
            }
            Acct: entity {
              Name: Text
              seen: many Sighting
              Go: action (k: Kind) { create in seen { V: k } }
            }
            """, "ParityEnumHoleBeforeWritten")
            .AssertAgree(side => {
                side.Create("Acct", ("Name", "Ada"));
                side.Invoke("Go", ("k", "B"));
                if (side.Outcomes[^1].Success)
                    side.UseLinked("seen");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["V"]).IsEqualTo("B");
    }

    [Test]
    public async Task CreateIn_WhenHoleNameSortsAfterWritten_Agrees() {
        // Control for #495: renaming the hole so it sorts last makes it a trailing
        // omission, which lowering already drops. Passes before and after L-06.
        var outcomes = await ParityScenario.FromDsl("""
            domain Ledger
            Kind: enum { A, B }
            Sighting: entity {
              V: Kind default(A)
              ZNote: Text optional
            }
            Acct: entity {
              Name: Text
              seen: many Sighting
              Go: action (k: Kind) { create in seen { V: k } }
            }
            """, "ParityEnumHoleTrailing")
            .AssertAgree(side => {
                side.Create("Acct", ("Name", "Ada"));
                side.Invoke("Go", ("k", "B"));
                if (side.Outcomes[^1].Success)
                    side.UseLinked("seen");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["V"]).IsEqualTo("B");
    }

    [Test]
    public async Task CreateIn_WhenHoleBeforeDefaultedEnum_Agrees() {
        // The defaulted enum slot stops being a trailing omission (D3), so it arrives
        // beside the typed null the hole needs (D4). Neither may recreate the other's bug.
        var outcomes = await ParityScenario.FromDsl("""
            domain Ledger
            Kind: enum { A, B }
            Sighting: entity {
              N: Text optional
              V: Kind default(A)
            }
            Acct: entity {
              Name: Text
              seen: many Sighting
              Go: action { create in seen { } }
            }
            """, "ParityHoleBeforeDefaultedEnum")
            .AssertAgree(side => {
                side.Create("Acct", ("Name", "Ada"));
                side.Invoke("Go");
                if (side.Outcomes[^1].Success)
                    side.UseLinked("seen");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["V"]).IsEqualTo("A");
    }

    [Test]
    public async Task Assign_WhenBareEnumMemberMatchArm_Agrees() {
        var outcomes = await ParityScenario.FromDsl("""
            domain Shop
            Tier: enum { Gold, Std }
            Order: entity {
              Total: Number
              Tier: Tier default(Std)
              Classify: action { assign Tier to match { Total > 5 -> Gold, else -> Std } }
            }
            """, "ParityMatchBareEnumArm")
            .AssertAgree(side => {
                side.Create("Order", ("Total", 10L));
                side.Invoke("Classify");
                side.Create("Order", ("Total", 1L));
                side.Invoke("Classify");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[1].State["Tier"]).IsEqualTo("Gold");
        await Assert.That(outcomes[3].Success).IsTrue()
            .Because($"step failed: {outcomes[3].ExceptionType} {outcomes[3].Message}");
        await Assert.That(outcomes[3].State["Tier"]).IsEqualTo("Std");
    }

    [Test]
    public async Task Assign_WhenTextMatchArmsToEnumProperty_CSharpCompiles() {
        var outcomes = await ParityScenario.FromDsl("""
            domain Shop
            Tier: enum { Gold, Standard }
            Order: entity {
              Total: Number
              Tier: Tier default(Standard)
              Classify: action { assign Tier to match { Total > 5 -> "Gold", else -> "Standard" } }
            }
            """, "ParityMatchTextEnumArm")
            .AssertAgree(side => {
                side.Create("Order", ("Total", 10L));
                side.Invoke("Classify");
                side.Create("Order", ("Total", 1L));
                side.Invoke("Classify");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[1].State["Tier"]).IsEqualTo("Gold");
        await Assert.That(outcomes[3].Success).IsTrue()
            .Because($"step failed: {outcomes[3].ExceptionType} {outcomes[3].Message}");
        await Assert.That(outcomes[3].State["Tier"]).IsEqualTo("Standard");
    }

    [Test]
    public async Task Assign_WhenParameterNamedLikeEnumMember_UsesParameter() {
        // Guard: an action parameter shadows an enum member of the same name.
        var outcomes = await ParityScenario.FromDsl("""
            domain Shop
            PatronStatus: enum { Active, Suspended }
            Patron: entity {
              Status: PatronStatus default(Active)
              Set: action (Suspended: PatronStatus) { assign Status to Suspended }
            }
            """, "ParityParamShadowsEnumMember")
            .AssertAgree(side => {
                side.Create("Patron");
                side.Invoke("Set", ("Suspended", "Active"));
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[1].State["Status"]).IsEqualTo("Active");
    }

    [Test]
    public async Task Assign_WhenOtherActionParameterNamedLikeEnumMember_StillQualifiesMember() {
        // B1: the shadow guard must be scoped to the action being lowered. A bare enum
        // member named like a parameter of ANOTHER action still qualifies.
        var outcomes = await ParityScenario.FromDsl("""
            domain Shop
            PatronStatus: enum { Active, Suspended }
            Patron: entity {
              Status: PatronStatus default(Active)
              Note: Text optional
              Hold: action (Suspended: Text) { assign Note to Suspended }
              Suspend: action { assign Status to Suspended }
            }
            """, "ParityOtherActionParameterShadows")
            .AssertAgree(side => {
                side.Create("Patron");
                side.Invoke("Suspend");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[1].State["Status"]).IsEqualTo("Suspended");
    }

    [Test]
    public async Task CreateIn_WhenBareNameIsAlsoAProperty_StaysInstanceRead() {
        // Guard: a bare initializer value that is a property of the creating entity
        // reads that property, it is not a member of the target enum.
        var outcomes = await ParityScenario.FromDsl("""
            domain Shop
            PatronStatus: enum { Active, Suspended }
            Patron: entity {
              Active: PatronStatus default(Active)
              Status: PatronStatus default(Active)
              kids: many Kid
              Add: action { create in kids { Status: Active } }
            }
            Kid: entity {
              Status: PatronStatus default(Active)
            }
            """, "ParityBareNameIsProperty")
            .AssertAgree(side => {
                side.Create("Patron", ("Active", "Suspended"), ("Status", "Active"));
                side.Invoke("Add");
                if (side.Outcomes[^1].Success)
                    side.UseLinked("kids");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["Status"]).IsEqualTo("Suspended");
    }

    [Test]
    public async Task CreateIn_WhenDecimalRangeDefaultZeroOmitted_Succeeds() {
        var outcomes = await ParityScenario.FromDsl("""
            domain Payroll
            Employee: entity {
              Name: Text
              Salary: Decimal range(0, 1000000) default(0)
            }
            Dept: entity {
              Name: Text
              staff: many Employee
              Hire: action { create in staff { Name: "Ada" } }
            }
            """, "ParityDecimalDefaultOmitted")
            .AssertAgree(side => {
                side.Create("Dept", ("Name", "R&D"));
                side.Invoke("Hire");
                if (side.Outcomes[^1].Success)
                    side.UseLinked("staff");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["Name"]).IsEqualTo("Ada");
    }

    [Test]
    public async Task CreateIn_WhenNumberAndEnumDefaultsTrail_Agrees() {
        // E2: the warehouse RegisterTruck shape — the factory call now carries the
        // trailing Number and enum defaults explicitly, and both sides link a child
        // whose stored values are the member name and the number.
        var outcomes = await ParityScenario.FromDsl("""
            domain Depot
            TruckStatus: enum { Idle, EnRoute }
            Truck: entity {
              Vin: Text length(17, 17)
              MaxLoad: Number range(1, 40000)
              CurrentLoad: Number range(0, 40000) default(0)
              Status: TruckStatus default(Idle)
            }
            Warehouse: entity {
              Name: Text
              trucks: many Truck
              RegisterTruck: action (vin: Text, maxLoad: Number) {
                create in trucks { Vin: vin MaxLoad: maxLoad }
              }
            }
            """, "ParityTrailingDefaultsCreateIn")
            .AssertAgree(side => {
                side.Create("Warehouse", ("Name", "W"));
                side.Invoke("RegisterTruck", ("vin", "1HGCM82633A004352"), ("maxLoad", 10L));
                if (side.Outcomes[^1].Success)
                    side.UseLinked("trucks");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["Status"]).IsEqualTo("Idle");
        await Assert.That(outcomes[2].State["CurrentLoad"]).IsEqualTo("0");
    }

    private const string OptionalScalarHolesDsl = """
        domain Ledger
        Tally: entity {
          N: Number optional
          D: Decimal optional
          V: Text optional
        }
        Acct: entity {
          Name: Text
          tallies: many Tally
          Add: action { create in tallies { V: "x" } }
        }
        """;

    [Test]
    public async Task CreateIn_WhenOptionalScalarHolesBeforeWrittenSlot_Agree() {
        // #495 for value types. V is optional too, so the slots are
        // [D hole, N hole, V written] and both typed nulls are KEPT in the call —
        // with a required V, V would be ctor slot 0 and D/N would be a trailing run
        // that is dropped, which exercises nothing.
        var outcomes = await ParityScenario.FromDsl(OptionalScalarHolesDsl, "ParityOptionalScalarHoles")
            .AssertAgree(side => {
                side.Create("Acct", ("Name", "Ada"));
                side.Invoke("Add");
                if (side.Outcomes[^1].Success)
                    side.UseLinked("tallies");
            });

        // The printed call is the proof the holes survived: two typed nulls, then the
        // written value. A dropped trailing run would carry only the value.
        var (domain, analysis) = EvolvedDomain.FromDsl(OptionalScalarHolesDsl);
        var printed = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));
        var add = ExportedCSharp.ExtractMethod(printed, "public DomainResult Add(");
        await Assert.That(add).Contains("(decimal?)null");
        await Assert.That(add).Contains("(long?)null");

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["V"]).IsEqualTo("x");
        await Assert.That(outcomes[2].State["N"]).IsNull();
        await Assert.That(outcomes[2].State["D"]).IsNull();
    }

    [Test]
    public async Task CreateIn_WhenDecimalInitializerZero_Agrees() {
        // E8 green control: dropping the initializer Decimal arm leaves a written 0 as
        // 0L, which the VM and the printed Decimal both store as zero.
        var outcomes = await ParityScenario.FromDsl("""
            domain Payroll
            Employee: entity {
              Name: Text
              Salary: Decimal range(0, 1000000)
            }
            Dept: entity {
              Name: Text
              staff: many Employee
              Hire: action { create in staff { Name: "Ada" Salary: 0 } }
            }
            """, "ParityDecimalInitializerZero")
            .AssertAgree(side => {
                side.Create("Dept", ("Name", "R&D"));
                side.Invoke("Hire");
                if (side.Outcomes[^1].Success)
                    side.UseLinked("staff");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["Salary"]).IsEqualTo("0");
    }

    [Test]
    public async Task Adapt_WhenStringNamesEnumMember_QualifiesMember() {
        // The #683 Node-form contract: an already-lowered string constant naming a member.
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            Tier: enum { Gold, Std }
            Order: entity { Tier: Tier default(Std) }
            """);
        var context = new LoweringContext(new SN.ThisReference(), analysis, domain);
        var target = domain.Types.OfType<Entity>().Single(e => e.Name == "Order")
            .Properties.Single(p => p.Name == "Tier").Type;

        var adapted = TargetTypeAdapter.Adapt(new SN.Constant("Gold"), target, context);

        await Assert.That(adapted).IsTypeOf<SN.Member>();
        await Assert.That(((SN.Member)adapted).MemberName).IsEqualTo("Gold");
        await Assert.That(((SN.NamedTypeReference)((SN.Member)adapted).Value).TypeName)
            .IsEqualTo("Tier");
    }

    [Test]
    public async Task Adapt_WhenIntegralConstantForDecimalTarget_MakesDecimal() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Pay
            Employee: entity { Salary: Decimal range(0, 1000000) }
            """);
        var context = new LoweringContext(new SN.ThisReference(), analysis, domain);
        var target = domain.Types.OfType<Entity>().Single(e => e.Name == "Employee")
            .Properties.Single(p => p.Name == "Salary").Type;

        var adapted = TargetTypeAdapter.Adapt(new SN.Constant(0L), target, context);

        await Assert.That(adapted).IsTypeOf<SN.Constant>();
        await Assert.That(((SN.Constant)adapted).Value).IsEqualTo(0m);
    }

    /// <summary>
    /// Pin: a function body names only its parameters (DMFUN005), so the nested pass
    /// keeps an empty enum-property map. An empty map means "no enum properties are
    /// known here" — a lookup miss, not a second mode — and the comparison stays text
    /// (<c>Calls.cs</c>). L-23 owns the C#-typed function body and flips this row when
    /// it lands; until then it keeps the current shape from drifting as the adapter
    /// takes over comparisons.
    /// </summary>
    [Test]
    public async Task FunctionBody_WhenEnumComparison_StillLowersAsText() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            PatronStatus: enum { Active, Suspended }
            function IsActive(s: PatronStatus): Boolean = s is "Active"
            Patron: entity {
              Status: PatronStatus default(Active)
              ActiveNow: policy { IsActive(Status) }
            }
            """);

        var printed = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));

        // The function body compares the bound argument to the text literal: no
        // enum type is known inside the nested pass, so nothing is qualified.
        await Assert.That(printed).Contains("ActiveNow() => this.Status == \"Active\"");
        await Assert.That(printed).DoesNotContain("ActiveNow() => this.Status == PatronStatus.Active");
    }

    /// <summary>
    /// Pin: the module's enum member carries the enum type (D1), so a lowered enum
    /// argument resolves against the Create signature. The stored value stays the
    /// member name, which the VM and the printed enum both read back as text.
    /// </summary>
    [Test]
    public async Task ModuleEnumMember_CarriesEnumTypeAndMemberName() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            PatronStatus: enum { Active, Suspended }
            Patron: entity {
              Status: PatronStatus default(Active)
            }
            """);

        var types = DomainProgramProjection.ToSyntax(domain, analysis);
        var status = types.Single(t => t.Name == "PatronStatus");
        var member = status.Fields!.Single(f => f.Name == "Active");

        await Assert.That(member.FieldType).IsTypeOf<SN.NamedTypeReference>();
        await Assert.That(((SN.NamedTypeReference)member.FieldType).TypeName).IsEqualTo("PatronStatus");
        await Assert.That((member.DefaultValue as SN.Constant)?.Value).IsEqualTo("Active");
    }
}