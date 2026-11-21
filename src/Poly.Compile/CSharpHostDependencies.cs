namespace Poly.Compile;

/// <summary>
/// What printed C# needs from the project that compiles it (#490): a Poly.Runtime reference for
/// the result family and the storage port, and EF Core when a generated DbContext is included.
/// </summary>
public static class CSharpHostDependencies {
    /// <summary>
    /// The references a successful C# export needs, naming the DbContext file when one is among
    /// <paramref name="fileNames"/>. Names may be bare file names or paths.
    /// </summary>
    public static string Requirement(IEnumerable<string> fileNames) {
        var dbContext = fileNames
            .Select(Path.GetFileName)
            .FirstOrDefault(name => name is not null && name.EndsWith("DbContext.cs", StringComparison.Ordinal));
        return dbContext is null
            ? "Poly.Runtime"
            : $"Poly.Runtime (and Microsoft.EntityFrameworkCore for {dbContext})";
    }
}