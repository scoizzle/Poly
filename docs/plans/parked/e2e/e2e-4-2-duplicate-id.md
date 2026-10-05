# e2e-4-2 — Disambiguate shadow-key `id`

**Difficulty:** S  
**Status:** `[x]` 2026-08-13 — DistinctChildKeyParam  
**Prereq:** 4-1  
**Fleet:** P3-2 · Repro: `09-transport/clinic.poly`

## Objective

Parent+child both shadow-keyed must not emit two `id` params.

## Exact steps

1. Failing compile/test: duplicate `id` → CS.  
2. Disambiguate child route token/param (e.g. parent id + child id names). One naming function.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `MinimalApiGenerator.cs` (route params) | |

## Status

**Status:** Not Started  
**Claimed by:**  
