using Poly.DomainModeling.Ontology;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Language;

// DomainDslPrinter partial: enum, value-type and facet printing (G-00b mechanical split).
public sealed partial class DomainDslPrinter {
    private void PrintEnumType(EnumType enumType) {
        _sb.Append($"{enumType.Name}: enum");
        foreach (var facet in enumType.Facets) {
            _sb.Append(' ');
            _sb.Append(PrintFacet(facet));
        }
        _sb.AppendLine(" {");
        foreach (var member in enumType.MemberNames) {
            _sb.Append($"  {member}");
            if (enumType.MemberFacets.TryGetValue(member, out var facets)) {
                foreach (var facet in facets) {
                    _sb.Append(' ');
                    _sb.Append(PrintFacet(facet));
                }
            }
            _sb.AppendLine(",");
        }
        _sb.AppendLine("}");
        _sb.AppendLine();
    }

    private void PrintValueType(ValueType valueType) {
        if (valueType.Visibility == Visibility.Public)
            _sb.Append("public value ").Append(valueType.Name);
        else
            _sb.Append(valueType.Name).Append(": value");
        _sb.AppendLine(" {");
        foreach (var prop in valueType.Properties) {
            _sb.Append("  ");
            _sb.Append(prop.Name);
            _sb.Append(": ");
            _sb.Append(prop.Type.TypeName);
            AppendPrintedConstraints(prop.Constraints);
            _sb.AppendLine();
        }
        _sb.AppendLine("}");
    }

    private string PrintFacet(Facet facet, string? propertyName = null) {
        var text = _annotations?.TryPrint(facet);
        if (text is not null)
            return text;

        var location = propertyName is null ? "" : $" on property '{propertyName}'";
        if (facet is Annotation ann) {
            throw new FormatException(
                $"Cannot print annotation '{ann.Name}'{location} — no pack registered for this keyword.");
        }

        throw new FormatException(
            $"Cannot print facet of type '{facet.GetType().Name}'{location} — no pack registered for it.");
    }
}