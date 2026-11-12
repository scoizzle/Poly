using System.Runtime.CompilerServices;

using Poly.DomainModeling.Compile;

namespace Poly.Benchmarks;

/// <summary>
/// The benchmark classes' shared loader: this checkout's root, one corpus document read with the
/// path in every failure, and the authoring session the invoke and analysis cases are built on. It
/// is not parse-specific, so the name does not say Parse.
/// </summary>
public static class BenchmarkSupport {
    /// <summary>This checkout's root: the parent of the directory holding this file at build time.</summary>
    public static string RepoRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, ".."));

    /// <summary>
    /// Reads <paramref name="fullPath"/> for the corpus member <paramref name="relativePath"/>.
    /// Every failure is an <see cref="InvalidOperationException"/> naming the member and the full
    /// path, so a missing or unreadable document names itself instead of surfacing an OS error.
    /// </summary>
    public static string ReadText(string relativePath, string fullPath) {
        if (!File.Exists(fullPath))
            throw new InvalidOperationException($"Corpus document '{relativePath}' does not exist ({fullPath}).");
        try {
            return File.ReadAllText(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            throw new InvalidOperationException(
                $"Corpus document '{relativePath}' cannot be read ({fullPath}): {exception.Message}", exception);
        }
    }

    /// <summary>Reads a corpus document by repo-relative path and rejects a blank one.</summary>
    public static string ReadDocument(string relativePath) {
        var fullPath = Path.Combine(RepoRoot(), relativePath);
        var text = ReadText(relativePath, fullPath);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"Corpus document '{relativePath}' is empty ({fullPath}).");
        return text;
    }

    /// <summary>
    /// The session the invoke and analysis cases are built on: the define gate's authoring
    /// pipeline (<see cref="DomainSession.ForAuthoringSource"/>) with
    /// <see cref="ExtensionCatalog.ProductAuthoring"/> as the seed when a document lists no
    /// <c>uses</c>. Every failure names the document path.
    /// </summary>
    public static DomainSession AuthoringSession(string text, string relativePath) {
        try {
            return DomainSession.ForAuthoringSource(text, ExtensionCatalog.ProductAuthoring);
        }
        catch (Exception exception) {
            throw new InvalidOperationException(
                $"Corpus document '{relativePath}' does not load ({Path.Combine(RepoRoot(), relativePath)}): {exception.Message}",
                exception);
        }
    }
}