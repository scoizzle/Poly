namespace Poly.Tests.Docs;

public class FrozenCoreInstructionTests {
    [Test]
    public async Task AgentsMd_StatesFrozenCore() {
        var agents = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "AGENTS.md"));
        await Assert.That(agents).Contains("## Frozen core");
        await Assert.That(agents).Contains("2026-09-04-frozen-core-pipeline.md");
        await Assert.That(agents).Contains("consumer-specific lowering flag");
        await Assert.That(agents).Contains("LowerStageTransitions");
    }

    [Test]
    public async Task CoreMd_StatesFrozenCoreSection() {
        var core = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "docs", "CORE.md"));
        await Assert.That(core).Contains("## 0. Frozen core");
        await Assert.That(core).Contains("Do not add siblings of `LowerStageTransitions`");
        await Assert.That(core).Contains("2026-09-04-frozen-core-pipeline.md");
    }

    [Test]
    public async Task FrozenCoreAdr_Exists() {
        var adr = Path.Combine(RepoRoot(), "docs", "decisions", "2026-09-04-frozen-core-pipeline.md");
        await Assert.That(File.Exists(adr)).IsTrue();
        var text = await File.ReadAllTextAsync(adr);
        await Assert.That(text).Contains("Frozen (change = platform change)");
        await Assert.That(text).Contains("Current (use; do not reinvent; do not freeze)");
    }

    private static string RepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            var agents = Path.Combine(dir.FullName, "AGENTS.md");
            var core = Path.Combine(dir.FullName, "docs", "CORE.md");
            if (File.Exists(agents) && File.Exists(core))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root with AGENTS.md and docs/CORE.md not found.");
    }
}
