# e2e-s-1 — Snapshot notify sweep list

**Difficulty:** S  
**Status:** `[ ]`  
**Fleet:** P6-1  

## Objective

`when … { create … }` must not throw `Collection was modified`. `NotifyTransition` foreach-iterates `_instances` (`DomainInstanceStore.cs` ~153); `Store.Add` mutates the list.

## Exact steps

1. Failing test: `create_instance` → `invoke_action` that transitions and a subscription creates a child (guide §0.4 shape). Name: `NotifyTransition_CreateDuringNotify_DoesNotThrow`.
2. Snapshot (`_instances.ToArray()` or equivalent) **before** the foreach. Do not change dispatch order in this task.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Runtime/DomainInstanceStore.cs` (`NotifyTransition` list) | `DomainEntityInstance` unique |
| tests | exporter |

## Status

**Status:** Not Started  
**Claimed by:**  
