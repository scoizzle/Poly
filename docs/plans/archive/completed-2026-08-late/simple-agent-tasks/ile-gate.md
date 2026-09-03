# ile-gate

**Depends on:** ile-0 … ile-3  
**Status:** DONE 2026-08-31 (F1–F5 closed)

- [x] Uncommitted review gate (`docs/plans/v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`).
- [x] Phenomenal-review 2026-08-31; F1–F5 closed on this branch.
- [x] CORE + Interpretation README match code (14 analysis passes; `Interpreter.Compile` fail-closed; Await / unresolved ParameterReference / Comment-as-value are compile-reject or no-op, not host escapes).
- [x] Full Interpretation test filter green.
- [x] No remaining `Passthrough for POC` in `Poly/Interpretation/`.
- [x] `Compile` == fail-closed (`CompileChecked` is an alias).
- [x] `LanguageVmTests` covers executable `CompileNodeInner` kinds (Compile + execute or compile-reject). `LanguageSurfaceTests` inventories Executable / CompileReject / AnalysisOnly.
