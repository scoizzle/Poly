# COH suite gate

**Suite:** [`coh-README.md`](./coh-README.md)  
**Status:** `[x]` — PASSED 2026-08-06

| ID | Check | Status |
|----|--------|--------|
| G1 | COH-0 ownership locks recorded | `[x]` — coh-0-design-locks.md notes: per-chain file ownership, no multi-project, behavior-preserving only |
| G2 | Runtime types moved or explicit block noted with residual plan | `[x]` — DomainEntityInstance/DomainInstanceStore/InvocationResult `git mv` → `Poly/DomainModeling/Runtime/`, namespace unchanged (zero usings churn); docs (README, domain-execution-model.md, PROJECT-SUMMARY) updated in same change; AGENTS placement table coarse row still holds |
| G3 | DE dual rewrites consolidated onto dispatch (or residual LOC justified) | `[x]` — BindPeerInExpression → PeerBindingRewrite, PreprocessQuantifiers → QuantifierPreprocessRewrite on shared DomainExpressionRewriteBase (leaf overrides, composites recurse in base, Default() throws NotSupportedException fail-loud) |
| G4 | Effect analysis dispatch progress landed | `[x]` — EffectAnalyzer.ValidateEffect switch → EffectValidationDispatch (11 typed overrides, Default() => null matching prior no-op default) |
| G5 | Evolution helpers landed with tests still green | `[x]` — AppendChildToEntity/AppendChildToStage/AppendChildToAction; 11 ApplyTo sites routed; RequireUpdate fail-loud preserved; 1855/1855 |
| G6 | Build + suite green; CORE/DomainModeling README placement if layout changed | `[x]` — build 0 errors, 1855/1855 green after each chain and final V1; DomainModeling README directory table has Runtime/ row; docs/CORE.md §3.4/3.5 path refs verified unaffected (types still under `Poly/DomainModeling/`) |
