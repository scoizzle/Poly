# Micro-Task: WS8 A+ polish

**Parent**: WS8 / WP5 (A+ package)  
**Suite:** [`ws8-README.md`](ws8-README.md) **#10**  
**Difficulty**: Small  
**Estimated Tokens**: ~3k  
**Status**: [ ] Not Started  
**Depends on**: Prefer after #8–#9; can do small items anytime

## Objective

Close non-blocking nits so the scorecard is clean.

## Exact Steps (pick all that still apply)

1. Rename `V3EvalTool` → something accurate (`V3PolicyTool`) if tools are inspect + evaluate.
2. One-line in `Poly/DomainModeling/README.md`: policies evaluate with `PolicyEvaluator.Evaluate` (VM).
3. Strengthen smoke asserts on structured `data` fields (`expression`, `result`, `policyName`).
4. Remove dead/confusing setup in older smoke tests (e.g. duplicate `AddEntity` no-ops).
5. Sync `ws8-README.md` + `simple-agent-tasks/README.md`: A+ tasks Done; residuals cleared.
6. Ensure WS8 code is committed with the feature work (orchestrator/human may own commit).
7. Confirm spike test counts and `policy-sample-subject.md` match code after #6b/#6c.
8. Domain-attached tests: optional assert evolve/configure success if API allows.

## Verification

- [ ] Naming/docs match behavior
- [ ] READMEs not claiming false Done/In Progress
- [ ] Build + relevant smokes green

## Out of Scope

- New DE features
- Contract codegen
