# create/create-in — Agent Queue (`create-create-in-*`)

**Parent:** [`../create-create-in-simulate.md`](../create-create-in-simulate.md)  
**Language:** [`../../decisions/2026-09-03-facts-concerns-bags-store-bind.md`](../../decisions/2026-09-03-facts-concerns-bags-store-bind.md)  
**Gate:** [`create-create-in-gate.md`](./create-create-in-gate.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)  
**Guide:** `Poly.Mcp/Docs/poly-dsl-guide.md`

**Status:** CURRENT. Authority: [`PIPELINE-STATUS.md`](./PIPELINE-STATUS.md). Unique Store bind already shipped. Remaining work is simulate = lowered program + bound Store.

Do not invent a second CURRENT. Do not admit mut-safety or dict-sqlite beside this.

---

## Objective

1. Create / create-in lower to Store jobs in generic Syntax (same pattern as `EnsureUnique`).
2. Simulate runs that tree through `Interpreter` — no Effect-IR walk.
3. `This` stays Interpretation’s dictionary-backed type-def subject (`IDictionary<string, object>`). No Expando. No third instance type.
4. MCP `create_instance` / `invoke_action` bind Store and run the cached program.

### Locks

See parent L1–L10. Short form:

- Facts / bags / Store / bind / lower / project — do not invent a framework catalog.
- Tree has no bag types. Lowering process may read bags.
- Notify-shaped Store jobs (`this.Create`, `this.CreateIn`) because dictionary `This` cannot Member-read `Store`.
- If emit is hard, the host surface is wrong — bind Store, do not add a lowering flag.

---

## Task order

| ID | File | Size | Status |
|----|------|------|--------|
| **1** | [`create-create-in-1-store-create.md`](./create-create-in-1-store-create.md) | M | `[ ]` |
| **2** | [`create-create-in-2-one-tree.md`](./create-create-in-2-one-tree.md) | M | `[ ]` |
| **3** | [`create-create-in-3-unify-factories.md`](./create-create-in-3-unify-factories.md) | M | `[ ]` |
| **4** | [`create-create-in-4-store-reads.md`](./create-create-in-4-store-reads.md) | M | `[ ]` |
| **5** | [`create-create-in-5-mcp-simulate.md`](./create-create-in-5-mcp-simulate.md) | S | `[ ]` |
| **G** | [`create-create-in-gate.md`](./create-create-in-gate.md) | S | `[ ]` |

Start only the next `[ ]` whose prereq is `[x]`. One failing TUnit test before production edits. File ownership is exclusive.

---

## Done definition

Parent **Done** checklist + gate green + PIPELINE-STATUS Agent pick updated in the **same** change as the gate.
