# DAS W0.3 — Metadata provider without AnalysisResult cast

**Wave:** W0 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) §4, P4  
**Difficulty:** Small–Medium  
**Status:** `[x]`  
**Prereq:** W0.1  

## Objective

Shared projection/export helpers must read metadata via `INodeMetadataProvider.GetMetadata<T>` (or `AnalysisResult` only when that is the real type)—**never** `metadata as AnalysisResult` as the sole way to reach bags. That cast was the EntitySyntax mid-pass blind spot and keeps dual nullable paths alive.

## Tasks

- [x] W0.3.1 Grep `as AnalysisResult` in `DomainToCSharpExporter` / `DomainProgramProjection` / related helpers; replace GetMetadata paths with provider interface.
- [x] W0.3.2 Keep `AnalysisResult`-required APIs at public export boundaries (`Export(domain, AnalysisResult)`).
- [x] W0.3.3 Prefer deleting temporary “analysis null = EntitySyntaxPass path” comments once W0.2 is done.
- [x] W0.3.4 Tests: export with full analysis still resolves RLM / ESM for ctor order and subscriptions where applicable.

## Primary files

- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs`
- `Poly/DomainModeling/Lowering/DomainProgramProjection.cs`

## Acceptance criteria

- [x] No critical GetMetadata path depends on `as AnalysisResult` succeeding for an `AnalysisContext`.
- [x] Export with `AnalysisResult` still fail-closed when RLM required and missing (F5-class).
- [x] Build + tests green.

## Progress notes

- Zero `as AnalysisResult` in C# sources (grep clean).
- Projection/export helpers take `INodeMetadataProvider` for GetMetadata; `CollectSubscriptionInfo` requires provider (no null dual path for subscriptions).
- `LoweringContext.Analysis` and `EffectLoweringPass.Analysis` are `INodeMetadataProvider?`; `TryGetStage` extended to provider.
- Public boundary kept: `DomainToCSharpExporter.Export(Domain, AnalysisResult)` and `ToSyntax(Domain, AnalysisResult)`.
- Tests: RLM subscription emit, ESM ctor order for CreateNav, `ToSyntax` via provider; F5 `ResolveRelationship_*` still green.
- Build: `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` OK; filters `/*/*/*/*Export_*`, `/*/*/*/*ResolveRelationship*`, `/*/*/*/*ToSyntax_ViaProvider*`, `/*/*/*/*EffectLowering*`.
- **Verify (pass, severity nit):** AC verified statically. (1) `rg`: zero `as AnalysisResult` / `(AnalysisResult)` casts in `**/*.cs`; only docs mention historical cast. (2) `Export(Domain, AnalysisResult)` kept → `ToSyntax`; helpers take `INodeMetadataProvider`; `CollectSubscriptionInfo` throws on null metadata. (3) `ResolveRelationship` fail-closed when analysis present but RLM missing + F5 tests. (4) `ToSyntax(AnalysisResult)` + `ToSyntax(INodeMetadataProvider)`; subscriptions always pass provider. (5) Lowering/effect analysis typed as `INodeMetadataProvider?`; `TryGetStage` on provider. (6) `AnalysisResult` and `AnalysisContext` both implement `INodeMetadataProvider`. (7) Export/ToSyntax provider tests present. (8) No EntitySyntax/analysis-null dual-path comments in exporter. Residual dual-path scans remain behind `DM-META-REMOVE-FALLBACK` for W4 (not W0.3 scope). Live suite not re-run by verifier.
