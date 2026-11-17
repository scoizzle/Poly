using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

using Cat = Poly.Interpretation.Analysis.Semantics.SyntaxTypeCompatibilityAnalyzer.Cat;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Characterization of the retired CLR category table: every type it categorized, and each nullable form, keeps the
/// category, numeric-ness and timestamp-ness it had, now read from the platform's answers
/// (<see cref="ITypeDefinition.PrimitiveType"/>, <see cref="ITypeDefinition.Kind"/>, <see cref="ITypeDefinition.NullableOf"/>).
/// The expectations below are the old table's answers, recorded before it was deleted.
/// </summary>
public class CategoryTableTests {
    private static readonly (Type Type, Cat Category, bool Numeric, bool Timestamp)[] Gold = [
        (typeof(bool), Cat.Boolean, false, false),
        (typeof(char), Cat.Text, true, false),
        (typeof(string), Cat.Text, false, false),
        (typeof(DateTime), Cat.Date, false, true),
        (typeof(DateTimeOffset), Cat.Date, false, true),
        (typeof(DateOnly), Cat.Date, false, false),
        (typeof(TimeOnly), Cat.Date, false, false),
        (typeof(TimeSpan), Cat.Date, false, false),
        (typeof(Guid), Cat.Guid, false, false),
        (typeof(sbyte), Cat.Number, true, false),
        (typeof(byte), Cat.Number, true, false),
        (typeof(short), Cat.Number, true, false),
        (typeof(ushort), Cat.Number, true, false),
        (typeof(int), Cat.Number, true, false),
        (typeof(uint), Cat.Number, true, false),
        (typeof(long), Cat.Number, true, false),
        (typeof(ulong), Cat.Number, true, false),
        (typeof(float), Cat.Number, true, false),
        (typeof(double), Cat.Number, true, false),
        (typeof(decimal), Cat.Number, true, false),
        (typeof(nint), Cat.Number, true, false),
        (typeof(nuint), Cat.Number, true, false),
        (typeof(DayOfWeek), Cat.Enum, false, false),
        (typeof(object), Cat.Unknown, false, false),
        (typeof(Uri), Cat.Unknown, false, false),
        (typeof(KeyValuePair<int, int>), Cat.Unknown, false, false),
        (typeof(byte[]), Cat.Unknown, false, false),
        // Nullable forms, each with the retired table's answer: a nullable char is not numeric, and a nullable
        // timestamp is not a timestamp (the old table looked at the exact type).
        (typeof(bool?), Cat.Boolean, false, false),
        (typeof(char?), Cat.Text, false, false),
        (typeof(int?), Cat.Number, true, false),
        (typeof(long?), Cat.Number, true, false),
        (typeof(decimal?), Cat.Number, true, false),
        (typeof(DateTime?), Cat.Date, false, false),
        (typeof(DateTimeOffset?), Cat.Date, false, false),
        (typeof(Guid?), Cat.Guid, false, false),
        (typeof(DayOfWeek?), Cat.Enum, false, false)
    ];

    [Test]
    public async Task CategoryOf_DefinitionAnswer_MatchesTheRetiredClrTable() {
        foreach (var (subject, category, numeric, timestamp) in Gold) {
            {
                var definition = (ITypeDefinition)ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(subject);
                await Assert.That(SyntaxTypeCompatibilityAnalyzer.CategoryOf(definition)).IsEqualTo(category)
                    .Because($"'{subject}' category");
                await Assert.That(SyntaxTypeCompatibilityAnalyzer.IsNumericLike(definition)).IsEqualTo(numeric)
                    .Because($"'{subject}' numeric-ness");
                await Assert.That(SyntaxTypeCompatibilityAnalyzer.IsTimestamp(definition)).IsEqualTo(timestamp)
                    .Because($"'{subject}' timestamp-ness");
            }
        }
    }
}