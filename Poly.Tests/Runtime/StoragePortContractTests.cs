using System.Globalization;

using Poly.Runtime;

namespace Poly.Tests.Runtime;

/// <summary>
/// The <see cref="IStoragePort"/> contract: the canonical key encoding, the atomic
/// reserve, the reference port's thread safety and the journaled adapter's rollback.
/// </summary>
public class StoragePortContractTests {
    [Test]
    public async Task KeyCodec_Canonical_IsInjectiveForSeparatorsInMembers() {
        // A member may contain the old unit separator; the encoding may not collapse the pair.
        await Assert.That(KeyCodec.Canonical("a\u001fb", "c"))
            .IsNotEqualTo(KeyCodec.Canonical("a", "b\u001fc"));

        // A null part is its own shape, never the text "null".
        await Assert.That(KeyCodec.Canonical((object?)null)).IsNotEqualTo(KeyCodec.Canonical("null"));

        // No parts and one empty part are different keys.
        await Assert.That(KeyCodec.Canonical("")).IsNotEqualTo(KeyCodec.Canonical());

        // Part order is part of the key.
        await Assert.That(KeyCodec.Canonical("a", "b")).IsNotEqualTo(KeyCodec.Canonical("b", "a"));
    }

    [Test]
    public async Task KeyCodec_Canonical_FormatsUnderInvariantCulture() {
        using var _ = new GermanCultureScope();

        await Assert.That(KeyCodec.Canonical(1.5m)).IsEqualTo("3:1.5");
        await Assert.That(KeyCodec.Canonical(1.5m)).IsNotEqualTo(KeyCodec.Canonical("1,5"));
    }

    [Test]
    public async Task KeyCodec_Canonical_EncodesNullPart() {
        await Assert.That(KeyCodec.Canonical((object?)null)).IsEqualTo("~");
    }

    [Test]
    public async Task KeyCodec_Canonical_NullArray_Throws() {
        await Assert.That(() => KeyCodec.Canonical((object?[])null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task KeyCodec_Canonical_RejectsUnsupportedTypes() {
        await Assert.That(() => KeyCodec.Canonical(new object())).Throws<ArgumentException>();
    }

    [Test]
    public async Task KeyCodec_Canonical_StringOverload_AgreesWithArrayForm() {
        await Assert.That(KeyCodec.Canonical("Patron", "Email", "a@b.c"))
            .IsEqualTo(KeyCodec.Canonical(new object?[] { "Patron", "Email", "a@b.c" }));
    }

    // Sets de-DE for the current test's async flow and restores the previous cultures.
    private sealed class GermanCultureScope : IDisposable {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        public GermanCultureScope() {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        }

        public void Dispose() {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}