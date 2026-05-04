# Library Domain Implementation - Roadblocks and Missing Features

## Successfully Implemented
1. **Primitives**: string, int, bool, instant, date, isbn, email, phone, decimal
2. **Entities with inheritance**: Person → Member, Person → Librarian
3. **Stages**: Loan (Active, Overdue, Returned, Renewed, Lost), Reservation (Waiting, Ready, Fulfilled, Cancelled, Expired), Book (Available, OutOfStock, Reserved, Archived)
4. **Events**: BookCheckedOut, BookReturned, ReservationReady, FineIssued, BookAdded
5. **Basic Actions**: CheckoutBook, ReturnBook, RenewLoan, ReportLost, ReserveBook, CancelReservation, FulfillReservation, MarkAsReady, IssueFine, PayFine, AddBook, ArchiveBook
6. **Relationships**: MemberLoans, BookLoans, LibrarianLoans, MemberReservations, BookReservations, MemberFines, BookCategories, LoanFines
7. **Policies**: MaxBooksPolicy, RequireValidISBN, ActiveMembershipRequired

## API Gaps and Roadblocks

### 1. Cross-Entity Property Modification (Mutation Effects)
**Issue**: The DomainModeling system lacks a straightforward way to modify properties on OTHER entities from an action.

**Example**: When `CheckoutBook` is executed on a Loan, it should decrement `Book.AvailableCopies`. Similarly, `ReturnBook` should increment `Book.AvailableCopies`.

**What I tried**:
- Attempted to use `Assign` effect from `Poly.Data.Modeling.Effects.Mutations`
- `Assign` requires `DomainValue Target` and `DomainValue Value`
- `DomainValue` is abstract - cannot instantiate directly
- `Property` inherits from `DomainValue`, but I can only reference properties on the SAME entity (Loan), not on Book

**Workaround**: None found - the current API seems to only support modifying properties on the same entity that owns the action.

**Suggested Fix**: Either:
- Add support for cross-entity property references in `Assign` effect
- Or add a new effect type like `CrossEntityMutation` that can target properties on related entities
- Or support navigation through relationships (e.g., `Loan.Book.AvailableCopies`)

### 2. Dynamic Value Calculation
**Issue**: Cannot compute dynamic values in effects (e.g., increment a counter, calculate a new date).

**Example**: `RenewLoan` should:
1. Increment `RenewalCount` by 1
2. Extend `DueDate` by a certain number of days

**What I tried**:
- Attempted to use `Assign` with a calculated value
- No way to express "current value + 1" or "current date + 14 days"

**Workaround**: None - had to omit these effects.

**Suggested Fix**: Add support for expressions in `Assign` effect:
```
new Assign(domain) {
    Target = loan.Property("RenewalCount"),
    Value = Expression(loan.Property("RenewalCount") + 1)
}
```

### 3. Conditional Effects
**Issue**: Cannot express conditional logic in effects.

**Example**: `ReportLost` should:
1. Transition Loan to Lost stage
2. Decrement Book.TotalCopies
3. Issue a Fine (create Fine entity + publish FineIssued event)

**What I tried**:
- Looked for `ConditionalEffect` or similar
- Found `Poly.Data.Modeling.Effects.Conditional` class exists!

**Workaround**: Could use `Conditional` effect, but the API for expressing conditions and then executing effects is not clear from the existing examples.

**Suggested Fix**: Provide clearer examples of how to use `Conditional` effect in the InteractiveDomainConsole or documentation.

### 4. Entity Creation with Initial Property Values
**Issue**: When using `CreateEntityInstance` effect, there's no way to set initial property values on the created entity.

**Example**: `CheckoutBook` creates a Loan entity, but how do we set `LoanDate`, `DueDate`, `LoanDate`?

**What I tried**:
- Used `CreateEntityInstance` effect
- No way to bind initial property values

**Workaround**: None - the created entity has default values.

**Suggested Fix**: Add support for setting initial property values in `CreateEntityInstance`:
```
new CreateEntityInstance(domain) {
    EntityType = loan,
    InitialStage = activeStage,
    InitialProperties = {
        { "LoanDate", someValue },
        { "DueDate", someOtherValue }
    }
}
```

### 5. InvokeAction Parameter Binding
**Issue**: When using `InvokeAction` to trigger another action, binding parameters is unclear.

**Example**: `FulfillReservation` triggers `CheckoutBook`, but how do we pass the BookTitle and MemberName?

**What I tried**:
- Used `InvokeAction` effect
- Has `BindParameter` method, but requires `Property` from the target action

**Workaround**: Partially works, but the binding syntax is not intuitive.

**Suggested Fix**: Simplify the parameter binding API or provide better examples.

## Compilation Errors Encountered
1. `TypeCategory` not found - needed to add `using Poly.Introspection;`
2. `OfType` not found - needed to add `using System.Linq;`
3. `DomainValue` cannot be instantiated - it's abstract, need to use `Property` or find another way
4. `Console` not found - needed to add `using System;` in test file

## Build and Test Results
- ✅ Build succeeds with `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`
- ✅ Domain renders correctly with `AsciiDomainRenderer.Render(domain)`
- ⚠️ Some action effects are simplified or missing due to API limitations

## Files Created/Modified
1. `/Users/scoizzle/Projects/Poly/Poly/Poly.Benchmarks/DomainModeling/Demos/LibraryDomain.cs` - Main implementation
2. `/Users/scoizzle/Projects/Poly/Poly/Poly.Benchmarks/DomainModeling/Demos/TestLibraryDomain.cs` - Test file
3. `/Users/scoizzle/Projects/Poly/Poly/Poly.Benchmarks/Program.cs` - Added `--library-domain` option

## Conclusion
The Library Management System domain model is implemented with the available API, but several advanced scenarios (cross-entity modifications, dynamic calculations, conditional effects) are not fully implementable with the current DomainModeling API. The core structure (entities, stages, events, basic actions, relationships, policies) works correctly.
