namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// One load entry. Duplicate library id fails closed; a library loaded via
/// <see cref="SessionBuilder.Load"/> registers into the host so
/// <see cref="SessionBuilder.Build"/> carries the surfaces forward.
/// </summary>
public sealed class DomainPackTests {
    private sealed class TestLibrary : IDomainLibrary {
        private readonly Action<SessionBuilder> _register;

        public TestLibrary(string id, Action<SessionBuilder> register) {
            Id = id;
            _register = register;
        }

        public string Id { get; }

        public void Register(SessionBuilder builder) => _register(builder);
    }

    [Test]
    public async Task Load_DuplicateId_Throws() {
        var library = new TestLibrary("dup", _ => { });
        var host = SessionBuilder.CreateEmpty().Load(new TemporalLibrary());

        host.Load(library);

        var ex = Assert.Throws<InvalidOperationException>(() => host.Load(library));
        await Assert.That(ex!.Message).Contains("dup");
    }

    [Test]
    public async Task Load_RegistersAnnotationsAndTypeMaps() {
        var host = SessionBuilder.CreateEmpty().Load(new TemporalLibrary());

        host.Load(new TestLibrary("maps", builder => {
            builder.Annotations.Register(new ColumnAnnotationSyntax());
            builder.TypeMaps.OverrideSqlColumnType("Text", "TEXT");
        }));

        var built = host.Build();

        await Assert.That(built.Annotations.CanAccept("column")).IsTrue();
        await Assert.That(built.TypeMaps.ToSqlColumnType("Text")).IsEqualTo("TEXT");
    }
}