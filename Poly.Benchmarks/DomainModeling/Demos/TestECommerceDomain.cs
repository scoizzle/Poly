using System;
using System.Linq;

using Poly.Benchmarks.DomainModeling;
using Poly.Data.Modeling;

namespace Poly.Benchmarks.DomainModeling.Demos;

internal static class TestECommerceDomain {
    public static void Run() {
        var domain = ECommerceDomain.BuildECommerceDomain();

        Console.WriteLine("=== E-Commerce Domain Verification ===\n");
        Console.WriteLine($"Entities: {domain.Types.OfType<Entity>().Count()}");
        Console.WriteLine($"Imported contracts: {domain.GetAvailableImportedContracts().Count()}");
        Console.WriteLine($"Contract bindings: {domain.GetAvailableContractBindings().Count()}");

        foreach (var contract in domain.GetAvailableImportedContracts()) {
            Console.WriteLine($"  - Contract: {contract.Name} ({contract.SourceIdentifier} @ {contract.Version})");
            foreach (var endpoint in contract.Endpoints) {
                Console.WriteLine($"    Endpoint: {endpoint.Name} [{endpoint.Direction}] Payload={endpoint.PayloadType.Name}");
            }
        }

        foreach (var binding in domain.GetAvailableContractBindings()) {
            Console.WriteLine($"  - Binding: {binding.Name} -> {binding.LocalAction.Entity?.Name}.{binding.LocalAction.Name}({binding.LocalParameterName})");
            foreach (var map in binding.FieldMaps) {
                Console.WriteLine($"    Map: {map.RemoteFieldName} -> {map.LocalFieldName}");
            }
        }

        Console.WriteLine("\n=== ASCII Rendering ===\n");
        Console.WriteLine(AsciiDomainRenderer.Render(domain));
    }
}