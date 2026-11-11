using System.Reflection;

using ModelContextProtocol.Server;

using Poly.Mcp.Surface;

namespace Poly.Tests.Mcp;

public class SurfaceRegistrationTests {
    private static readonly Assembly McpAssembly = typeof(PolySurfaceTools).Assembly;

    [Test]
    public async Task OnlyPolySurfaceTools_IsMcpServerToolType_WhenTheAssemblyIsScanned() {
        var toolTypes = McpAssembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .Select(t => t.FullName)
            .ToList();

        await Assert.That(toolTypes).IsEquivalentTo(new[] { typeof(PolySurfaceTools).FullName });

        var strayTools = McpAssembly.GetTypes()
            .Where(t => t != typeof(PolySurfaceTools))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        await Assert.That(strayTools).IsEmpty();
    }
}