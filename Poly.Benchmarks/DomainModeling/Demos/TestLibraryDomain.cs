using System;
using System.Linq;

using Poly.Benchmarks.DomainModeling;
using Poly.Benchmarks.DomainModeling.Demos;
using Poly.Data.Modeling;

namespace Poly.Benchmarks.DomainModeling.Demos;

internal static class TestLibraryDomain {
    public static void Run() {
        var domain = LibraryDomain.Build();

        Console.WriteLine("=== Library Domain Verification ===\n");

        // Check primitives
        Console.WriteLine($"Primitives: {domain.Types.OfType<Poly.Data.Modeling.TypeSystem.Primitive>().Count()}");

        // Check entities
        var entities = domain.Types.OfType<Entity>().ToList();
        Console.WriteLine($"Entities: {entities.Count}");
        foreach (var entity in entities) {
            Console.WriteLine($"  - {entity.Name} (Stages: {entity.Stages.Count}, Actions: {entity.Actions.Count}, Events: {entity.Events.Count})");

            // Check stage actions
            foreach (var stage in entity.Stages) {
                Console.WriteLine($"    Stage '{stage.Name}': {stage.Actions.Count} actions");
                foreach (var action in stage.Actions) {
                    Console.WriteLine($"      - {action.Name}");
                }
            }
        }

        // Check relationships
        var relationships = domain.Relationships.ToList();
        Console.WriteLine($"\nRelationships: {relationships.Count}");
        foreach (var rel in relationships) {
            Console.WriteLine($"  - {rel.Name}: {rel.Source.Name} -> {rel.Target.Name} ({rel.Cardinality})");
        }

        Console.WriteLine("\n=== ASCII Rendering ===\n");
        Console.WriteLine(AsciiDomainRenderer.Render(domain));
    }
}