# Micro-Task: DACR.P3 - DslCompiler Semantic Lookup Migration

Parent: ../downstream-analysis-consumption-remediation.md
Queue: ./dacr-README.md
Difficulty: Medium
Status: [ ] Not Started
Prereq: DACR.P1 complete

## Objective

Use analysis metadata for semantic generation decisions while preserving structural output traversal.

## Tasks

- [ ] P3.1 Keep DslCompiler core generation on analysis metadata as the only semantic source.
- [ ] P3.2 Replace repeated enum and relationship semantic scans in generators with metadata-backed lookups.
- [ ] P3.3 Preserve direct traversal only for ordering and rendering.
- [ ] P3.4 Require AnalysisResult in generator entry points where semantic decisions are made.

## Primary Files

- src/Poly.DslCompiler/DslCompiler.cs
- src/Poly.DslCompiler/MinimalApiGenerator.cs
- src/Poly.DslCompiler/HttpFileGenerator.cs
- src/Poly.DslCompiler/DbContextGenerator.cs

## Acceptance Criteria

- [ ] Semantic generator logic no longer re-derives meaning from direct scans in touched paths.
- [ ] Entry points in scope do not allow semantic generation without analysis.

## Verification

- [ ] Build green.
- [ ] DslCompiler tests green.
- [ ] Output regression tests unchanged where behavior should match.
