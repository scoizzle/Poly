using Poly.Data.Modeling;

namespace Poly.Tests.Data.Modeling;

internal static class DomainTestFactory {
    internal static Domain CreateDomain(string name = "Test Domain") {
        return new Domain(name);
    }
}