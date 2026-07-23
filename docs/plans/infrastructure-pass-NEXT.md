# Infrastructure Pass — WHAT TO DO NEXT

> **For agents:** Open this file first.  
> Full ladder: [`infrastructure-pass-task-list.md`](infrastructure-pass-task-list.md)

---

## Status

| Field | Value |
|-------|--------|
| **Group 1** | ✅ Entity Syntax |
| **Group 2** | ✅ Bar A IR side-path (production string `Generate()`) |
| **Groups 3–5** | ✅ **Done** under current bar |
| **3y.1** | ✅ Fail-closed behavior + aggregate (symmetric with storage) |
| **Production IR** | Still string `Generate()` — pull until explicit wire-up |

---

## 3y.1 fix (done)

`DslCompiler` no longer re-analyzes when metadata is missing:

- **Storage** (db/all): throw if null  
- **Behavior** / **aggregate** (all): throw if null  
- Caught as `InvalidOperationException` → `CompileResult.Fail`  
- Generators receive pipeline models only (no `?? new *Analyzer(domain)`)

---

## Follow-up (non-blocking)

| ID | Sev | Item |
|----|-----|------|
| **3y.2** | Low | TransportPass keep-or-drop (unused consumer — doc OK) |
| **3y.3** | Low | CrossReferencePass deferred until consumer |
| **3y.4** | Ops | **Commit** Groups 3–5 working tree |
| **3y.5–.7** | Pull | Production IR; Bar B; RestApiSurfacePass |

---

## Agent pick

```text
DONE:    Groups 1–5 under current bar (incl. 3y.1)
CURRENT: Commit infrastructure pass working tree
PULL:    Production IR wire-up; Bar B; RestApiSurfacePass
```
