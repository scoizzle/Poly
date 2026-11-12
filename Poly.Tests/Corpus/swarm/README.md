# Swarm repro directory

Drop a `.poly` file here to add it to the shared golden corpus. **No code change
is needed.** `Poly.Tests/TestHelpers/GoldenDomains.cs` globs `*.poly` in this
directory, so a file placed here joins `GoldenDomains.All` on the next test run.

```csharp
foreach (var domain in GoldenDomains.All) { /* ... */ }
```

## How it is picked up

`GoldenDomains.Swarm()` enumerates `Directory.GetFiles(SwarmDirectory, "*.poly")`
at test start and `GoldenDomains.All` is `Compilable` followed by that list. The
directory is resolved from the repository root, so nothing is copied to the test
output and `Poly.Tests.csproj` stays untouched. `GoldenDomainsTests` asserts that
`All` minus `Compilable` equals what is on disk right now, so a hardcoded list
cannot silently go stale.

## Naming

- `<issue>.poly` for a GitHub-issue repro. `GoldenDomains.Swarm(<issue>)` returns
  it by name.
- Anything else: name it after the behaviour it reproduces.

## What belongs here

A minimal domain that reproduces a bug the suite does not cover yet, or a shape
worth keeping as a corpus witness. The file must parse with the session factory
(`DomainSession.ForSource` with the product seed and the sqlite/http catalog);
`GoldenDomainsTests.All_EveryPathExists_AndParsesWithTheSessionFactory` fails
otherwise.

## What does not belong here

- **The golden emit set.** `GoldenDomains.Compilable` is frozen to the ten
  domains `EmitGoldenTests` snapshots. A file here is deliberately *not* in that
  set, so adding one never moves a golden.
- **Probes with no lasting value.** Those live under `docs/probes/` and are
  transient by design.
- **Fixtures that need a hand-edited expectation.** If a repro needs a new golden
  directory, it is a feature change, not a swarm drop.
