# A type system owns its definitions, vocabulary and rules

Status: **Approved, 2026-11-19** (revision 2 at `4434f1223`, with the three rulings below folded in). Stream
`stream/introspection` (#998, draft PR #997), base `dfafe16b4`. Revision 1 was titled "Introspection is structure,
identity and providers".

## Rulings this revision records

Owner, 2026-11-19:

- "43 members on the thing that *is* the definition of a type is wrong."
- "The root cause is that we never accepted we need a 'this is a type system' abstraction. It prescribes a set of
  `ITypeDefinition`s and also has mechanisms to describe the other aspects of a particular platform or environment."
- "A type declaring its own implicit and explicit conversions is correct, so those stay on `ITypeDefinition`. The
  missing piece is the type system describing its global type promotion and implicit conversion rules … The type
  system is also the authority on mixed pairs and binary-operator promotion."

Owner, approving revision 2:

1. Declared operators stay on the definition, like declared conversions.
2. The target system decides mixed pairs. Row T3 may retarget `MixedPairAuthorityTests`; this ruling is the
   sign-off.
3. Every rule implements a common base interface, `ITypeSystemRule`, even while it is empty. `Rule<T>()` and
   registration are constrained to it. Guards check that every rule implements it, that Introspection declares no
   rule (owners live outside Introspection), and that the base adds no classification member.

Owner, during T6 and T7 (2026-11-19):

4. The compatibility analyzer's Date, Guid and byte[] categories match the CLR's own definitions (the CLR host's
   table), not a C# rule; the category table keeps its removing row, I8b (#972). `ICSharpTypeRules` keeps three
   members: `SimpleTypeOf`, and `IsEnumType`/`HasValueSemantics` (named so the architecture gate's text match on
   `System.Type`'s `IsEnum`/`IsValueType` does not count them).
5. `string` and `object` are found by keyword identity: the definition the target's word `string`/`object` binds.
6. T6 is split into T6a (the rule, constant folding, `DateOffsetRule`, the shift policy), T6b (the category table)
   and T6c (the product pack's scalar rule and the indexer pick). C#'s `SimpleType` (§8.3.5 plus `nint`/`nuint`)
   overlaps the CLR host's own table for numbers: an interim rule removed by T10.
7. Constraint applicability follows what the domain word binds, for the analysis and the emitted guards together:
   every domain number word (Number, Int, Int64, Int32, Decimal, Float, Double) enforces its range and every text
   word its length and pattern. Today Int64, Int32, Float and Double ranges are not enforced at run time; after
   T7 they are, and a write outside them fails. (Found during T7: the DSL's number keywords are Number and Decimal,
   so these words reach a domain that declares them as primitives through the API or an MCP change.)

Owner, during T8 and T9 (2026-11-19):

8. T8 runs in three steps: lower the ratchet first (red); retarget every surviving fact to its owner, deleting nothing
   (T8a); then delete the members, removing only the tests that can no longer compile, each named with why nothing else
   covers it (T8b).
9. `byte[]`'s element type stays `byte` (C#'s rule: `foreach` over `byte[]` keeps working); the binary exclusion stays
   in the compatibility analyzer's collection match and the CLR host's table. T9's pin is amended accordingly.
10. T9 is split (advisor, 2026-11-19) so each slice is green and leaves no interim of its own: T9a adds `System` and
    moves identity to the system; T9b moves the three doors and removes the optional-system helpers; T9c moves
    `NullableOf` and the merged `ElementType` and removes the `IClrTypeDefinition` marker. Interim, removed by T12 (the
    analysis holds a target system): C#'s own declarations (`AstTypeDefinition` and its synthetic collections and generic
    parameters, the domain's synthetic declarations) belong to the shared C# system, not to a per-unit layer.

The recount at `dfafe16b4` is **42** members, not 43: 13 structural, 5 internal relations, 21 classification answers
and 3 conversion/operator doors.

## Why

At `16a62dcc8` (2026-02-01), `ITypeDefinition` described structure only. `TypeCategory` arrived in `80b0d50ae`
(2026-02-02) so validation could decide which constraint applies to which property type. With no type-system object
to own that question, it went on the definition, and it has stayed there, changing form each round: flags, then
`TypeKind` and `PrimitiveType`, then facts, then 21 booleans.

The rules suffered the same way. Promotion, widening, lifting and mixed-pair authority had no owner, so they were
spread over `ClrBuiltInRules`, `ClrOperatorResolution`, `ModeledConversions`, the domain's `OperatorTable`, and a
convention that "the left operand's platform decides" (`MixedPairAuthorityTests`).

**Decision.** Introduce the type system as the object that owns:

- its definitions and how they are resolved;
- its vocabulary;
- its rules;
- the questions its consumers need answered.

`ITypeDefinition` keeps only what a type declares about itself, and a back-reference to its system.

## 1. `ITypeDefinition`: what a type declares (10 members)

| # | Member | Why |
|---|---|---|
| 1 | `ITypeSystem System` | How a definition reaches its rules (§2.4). |
| 2 | `string Name` | Every diagnostic, printer and lookup. |
| 3 | `string? Namespace` | Sources list namespaces; qualified names. |
| 4 | `string FullName` (default body) | Display and qualified lookup. Not identity. |
| 5 | `ITypeDefinition? BaseType` | Declared hierarchy. |
| 6 | `IEnumerable<ITypeDefinition> Interfaces` | Declared hierarchy. |
| 7 | `IEnumerable<IParameter> GenericParameters` | Declared arity and parameters. |
| 8 | `IEnumerable<ITypeMember> Members` | Declared members; `Fields`, `Properties`, `Methods` and `Constructors` become views over it. |
| 9 | `IEnumerable<ITypeConversion> Conversions` | **Declared** conversions only: the type's own `op_Implicit`/`op_Explicit` (owner ruling). Built-in conversions leave this list. |
| 10 | `IEnumerable<ITypeOperator> Operators` | **Declared** operators only (`op_Addition` style), owner ruling 1. Built-in operators leave this list. |

Identity, nullability, element type, assignability, and conversion and operator *resolution* are rules, so they move
to the system. The contract declares no `bool`, `int` or enum property.

## 2. The type system: `ITypeSystem`

**Name.** I recommend keeping `ITypeSystem`. "Type system" is the standard term, and it says the object prescribes
types and their rules. `IPlatform` is too broad: platform also means the runtime and the host. `ITypeUniverse`
names only the definitions.

```csharp
public interface ITypeSystem {
    ITypeSystem? Target { get; }                                   // the system this one is layered on, or null

    // Definitions
    IReadOnlyList<ITypeSource> Sources { get; }                    // what it contains (#998 S1)
    ITypeDefinition? Resolve(string qualifiedName);                // bare names stay the analysis scope's (S2a)

    // Vocabulary
    ITypeDefinition? Bind(string word);                            // "int" → Int32; "Number" → long
    string? Spell(ITypeDefinition definition);                     // the reverse, for writers and diagnostics

    // Rules
    bool IsSame(ITypeDefinition a, ITypeDefinition b);
    ITypeDefinition? NullableOf(ITypeDefinition definition);
    ITypeDefinition? ElementType(ITypeDefinition definition);
    bool IsAssignableWithoutConversion(ITypeDefinition destination, ITypeDefinition source);
    ITypeConversion? Conversion(ITypeDefinition source, ITypeDefinition destination);
    ITypeOperator? Operator(OperatorKind kind, ITypeDefinition left, ITypeDefinition? right);

    // Questions its consumers need answered (§3)
    TRule? Rule<TRule>() where TRule : class, ITypeSystemRule;
}

public interface ITypeSystemRule { }   // the base every rule implements (owner ruling 3); empty, and kept free of classification

public sealed class TypeSystemRules {  // registration, held by a system
    public void Add<TRule>(TRule rule) where TRule : class, ITypeSystemRule;
    public TRule? Find<TRule>() where TRule : class, ITypeSystemRule;
}
```

That is 12 members (revision 2 said 13; the recount in T1 is 12). A layered system answers what it owns and asks its `Target` for the rest.

### 2.1 What it absorbs

| Today | In the type system |
|---|---|
| `ITypeDefinitionProvider` (by name) | `Resolve`. The by-`Type` lookup stays inside the CLR source. |
| `TypeDefinitionProviderCollection` (a fallback stack) | Layering through `Target`; a scope reads the systems' `Sources`. |
| `ITypeSource`, `ClrAssemblySource`, `AstTypeRegistry`, `ClrTypeDefinitionRegistry` | `Sources`. |
| C# keyword table (`CSharpTypeNames`) | The C# system's `Bind`/`Spell`. |
| `DomainTypeMapping` names; the domain prelude (S3) | The domain system's `Bind`/`Spell`. |
| `ClrBuiltInRules` (cites C# §10, §12) | The C# system's promotion, widening, lifting and built-in implicit conversions, inside `Conversion`/`Operator`. |
| `ClrPrimitive` | The C# system's own question: the C# simple types (§3.3). |
| `ClrOperatorResolution` | The C# system's `Operator`. |
| `ModeledConversions` | The C# system's `Conversion`, which reads both ends' declared `Conversions`. |
| `OperatorTable`, `DomainTypeModel.Assign` (Q6 numeric rules) | The domain system's `Operator`/`Conversion`, layered on C#. |
| `IClrTypeDefinition.RuntimeType`, `ClrTypeReferences` | Stay inside the C# system and the VM. |

### 2.2 Conversion resolution

`system.Conversion(source, destination)` combines two kinds of input:

- **The system's global rules:**
  - identity and reference widening (`IsAssignableWithoutConversion`);
  - implicit numeric conversions and the platform's other built-in implicit and explicit conversions;
  - nullable lifting (C# 10.6.1, `S → T?` and `S? → T?`).
- **The conversions the two types declare,** taken from `source.Conversions` and `destination.Conversions`, as
  C# 10.5 gathers user-defined candidates from both ends.

The C# system's order is the one `ClrTypeDefinition.ResolveConversionFrom` uses today, moved, not invented:

1. A declared conversion that names a method wins over a built-in.
2. Implicit is preferred to explicit.
3. The native-integer exception (C# 11) keeps its place.

### 2.3 Binary-operator promotion and mixed pairs

`system.Operator(kind, left, right)` gathers candidates and decides:

- **Candidates:** the operators both operand types declare, plus the system's predefined operators.
- **Promotion:** the system's own binary promotion (C# §12.4.7 binary numeric promotion).
- **Choice:** the system's own better-function rule.

**Which system decides `clrInt + ledgerDecimal`?** The system the unit is analysed for: the analysis' **target
system**. It is never the left operand's.

- It reads `Operators` declared by both operand types, whichever system each comes from.
- It applies its own promotion and predefined operators.
- The ledger's built-in arithmetic applies only if the ledger system is the target, or is layered under it.
- If no candidate is chosen, there is no operator, and analysis fails closed with a positioned error.

So the same system decides in either operand order. A declared operator still applies only in its own parameter
order, and whether an operand applies to a parameter is the target's own `Conversion`, never the parameter type's
door: otherwise a foreign system would decide one level down (advisor, T3). Today's "left operand's platform answers"
(I4) let a foreign left operand, or a foreign destination, decide its pair. Owner ruling 2 approves retargeting
`MixedPairAuthorityTests` in row T3.

### 2.4 How a definition reaches its system: a back-reference

`ITypeDefinition.System` is set by the system that created the definition.

The alternative, a lookup by identity, needs a registry of every system, which is the process-wide lookup S2a
removed. It also cannot say which system an AST declaration was declared into.

It is added in row T9, together with identity leaving the definition; nothing needs it before then. What the
back-reference costs:

- Each of the 4 production implementers and 7 test doubles carries one reference.
- Test doubles share a `TestTypeSystem`.
- CLR definitions are cached process-wide, so their `System` is the one shared CLR-backed C# system. A unit's
  declarations live in a thin layer whose `Target` is that shared system.

Identity is the owning system's: `IsSameType(a, b)` is `a.System == b.System && a.System.IsSame(a, b)`. Two systems
never share a definition.

## 3. Classification without a closed list

Neither `ITypeDefinition` nor `ITypeSystem` has a `Kind`, a category or a classification flag. A consumer that needs
to classify has three ways to get an answer, and Introspection declares none of the questions:

1. **Ask the vocabulary.** "Is this the definition my word binds?" uses `Bind` and `IsSame`.
2. **Ask a question the system owns.** The system implements an interface its own language defines, for example
   the C# system's `ICSharpTypeSystem`.
3. **Register a rule.** A library adds a rule object to the system when the session builds it
   (`TypeSystemRules.Add<TRule>(rule)`), and a consumer asks for it.

Every rule, whether owned by a system's language (2) or registered (3), is an interface that extends
`ITypeSystemRule` and is declared outside `Poly.Introspection`. `Rule<T>` and `Add<T>` accept nothing else.

`Rule<TRule>()` returns a registered rule, else the system itself if it implements `TRule`, else
`Target?.Rule<TRule>()`. A null answer means the system has no rule, and the consumer treats the type as not
applicable (#998 Q5).

How this differs from `TypeFacts`:

- A question is declared by the consumer or by the language that owns it, never by Introspection, and it extends
  `ITypeSystemRule`. Guards enforce both (§5).
- The system is asked, not the definition.
- The answer is behaviour, not a record of data about the type.

### 3.1 The domain's `Number` family (vocabulary)

```csharp
// Domain system. The words are the DSL's own (DSL guide, built-in types); the result is a set of definitions.
var numbers = new[] { "Number", "Int32", "Decimal", "Float" }.Select(domain.Bind);   // bound on its target (C#)
bool isNumber = numbers.Any(n => domain.IsSame(n, definition));
```

`nint`, `byte` or a ledger decimal joins the family only if the domain binds a word to it, so the family cannot
widen by accident. `DomainGround` is deleted. Grouping words into families is the domain language's rule, private to
the domain system.

### 3.2 Validation applicability (registered rule)

Constraints come from domain libraries: `range` and `length` from core, `unique` from `uses persistence`. The
question is the constraint owner's:

```csharp
// Declared in DomainModeling, registered by each library that contributes a constraint.
interface IConstraintApplicability : ITypeSystemRule { bool Applies(Constraint constraint, ITypeDefinition propertyType); }

rules.Add<IConstraintApplicability>(new CoreConstraintApplicability(domain));   // range → number words, length and pattern → Text
bool applies = domain.Rule<IConstraintApplicability>()?.Applies(range, propertyType) ?? false;
```

This replaces `GuardClosedProperties`' name strings (`typeName is "Number" or "Int" or "Decimal"`). The type is never
asked what it is.

### 3.3 Constant folding's integer width (a question the system owns)

```csharp
// ICSharpTypeRules : ITypeSystemRule, declared by the C# surface; SimpleType is C#'s own closed list (C# §8.3.5).
int? width = target.Rule<ICSharpTypeRules>()?.SimpleTypeOf(definition) switch {
    SimpleType.Byte => 8, SimpleType.UShort => 16, SimpleType.UInt => 32, SimpleType.ULong => 64, _ => null };
```

If the target is not a C# system, the answer is null and the fold is skipped. That is sound, because folding is an
optimisation. `SimpleType` (today's `ClrPrimitive`) is a closed list, but it is the C# specification's, it lives on
the C# system, and the neutral contracts never name it.

### 3.4 Shape (replaces revision 1's "CLR or AST" helper)

Enum and value-type checks are C# rules (C# struct equality, enum comparison). The C# system answers
`ICSharpTypeRules.IsEnum` and `IsValueType` for its own definitions, both CLR and C#-declared. The domain's Entity,
Value and Enum families are the domain system's, because they are its declarations. A definition from any other
system is never asked these questions.

## 4. The systems we have

**C# over the CLR.** The CLR supplies definitions: reflection (`ClrAssemblySource`, the registry). C# supplies the
vocabulary (keywords) and the rules (`ClrBuiltInRules` cites the C# standard), so the system is C#'s and the CLR is
its source. VB over the same CLR would be a different system with different rules. There is one shared CLR-backed
C# system, and each C# unit adds a layer holding its own declarations.

**The domain DSL: its own system, layered on C#.** It is more than a vocabulary, because its rules differ from C#'s:

- Q6: `Number / Number` is integer division with a warning when assigned to `Decimal`, and `Decimal → Number` rounds
  only when analysis proves it safe.
- The domain has its own assignment matrix.
- Its entity, value and enum declarations are its own.

It binds its words to the target's definitions and adds no ground types of its own. It answers its own rules, and
everything else (member lookup, built-in conversions it does not override) goes to its `Target`, the C# system.

**AST-declared types: not a system.** They are definitions declared *into* a system. A C# unit's classes join that
unit's C# layer, and the domain's entities and values join the domain system. `AstTypeRegistry` becomes that layer's
source, and `AstTypeDefinition.System` points at it.

**`LedgerHost` (test only).** A base system with no target:

- one source (`amount`, `account`, `entry`);
- no keywords;
- its own `Operator` for `amount + amount`;
- its own rule, `ILedgerRules : ITypeSystemRule` with `MinorUnits(definition)`.

It is the second system the mixed-pair and fail-closed tests run against.

## 5. Guards

All of these extend the existing `TypeShapeTests.Contract_DeclaresNoClosedListOfKinds`:

- **`ITypeDefinition_IsExactlyTheDecidedMembers`:** a ratchet. It is pinned at today's 42, each row removes names,
  and it ends at §1's 10.
- **`ITypeDefinition_StatesNoClassification`:** a ratchet over the `bool`, `int` and enum properties. It is pinned at
  today's 23 (the 21 answers, `IsCollectionShape`, `AccessModifier`) and only shrinks; it ends empty.
- **`ITypeSystem_IsExactlyTheDecidedMembers`:** pinned at §2's 12 from the row that adds it.
- **`ITypeSystem_ClassifiesNothing`:** no member takes exactly one `ITypeDefinition` and returns `bool`, `int` or an
  enum.
- **`Rules_AreConstrainedToTheBase`:** `ITypeSystem.Rule<T>`, `TypeSystemRules.Add<T>` and `Find<T>` constrain `T`
  to `ITypeSystemRule`. The constraint is what makes every rule implement the base, and the test stops it loosening.
- **`Introspection_DeclaresNoRule`:** no type in `Poly.Introspection` or its sub-namespaces implements
  `ITypeSystemRule`, other than the base itself. Rule owners live outside Introspection.
- **`ITypeSystemRule_ClassifiesNothing`:** the base declares no member that takes one `ITypeDefinition` and returns
  `bool`, `int` or an enum.

## 6. Migration rows

Each row begins with a failing test, run red and recorded. Each ends with the full suite green and Benchmarks at
0 warnings. Interim rules name their removing row.

| Row | Change | Failing test first |
|---|---|---|
| T0 | The `ITypeDefinition` ratchet and no-classification guards, pinned at 42. | A scratch 43rd member fails the ratchet (recorded, then removed). |
| T1 | `ITypeSystem`, `ITypeSystemRule`, `TypeSystemRules` and their guards. The shared C# system wraps today's pieces (keyword table, registry, `ClrBuiltInRules`, `ClrOperatorResolution`, `ModeledConversions`). **Interim:** its `Conversion`/`Operator` delegate to today's definition doors. Removed by T9. | `csharp.Bind("int")` is `Int32`, `Spell` round-trips every keyword, and `Conversion`/`Operator` agree with today's doors on the full numeric matrix. |
| T2 | `LedgerHost` becomes a system with its own operator and its own rule. | `ledger.Rule<ILedgerRules>()` answers; `csharp.Rule<ILedgerRules>()` is null. |
| T3 | The target system decides mixed pairs (§2.3, owner ruling 2); `MixedPairAuthorityTests` is retargeted after the advisor consult. | A foreign definition whose own door answers any `Add` or any conversion does not decide `third + long` or `long → third`; the target does (they differ today). The planned `clrInt + ledgerAmount` case was already symmetric, since the ledger declares no operators. |
| T4a | The analysis' target system is an analysis setting (`AnalysisSettings.With<ITypeSystem>`, else C#; `AnalysisContext.TypeSystem`). Every analysis-time operator lookup asks it: compatibility, type resolution, constant folding, the domain's expression typing. | An analysis whose target is the ledger refuses `1 + 2` and `-1` (today the CLR definition answers). |
| T4b | Conversion and assignability in the analysis and the domain type model ask the target: `IsAssignableFrom`, overload scoring and `GetCommonType` take the system. **Interim:** a call that passes no system still asks the definitions' doors; removed by T9, where the argument becomes required. | Under a ledger target, `x = 1L; x = 2` and `Math.Max(1L, 2)` are refused (C# widens int to long). |
| T4c | The VM asks the analysis' target system (`AnalysisResult.TypeSystem`): the lambda bridge's operators, conversions and common type. A run-time store's implicit conversion (`LocalType.ConvertImplicitly`) is C#'s: a `VmProgram` carries no analysis and every value it stores is a CLR value. | A pure lambda over a ledger-target analysis gets no CLR operator. |
| T4d | The domain type model (`DomainTypeModel.Assign`) asks the session's target system. | The domain's assignment verdict changes with the target system, and so do its operator stamps (T4a migrated them; T4a's review asked for their target-system test here, where the domain session gets its target). |
| T5 | The domain system is layered on C#. `Bind` uses `DomainTypeMapping`'s table, `DomainGround` is deleted, and families come from §3.1. **Interim:** that table stands in for the prelude. Removed by #998 S3. | A 64-bit integral definition that no domain word binds is not in the Number family (today it is `Int64`). |
| T6 | `ICSharpTypeRules : ITypeSystemRule` (`SimpleTypeOf`, `IsEnumType`, `HasValueSemantics`; ruling 4). The analyzer, constant folding, `DateOffsetRule`, the shift policy and `Classify` ask it. Split into T6a, T6b and T6c (ruling 6). | A ledger definition is not folded and is `Cat.Unknown` (today `IsNumber` makes it `Cat.Number`). |
| T7 | `IConstraintApplicability` is registered; `GuardClosedProperties` and the domain's `Family` ask the domain system. | `range` on a property bound to `Number` applies, and on one bound to `nint` does not, whatever the spelling. |
| T8 | Delete `IsGround()`, the 21 answers and `ClrGround`. Ratchet 42 → 21. | The ratchet is lowered first (red). |
| T9 | `ITypeDefinition.System` is added (§2.4); `IsSameTypeAs`, `NullableOf`, `ElementType` (merging `IsCollectionShape` and `SequenceElementType`) and the three doors leave the definition. The T1 interim is removed. Ratchet 21 + 1 − 7 = 15. Split into T9a, T9b, T9c (ruling 10). | Pins first: `byte[]` element type is `byte` (ruling 9) and `List<int>` is `int`. Then the ratchet. |
| T10 | `Conversions` and `Operators` list only declared ones; built-ins move into the C# system's rules. | `Clr<int>().Conversions` contains no built-in (it lists `int → long` today), with resolution unchanged. |
| T11 | Typed member views become extensions, the CLR's `Members` includes constructors, and `AccessModifier` moves to the hosts. Ratchet → 10. | A CLR type's `Members` contains its constructors (fails today). |
| T12 | `ITypeDefinitionProvider` and `TypeDefinitionProviderCollection` are retired: `AnalysisContext` holds a target system and the scope reads its `Sources`. | An analysis given only the ledger system resolves no CLR name (today the stack falls back to the CLR). |

Not in scope: the analyzer's own `Cat` table (it keeps its removing row, I8b, #972), the 74 production callers of
`ClrTypeReferences`, and the domain's storage mappings (`ToSqlColumnType`).
