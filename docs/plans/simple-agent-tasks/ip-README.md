# Infrastructure Pass — Simple-Agent Queue (`ip-*`)

**Parent:** [`../infrastructure-pass-NEXT.md`](../infrastructure-pass-NEXT.md)  
**Full ladder:** [`../infrastructure-pass-task-list.md`](../infrastructure-pass-task-list.md)  
**Suite design:** [`../infrastructure-concern-analyzer-suite.md`](../infrastructure-concern-analyzer-suite.md)

---

## Rules

1. **One micro-task at a time.** Do not open Bar B or RestApiSurface to “finish” Group 6.
2. Read **Required Reading** only — skip the full suite design unless G6.0.
3. **Bar A renorms stay legal** for production IR (see task-list renorm table). Do not “fix” IR to match anonymous objects this round.
4. Pre-ship gate: [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md).
5. Prefer **smallest vertical**: DbContext first, then MinimalApi, then compiler smoke.

---

## Current

```text
DONE:    G6.5 + G7 product bar (IR-only emit, dead string gone, tests green) — uncommitted
CURRENT: Commit G6.5+G7 batch (product + tests + plans)
THEN:    Post-suite; optional MaxLength Constant 50 (G7′′.1)
PULL:    Bar B; RestApiSurfacePass; StorageAccessPass; G6.h1
```

**Review:** parent [`../infrastructure-pass-NEXT.md`](../infrastructure-pass-NEXT.md) **§ Review G7′′**

---

## Group 6 — Production IR wire-up ✅ (`c5d2220`)

| # | Task | Status |
|---|------|--------|
| G6.0–G6.4, G6.6 | Production IR + smoke + CLI | `[x]` committed |
| **G6.5** | string Generate → IR delegate | `[x]` code; ⬜ commit |

---

## Group 7 — Structural IR + G6.5 cleanup ✅ product / ⬜ commit

| # | Work | Status |
|---|------|--------|
| **G7** | `GenerationAssertions` + suite refactor | `[x]` |
| **G7′.1** | Dead MinimalApi `Append*(StringBuilder)` deleted | `[x]` |
| **G7′.2** | Dead DbContext `EscapeCSharpString` / usings removed | `[x]` |
| **G7′.3–.4** | `GetFluentChain` used by RequiredColumn test | `[x]` |
| **G7′.6** | Class doc filename fixed | `[x]` |
| **Product proof** | 11+24+AllMode green; build clean | `[x]` |
| **Commit** | **G7′′.4** | `[ ]` |

**Exit:** One real emit path + structural suite **committed**.

---

## Optional hygiene (parallel; do not block)

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

## Principles (always)

- Domain fidelity + CORE seams (`docs/CORE.md`)  
- Tests more specific; production more generic  
- Smallest coherent path that proves production IR  
- Fail-closed: do not reintroduce `?? new *Analyzer` fallbacks  
