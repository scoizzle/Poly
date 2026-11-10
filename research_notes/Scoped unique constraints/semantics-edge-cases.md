# Scoped unique constraints: semantics and edge cases

**Scope.** This note specifies the meaning of a proposed `.poly` constraint, `unique_within(scope)`, independently of any one implementation. It is written against the Poly repository as it stands on 2026-11-06 (commit tree under `/Users/scoizzle/.t3/worktrees/Poly/t3-4cc2823d`). Poly today has only a global, property-level `unique`; `unique_within` is not in the grammar. The note therefore separates **cited findings** (what the code and primary external sources say) from **inferences** (the normative choices this specification recommends). Repo citations are links relative to this note's directory; line numbers are from the 2026-11-06 tree.

**Method.** Read the domain-modeling pipeline (`Poly/DomainModeling/{Ontology,Analysis,Runtime,Libraries,Language}`), the MCP DSL guide, the storage/pack emitters, and the tests that pin `unique`. Compared the semantics with SQL unique indexes (PostgreSQL, SQL Server, SQLite), EF Core keys, OWL 2 `hasKey`, and the weak-entity/identifying-relationship model. No repo files were modified.

**Current state in one paragraph.** `unique` is a bare property-constraint marker ([UniqueConstraint.cs](../../Poly/DomainModeling/Ontology/Constraints/UniqueConstraint.cs)). It is enforced by a storage-port check keyed only by `(entity, property, value)` ([IStoragePort.cs](../../Poly/DomainModeling/Runtime/IStoragePort.cs), [ModuleStoragePort.cs](../../Poly/DomainModeling/Runtime/ModuleStoragePort.cs)), compared with ordinal string equality, with null skipped only on the lowered assign path ([ModuleInstanceExecutor.cs](../../Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs)). The first globally unique property is also used as the entity's natural key for storage and HTTP routes ([StorageAnalyzer.cs](../../Poly/DomainModeling/Analysis/StorageAnalyzer.cs), [MinimalApiGenerator.cs](../../src/Poly.Packs.Product/MinimalApiGenerator.cs)), while the physical primary key is a synthetic integer ([DbContextGenerator.cs](../../src/Poly.Packs.Product/DbContextGenerator.cs)). A scoped constraint must therefore be a *new semantic kind*, not a flag on the existing one, or every `is UniqueConstraint` site silently treats it as global.

---

## 1. What does `unique_within(parent)` mean?

### Takeaway
It is a conditional **at-most-one** invariant over the instances of one entity type: no two *distinct* instances that have the same **non-absent scope instance** and the same **non-absent key value** may exist. It never asserts existence, never merges instances, and never says anything about how many rows a relationship may hold.

### Cited Findings
- `unique` is today a marker record with no payload; the type is the whole declaration — [UniqueConstraint.cs](../../Poly/DomainModeling/Ontology/Constraints/UniqueConstraint.cs).
- The domain analyzer explicitly refuses to validate `unique` statically: `UniqueConstraint => true, // cannot validate statically; requires instance set` — [ConstraintValidation.cs](../../Poly/DomainModeling/Analysis/ConstraintValidation.cs).
- The runtime key is `(entity, property, value)`, with no scope component: `Exists(entity, property, value)`, `Record(...)`, `Release(...)` — [IStoragePort.cs](../../Poly/DomainModeling/Runtime/IStoragePort.cs); the in-memory key concatenates exactly those three parts with an ASCII unit separator — [ModuleStoragePort.cs](../../Poly/DomainModeling/Runtime/ModuleStoragePort.cs).
- The assignment path checks the port before writing and records the new key after — [ModuleInstanceExecutor.cs](../../Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs).
- The guide states the guard is the compiled module's, not an export-only stub — [poly-dsl-guide.md](../../Poly.Mcp/Docs/poly-dsl-guide.md).

### Inferences
**Decision (normative definition).** Let:

- `E` be the declaring entity type and `I_E` its instance set in a state `s`;
- `σ : E → S_⊥` be the **scope expression**: the instance reached by the named singular navigation, or `⊥` when unlinked;
- `κ : E → V_⊥` be the **key expression**: the value of the constrained property (or property tuple), or `⊥` when null (or when any component is null);
- `≈_V` be the domain's value equality on `V` (see §5);
- `=` be instance identity on `S` and `E`.

Then `unique_within(σ)` on key `κ` holds in `s` iff:

```
∀ e1, e2 ∈ I_E(s):
    σ(e1) ≠ ⊥ ∧ σ(e2) ≠ ⊥
  ∧ κ(e1) ≠ ⊥ ∧ κ(e2) ≠ ⊥
  ∧ σ(e1) = σ(e2)
  ∧ κ(e1) ≈_V κ(e2)
  ⇒ e1 = e2
```

A **violation witness** is a pair `(e1, e2)` with `e1 ≠ e2`, both scoped, both keyed, `σ(e1) = σ(e2)`, and `κ(e1) ≈ κ(e2)`. The constraint holds iff no witness exists. It is **vacuous** when no instance has both a scope and a key.

The constraint is *not*: an existence claim (it does not say any row has the key); an identity/merging rule (unlike OWL `hasKey`, it never concludes `e1 = e2`, it rejects the state); a relationship cardinality; a primary-key declaration; or a route-key declaration.

### Gaps
- No code currently models `σ`; the formal definition above is the proposed contract, not an implemented one.
- The port's string key cannot represent a scope without a format change; whether the scope is encoded as a fourth key component or an opaque scope token is an implementation decision with no semantic difference, provided identity is preserved (§2).

---

## 2. Is the key `(scope instance, value)`? Scope identity versus scope value

### Takeaway
Yes, but the full key is `(entity type, constrained property/constraint, scope instance identity, value tuple)`. The scope is the **identity** of one instance, not the value of its natural key, and not "the set of instances reachable by a collection".

### Cited Findings
- The storage FK from a child to its aggregate parent is built from the parent's unique property if one exists, otherwise `Id` — [StorageAnalyzer.cs](../../Poly/DomainModeling/Analysis/StorageAnalyzer.cs). This shows the physical layer already has two ways to name a parent: natural key or surrogate identity.
- The aggregate model states that every child has exactly one aggregate root (parent), and that the model is shared by storage (cascade deletes, constraint ordering) and transport (URL nesting) — [AggregateModel.cs](../../Poly/DomainModeling/Analysis/AggregateModel.cs).
- Relationship cardinality is `one`/`many`; a singular nav is a reference, a collection nav means the declaring entity owns the target — [poly-dsl-guide.md](../../Poly.Mcp/Docs/poly-dsl-guide.md).
- A singular navigation (including a back-reference) is **optional at create**: "Navs are optional references (set at link/create time, may be null)" — [DomainToCSharpExporter.cs](../../Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs).
- OWL `hasKey` keys a class by a *set of property expressions*, not by an instance identity, and only over named individuals — [OWL 2 Direct Semantics, Table 9](https://www.w3.org/TR/owl2-direct-semantics/#Keys).

### Inferences
**Decision: identity, not value.** Two tenants that happen to share the same `code` are two scopes. Renaming a tenant must not change which rows are unique together. A physical index may use `(tenant.code, email)` only if it cascades code updates; the safe default is `(tenant.id, email)`. The semantic rule must be stated in terms of identity so the VM and the database agree after a rename.

**Decision: namespace includes the entity and the constraint.** `Email` scoped to a tenant and `Username` scoped to the same tenant are separate key spaces; a `Customer` and a `User` in the same tenant with the same `Email` do not collide. The port key therefore needs at least `(entity, property, scope, value)`, not `(scope, value)`.

**Decision: one scope instance.** `σ(e)` must be a single instance. A collection scope would mean "no two instances share a key in *any* common scope", which is a different, non-local constraint and is not what "within a parent" means. Reject `many` / `ManyToMany` scope navigations at analysis.

**Decision: scope target must be an entity.** Value types have no identity and cannot be a foreign-key target; a value-type "scope" would make two equal values the same scope, which collapses the distinction between identity and equality. Reject.

**Decision: self-scope is legal.** `parent: Node` on `Node` with `unique_within(parent)` means "unique among siblings". The scope identity is well defined; ownership cycles are already a separate structural error ([DomainModelDiagnosticCodes.cs](../../Poly/DomainModeling/Analysis/DomainModelDiagnosticCodes.cs), `DMSTR003`; `DMSTR002` is now the key-navigation cycle), and a non-owning self-reference cycle is harmless for this constraint.

**Decision: declare on the child.** The canonical form points from the constrained entity to its one parent. The parent-side form ("all my children have distinct X") is the same constraint written on the child with the back-reference, so it does not need a second surface.

### Gaps
- How an unsaved parent's identity maps to a foreign key inside one transaction is unspecified. The identity must be stable from `create` through `link`; a handle-to-FK map is an implementation obligation.
- Poly's aggregate resolution for a 1:1 pair is described as "a resolution detail, not something the DSL distinguishes" ([poly-dsl-guide.md](../../Poly.Mcp/Docs/poly-dsl-guide.md)), so the aggregate parent is not always the author's intended scope. This is a further reason to accept any singular nav rather than only the owner (§8).

---

## 3. Optional scope, reparenting, and collection scopes

### Takeaway
An absent scope means **no constraint** (vacuously true), not a shared "null bucket". Reparenting is a state transition and must be re-checked in the new scope. A collection is not a scope.

### Cited Findings
- Singular navs are unlinked at create and may be null — [DomainToCSharpExporter.cs](../../Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs).
- A to-one source refuses a second link: "Cannot add a second link on '<rel>': the source already has a target." — [Relationship.cs](../../Poly/DomainModeling/Ontology/Relationship.cs), enforced in [ModuleInstanceExecutor.Links.cs](../../Poly/DomainModeling/Runtime/ModuleInstanceExecutor.Links.cs).
- Unlinking the last owner of an `owned` target is refused: "Cannot unlink '<rel>': the owned target must keep its owner." — [Relationship.cs](../../Poly/DomainModeling/Ontology/Relationship.cs), enforced in [ModuleInstanceExecutor.Links.cs](../../Poly/DomainModeling/Runtime/ModuleInstanceExecutor.Links.cs).
- Link and unlink are each their own transaction (`Transact` with capture/restore) — [ModuleInstanceExecutor.Links.cs](../../Poly/DomainModeling/Runtime/ModuleInstanceExecutor.Links.cs), [ModuleInstanceExecutor.cs](../../Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs).
- A collection nav (`many`) is the one-to-many form; cardinality is `one` (default) or `many` — [poly-dsl-guide.md](../../Poly.Mcp/Docs/poly-dsl-guide.md).

### Inferences
**Decision: null scope is vacuous.** The two coherent readings are (a) null is outside every scope, so the constraint is vacuous; (b) null is one shared scope, so all parentless rows with the same key collide. Choose (a). Reading (b) makes `unique_within(tenant)` silently become a *global* unique constraint for orphans, which is exactly what global `unique` already expresses; a domain that wants that should say `unique`. Reading (a) also makes the constraint total: removing a scope can never create a violation.

**Decision: scope presence is a separate fact.** `unique_within` does not imply the scope is required. Today every singular nav is optional at create, so "required scope" is not expressible on the nav; `owned` plus the minimum-one-owner rule is the closest existing mechanism, and it constrains *unlink*, not create. The spec should say this explicitly and emit an analysis hint when the scope is optional: "instances with no `<scope>` are not constrained." A future required-navigation declaration would make the constraint total without changing its meaning.

**Decision: reparent is checked at the new scope.** Reparenting cannot create a duplicate in the old scope (removal only shrinks a bucket), but it can in the new one. The check must run in the same transaction as the move, against the post-state, excluding the moved instance itself. If a host exposes reparent as `unlink` then `link`, there is an intermediate state where the instance is unscoped; a concurrent create can take the key before the link, and the link then fails. That is correct for an at-most-one invariant, but it is not atomic. The spec must say so, and a future reparent primitive should do both edges in one transaction.

**Decision: owned scopes cannot currently be reparented at all.** `Unlink` refuses to drop the last owner and `Link` refuses a second target on a to-one source, so the only reparent order is blocked from both sides for an `owned` child. Non-owned scopes can be reparented (`unlink` then `link`). The collision case therefore matters now for reference scopes, and only later for owned scopes.

**Decision: reject collection scopes.** A collection nav is the set being scoped, not a scope. If an author writes `unique_within(orders)` where `orders` is `many Order`, the analysis error should say: "scope must be a single navigation; 'orders' is a collection. Declare the constraint on the child with its back-reference." A stage is also not a scope: it is a state, it is transient, and key uniqueness across stage transitions would resurrect collisions.

### Gaps
- There is no instance-delete operation in the runtime or MCP surface today (a search of `Poly/DomainModeling` and `Poly.Mcp/Tools` on 2026-11-06 finds only comments about future cascade/soft-delete), so parent-delete semantics are necessarily a forward-looking part of this spec (§7).
- No test exercises reparenting under a scoped constraint.

---

## 4. At-most-one versus exactly-one; `required`/`optional`; multiple nulls; the empty string

### Takeaway
At-most-one, never exactly-one. `required` and `unique_within` are orthogonal: `required` says the value is present; `unique_within` says a present value is not duplicated in its scope. Multiple nulls are allowed because null is absence, not a value. The empty string is an ordinary value and collides with another empty string.

### Cited Findings
- `OptionalConstraint` is the opt-out of requiredness, and the validator treats any value including null as legal: `OptionalConstraint => true, // optional is the opt-out of requiredness; any value (incl. null) is legal` — [ConstraintValidation.cs](../../Poly/DomainModeling/Analysis/ConstraintValidation.cs).
- `required` is the default; `optional` is its opt-out; carrying both is a contradiction — [ConstraintQualityAnalyzer.cs](../../Poly/DomainModeling/Analysis/ConstraintQualityAnalyzer.cs), [ConstraintValidation.cs](../../Poly/DomainModeling/Analysis/ConstraintValidation.cs).
- The assign path skips both `Exists` and `Record` when the new value is null — [UniqueConstraintCompiler.cs](../../Poly/DomainModeling/Libraries/Storage/UniqueConstraintCompiler.cs); the host `SetProperty` does **not**: when it checks or records, it converts null to `""` and uses that empty key — [ModuleInstanceExecutor.cs](../../Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs).
- The create contribution has **no** null guard: it emits `if (ExistsWith(db, value)) return Failure(...)` and `Record(db, value)` unconditionally — [UniqueConstraintCompiler.cs](../../Poly/DomainModeling/Libraries/Storage/UniqueConstraintCompiler.cs). Optional properties are `T? = null` constructor parameters — [DomainToCSharpExporter.cs](../../Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs).
- SQL engines disagree on nulls in unique indexes: PostgreSQL treats nulls as distinct by default, allowing multiple nulls, and offers `NULLS NOT DISTINCT` to change that — [PostgreSQL unique indexes](https://www.postgresql.org/docs/current/indexes-unique.html); SQLite says "NULLs are still distinct in a UNIQUE column" — [SQLite NULL handling](https://www.sqlite.org/nulls.html); SQL Server cannot create a unique index on a column containing NULL in more than one row — [SQL Server unique indexes](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/create-unique-indexes).
- The guide says duplicate values fail loud at the session port and at the database — [poly-dsl-guide.md](../../Poly.Mcp/Docs/poly-dsl-guide.md).

### Inferences
**Decision: null is absence.** `κ(e) = ⊥` for null, so no key exists and the constraint is vacuous for that instance. Consequences:

| Scope | Key | Constraint |
|---|---|---|
| absent | any | vacuous |
| present | null | vacuous (unless `required` independently rejects null) |
| present | non-null | at most one instance |
| absent | non-null | vacuous; *not* global unique |

Multiple nulls are allowed. A `required` key plus `unique_within` means: every row has a value, and at most one row per scope has each value. It still does not mean "every scope has exactly one row"; that is an existence/cardinality constraint on the scope's collection and must not be folded into uniqueness.

**Decision: the empty string is a value.** `""` collides with `""` in the same scope. `optional` is the only absence marker; it does not mean "empty or whitespace is absent". If the business wants trimmed/blank-as-absent semantics, that is a write-time normalization (or a future `notEmpty`/`trim` facet), not a hidden rule in uniqueness. The same applies to whitespace-only strings: `" "` and `""` are different values.

**Decision: a constant `default` plus a scoped key is a modeling smell.** At most one row per scope can take the constant default. Analysis should hint: "default(...) is constant; only one instance per scope can use it." A default that depends on the scope is not expressible today.

**Observed tension to settle in the spec.** The create arm has no null guard, while the assign arm skips null. Worse, the create arm's `IsString` check only recognizes `ClrTypeReference`, `PrimitiveTypeReference`, and a `NamedTypeReference` named `Text` — an optional property's parameter is an `OptionalTypeReference`, which falls through to `new Invoke(new Member(value, "ToString"))` — [UniqueConstraintCompiler.cs](../../Poly/DomainModeling/Libraries/Storage/UniqueConstraintCompiler.cs). So an optional Text unique can emit `value.ToString()` on a null create parameter (a null-reference throw), and even where a null reaches the port, null stringifies into the same empty segment as `""`. Two null creates can therefore throw or collide, and two null **lowered** assigns do neither; the host `SetProperty` path treats null as `""` and can collide. This is a code-level inconsistency, not a validated bug (no red test was written for this note), but the semantic rule must be one rule: **null never participates**, on create, lowered assign, and host write alike. Any implementation must add an explicit null guard to the create arm, unwrap `OptionalTypeReference` in `IsString`, and must not let `ToString()` conflate null with `""`.

### Gaps
- No test pins the null behavior of `optional unique`; the existing optional-unique fixture only checks parameter threading — [ContributedMethodTests.cs](../../Poly.Tests/DomainModeling/Compile/ContributedMethodTests.cs).
- Whether a `required` + `unique` property emits a non-nullable create parameter and a non-null column is handled by the requiredness analysis, but no test combines it with the null-key edge.

---

## 5. Equality: case, collation, normalization — who decides?

### Takeaway
The domain declares the constraint; the language fixes **one canonical comparison**; the storage pack may only implement that comparison exactly, never a looser one; the generated schema must carry the same comparison (binary collation / matching operator class). Recommended default: ordinal, typed-value equality, with no implicit case folding or Unicode normalization.

### Cited Findings
- The in-memory port compares with `StringComparer.Ordinal` — [ModuleStoragePort.cs](../../Poly/DomainModeling/Runtime/ModuleStoragePort.cs).
- The host `SetProperty` compares the old and new value with `StringComparison.Ordinal` and renders values with the invariant culture — [ModuleInstanceExecutor.cs](../../Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs).
- The assign lowering compares the previous value with `object.Equals` and passes `ToString()` to the port — [UniqueConstraintCompiler.cs](../../Poly/DomainModeling/Libraries/Storage/UniqueConstraintCompiler.cs).
- PostgreSQL equality in a unique index is by the indexed values; the column's collation determines comparison, and a nondeterministic collation can treat case- or accent-different strings as equal — [PostgreSQL unique indexes](https://www.postgresql.org/docs/current/indexes-unique.html), [PostgreSQL collation](https://www.postgresql.org/docs/current/collation.html).
- SQL Server's unique index is a B-tree key equality; the docs describe duplicate-key rejection without any culture-aware folding — [SQL Server unique indexes](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/create-unique-indexes).
- Poly has `pattern(regex)` and `length(min,max)` as write-time checks; a pattern is not projected to SQL and a length minimum is enforced only at write time — [ConstraintQualityAnalyzer.cs](../../Poly/DomainModeling/Analysis/ConstraintQualityAnalyzer.cs), [poly-dsl-guide.md](../../Poly.Mcp/Docs/poly-dsl-guide.md).

### Inferences
**Decision: ordinal by default.** `"Sam"` and `"sam"` are different keys. This matches the simulator today, is deterministic across hosts, and avoids the trap where a case-insensitive database rejects a pair the simulator accepted. Case-insensitive uniqueness must be an **explicit** declaration (for example a future `unique_within(tenant) ci` or a `fold(casefold)` facet), never an inherited database default. The same rule applies to accent-insensitivity and to trailing-space semantics.

**Decision: no implicit Unicode normalization.** Precomposed `é` and decomposed `e` + combining acute are different code-point sequences and therefore different keys under ordinal equality. If the business means them to be the same, normalize at write (a facet or a policy), because a constraint whose meaning depends on the storage engine's collation cannot be simulated identically by the VM and printed identically for every pack. PostgreSQL's nondeterministic collations can equate normalization forms and even ignore punctuation — [PostgreSQL collation](https://www.postgresql.org/docs/current/collation.html) — which is precisely the divergence the language must not inherit silently.

**Decision: compare typed values, not renderings.** `long 1` and `double 1.0` render differently; `decimal 1.0` and `1.00` compare equal under .NET `decimal.Equals` but render differently. The port's string key must be a canonical, injective encoding of the domain value (or the port must take the typed value). The current `ToString()`-based key is a semantic hazard for non-text keys, not just a performance detail.

**Decision: the pack implements, it does not decide.** The matrix:

| Question | Domain (`.poly`) | Analysis | Storage pack | Generated schema |
|---|---|---|---|---|
| Is there a constraint? | declares `unique_within(nav)` | validates the nav and key | — | — |
| What is equal? | the property's domain type | type-checks | must match the canonical equality exactly | index collation/operator class must match |
| Is null a value? | no | hint if scope optional | skip null keys | partial index `WHERE key IS NOT NULL`, or engine equivalent |
| Who enforces atomically? | — | refuses to prove instance uniqueness | atomic check + port | unique index on `(scope_fk, key...)` |
| What on parent delete? | ownership declares cascade; reference is restrict/set-null | ownership analysis | FK action | `ON DELETE` |
| Case sensitivity | ordinal default | — | binary collation | binary/`_bin` collation |

A pack that cannot express the canonical comparison must fail closed at compile time, not quietly emit a looser index.

### Gaps
- MySQL's default collation is commonly case-insensitive, but no MySQL primary doc was fetched for this note; the pack must be audited against the rule rather than assumed.
- No repo test pins case sensitivity or Unicode normalization for `unique`; the current ordinal behavior is visible in code but not contractual.
- `Text` has `length` and `pattern` but no normalization facet; where normalization belongs (write-time, a new facet, or a pack convention) is an open design question.

---

## 6. Composite scopes and composite keys

### Takeaway
`unique_within(a, b)` should mean **one tuple scope** ("same `a` and same `b`"), not two independent constraints. Tuple equality is permutation-invariant, so order is semantically irrelevant; duplicates should be rejected. A scope that is itself globally unique changes nothing semantically. Composite *keys* (multiple key properties) are a separate, entity-level extension and should not be smuggled into the property-level form.

### Cited Findings
- Constraints are attached to a single `Property` as a list — [Property.cs](../../Poly/DomainModeling/Ontology/Property.cs); the parser builds each constraint from a keyword in that position — [DomainResolver.cs](../../Poly/DomainModeling/Language/DomainResolver.cs).
- `unique` takes no arguments today; `range`/`length`/`pattern`/`default` take parenthesized arguments — [DslGrammar.cs](../../Poly/DomainModeling/Language/DslGrammar.cs), [DomainResolver.cs](../../Poly/DomainModeling/Language/DomainResolver.cs).
- `ConstraintMerge` merges range, length, required, optional, equality, and pattern, but **not** unique: the fallback returns null for a different type, and unique has no arm — [ConstraintMerge.cs](../../Poly/DomainModeling/Ontology/Constraints/ConstraintMerge.cs).
- The storage column records uniqueness as a single boolean, `IsUnique` — [StorageModel.cs](../../Poly/DomainModeling/Analysis/StorageModel.cs); the index is emitted per column — [DbContextGenerator.cs](../../src/Poly.Packs.Product/DbContextGenerator.cs).
- EF Core distinguishes a composite key/alternate key from a plain unique index; an alternate key is a unique constraint that can be the target of a foreign key — [EF Core keys](https://learn.microsoft.com/en-us/ef/core/modeling/keys?tabs=data-annotations).

### Inferences
**Decision: comma-separated scopes are a tuple, not a conjunction.** `unique_within(user, group)` means `σ(e) = (user(e), group(e))` and the rule is "same pair". The conjunction reading ("unique within each user, and also unique within each group") is strictly stronger and is expressible as two separate `unique_within` constraints; it should not be the default reading. The analysis diagnostic/printed form should make the tuple explicit (for example `unique_within(user, group)` prints back verbatim).

**Decision: order is semantically irrelevant, duplicates are rejected.** `(a, b)` and `(b, a)` denote the same relation because `(a1,b1) = (a2,b2)` iff `(b1,a1) = (b2,a2)`. Preserve the author's order for printing and diagnostics, canonicalize (for example sort by declaration index) for constraint identity, and reject `unique_within(a, a)` as a duplicate scope. Reject an empty scope list.

**Decision: strict key tuple.** If composite keys are ever allowed, `κ(e) = (p1(e), ..., pn(e))` and `κ(e) = ⊥` if **any** component is null. A partial key is not a key; otherwise `(null, "x")` would form a bucket and nulls would collide, contradicting §4. Composite scopes are also strict: `σ(e) = ⊥` if any scope nav is unlinked.

**Decision: a globally unique scope adds no meaning.** If `Tenant.code` is globally `unique`, `unique_within(tenant)` still means "same tenant identity". The parent's global uniqueness may let the physical index use `tenant.code` instead of `tenant.id`, but then a rename must cascade to the child index. Meaning is identity; physical choice is an optimization that must preserve it. Conversely, a globally unique parent does **not** make the child's key globally unique: two tenants can still share a child key.

**Decision: multiple constraints on one property are a conjunction.** `Code: Text unique_within(tenant) unique_within(region)` is legal and means both. `unique` plus `unique_within` on the same property is redundant (global implies scoped); report a hint, not an error.

**Decision: ship one scope and one key first; define tuples now.** Poly's tenets favor the thinnest slice ("shipped capability over completeness"; "seam when multiplicity is known") — [AGENTS.md](../../AGENTS.md). The common case is one property, one scope. Composite *scopes* are a small extension of the same rule and can ship when a real consumer appears. Composite *keys* cannot be expressed as a property constraint at all, so they need an entity-level form (for example `key (tenant, first, last)`), and should not be forced into `unique_within`'s argument list.

**Decision: reject a scope that is the key.** `Code: Text unique_within(Code)` is self-referential and meaningless; key and scope must be disjoint. A scope that is reachable through another scope (for example `group` is a nav on `user`) is redundant; a hint is appropriate, but proving functional dependency in general is out of scope.

### Gaps
- `StorageColumn.IsUnique` is a single-column boolean; a scoped or composite index needs a new storage-model shape (a constraint/index record with a column list and an optional scope). This is implementation, but the semantic model should not pretend `IsUnique` can carry it.
- The parser/printer have no `unique_within` token or case; the printer currently maps `UniqueConstraint => "unique"` — [DomainDslPrinter.cs](../../Poly/DomainModeling/Language/DomainDslPrinter.cs).
- No repo test covers multiple `unique` constraints on one entity beyond the "second unique is stored, not dropped" case — [DbContextGenerator.cs](../../src/Poly.Packs.Product/DbContextGenerator.cs).

---

## 7. Mutation: create, link/unlink, reparent, delete, concurrency, transaction boundaries

### Takeaway
Uniqueness is a **post-state invariant**. Every transition that can add a `(scope, key)` pair must check it; every transition that removes one must release it; the only cross-process guarantee is a real unique index, not an application-level `Exists`-then-`Insert`.

### Cited Findings
- `SetProperty` checks `Exists`, releases the previous key, records the new key, and rolls the bag back on failure — [ModuleInstanceExecutor.cs](../../Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs).
- `Transact` captures bag state and the port mark, and restores both on failure — [ModuleInstanceExecutor.cs](../../Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs); `ModuleStoragePort` journals every add/remove and can roll back to a mark — [ModuleStoragePort.cs](../../Poly/DomainModeling/Runtime/ModuleStoragePort.cs).
- The generated context's `Exists` checks both the database and `_pendingKeys` within the current invocation — [DbContextGenerator.cs](../../src/Poly.Packs.Product/DbContextGenerator.cs).
- `Link` is idempotent; a second link on a to-one source fails; `Unlink` is a no-op if not linked and refuses to drop the last owner of an owned target — [ModuleInstanceExecutor.Links.cs](../../Poly/DomainModeling/Runtime/ModuleInstanceExecutor.Links.cs).
- `create in Relationship { ... }` creates the child and links it to the source at runtime — [poly-dsl-guide.md](../../Poly.Mcp/Docs/poly-dsl-guide.md).
- A unique collision maps to HTTP 409 in the generated API — [MinimalApiGenerator.cs](../../src/Poly.Packs.Product/MinimalApiGenerator.cs).
- No instance-delete operation exists in the runtime or MCP tools (repo search on 2026-11-06).

### Inferences
**Decision: create with a key and a scope checks at create; create without a scope is unconstrained until the link.** This permits the natural "create a draft, then attach it" flow. It also means the create can succeed and the later link can fail; that is correct for an at-most-one invariant, but a product that wants create-or-fail atomically must use `create in`, which sets the link at create time. The spec should recommend `create in` for the atomic path and say explicitly that a bare `create` followed by `link` has a visible intermediate state.

**Decision: link checks the target's key in the new scope, excluding the target itself.** The check is against the post-link state. The current `Link` implementation does not consult the unique port at all; a scoped constraint must hook `Link` (and any future reparent primitive), not just `assign` and `create`.

**Decision: unlink cannot create a violation, but must release the scoped key.** Removing an instance from a scope only shrinks a bucket. If the port records per-scope keys, the unlink must release the `(scope, key)` entry; otherwise a later create in that scope with the same key fails against a stale key. Today `Release` is only called on assignment, not on unlink or delete.

**Decision: reparent is a two-edge move.** The new scope must be checked in the same transaction as the edge change. If the host performs `unlink` then `link` as two transactions, a concurrent create can take the key in the gap; the link then fails, which is correct but not atomic. A future reparent primitive should do both edges in one transaction and check once against the final state.

**Decision: parent delete has three legal shapes, and the language must name one.**
- **Cascade** (owned/aggregate parent): deleting the parent deletes the scoped children; no violation is possible; the port must release every child key. This matches the aggregate model's stated use for "cascade deletes, constraint ordering" — [AggregateModel.cs](../../Poly/DomainModeling/Analysis/AggregateModel.cs).
- **Restrict** (non-owned reference): the parent cannot be deleted while a child references it. This preserves the scope and never silently changes the constraint's meaning.
- **Set null** (non-owned reference, scope optional): children survive with no scope and become unconstrained; this is the null-scope rule (§3), not a global bucket.

Leaving a dangling reference, or promoting orphaned rows to a global bucket, is forbidden. Because Poly has no instance-delete operation today, the spec should state these as the required semantics for whichever delete door lands first, and should require the FK action to match the ownership flag.

**Decision: deleting a constrained instance releases its key.** Otherwise a later create with the same key fails against a ghost. This is the same stale-key problem as unlink.

**Decision: concurrency is per-executor in the simulator, per-database in production.** `ModuleInstanceExecutor` serializes its host operations under `_gate` and journals the port, so within one executor the check-then-record is atomic. Across executors or processes, the `Exists`-then-`Insert` sequence is a TOCTOU race. The generated schema must carry a unique index on `(scope_fk, key...)`; the module check is the explainable, fast failure, not the guarantee. The guide's "fail loud at the session port and at the database" is the right product posture — [poly-dsl-guide.md](../../Poly.Mcp/Docs/poly-dsl-guide.md) — but the database is the arbiter under concurrency.

**Decision: transaction boundaries are all-or-nothing.** A create that records a key and then fails must release it; a reparent that releases the old key and records the new one must do both or neither. `Transact` already includes the port mark in its capture/restore, so this is achievable; any new scoped key path must use the same journal.

### Gaps
- No delete or reparent primitive exists, so the cascade/restrict/set-null rules are forward-looking.
- No cross-process concurrency test exists for `unique`; the interpreter tests exercise a single executor.
- Whether `create in` checks the unique port before or after the link is set is not specified in the current lowering; the spec requires "after the link exists, before the operation returns".

---

## 8. Owner versus any singular navigation; what ownership implies for delete

### Takeaway
A scope may be **any singular navigation**, not only the aggregate owner. Ownership is about lifetime (composition, cascade, minimum one owner), not about uniqueness. Requiring the owner would conflate two independent facts and would exclude legitimate business scopes such as `tenant`, `account`, or `warehouse`.

### Cited Findings
- A weak entity is identified by a foreign key to its owner plus its own attributes; the identifying relationship is many-to-one and each weak entity has exactly one owner — [Weak entity](https://en.wikipedia.org/wiki/Weak_entity). Poly's aggregate model encodes the same "exactly one parent" rule — [AggregateModel.cs](../../Poly/DomainModeling/Analysis/AggregateModel.cs).
- `Entity.Navigations` are relationships owned by the source entity; ownership is `SourceOwnsTarget` — [Entity.cs](../../Poly/DomainModeling/Ontology/Entity.cs), [Relationship.cs](../../Poly/DomainModeling/Ontology/Relationship.cs).
- `owned` marks composition; a collection nav also means the declaring entity owns the target; a plain singular nav is a reference ("a truck that names its warehouse") — [poly-dsl-guide.md](../../Poly.Mcp/Docs/poly-dsl-guide.md).
- Owned targets keep their owner: `unlink` refuses the last owner; non-owned links stay minimum zero; removing the owner instance entirely is exempt because it is not an unlink — [poly-dsl-guide.md](../../Poly.Mcp/Docs/poly-dsl-guide.md), [Relationship.cs](../../Poly/DomainModeling/Ontology/Relationship.cs).
- The aggregate model is "inherently cross-entity" and depends on relationship topology plus create-in analysis; it is shared by storage (cascade deletes) and transport (URL nesting) — [AggregateModel.cs](../../Poly/DomainModeling/Analysis/AggregateModel.cs).
- Storage builds a foreign key from the child to the aggregate parent, using the parent's unique property or `Id` — [StorageAnalyzer.cs](../../Poly/DomainModeling/Analysis/StorageAnalyzer.cs).

### Inferences
**Decision: ownership is not a precondition for scoping.** `unique_within(tenant)` is valid even when `Tenant` does not own `User` and `User` is a root entity. The constraint only needs a singular, identity-bearing navigation.

**Decision: ownership changes delete, not uniqueness.** For an owned scope, the parent is the aggregate root and cascade is the natural delete action; orphan is impossible (the child has exactly one owner). For a reference scope, delete must restrict or set-null; orphan is possible only if the scope is optional, and then the child is unconstrained. In both cases the uniqueness rule is identical. A child can be owned and still have duplicate keys; a non-owned tenant can scope a key.

**Decision: cardinality is checked on the scope edge, not inferred from `owned`.** A `ManyToOne` in Poly is a singular nav on the many side plus a collection nav on the one side. The scope test is "is the nav singular on the constrained entity?", not "is the inverse a collection?" and not "is it owned?". This admits the weak-entity shape (`Loan.book: Book`, `Book.loans: many Loan`) and the reference shape (`User.tenant: Tenant`) with one rule.

**Decision: a root entity may carry a scoped constraint.** "Root" means no aggregate parent and its own key/endpoints; it does not mean "unscoped". A `User` can be a root and still be unique within a `Tenant` reference. What it cannot do is use the scoped key as its global route key (§10).

### Gaps
- The aggregate resolution for a 1:1 pair is explicitly "a resolution detail, not something the DSL distinguishes", so the aggregate parent can differ from the author's intended scope. This is the strongest argument for accepting any singular nav and requiring the author to name the scope.
- No delete semantics exist to test ownership's cascade/restrict choice.

---

## 9. Diagnostics: what the error says, what the solver proves, what it must refuse

### Takeaway
Analysis proves **well-formedness** and refuses **instance-level uniqueness**. Runtime reports a violation naming the constrained property, the scope *kind*, and the key value, without leaking the identity of the other holder. The solver must never turn a scoped declaration into a global uniqueness claim, an existence claim, or a reparent-safety claim.

### Cited Findings
- The existing analyzer already refuses static unique validation — [ConstraintValidation.cs](../../Poly/DomainModeling/Analysis/ConstraintValidation.cs).
- Constraint-quality analysis reports hints for constraints that are enforced at write time but not projected to SQL (`pattern`, length minimum) — [ConstraintQualityAnalyzer.cs](../../Poly/DomainModeling/Analysis/ConstraintQualityAnalyzer.cs).
- The diagnostic codes include `DMCS001 ConstraintSatisfiability` and `DMCS002 ConstraintFixedPoint` — [DomainModelDiagnosticCodes.cs](../../Poly/DomainModeling/Analysis/DomainModelDiagnosticCodes.cs).
- The runtime message today is "Unique constraint violated: '<property>' value is already used on another '<entity>'." — [ModuleInstanceExecutor.cs](../../Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs), [UniqueConstraintCompiler.cs](../../Poly/DomainModeling/Libraries/Storage/UniqueConstraintCompiler.cs).
- The generated API maps unique and link conflicts to 409 — [MinimalApiGenerator.cs](../../src/Poly.Packs.Product/MinimalApiGenerator.cs).
- SHACL export marks `x-poly-unique`; JSON Schema cannot express uniqueness and says so — [ShaclExporter.cs](../../Poly/DomainModeling/Queries/ShaclExporter.cs), [JsonSchemaExporter.cs](../../Poly/DomainModeling/Queries/JsonSchemaExporter.cs).

### Inferences
**What analysis must prove.**
1. The scope name resolves to a navigation on the declaring entity.
2. The scope is singular and targets an entity (not a collection, value type, or stage).
3. The key is a non-empty tuple of scalar properties declared on the entity.
4. No duplicate scope path; no empty scope list; key and scope are disjoint.
5. The key property's own constraints are jointly satisfiable (the existing range/length/pattern/equality work).
6. An `equals(...)` key is satisfiable but means at most one row per scope; report it as a hint, not an error.
7. A constant `default(...)` on the key plus a scoped constraint is a hint (only one row per scope can take the default).
8. `unique` plus `unique_within` on the same property is redundant; hint.
9. An optional scope is a hint: "instances with no `<scope>` are not constrained."
10. A **local** effect proof is allowed: an action body that creates two instances with the same literal key in the same literal scope is a provable violation. Do not attempt general state proofs across actions or transactions.

**What analysis must explicitly refuse to prove.**
- That the key is globally unique (a scoped declaration says the opposite).
- That every scope has a row with the key (existence/exactly-one).
- That the scope is present (unless a required-navigation declaration exists; today it does not).
- That a reparent cannot collide (it can).
- That the storage index is equivalent to the constraint (it is an enforcement mechanism; a pack may not support it).
- That two scopes with the same natural key are the same scope (identity, not value).
- That a failed link leaves no trace (the transaction must prove that at runtime, not the solver at analysis time).

**Diagnostic shapes (proposed).**
- Error: `'Email' is unique within 'tenant', but 'tenant' is not a navigation on 'User'.`
- Error: `'Email' is unique within 'orders', but 'orders' is a collection; a scope must be a single navigation. Declare the constraint on the child with its back-reference.`
- Error: `'Email' is unique within 'tenant', but 'tenant' is also the key property. A key cannot scope itself.`
- Error: `'Email' is unique within 'tenant, tenant': duplicate scope path.`
- Hint: `'Email' is unique within 'tenant'; instances with no 'tenant' are not constrained.`
- Hint: `'Email' is globally unique and unique within 'tenant'; the scoped declaration is redundant.`
- Runtime: `Unique constraint violated: 'Email' is already used by another 'User' in this tenant (value 'sam@x').`
  Name the scope **kind**, not the other instance's id or natural key, to avoid leaking "this email is taken" beyond what the product intends. MCP/debug output may add the scope handle and witness id in a structured field.
- 409 remains the transport mapping for a unique or link conflict — [MinimalApiGenerator.cs](../../src/Poly.Packs.Product/MinimalApiGenerator.cs).

**Projection obligations.** The DSL printer must round-trip `unique_within(...)`; SHACL has no scoped-unique keyword, so it needs a documented `x-poly-*` annotation (and must not pretend `x-poly-unique` means the same thing); JSON Schema cannot express it and should keep saying so; the MCP `describe_domain_element` and `get_dsl_guide` surfaces need the new vocabulary. These are consequences of the semantics, not optional polish: a constraint that cannot be described or printed is not part of the language.

### Gaps
- There is no scoped-constraint diagnostic code; `DMCS001` can carry the messages, but a distinct code (for example `DMCS003 ScopedConstraint`) would make triage easier.
- No test pins the runtime message or the 409 mapping for a scoped collision.
- The solver has no effect-level analysis for "two creates in one scope with the same literal key"; that check would be new.

---

## 10. Relationship to global `unique`, natural keys, and relationship cardinality

### Takeaway
Global `unique` is the degenerate case `unique_within(⊤)` (one implicit scope). A scoped key must **not** become the entity's natural/route key. Cardinality (`one`/`many`) and ownership (`owned`) are orthogonal to uniqueness and must not be conflated with it.

### Cited Findings
- `StorageAnalyzer` picks the first property with a `UniqueConstraint` as the entity's `KeyProperty`, and derives the key name/CLR type from it; when there is none, it uses `Id`/`int` — [StorageAnalyzer.cs](../../Poly/DomainModeling/Analysis/StorageAnalyzer.cs).
- The generated API routes `/api/{key}` through that natural key; `FindAsync` is the shadow key — [MinimalApiGenerator.cs](../../src/Poly.Packs.Product/MinimalApiGenerator.cs).
- The EF model uses a synthetic integer `Id` primary key, and every `unique` property is `HasIndex(...).IsUnique()`; the unique property is not the primary key — [DbContextGenerator.cs](../../src/Poly.Packs.Product/DbContextGenerator.cs), [poly-dsl-guide.md](../../Poly.Mcp/Docs/poly-dsl-guide.md).
- The guide says "Every `unique` property, including a second one on the same entity, is `HasIndex(...).IsUnique()`" — [poly-dsl-guide.md](../../Poly.Mcp/Docs/poly-dsl-guide.md).
- EF Core's alternate key is a unique constraint usable as an FK target; a plain unique index is not — [EF Core keys](https://learn.microsoft.com/en-us/ef/core/modeling/keys?tabs=data-annotations).
- Relationship cardinality is `one`/`many`; `owned` is composition; the two are separate flags — [Relationship.cs](../../Poly/DomainModeling/Ontology/Relationship.cs), [poly-dsl-guide.md](../../Poly.Mcp/Docs/poly-dsl-guide.md).
- OWL `hasKey` is an identity-inference rule over named individuals, not a write-time rejection — [OWL 2 Direct Semantics, Table 9](https://www.w3.org/TR/owl2-direct-semantics/#Keys); its syntax is `HasKey(C (P1…Pm) (R1…Rn))` — [OWL 2 Quick Reference](https://www.w3.org/TR/owl2-quick-reference/). The table's condition requires `x` and `y` to be in `NAMED`, and object-property key values `zi` to be named individuals; anonymous individuals are outside the rule.

### Inferences
**Decision: one mechanism, two scopes.** Global `unique` is `unique_within(⊤)`, where `⊤` is the single implicit scope shared by all instances. Do not implement it by creating a fake root instance; keep the empty scope as a distinct case so a null parent never accidentally becomes global. The existing port's flat key is exactly the `⊤` case; a scoped key adds one component.

**Decision: a scoped unique is not a natural key.** `StorageAnalyzer.KeyProperty` must ignore scoped uniqueness; only a global `unique` qualifies. If the entity's only unique property is scoped, `KeyProperty` should be null and routes should use the synthetic `Id`. A route like `/api/users/{email}` is invalid for a per-tenant unique email; the correct product surface is either `/api/users/{id}` or `/api/tenants/{tenant}/users/{email}`. The latter is a route composition over two keys, not a change to the constraint.

**Decision: a scoped unique is not an alternate key.** EF Core's alternate key is a globally unique constraint that can be an FK target. A scoped key is not globally unique; its scope *is* an FK. Modeling it as an alternate key would either fail (duplicate values across scopes) or force a wrong composite key. Model it as a composite unique index on `(scope_fk, key)`, not as an alternate key.

**Decision: `HasIndex(...).IsUnique()` on one column cannot express a scope.** A scoped constraint needs a composite index `(scope_fk, key...)`; a scoped composite key needs `(scope_fk, k1, k2, ...)`. `StorageColumn.IsUnique` as a boolean is insufficient; the storage model needs an index/constraint record with a column list and an optional scope. This is the implementation seam the semantics implies.

**Decision: cardinality and ownership are orthogonal.**
- `one` scope does not imply unique keys; `many` inverse does not forbid them.
- `owned` is lifetime/minimum-one-owner, not uniqueness; a child can be owned and have duplicate keys.
- `unique_within` says nothing about how many children a parent may have; it only says how many children may share a key within that parent.

**Decision: do not import OWL `hasKey` semantics.** OWL `hasKey` concludes `x = y` for named individuals that share key values; it is a reasoning rule under the open-world assumption, with no null concept and no scope. Poly's `unique` is a closed-world write-time rejection (like a SQL unique index), and `unique_within` must stay that way: it rejects a state, it does not merge instances. Importing identity inference would silently collapse two business objects, which is a hard no for an operational domain.

**Decision: do not import SQL's null behavior wholesale.** SQL engines disagree: PostgreSQL and SQLite allow multiple nulls in a unique column; SQL Server allows at most one; `NULLS NOT DISTINCT` is opt-in in PostgreSQL. The language must define null as absence and require the pack to emit a matching mechanism (a partial/filtered unique index on `key IS NOT NULL`, or an equivalent), not inherit the engine's default.

### Gaps
- `StorageAnalyzer` has no way to distinguish a scoped unique from a global one today; every `c is UniqueConstraint` site would need a scoped case. If `unique_within` is modeled as a subclass of `UniqueConstraint`, those sites silently treat it as global — a concrete implementation trap.
- No repo test covers an entity whose only unique property is scoped, so the route-key regression is untested.
- SHACL/JSON Schema projections cannot express the scope; the `x-poly-*` vocabulary needs a documented shape.

---

## 11. The minimal teachable rule and the `.poly` surface

### Takeaway
**"Same parent, same value, at most one row. No parent or no value, no rule."** Everything else in this note is a consequence of that sentence.

### Cited Findings
- The DSL constraint table lists `unique` as a bare property keyword — [poly-dsl-guide.md](../../Poly.Mcp/Docs/poly-dsl-guide.md); the token is `Unique` — [DslGrammar.cs](../../Poly/DomainModeling/Language/DslGrammar.cs); the parser constructs `new UniqueConstraint()` — [DomainResolver.cs](../../Poly/DomainModeling/Language/DomainResolver.cs); the printer emits `"unique"` — [DomainDslPrinter.cs](../../Poly/DomainModeling/Language/DomainDslPrinter.cs).
- The guide's shipped-surface section describes the port check, the synthetic `Id`, the per-property unique index, and that duplicate values fail loud at the session port and the database — [poly-dsl-guide.md](../../Poly.Mcp/Docs/poly-dsl-guide.md). The 409 mapping for unique and link conflicts is in the API generator, not the guide — [MinimalApiGenerator.cs](../../src/Poly.Packs.Product/MinimalApiGenerator.cs).
- The repo's tenets favor the thinnest slice through the right seam, no framework for an imaginary second use, and guardrails only with real consumers — [AGENTS.md](../../AGENTS.md).

### Inferences
**Proposed surface (property-level):**

```poly
domain Billing

Customer: entity {
  Email: Text unique_within(tenant)
  tenant: Tenant
}

Invoice: entity {
  Number: Text unique_within(customer)
  customer: Customer
}

Product: entity {
  // Composite scope (tuple): unique within the same (tenant, region) pair.
  Sku: Text unique_within(tenant, region)
  tenant: Tenant
  region: Region
}
```

`unique_within` sits in the constraint position, like `range(...)` and `pattern(...)`. Parenthesized arguments keep it unambiguous against a bare `unique`. The printer must emit it verbatim, and the DSL guide's constraint table must gain a row (and a "not yet shipped" note until it is implemented).

**Do not ship yet (no consumer):**
- composite key tuples on one property (`unique_within(tenant) (first, last)`) — needs an entity-level form;
- case-insensitive or accent-insensitive variants — needs an explicit facet, not a flag;
- a required-navigation declaration — needs a separate nav-requiredness feature.

**The rule in four lines:**

| Two distinct rows? | Scope | Key | Result |
|---|---|---|---|
| yes | same scope instance | same value | violation |
| yes | same scope instance | different value | legal |
| yes | different scope instances | same value | legal |
| any | absent scope, or absent key | — | no rule |

**What it does not mean (teach these explicitly):**
- It does not mean "there must be one". That is existence/cardinality.
- It does not mean "the value is globally unique". That is `unique`.
- It does not mean "the parent owns the child". That is `owned`.
- It does not mean "the key is the route id". A scoped key is not a global identifier.
- It does not mean `"Sam"` equals `"sam"`. Equality is the canonical comparison; case is a separate, explicit decision.
- It does not mean an empty string is absent. Only null is absent.

**Authoring guidance.**
- Declare on the child (the many side), pointing at the one side.
- Use `create in parent { ... }` when create and scope must be atomic.
- Use global `unique` for a route/natural key; use `unique_within` for a business rule.
- If the business means case- or whitespace-insensitive, normalize at write; do not rely on the database.

**Agent rule of thumb.** If the sentence is "unique per X", write `unique_within(X)`. If the sentence is "unique", write `unique`. If the sentence is "exactly one per X", neither is enough — say so and ask for an existence constraint.

### Gaps
- The parser, printer, analysis metadata, storage model, port key, EF index, SHACL/JSON Schema annotations, and MCP docs all need new work; this note specifies only the semantics those changes must preserve.
- No decision has been made on the diagnostic code (`DMCS001` vs a new code) or on the exact runtime message; both are proposed above, not ratified.
- No decision has been made on whether a future entity-level composite key shares the `unique_within` name or gets a distinct `key (...)` form; the recommendation is a distinct form, but that is a language-surface question for the owner.
