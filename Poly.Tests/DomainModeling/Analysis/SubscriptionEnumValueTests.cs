using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #509: a bare name in a subscription effect that is no subscriber property can be an enum member
/// used as a value — <c>create in fines { Why: Late }</c>. The binding check read every bare name as a
/// property read and warned DMSS004 ("references property 'Late' ... but that property does not exist")
/// for a model whose enum member the create initializer resolves. The rule is the one
/// <c>ExpressionTypeAnalyzer</c> uses for a bare name: a value when the target property's enum declares
/// it, else when exactly one enum in the domain declares it.
/// </summary>
public class SubscriptionEnumValueTests {
    private const string Issue509 = """
        domain Probe

        Reason: enum {
          Late
          Broken
        }

        Fine: entity {
          Why: Reason
        }

        Loan: entity {
          Active: stage {
            Slip: action { transition to Done }
          }
          Done: stage { }
        }

        Member: entity {
          fines: many Fine
          loans: many Loan
          Mk: action { create in loans { } }
          when loans Done {
            create in fines { Why: Late }
          }
        }
        """;

    private static (Domain Domain, AnalysisResult Analysis) ParseAndAnalyze(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException("Evolution failed: " + string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        return (result.Root!, DomainModelAnalyzer.Analyze(result.Root!));
    }

    private static List<Diagnostic> BindingWarnings(Domain domain) =>
        DomainModelAnalyzer.Analyze(domain).Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.SubscriptionEffectBinding
                && d.Message.Contains("on subscriber entity", StringComparison.Ordinal))
            .ToList();

    [Test]
    public async Task CreateIn_WithEnumMemberValue_DoesNotWarnAMissingSubscriberProperty() {
        var domain = ParseAndAnalyze(Issue509).Domain;

        var warnings = BindingWarnings(domain);

        await Assert.That(warnings.Select(w => w.Message).ToList()).IsEmpty();
    }

    [Test]
    public async Task CreateIn_WithEnumMemberValue_AnalyzesWithoutErrors() {
        var analysis = ParseAndAnalyze(Issue509).Analysis;

        await Assert.That(analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Count()).IsEqualTo(0);
    }

    [Test]
    public async Task CreateIn_WithABareNameNoEnumDeclares_StillWarns() {
        // The parser resolves or rejects a bare name before analysis, so this shape only reaches
        // the binding check from a model-API-built tree; the check still names it.
        var domain = WithMemberSubscriptionEffects(ParseAndAnalyze(Issue509).Domain,
            new CreateEntityInRelationshipEffect("fines",
                [new PropertyBinding("Why", DomainExpression.Property("Bogus"))]));

        var warnings = BindingWarnings(domain);

        await Assert.That(warnings.Count).IsEqualTo(1);
        await Assert.That(warnings[0].Message).Contains("'Bogus'");
    }

    private static Domain WithMemberSubscriptionEffects(Domain domain, params Effect[] effects) =>
        domain with {
            Types = domain.Types.Select(type => type is Entity { Name: "Member" } member
                ? member with {
                    Subscriptions = member.Subscriptions
                        .Select(subscription => subscription with { Effects = effects }).ToList()
                }
                : type).ToList()
        };

    [Test]
    public async Task Assign_WithEnumMemberValue_DoesNotWarnAMissingSubscriberProperty() {
        var domain = ParseAndAnalyze("""
            domain Probe

            Reason: enum { Late, Broken }

            Loan: entity {
              Active: stage { Slip: action { transition to Done } }
              Done: stage { }
            }

            Member: entity {
              Why: Reason
              loans: many Loan
              Mk: action { create in loans { } }
              when loans Done { assign Why to Late }
            }
            """).Domain;

        var warnings = BindingWarnings(domain);

        await Assert.That(warnings.Select(w => w.Message).ToList()).IsEmpty();
    }

    [Test]
    public async Task CreateIn_WithTheTargetPropertyEnumDeclaringTheName_DoesNotWarn() {
        // Both enums declare 'Late'; the initializer's target property ('Why: Reason') decides.
        var domain = ParseAndAnalyze(Issue509.Replace("""
            Reason: enum {
              Late
              Broken
            }
            """, """
            Reason: enum {
              Late
              Broken
            }
            Other: enum {
              Late
              LateOther
            }
            """, StringComparison.Ordinal)).Domain;

        var warnings = BindingWarnings(domain);

        await Assert.That(warnings.Select(w => w.Message).ToList()).IsEmpty();
    }
}