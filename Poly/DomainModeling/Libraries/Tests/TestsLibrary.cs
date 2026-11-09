using Poly.DomainModeling.Compile;

namespace Poly.DomainModeling.Libraries.Tests;

/// <summary>
/// Product door: <c>uses tests</c> registers <see cref="TestsArtifactContributor"/>, which adds
/// <c>test-tree</c> and <c>test-file</c> artifacts to the Emit catalog. Nothing is lowered
/// differently and nothing is added to the Emit file list.
/// </summary>
public sealed class TestsLibrary : IDomainLibrary {
    public const string LibraryId = "tests";

    public string Id => LibraryId;

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddArtifactContributor(new TestsArtifactContributor());
    }
}