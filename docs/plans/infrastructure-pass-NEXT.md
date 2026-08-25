# Infrastructure Pass — Status (complete)

> **Suite complete under current bar.** Historical ladder + reviews:  
> [`archive/infrastructure-pass/README.md`](archive/infrastructure-pass/README.md)

---

## Status

| Field | Value |
|-------|--------|
| **Groups 1–5** | ✅ Done (Bar A IR side-path; fail-closed pipeline) |
| **Group 6** | ✅ Production IR for DbContext + Program — `c5d2220` |
| **G6.5 + G7** | ✅ IR-only `Generate()`; dead string paths removed; structural tests — `b394a0e` |
| **HttpFile** | Still string (intentional) |
| **Bar B / RestApi / StorageAccess** | **Pull only** |

---

## Agent pick

```text
DONE:    Infrastructure pass Groups 1–7 under current bar
CURRENT: (none on this track) — pick product usefulness elsewhere
PULL:    Bar B; RestApiSurfacePass; StorageAccessPass; TransportPass keep/drop; HttpFile IR
```

---

## Pull backlog (do not invent new suite without pain)

| ID | When to pull |
|----|----------------|
| **Bar B** | Need byte-identical anonymous `{ error = }` oracle |
| **RestApiSurfacePass** | Real consumer needs route/DTO analysis pass |
| **StorageAccessPass** | Query/mutation generation beyond current MinimalApi |
| **G6.h1** | Cleaning unused TransportPass |
| **HttpFile IR** | Agents need IR for `.http` emit |

---

## Production path (authoritative)

```csharp
// DslCompiler.GenerateAllFiles — Db + All
new CSharpGenerator().Generate(dbGen.GenerateCompilationUnit());
new CSharpGenerator().Generate(apiGen.GenerateCompilationUnit(dbContextName));
httpGen.Generate(); // string
```

File name: `{domain.Name}DbContext.cs`.
