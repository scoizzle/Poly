# owned-1 — Guide honesty for owned/nested access

**Suite:** [`dogfood-owned-README.md`](dogfood-owned-README.md)  
**Source finding:** S3-B1 — guide says owned access is "Pull"  
**Difficulty:** Small  
**Status:** `[x]`

## What was done

Updated the DSL guide to reflect that **owned/nested access works via the same path-prefix mechanism as relationship navigation**. The path-prefix parser (`ParseRelatedAccess`) treats any relationship name (including `owned` relationships) the same way — `RelationshipName PropertyName op Value` is parsed as `RelationshipNavigation(relName, PropertyAccess(propName))`, which lowers to `Member` access and produces the correct result.

**Key finding:** The guide's Expression Gaps table marked owned/nested access as "Pull (same path-prefix approach)" — but path-prefix already works for owned because owned relationships are stored as relationships with `sourceOwnsTarget=true`. The DSL policy example below was tested end-to-end:

```poly
Customer: entity {
  profile: owned Profile
  IsUrban: policy { profile City is "Metropolis" }
}
```

**Guide changes made:**
- Promoted Owned/nested access from "Pull" to "✅ **shipped** (path-prefix)" in the Expression Gaps table
- Added note: "Owned and relationship navigations use the same space-delimited path-prefix syntax (`RelName PropName op Value`)."
- Removed from "Not yet shipped" section

**`add_policy` JSON note:** The guide already correctly states that JSON policies do not support path-prefix (`Rel exists`, `where`, or path-prefix). This is still accurate and will be addressed in owned-2.

## Files changed

- `Poly.Mcp/Docs/poly-dsl-guide.md` — promoted owned/nested access from Pull to shipped
- `Poly.Mcp/Docs/poly-dsl-guide.md` — removed "Owned/nested access" from "Not yet shipped" list

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
```

No test changes — non-functional doc update.
