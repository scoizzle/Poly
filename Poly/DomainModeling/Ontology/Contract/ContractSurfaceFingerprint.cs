using System.Security.Cryptography;
using System.Text;

using Poly.DomainModeling.Language;

namespace Poly.DomainModeling.Ontology.Contract;

/// <summary>
/// Canonical surface digest for an <see cref="ImportedContract"/> (spike #413 §3). Only the
/// public signature is hashed: endpoint source names, directions, kinds, payload types,
/// public entity names, enum members, constraints on public value-type properties, public
/// action preconditions, and the reserved <c>accepts</c> component.
/// <para>
/// Not hashed: implementation AST, effects, retries, auth, an internal constraint, an
/// internal value type, the exported endpoint <c>Name</c> suffix, the contract name, version,
/// or source identifier. The digest is the surface's identity; the version is the human pin.
/// </para>
/// </summary>
internal static class ContractSurfaceFingerprint {
    public static string Compute(ImportedContract contract) {
        ArgumentNullException.ThrowIfNull(contract);
        var printer = new DomainDslPrinter();
        var lines = new List<string> { "surface|1" };

        foreach (var entity in contract.Entities.OrderBy(e => e, StringComparer.Ordinal))
            lines.Add($"entity|{entity}");

        foreach (var valueType in contract.Types.OrderBy(t => t.Name, StringComparer.Ordinal)) {
            lines.Add($"value|{valueType.Name}");
            foreach (var constraint in Constraints(valueType.Constraints, printer))
                lines.Add($"valueconstraint|{valueType.Name}|{constraint}");
            foreach (var property in valueType.Properties.OrderBy(p => p.Name, StringComparer.Ordinal)) {
                lines.Add(
                    $"prop|{valueType.Name}|{property.Name}|{property.Type.TypeName}" +
                    $"|{string.Join(",", Constraints(property.Constraints, printer))}");
            }
        }

        foreach (var enumType in contract.Enums.OrderBy(e => e.Name, StringComparer.Ordinal)) {
            foreach (var constraint in Constraints(enumType.Constraints, printer))
                lines.Add($"enumconstraint|{enumType.Name}|{constraint}");
            lines.Add(
                $"enum|{enumType.Name}" +
                $"|{string.Join(",", enumType.MemberNames.OrderBy(m => m, StringComparer.Ordinal))}");
        }

        foreach (var endpoint in contract.Endpoints
                     .OrderBy(e => e.SourceName ?? e.Name, StringComparer.Ordinal)
                     .ThenBy(e => e.PayloadType.TypeName, StringComparer.Ordinal)
                     .ThenBy(e => e.Name, StringComparer.Ordinal)) {
            var sourceName = endpoint.SourceName ?? endpoint.Name;
            lines.Add(
                $"endpoint|{sourceName}|{endpoint.Direction}|{endpoint.Kind}|{endpoint.PayloadType.TypeName}");
            foreach (var precondition in endpoint.Preconditions
                         .Select(p => $"{p.Name}|{printer.PrintExpressionText(p.Expression)}")
                         .OrderBy(p => p, StringComparer.Ordinal)) {
                lines.Add($"precondition|{sourceName}|{precondition}");
            }
        }

        // Decision 20: the surface may later declare the sensitivity classes it accepts.
        // The component is reserved now and empty; authoring an `accepts` must move the digest.
        lines.Add("accepts|");

        var canonical = string.Join("\n", lines);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static IEnumerable<string> Constraints(
        IEnumerable<Constraint> constraints, DomainDslPrinter printer) =>
        constraints
            .Select(printer.PrintConstraintText)
            .OrderBy(c => c, StringComparer.Ordinal);
}