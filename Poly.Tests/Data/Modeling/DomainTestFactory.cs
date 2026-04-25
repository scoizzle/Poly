using System.Reflection;

using Poly.Data.Modeling;
using Poly.Data.Modeling.TypeSystem;

namespace Poly.Tests.Data.Modeling;

internal static class DomainTestFactory {
    internal static Domain CreateDomain(string name = "Test Domain") {
        var domain = (Domain?)Activator.CreateInstance(
            typeof(Domain),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new List<IDomainType>(), new List<Relationship>()],
            culture: null);

        if (domain is null) {
            throw new InvalidOperationException("Failed to construct domain for testing.");
        }

        domain.Name = name;
        return domain;
    }
}