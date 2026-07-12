# Micro-Task: Spike doc — demote Reflection.Emit until proven

**Parent**: WS8 / WP5 (A+ package)  
**Suite:** [`ws8-README.md`](ws8-README.md) **#6c**  
**Difficulty**: Small (docs + optional mini-test)  
**Estimated Tokens**: ~3–5k  
**Status**: [x] **Done** — spike doc updated: primary path = non-nullable sealed record (proven ✅). Reflection.Emit = secondary/unproven. Dict/Expando invariant documented.

## Objective

Align the spike **recommendation** with what was **measured**. Do not tell `evaluate_policy` implementers to use Reflection.Emit as the primary path until a test proves it.

## Code review finding

`docs/plans/v2-to-v3/spikes/policy-sample-subject.md` recommends **Reflection.Emit** as the MCP approach, but **no Emit test exists**. Proven paths: anonymous/sealed records, non-nullable property bags (`StrictBag`), non-null `int?`.

## Exact Steps

1. Edit `spikes/policy-sample-subject.md`:
   - **Primary recommendation for #8:** non-nullable CLR property bag (`StrictBag`-style) or entity-shaped sealed type with defaults for missing keys — **proven**.
   - **Secondary / future:** Reflection.Emit dynamic type — mark **unproven**; only after a green test.
   - Explicit invariant: **never** use `Dictionary<string,object>` or `ExpandoObject` as PolicyEvaluator subjects.
2. Optional but preferred: add `ReflectionEmit_GeneratedType_PropertyAccess_Works` spike test:
   - Emit a type with `int Age` property, set 25/15, evaluate `Age >= 18` on VM via Constant subject or `CompileVMPredicate` if open generic allows.
   - If Emit works, promote it to co-primary in the spike doc.
   - If Emit fails, document and keep StrictBag primary.
3. Update `ws8-spike-policy-sample-subject.md` status notes if they still say “Recommendation: Reflection.Emit for MCP” as sole answer.
4. Point `ws8-mcp-evaluate-policy-vm.md` implementers at the **revised** spike doc (see that task’s Depends on).

## Verification

- [ ] Spike doc primary path = proven approach
- [ ] Emit either tested green or explicitly “unproven”
- [ ] Invariant: no Dict/Expando as product subjects

## Out of Scope

- Full MCP evaluate_policy implementation (task #8)
