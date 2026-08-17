using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Packs;
using Poly.DomainModeling.Packs.Temporal;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// One load entry. Duplicate library id fails closed; a library loaded via
/// <see cref="DomainHostBuilder.Load"/> registers into the host so
/// <see cref="DomainHostBuilder.Build"/> carries the surfaces forward.
/// </summary>
public sealed class DomainPackTests {
    private sealed class TestLibrary : IDomainLibrary {
        private readonly Action<DomainHostBuilder> _register;

        public TestLibrary(string id, Action<DomainHostBuilder> register) {
            Id = id;
            _register = register;
        }

        public string Id { get; }

        public void Register(DomainHostBuilder builder) => _register(builder);
    }

    [Test]
    public async Task Load_DuplicateId_Throws() {
        var library = new TestLibrary("dup", _ => { });
        var host = DomainHostBuilder.CreateEmpty().Load(new TemporalLibrary());

        host.Load(library);

        var ex = Assert.Throws<InvalidOperationException>(() => host.Load(library));
        await Assert.That(ex!.Message).Contains("dup");
    }

    [Test]
    public async Task Load_RegistersAnnotationsAndTypeMaps() {
        var host = DomainHostBuilder.CreateEmpty().Load(new TemporalLibrary());

        host.Load(new TestLibrary("maps", builder => {
            builder.Annotations.Register(new ColumnAnnotationSyntax());
            builder.TypeMaps.OverrideSqlColumnType("Text", "TEXT");
        }));

        var built = host.Build();

        await Assert.That(built.Parser.Annotations.CanAccept("column")).IsTrue();
        await Assert.That(built.Analysis.TypeMaps.ToSqlColumnType("Text")).IsEqualTo("TEXT");
    }
}