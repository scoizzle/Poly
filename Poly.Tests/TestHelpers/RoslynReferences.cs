using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// The suite's Roslyn reference sets, each built once per test process. A <see cref="MetadataReference"/> owns the
/// metadata Roslyn reads from its file, so compilations that share reference instances share that metadata; a set built
/// per compilation re-reads every assembly (about 270 for the platform) each time. On 2026-11-16 the per-call sets cost
/// the full suite 6.5 GB of peak RSS against 2.7 GB with shared ones. <c>RoslynReferenceReuseTests</c> keeps every
/// compilation on these.
/// </summary>
public static class RoslynReferences {
    private static readonly Lazy<MetadataReference[]> _platform = new(static () => [.. TrustedPlatformAssemblies().Select(Reference)]);

    private static readonly Lazy<MetadataReference[]> _core = new(static () => [.. TrustedPlatformAssemblies()
        .Where(static path => Path.GetFileName(path) is "System.Private.CoreLib.dll" or "System.Runtime.dll")
        .Select(Reference)]);

    private static readonly Lazy<MetadataReference[]> _platformAndTestOutput = new(static () => [.. TrustedPlatformAssemblies()
        .Concat(Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        .Distinct(StringComparer.Ordinal)
        .Where(File.Exists)
        .Select(Reference)]);

    private static readonly Lazy<MetadataReference[]> _host = new(BuildHost);

    private static readonly ConcurrentDictionary<Assembly, MetadataReference> _assemblies = new();

    /// <summary>Every trusted platform assembly of the test process: the .NET shared framework and the test's own dependencies.</summary>
    public static IReadOnlyList<MetadataReference> Platform => _platform.Value;

    /// <summary><c>System.Private.CoreLib</c> and <c>System.Runtime</c> only, for an oracle that must bind against the core library alone.</summary>
    public static IReadOnlyList<MetadataReference> Core => _core.Value;

    /// <summary><see cref="Platform"/> and every assembly in the test output folder.</summary>
    public static IReadOnlyList<MetadataReference> PlatformAndTestOutput => _platformAndTestOutput.Value;

    /// <summary>
    /// What a generated ASP.NET Core and EF host compiles against: the platform, the ASP.NET Core shared framework of the
    /// process's runtime version, and the test output (EF Core and its dependencies), first by file name wins.
    /// </summary>
    public static IReadOnlyList<MetadataReference> Host => _host.Value;

    /// <summary>The reference for a loaded assembly, one instance per assembly.</summary>
    public static MetadataReference Of(Assembly assembly) =>
        _assemblies.GetOrAdd(assembly, static a => Reference(a.Location));

    /// <summary>
    /// Unloads the collectible context <paramref name="assembly"/> was loaded into, once the caller holds nothing from it.
    /// An assembly in a collectible context stays loaded until <see cref="AssemblyLoadContext.Unload"/> is called.
    /// </summary>
    public static void Unload(Assembly assembly) {
        if (AssemblyLoadContext.GetLoadContext(assembly) is { IsCollectible: true } context)
            context.Unload();
    }

    private static IEnumerable<string> TrustedPlatformAssemblies() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

    private static MetadataReference Reference(string path) => MetadataReference.CreateFromFile(path);

    private static MetadataReference[] BuildHost() {
        var byName = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        void AddFrom(string path) {
            var name = Path.GetFileName(path);
            if (!byName.ContainsKey(name) && File.Exists(path))
                byName[name] = Reference(path);
        }

        // 1. .NET shared framework.
        foreach (var path in TrustedPlatformAssemblies())
            AddFrom(path);

        // 2. ASP.NET Core shared framework (WebApplication, routing, http) — the version
        // matching the process runtime. Scanning ALL installed versions (the machine may
        // also hold 8.0/9.0 runtimes) makes the reference set depend on directory
        // enumeration order: an old facade can win the by-name dedupe and emit CS1701
        // against the 10.0 references (seen on ubuntu-latest, which preinstalls 8.0).
        var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location);          // .../Microsoft.NETCore.App/<ver>
        var sharedDir = coreDir is not null ? Path.GetDirectoryName(Path.GetDirectoryName(coreDir)) : null; // .../shared
        var aspNetCoreRoot = sharedDir is not null
            ? Path.Combine(sharedDir, "Microsoft.AspNetCore.App")
            : null;
        var aspNetCoreDir = ResolveSharedFrameworkVersion(aspNetCoreRoot, Path.GetFileName(coreDir));
        if (aspNetCoreDir is not null && Directory.Exists(aspNetCoreDir))
            foreach (var path in Directory.GetFiles(aspNetCoreDir, "*.dll", SearchOption.AllDirectories))
                AddFrom(path);

        // 3. EF Core + transitive deps from the test output.
        foreach (var path in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
            AddFrom(path);

        return [.. byName.Values];
    }

    /// <summary>Picks the ASP.NET Core shared-framework version dir matching the process
    /// runtime version, falling back to the highest installed GA version.</summary>
    internal static string? ResolveSharedFrameworkVersion(string? aspNetCoreRoot, string? coreVersion) {
        if (aspNetCoreRoot is null || !Directory.Exists(aspNetCoreRoot))
            return null;

        var versionDirs = Directory.GetDirectories(aspNetCoreRoot)
            .Select(Path.GetFileName)
            .Where(v => v is not null && Version.TryParse(v, out _))
            .Select(v => Version.Parse(v!))
            .OrderByDescending(v => v)
            .ToList();
        if (versionDirs.Count == 0) return null;

        if (coreVersion is not null && Version.TryParse(coreVersion, out var exact)) {
            var exactDir = versionDirs.FirstOrDefault(v => v == exact);
            if (exactDir is not null)
                return Path.Combine(aspNetCoreRoot, exactDir.ToString());
        }
        return Path.Combine(aspNetCoreRoot, versionDirs[0].ToString());
    }
}