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

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>Accepted duration unit spellings (singular and plural), exact PascalCase.</summary>
public static class DurationForm {
    internal static bool TryGetUnit(string text, out DurationUnit unit) {
        switch (text) {
            case "Day":
            case "Days":
                unit = DurationUnit.Days;
                return true;
            case "Month":
            case "Months":
                unit = DurationUnit.Months;
                return true;
            default:
                unit = default;
                return false;
        }
    }
}