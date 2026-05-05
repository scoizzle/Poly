using Poly.Data.Modeling;
using Poly.Data.Modeling.TypeSystem;
using Poly.Introspection;

namespace Poly.Tests.Data.Modeling;

internal static class DomainTestFactory {
    internal static Domain CreateDomain(string name = "Test Domain") {
        return new Domain(name);
    }

    internal static Primitive GetStringType(Domain domain) {
        return new Primitive(domain, "string", TypeCategory.Text);
    }
}