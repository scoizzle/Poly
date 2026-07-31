# DAS W3.1 — Declare accurate pass dependencies

**Wave:** W3 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) P5  
**Difficulty:** Small–Medium  
**Status:** `[ ]`  
**Prereq:** W1 gate (catalog exists so deps can name it)  

## Objective

Every fact-publishing pass declares real `Dependencies`. Lint-only passes are labeled (empty deps OK only if they write no metadata others read).

## Tasks

- [ ] W3.1.1 Inventory passes: metadata written vs read.
- [ ] W3.1.2 Fill `Dependencies` arrays for Structure, Topology, Ownership, Storage, Transport, Capability, Catalog, etc.
- [ ] W3.1.3 Fix registration order if builder requires topological order.
- [ ] W3.1.4 Add a test or analyzer check that fails if a known consumer pass lacks declared dep (lightweight OK).

## Acceptance criteria

- [ ] No fact pass with silent undeclared reads of catalog/structure/topology.
- [ ] Build + tests green.

## Progress notes

(empty)
