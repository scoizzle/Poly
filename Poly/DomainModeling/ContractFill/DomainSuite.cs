using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.ContractFill;

/// <summary>
/// Small multi-domain holder used by the poly-to-poly composition flow: a loaded
/// <see cref="Domain"/> set is keyed by name so a parent domain's
/// <c>contract internal &lt;sourceIdentifier&gt;</c> can resolve to its source
/// domain and be filled. Kept deliberately small — no nested Domain IR, no merge.
/// </summary>
public sealed class DomainSuite {
    private readonly IReadOnlyDictionary<string, Domain> _byName;
    private readonly InternalDomainProducer _producer = new();

    public DomainSuite(IEnumerable<Domain> domains) {
        ArgumentNullException.ThrowIfNull(domains);
        var list = domains.ToList();
        var duplicate = list
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) {
            throw new ArgumentException(
                $"Domain suite contains more than one domain named '{duplicate.Key}'. " +
                "Source identifiers resolve by domain name and must be unique.");
        }
        _byName = list.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Resolves a contract <see cref="ImportedContract.SourceIdentifier"/> (or file
    /// stem) to the loaded domain it names, or <c>null</c> when no such domain exists.</summary>
    public Domain? FindSource(string sourceIdentifier) =>
        sourceIdentifier is null ? null : _byName.GetValueOrDefault(sourceIdentifier);

    /// <summary>
    /// Returns <paramref name="parent"/> with every <see cref="ContractSourceKind.InternalDomain"/>
    /// contract filled from the loaded domain named by its
    /// <see cref="ImportedContract.SourceIdentifier"/>. Unresolvable sources fail closed.
    /// Non-internal contracts are returned untouched.
    /// </summary>
    public Domain FillInternalContracts(Domain parent) {
        ArgumentNullException.ThrowIfNull(parent);
        var filled = parent.ImportedContracts
            .Select(c => c.SourceKind == ContractSourceKind.InternalDomain ? FillInternal(c) : c)
            .ToList();
        return parent with { ImportedContracts = filled };
    }

    private ImportedContract FillInternal(ImportedContract declared) {
        var source = FindSource(declared.SourceIdentifier)
            ?? throw new InvalidOperationException(
                $"Contract '{declared.Name}' declares source '{declared.SourceIdentifier}' but no loaded domain has that name. " +
                "Add the source domain to the DomainSuite so the producer can fill the contract.");
        return _producer.Fill(declared, source);
    }
}