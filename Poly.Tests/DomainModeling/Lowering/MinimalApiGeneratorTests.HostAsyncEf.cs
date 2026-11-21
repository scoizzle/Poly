using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #629: the host's own EF calls must be the async forms. Every EF query terminal rooted at the
/// generated host's <c>db</c> parameter is asserted to be the <c>…Async</c> overload, so a handler
/// cannot block a thread-pool thread on SQLite. What the host does with an already-loaded navigation
/// (<c>parent.Loans.Any(…)</c>, <c>entity.Children.ToList()</c>) is LINQ to objects, not EF, and is not
/// in scope: only invocations whose receiver chain is rooted at <c>db</c> are EF round trips.
/// </summary>
public partial class MinimalApiGeneratorTests {
    /// <summary>EF Core query terminals that read the database on the calling thread.</summary>
    private static readonly string[] SyncEfTerminals = [
        "ToList", "ToArray", "ToDictionary", "ToHashSet",
        "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault",
        "Any", "All", "Count", "LongCount", "Sum", "Min", "Max",
        "Find", "SaveChanges", "Contains",
    ];

    /// <summary>
    /// Root entity (list, detail, create, seed), unique keys on both the root and the child (the
    /// detail route's key and shadow-key branches), a child collection (the back-ref detail route),
    /// an action, and a to-one reference into the child (reference loads).
    /// </summary>
    private const string HostWithEveryEfSurface = """
        domain HostAsync
        Book: entity {
          Sku: Text unique
          Title: Text
          Restock: action { assign Title to Title }
        }
        Patron: entity {
          Email: Text unique
          loans: many Loan
        }
        Loan: entity {
          Days: Number unique
          borrower: Patron via loans
        }
        """;

    [Test]
    public async Task HostHandlers_UseOnlyAsyncEfTerminals() {
        var rendered = Render(ParseDomain(HostWithEveryEfSurface));
        var root = CSharpSyntaxTree.ParseText(rendered).GetRoot();

        var syncCalls = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is MemberAccessExpressionSyntax access
                && SyncEfTerminals.Contains(access.Name.Identifier.ValueText, StringComparer.Ordinal)
                && RootIdentifier(access.Expression) == "db")
            .Select(invocation => invocation.ToString())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        await Assert.That(syncCalls.Count).IsEqualTo(0)
            .Because("the host's own EF calls must be async; sync terminals found: "
                     + string.Join(" | ", syncCalls));
    }

    /// <summary>
    /// The no-sync test would pass vacuously if the host stopped querying EF at all, so the same
    /// rendered host must show the async terminals it is asserted to use.
    /// </summary>
    [Test]
    public async Task HostHandlers_EmitTheAsyncTerminalsTheyAreHeldTo() {
        var rendered = Render(ParseDomain(HostWithEveryEfSurface));

        await Assert.That(rendered).Contains("ToListAsync");
        await Assert.That(rendered).Contains("FirstOrDefaultAsync");
        await Assert.That(rendered).Contains("SaveChangesAsync");
        await Assert.That(rendered).Contains("AnyAsync");
    }

    /// <summary>The innermost identifier of a receiver chain, through calls, member access and parentheses.</summary>
    private static string? RootIdentifier(ExpressionSyntax expression) => expression switch {
        InvocationExpressionSyntax invocation => RootIdentifier(invocation.Expression),
        MemberAccessExpressionSyntax access => RootIdentifier(access.Expression),
        ParenthesizedExpressionSyntax parenthesized => RootIdentifier(parenthesized.Expression),
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        _ => null,
    };
}