# Product-surface claims audit — 2026-08-10

**Kind:** Verification pass — walk the DSL guide's *shipped* surface and confirm each construct has an end-to-end path (parse → analyze → runtime → export → DslCompiler).
**Result:** All documented constructs have working paths. Two were **thin (no runtime-execution test)**; both locked with tests. No "documented but unsupported" gaps found.

---

## Coverage matrix (shipped surface)

| Construct | Parse | Analyze | Runtime | Export | DslCompiler |
|-----------|:-----:|:-------:|:-------:|:------:|:-----------:|
| Entities / properties / constraints | ✅ | ✅ | ✅ | ✅ | ✅ |
| Enum types | ✅ | ✅ | ✅ | ✅ | ✅ |
| Navs (one / many / owned) | ✅ | ✅ | ✅ | ✅ | ✅ |
| Stages + transition | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Entry/exit effects** | ✅ | ✅ | ✅ *(was untested)* | ✅ | — |
| Actions: params, require gates | ✅ | ✅ | ✅ | ✅ | ✅ |
| Action return `-> Entity` | ✅ | ✅ | ✅ | ✅ | — |
| Effects: assign / transition / create / create in (auto-wire) | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Invoke: cross-entity with param binding** | ✅ | ✅ | ✅ *(was untested)* | ✅ | — |
| Invoke: any/all/where | ✅ | ✅ | ✅ | ✅ | — |
| Conditional `if / else if / else` | ✅ | ✅ | ✅ | ✅ | — |
| Policies: local / path-prefix / exists | ✅ | ✅ | ✅ | —* | — |
| Policies: quantifiers any / all / none / count | ✅ | ✅ | ✅ | —* | — |
| Subscriptions: stage + entity-level, quantifiers, peer binder | ✅ | ✅ | ✅ | ✅ | — |
| Annotations `column` / `table` | ✅ | — | — | — | ✅ (packs) |
| Full DslCompiler solution (entities + DbContext + MinimalApi + demo.http) | — | — | — | — | ✅ compile oracle |

\* Store-aware policy forms (path-prefix / quantifiers / exists) throw `NotSupportedException` in the C# export — by design (they require the store). Not a gap.

## Gaps found + closed (2026-08-10)

1. **Entry/exit effects had no runtime-execution test** (only parse/evolution coverage). The runtime executes them (`DomainEntityInstance.cs:677-699`), but nothing asserted the `assign` lands.
   - **Test added:** `InvokeAction_StageTransition_RunsExitThenEntryEffects` — transitions Draft→Active→Done and asserts the exit/entry assignments.
2. **Cross-entity invoke with a param binding had only parse coverage.** The runtime supports it (`ExecuteInvokeEffect` → `target.InvokeAction(name, chainedArgs)`).
   - **Test added:** `InvokeAction_CrossEntity_WithParameterBinding_PassesArgs` — `invoke service.Process(message: "hi")` propagates the arg to the linked target.

## Findings worth noting

- **Action args are injected into the instance bag** (`DomainEntityInstance.cs:238-245`); params read as property access. `DomainExpression.Parameter` (VM parameter slot) is **not** the action-arg surface — a subtle API trap for programmatic IR authors (surfaced while writing the cross-entity test).
- The two compile oracles (export + DslCompiler full solution) now require **warning-free** output — this surfaced and fixed CS8618, CS8625, and a real MinimalApi generator CS0168 (`catch (Exception ex)` unused).
