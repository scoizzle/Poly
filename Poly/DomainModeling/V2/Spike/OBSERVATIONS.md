# Vertical Slice Observations

Scenario implemented:
- One type: `PersonInput`
- One property: `Name`
- One constraint: required/non-empty string
- One evaluation path: `VerticalSliceSpike.Evaluate`
- One output shape: structured diagnostic (`Code`, `Message`, `Path`)

## What naturally emerged
1. Stable machine-readable diagnostic codes are required (`STRUCT.MISSING_FIELD`).
2. A diagnostic path (`name`) is needed for UI/API parity.
3. Constraint evaluation and input representation are separate concerns.
4. Deterministic ordering is easy with a single constraint but will need explicit policy as rules grow.

## What was awkward
1. Type identity is implicit; no stable semantic identity exists yet.
2. No version marker exists for model shape or rules.
3. No tenant/context boundary is represented in this minimal flow.
4. No lifecycle/read-only property semantics are represented.

## First abstractions to extract next
1. `SemanticId` value object for stable IDs.
2. `ModelVersion` and `RuleSetVersion` value objects.
3. Standard diagnostic envelope contract.
4. TypeExpression vocabulary and parser.

This spike is intentionally raw and not production code.
