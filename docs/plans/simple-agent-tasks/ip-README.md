# Infrastructure Pass — Simple-Agent Queue (`ip-*`)

**Parent:** [`../infrastructure-pass-NEXT.md`](../infrastructure-pass-NEXT.md)  
**Full ladder:** [`../infrastructure-pass-task-list.md`](../infrastructure-pass-task-list.md)  
**Suite design:** [`../infrastructure-concern-analyzer-suite.md`](../infrastructure-concern-analyzer-suite.md)

---

## Rules

1. **One micro-task at a time.** Do not open Bar B or RestApiSurface to “finish” Group 6.
2. Read **Required Reading** only — skip the full suite design unless G6.0.
3. **Bar A renorms stay legal** for production IR (see task-list renorm table). Do not “fix” IR to match anonymous objects this round.
4. Pre-ship gate before `[x]` on Group 6: [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md).
5. Prefer **smallest vertical**: DbContext first, then MinimalApi, then compiler smoke.

---

## Current

```text
DONE:    G6 product bar (IR wire-up + AllMode smoke) — working tree
CURRENT: Commit G6 batch (exclude demo.http / library.db)
THEN:    Optional G6.5; G6.h1; next product work
PULL:    Bar B; RestApiSurfacePass; StorageAccessPass
```

**Review:** parent [`../infrastructure-pass-NEXT.md`](../infrastructure-pass-NEXT.md) **§ Review G6′**

---

## Group 6 — Production IR wire-up

| # | Task | File | Status | Difficulty |
|---|------|------|--------|------------|
| **G6.0** | Inventory call sites + tests | [`ip-g6-0-inventory.md`](ip-g6-0-inventory.md) | `[x]` | Small |
| **G6.1** | DbContext production → IR | [`ip-g6-1-dbcontext-production-ir.md`](ip-g6-1-dbcontext-production-ir.md) | `[x]` uncommitted | Medium |
| **G6.2** | MinimalApi production → IR | [`ip-g6-2-minimalapi-production-ir.md`](ip-g6-2-minimalapi-production-ir.md) | `[x]` uncommitted | Medium |
| **G6.3** | DslCompiler smoke Db + All | [`ip-g6-3-compiler-ir-smoke.md`](ip-g6-3-compiler-ir-smoke.md) | `[x]` AllMode green | Small |
| **G6.4** | Plan/docs honesty after wire-up | [`ip-g6-4-plan-honesty.md`](ip-g6-4-plan-honesty.md) | `[~]` finalize on commit | Small |
| **G6.5** | Optional: string Generate → IR delegate | [`ip-g6-5-generate-delegates-ir.md`](ip-g6-5-generate-delegates-ir.md) | `[ ]` pull | Low |
| **G6.6** | Update Program.cs usage text | (inline) | `[x]` uncommitted | Small |
| **G6.R / G6′** | Review residuals | NEXT § Review G6′ | `[~]` commit + hygiene | Small |
| **Gate** | Product proof | AllMode green | `[x]` product; ⬜ commit | Process |

**Exit:** Product bar met. **Full exit = commit** (no demo.db).

---

## Optional hygiene (parallel; do not block G6.1–G6.3)

| ID | Work | Status |
|----|------|--------|
| **G6.h1** | TransportPass keep-or-drop (doc only OK) | `[ ]` |
| **G6.h2** | CrossReferencePass still deferred (no code) | n/a |

---

## Do not pick

| Item | Why |
|------|-----|
| Bar B anonymous-object oracle | Separate suite; needs Syntax growth |
| RestApiSurfacePass / StorageAccessPass | No production consumer this round |
| Re-decompose analysis pipeline | Groups 3–5 closed |
| Query Q4 / dates / link_instances | Different track (`qe-*`) |

---

## Session sketch

| Session | Tasks | Outcome |
|---------|-------|---------|
| **1 — Map** | G6.0 | Call-site table; risk notes |
| **2 — DbContext** | G6.1 | Production Db file from IR |
| **3 — API** | G6.2 | Production Program.cs from IR |
| **4 — Prove** | G6.3 + Gate | Compiler smoke + suite |
| **5 — Close** | G6.4 (+ optional G6.5) | Docs honest |

---

## Principles (always)

- Domain fidelity + CORE seams (`docs/CORE.md`)  
- Tests more specific; production more generic  
- Smallest coherent path that proves production IR  
- Fail-closed: do not reintroduce `?? new *Analyzer` fallbacks  
