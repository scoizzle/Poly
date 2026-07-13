using Poly.DomainModeling;
using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Queries;

var domain = DomainFactory.Create("Test");
var result = new DomainEvolution(domain).Evolve()
    .AddEntity("Product")
    .AddPropertyToEntity("Product", new Property("Price", new DomainTypeReference("Number"), []))
    .Apply();

Console.WriteLine($"Bootstrap: {result.Succeeded}, {result.FailureSummary}");

var r2 = new DomainEvolution(result.Root).Evolve()
    .AddConstraintToProperty("Product", "Price", new RequiredConstraint())
    .Apply();

Console.WriteLine($"AddConstraint: {r2.Succeeded}, {r2.FailureSummary}");
if (!r2.Succeeded) {
    foreach (var d in r2.Analysis.Diagnostics.Take(5))
        Console.WriteLine($"  [{d.Severity}] {d.Message}");
}