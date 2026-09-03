# Fleet eval 2026-08-12 — agent findings (slice: Minimal API transport)

Slice: `src/Poly.DslCompiler/MinimalApiGenerator.cs`, `HttpFileGenerator.cs`, `DbmsPack`,
`DslCompiler.cs`.

Probes (all pass the `run-probe.sh` 0/0 gate — but that gate only compiles **entities**
mode; Program.cs is never compile-checked by it):
- `probes/fleet-eval/09-transport/warehouse.poly` — warehouse/truck/delivery aggregate, full
  constraint surface + enum props + child actions (0/0 entities).
- `probes/fleet-eval/09-transport/orders.poly` — customer/order/orderitem, `for` fan-out,
  shadow-keyed child (0/0 entities).
- `probes/fleet-eval/09-transport/clinic.poly` — patient/visit + doctor to-one + entity-ref
  action param (0/0 entities).

Each was additionally compiled as a real host: the generated 7-file solution copied into a
copy of `demo/Poly.RestApi` (ASP.NET + EF Core SQLite 10.0.10). Results: **all three
Program.cs files fail to compile** (CS0103/CS1660/CS0411/CS0100/CS0229). The oracle domain
(`DslCompilerCompileOracleTests`) compiles clean only because it avoids every failing shape.

## F1 — Child-entity action endpoints with parameters never declare the `dto` lambda param (CS0103)
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** transport (MinimalApiGenerator.AppendActionEndpointStatements)
- **Repro:** `probes/fleet-eval/09-transport/warehouse.poly` →
  `POST /api/warehouses/{code}/trucks/{vin}/load` emits
  `async (string code, string vin, FleetDbContext db) => { … entity.Load(dto.weight) … }`
  — `dto` is referenced but the child branch of the lambda never adds it.
- **Expected:** a child-entity action with parameters binds the `{ActionName}Dto` like the
  root branch does (`if (ia.Parameters.Count > 0) actionParams.Add("dto")`).
- **Actual:** `error CS0103: The name 'dto' does not exist` at `Program.cs(150,30)`.
  The child branch (`parentCtx is { } ctx`) only appends parentKey/key/db params; the root
  else-branch appends `dto`. Any non-root action with params → compile fail. Reproduced in
  all 3 probes (Truck.Load, Order.Cancel/ApplyDiscount, Visit.AssignDoctor).
- **Proposed patch:** in the `parentCtx` branch, add the `dto` parameter when
  `ia.Parameters.Count > 0` (same shape as the root branch).

## F2 — Parent+child both shadow-keyed produce a duplicate `id` lambda param (CS0100/CS0229)
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** transport (AppendChildEndpointStatements / AppendActionEndpointStatements)
- **Repro:** `probes/fleet-eval/09-transport/clinic.poly` — Patient and Visit have no
  `unique` prop, so both keys default to `id`. Routes emit
  `app.MapGet("/api/patients/{id}/visits/{id}", async (int id, int id, db) => …)`.
- **Expected:** distinct parameter names (e.g. parent key `pid`/`id`, child key `id`) or
  a scoped child-key name; route template `{id}/{id}` is ambiguous.
- **Actual:** `error CS0100: parameter name 'id' is a duplicate` + `CS0229: Ambiguity
  between 'int id' and 'int id'` (Program.cs lines 86, 183, 217). Same for action routes
  `/api/patients/{id}/visits/{id}/checkin`.
- **Proposed patch:** when `parentStore.KeyName == childStore.KeyName`, rename the child
  param/route token (e.g. child key `{childId}`), or qualify with the rel name.

## F3 — To-one relationship target routed as an aggregate child with Collection()/Any() (CS1660/CS0411)
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** transport (child list/detail + child-action membership check)
- **Repro:** `probes/fleet-eval/09-transport/clinic.poly` — `Visit.primaryDoctor: Doctor`
  (to-one). The aggregate pass makes Doctor a child of Visit; the generator emits
  `db.Entry(parent).Collection(e => e.PrimaryDoctor)` and `parent.PrimaryDoctor
  .FirstOrDefault()` / `.Any(e => e == entity)` on a **reference** nav.
- **Expected:** to-one navs are not collections — child endpoints should either be omitted
  for to-one children or use `Reference()`/direct access. (Guide §4: `one` = default.)
- **Actual:** `error CS1660: Cannot convert lambda expression to type 'INavigationBase'`
  (x2) + `error CS0411: type arguments for FirstOrDefault/Any cannot be inferred`
  (Program.cs lines 59, 68-69, 110-111).
- **Proposed patch:** in `AppendChildEndpointStatements` and the child-action membership
  check, branch on `ReferenceNavigations` vs `CollectionNavigations`; skip child
  list/detail emission when the parent link is to-one, or emit a detail-only route.

## F4 — Child detail route ignores the `{id}` when the child has a shadow key (returns first child)
- **Signal:** silent-gap / wrong-result (export/runtime divergence)
- **Severity:** 🟠
- **Slice:** transport (AppendChildEndpointStatements else-branch)
- **Repro:** `probes/fleet-eval/09-transport/orders.poly` →
  `GET /api/customers/{email}/orders/{id}` emits
  `var child = parent.Orders.FirstOrDefault();` — the `{id}` route value is never used.
  Same in clinic (`parent.Visits.FirstOrDefault()`) and in the shipped Library demo
  (`parent.Loans.FirstOrDefault()`).
- **Expected:** return the child matching `{id}` (or 404).
- **Actual:** any `{id}` returns the first child — wrong record, no failure.
- **Proposed patch:** filter the loaded collection by `childKeyPropName == childKey`
  (`parent.Orders.FirstOrDefault(c => c.Id == childKey)`).

## F5 — CS8602 nullable back-ref dereference in child-detail Where path (0-warning gate violation)
- **Signal:** compile warning in generated Program.cs (hosts gate on 0 warnings)
- **Severity:** 🟠
- **Slice:** transport (child detail back-ref filter)
- **Repro:** `probes/fleet-eval/09-transport/warehouse.poly` →
  `db.Trucks.Where(e => e.Warehouse.Code == code)` where the generated `Truck.Warehouse`
  is `Warehouse?` → `warning CS8602: Dereference of a possibly null reference` at
  `Program.cs(49,146)`.
- **Expected:** `e.Warehouse!.Code` or a null guard; the oracle test asserts
  warnings are empty.
- **Actual:** the generated Program.cs ships with a CS8602 warning. The Library oracle
  avoids it only because its child detail takes the FirstOrDefault (else) branch.
- **Proposed patch:** emit `!` on the back-ref deref in the Where lambda, or compare
  against the FK directly.

## F6 — Grandchild entities (child of non-root) are orphaned: no CRUD, actions exposed at root scope
- **Signal:** silent-gap + security (no parent verification)
- **Severity:** 🟠
- **Slice:** transport (aggregate parent resolution for non-root parents)
- **Repro:** `probes/fleet-eval/09-transport/warehouse.poly` — `Delivery` (child of
  `Truck`, which is child of `Warehouse`) gets **no** child endpoints and **no** CRUD,
  but its action is emitted as a root-scoped route
  `POST /api/deliverys/{id}/confirm` (no parent-truck membership check).
  `OrderItem` in orders.poly gets no endpoints at all (demo.http prints an empty section).
- **Expected:** either full child routing under the non-root parent or a loud
  DMAGG001-or-more failure; an action must not float to root scope silently.
- **Actual:** the aggregate pass only assigns a parent when the parent is a root
  (`parentAgg.IsRoot`), so grandchildren stay orphaned (DMAGG001 is a warning only).
  The transport then falls back to root-style action routes with no parent scoping.
- **Proposed patch:** fail loud (compile-time error) for non-root entities with no
  aggregate parent when they have actions, or generate scoped routes through the chain.

## F7 — Seed silently inserts nothing when sample values violate declared constraints
- **Signal:** silent-gap
- **Severity:** 🟠
- **Slice:** transport (MakeSampleValue + SeedAsync)
- **Repro:** `probes/fleet-eval/09-transport/warehouse.poly` — seed emits
  `Warehouse.Create(1, "XXXXXXXX", "XXXXXXXX", "Sample", …)`. `Code` requires
  `pattern("^WH-[0-9]{3}$")` and `Zip` requires `^\d{5}$`; both sample values fail,
  so `Warehouse.Create` returns Failure and the `if (result.IsSuccess)` guard silently
  skips — the DB starts empty, no error, no data.
- **Expected:** seed values satisfy the entity's own constraints (MakeSampleValue honors
  `pattern`, or the seed fails loud).
- **Actual:** pattern-constrained root entities silently seed nothing.
- **Proposed patch:** MakeSampleValue: match `PatternConstraint` (emit a conforming
  example) and honor `length` min; or have SeedAsync report failed creates.

## F8 — demo.http create/action bodies fail the DTO validation attributes the generator itself emits
- **Signal:** guide-drift / product (API unusable via shipped demo.http)
- **Severity:** 🟠
- **Slice:** transport (HttpFileGenerator.GetExampleJsonValue / transport params)
- **Repro:** `probes/fleet-eval/09-transport/warehouse.poly` demo.http —
  create body `{"Capacity": 0, "Code": "example-code", "Name": "sample", "Zip": "sample"}`
  vs the generated `WarehouseDto` `[Range(1,10000)]`, `[Required]`,
  `[RegularExpression("^WH-[0-9]{3}$")]`, `[RegularExpression("^\\d{5}$")]` →
  every POST is a 400. `RegisterTruck` body `{"vin":"sample","maxLoad":0}` fails
  `[MinLength(17)]`/`[Range(1,40000)]` too.
- **Expected:** demo.http bodies round-trip through the DTO validation (guide: the API
  boundary enforces the domain envelopes; the demo should demonstrate a working call).
- **Actual:** demo.http create requests are guaranteed rejected by the generated DTOs.
- **Proposed patch:** example-value generation must satisfy the same constraints the DTO
  emitter applies (range/length/pattern-aware sample values).

## F9 — Action DTO for an arithmetic assign (`assign Capacity to Capacity + delta`) gets no [Range]
- **Signal:** sharp / validation gap
- **Severity:** 🟡
- **Slice:** transport (GetActionParamImplicitConstraints — only bare-param RHS matched)
- **Repro:** `probes/fleet-eval/09-transport/warehouse.poly` —
  `AdjustCapacity: action (delta: Number) { assign Capacity to Capacity + delta }` with
  `Capacity: Number range(1, 10000)`. `AdjustCapacityDto.delta` is emitted with **no**
  `[Range]`, and the generated `AdjustCapacity` action does not re-validate the result —
  `delta = -100000` drives `Capacity` out of the declared envelope through the API.
- **Expected:** per guide §11 "a parameter that flows into a constrained property inherits
  that property's constraints" — at least a bounded attribute or a loud analysis error for
  unprovable arithmetic flows; the domain envelope should not be silently breakable.
- **Actual:** no attribute, no validation, envelope drift. (Direct `assign Prop to param`
  works — `ApplyDiscountDto.amount` gets `[Range(0,100)]` — the arithmetic sibling does
  not.)
- **Proposed patch:** either derive a conservative delta bound from the target's verified
  range, or fail-loud in the generator when an unconditional assign mixes the param with
  arithmetic.

## Gate note (meta)
`scripts/run-probe.sh` compiles **entities mode only** — it never generates or compiles
`Program.cs`/`demo.http`, so the 0/0 gate does not cover this slice at all. The
`DslCompilerCompileOracleTests` compile oracle uses one fixed Library domain that dodges
every failing shape above (no child action params, no shadow-shadow keys, no to-one
aggregate child, natural-keyed parent only).

## F10 — demo.http action routes use camelCase while Program.cs routes are lowercased (cosmetic)
- **Signal:** guide-drift (cosmetic; no functional failure — ASP.NET routing is case-insensitive)
- **Severity:** 🟡
- **Slice:** transport (HttpFileGenerator vs MinimalApiGenerator route casing)
- **Repro:** `warehouse.poly` demo.http emits `POST .../adjustCapacity` while Program.cs
  registers `/api/warehouses/{code}/adjustcapacity` (ToCamelCase().ToLowerInvariant()).
- **Expected:** the demo.http requests should mirror the exact registered route.
- **Actual:** casing differs (`adjustCapacity` vs `adjustcapacity`). Works at runtime due
  to case-insensitive matching, but drift makes the .http file misleading.
- **Proposed patch:** use the same casing transform in both generators
  (ToCamelCase(ia.Name).ToLowerInvariant() in HttpFileGenerator).
