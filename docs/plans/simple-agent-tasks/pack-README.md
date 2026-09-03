# Pack host — Fleet queue (`pack-*`)

**Parent:** [`../pack-host-2026-08-13.md`](../pack-host-2026-08-13.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)  
**Guide:** `Poly.Mcp/Docs/poly-dsl-guide.md` (core-only until 3a)  
**Sequence:** DSL Grammar → pack surface → built-in packs  

**Status:** Parked — not CURRENT. Phase 1 shipped; extension model superseded. Authority: [`PIPELINE-STATUS.md`](./PIPELINE-STATUS.md).

---

## Copy-paste agent prompt

```text
Read docs/plans/simple-agent-tasks/pack-README.md and the task file you were assigned.
Write Claimed by on that task file BEFORE editing code.
Follow Exact steps. File ownership is exclusive. One failing TUnit test before production edits.
Do not add IExpressionPrintForm. Do not invent DslLayout. Do not add import keyword.
Do not re-add Link/Unlink/Delete effects. Do not extract DomainToCSharpExporter.
Verify with the task's filter first, then:
  dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
  dotnet run --project Poly.Tests/Poly.Tests.csproj
Mark the task [x] and the slice README table. Stop at the slice gate for pr1.
```

## How to dispatch (opencode)

From repo root. `--auto` is required (unattended). One agent per task file.

```bash
opencode run --dir . --auto --title pack-2-1 -- \
  "You are fleet agent pack-2-1. $(cat docs/plans/simple-agent-tasks/pack-README.md | head -20)
   Assigned task: docs/plans/simple-agent-tasks/pack-2-1-idomainpack.md
   Do only that task. Claim it, implement, verify, mark [x]."
```

pack-1 (TokenWriter + binders) is archived under [`../archive/completed-2026-08-late/simple-agent-tasks/`](../archive/completed-2026-08-late/simple-agent-tasks/pack-1-README.md).

Start only tasks in the **current wave** whose prereqs are `[x]`.

### Wave DAG

```text
Phase 1 — DSL Grammar
  Wave A (2 agents, no overlap)
    pack-1-1 token writer     Grammar Printer + ITokenWriter + DslTokenWriter
    pack-1-2 print binder     NEW binder registry only
  Wave B (after A)
    pack-1-3 dsl printer      DomainDslPrinter uses binder + Printer; kill ?Type
    pack-1-4 e1 patterns      (after 1-3) E1 MAGIC/N unit as patterns + round-trip
    pack-1-gate

Phase 2 — Pack surface (after phase 1 gate)
  Wave C
    pack-2-1 IDomainPack      NEW host types + DomainInputBuilder.AddPack
  Wave D (after 2-1; 3 agents)
    pack-2-2 sqlite
    pack-2-3 sqlserver
    pack-2-4 mysql
  Wave E (after 2-2 at least; 2-3/2-4 may still be in flight if compiler stays generic)
    pack-2-5 compiler         DslCompiler PackSet
    pack-2-6 mcp              session parse/print share PackSet
    pack-2-gate

Phase 3 — Built-in packs (after phase 2 gate; one sub-slice at a time)
  3a  existing p1-* suite + pack-3a-print-roundtrip
  3b  pack-3b-* InternalDomain producer
  3c  pack-3c-* root artifacts + bind-as-call
```

| Agent | Assign |
|-------|--------|
| A | [`pack-1-1-token-writer.md`](./pack-1-1-token-writer.md) |
| B | [`pack-1-2-print-binder.md`](./pack-1-2-print-binder.md) |
| C | [`pack-1-3-dsl-printer.md`](./pack-1-3-dsl-printer.md) after A+B |
| D | [`pack-1-4-e1-patterns.md`](./pack-1-4-e1-patterns.md) after C |
| E | [`pack-2-1-idomainpack.md`](./pack-2-1-idomainpack.md) after phase-1 gate |
| F/G/H | 2-2 / 2-3 / 2-4 after E |
| I | 2-5 after F |
| J | 2-6 after E |
| K | [`p1-README.md`](./p1-README.md) after phase-2 gate |
| L | [`pack-3b-README.md`](./pack-3b-README.md) after 3a |
| M | [`pack-3c-README.md`](./pack-3c-README.md) after 3b |

---

## Locks (every agent)

1. Parser and printer are one Grammar seam. No `IExpressionPrintForm`.
2. Whitespace is `DslTokenWriter`, inverse of `DslTokenReader.SkipWhitespaceAndComments`.
3. Packs do not add core keywords. Producers emit `ImportedContract`.
4. `DomainToCSharpExporter` stays in core.
5. Fail closed: unprintable IR, duplicate pack id / keyword / pass name.
6. Tests first. TUnit names `Method_Condition_ExpectedResult`.
