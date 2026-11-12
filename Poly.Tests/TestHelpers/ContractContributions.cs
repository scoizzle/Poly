using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// Shared hand-built contract libraries for the `uses` / bind slices: a surface-only
/// sibling and an external provider that registers the old trace body.
/// </summary>
public static class ContractContributions {
    public static readonly IReadOnlyDictionary<string, Func<Block>> NoBodies =
        new Dictionary<string, Func<Block>>(StringComparer.Ordinal);

    public static ImportedContract BillingSurface(
        ContractSourceKind kind = ContractSourceKind.InternalDomain,
        string version = "v1",
        string name = "Billing",
        string source = "billing") =>
        new(
            name,
            kind,
            source,
            version,
            [
                new ContractEndpoint(
                    "Charge",
                    ContractEndpointKind.Operation,
                    ContractEndpointDirection.Outbound,
                    new DomainTypeReference("ChargeRequest")),
                new ContractEndpoint(
                    "Refund",
                    ContractEndpointKind.Operation,
                    ContractEndpointDirection.Outbound,
                    new DomainTypeReference("Number")),
            ]) {
            Types = [
                new ValueType("ChargeRequest", [
                    new Property("Amount", new DomainTypeReference("Number"), []),
                ], []),
            ]
        };

    /// <summary>
    /// The body the hard-coded adapter stub used to hold: trace the request, then return
    /// success. A fresh block per call, as a library body factory must be.
    /// </summary>
    public static Func<Block> TraceBody(string endpointName) => () => new Block([
        new Invoke(
            new Member(new ClrTypeReference(typeof(System.Diagnostics.Debug)), "WriteLine"),
            [
                new Invoke(
                    new Member(TypeReference.To<string>(), "Concat"),
                    [
                        new Constant($"Contract endpoint '{endpointName}' request: "),
                        new Parameter("request"),
                    ]),
            ]),
        new Return(new Invoke(new Member(new NamedTypeReference("DomainResult"), "Success"), [])),
    ]);

    public static IDomainLibrary StripeLibrary(string contract = "Stripe", string endpoint = "Charge") =>
        new ContractLibrary(
            "stripe",
            BillingSurface(ContractSourceKind.ExternalProvider, name: contract, source: "stripe"),
            new Dictionary<string, Func<Block>>(StringComparer.Ordinal) {
                [endpoint] = TraceBody($"{contract}.{endpoint}"),
            });

    public static ExtensionCatalog Catalog(params IDomainLibrary[] libraries) {
        var catalog = new ExtensionCatalog();
        foreach (var library in libraries)
            catalog = catalog.With(library);
        return catalog;
    }

    public static (Domain Domain, DomainSession Session, AnalysisResult Analysis) Evolve(
        string poly,
        params IDomainLibrary[] libraries) {
        var tree = DslSyntaxTree.Parse(poly);
        var session = DomainSession.Create(tree.Uses, SessionOptions.Bare(Catalog(libraries)));
        var changes = new DomainResolver(tree, session).Resolve();
        var result = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded) {
            throw new InvalidOperationException(
                "Test domain failed to apply: " +
                string.Join("; ", result.Analysis.Diagnostics.Select(d => d.Message)));
        }
        return (result.Root!, session, result.Analysis);
    }

    /// <summary>One library that contributes a surface and, optionally, per-endpoint bodies.</summary>
    public sealed class ContractLibrary(
        string id,
        ImportedContract surface,
        IReadOnlyDictionary<string, Func<Block>>? bodies = null) : IDomainLibrary {
        public string Id => id;

        public void Register(SessionBuilder builder) =>
            builder.AddContract(surface, bodies ?? NoBodies);
    }
}