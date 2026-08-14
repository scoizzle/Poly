using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Packs;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// One load entry. Duplicate library id fails closed; a library loaded via
/// <see cref="DomainHostBuilder.Load"/> registers into the host so
/// <see cref="DomainHostBuilder.Build"/> carries the surfaces forward.
/// </summary>
public sealed class DomainPackTests {
    private sealed class TestLibrary : IDomainLibrary {
        private readonly Action<HostSurfaces> _register;

        public TestLibrary(string id, Action<HostSurfaces> register) {
            Id = id;
            _register = register;
        }

        public string Id { get; }

        public void Register(HostSurfaces surfaces) => _register(surfaces);
    }

    [Test]
    public async Task Load_DuplicateId_Throws() {
        var library = new TestLibrary("dup", _ => { });
        var host = DomainHostBuilder.Create();

        host.Load(library);

        var ex = Assert.Throws<InvalidOperationException>(() => host.Load(library));
        await Assert.That(ex!.Message).Contains("dup");
    }

    [Test]
    public async Task Load_RegistersAnnotationsAndTypeMaps() {
        var host = DomainHostBuilder.Create();

        host.Load(new TestLibrary("maps", surfaces => {
            surfaces.Annotations.Register(new ColumnAnnotationSyntax());
            surfaces.TypeMaps.OverrideSqlColumnType("Text", "TEXT");
        }));

        var built = host.Build();

        await Assert.That(built.Parser.Annotations.CanAccept("column")).IsTrue();
        await Assert.That(built.Analysis.TypeMaps.ToSqlColumnType("Text")).IsEqualTo("TEXT");
    }
}