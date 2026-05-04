# Healthcare Domain - Roadblocks and Issues

## Summary
The Healthcare Patient Management domain was successfully implemented. The domain builds and renders correctly.

## Issue: Multiple Ownership Constraint

### Problem
When creating relationships where multiple sources claim ownership of the same target entity, the DomainModeling system's validation fails and rolls back the transaction.

For example:
- PatientAppointments: Patient → Appointment (SourceOwnsTarget = true)
- DoctorAppointments: Doctor → Appointment (SourceOwnsTarget = true)

Both relationships have `SourceOwnsTarget = true` and both target `Appointment`. The validation error is:
```
[Error] Target 'Appointment' has multiple ownership relationships.
```

### Affected Relationships
The following combinations caused conflicts (same target with multiple owners):
1. Appointment targeted by PatientAppointments (Patient owns) and DoctorAppointments (Doctor owns)
2. MedicalRecord targeted by PatientRecords (Patient owns) and AppointmentRecords (Appointment owns)
3. Billing targeted by PatientBilling (Patient owns) and DoctorBilling (Doctor owns)

### Workaround
Set `SourceOwnsTarget = false` for the secondary relationships. This means only one entity can "own" a target entity.

### Suggested Fix
Either:
1. Allow multiple ownership relationships and let the domain model handle it
2. Provide a clearer error message explaining the constraint
3. Support joint ownership scenarios where multiple entities share ownership

### Changes Made
- DoctorAppointments: Set SourceOwnsTarget = false
- AppointmentRecords: Set SourceOwnsTarget = false
- DoctorBilling: Set SourceOwnsTarget = false

## Other Notes
- The spelling of "Confirmed" stage was initially incorrect ("Confirmed" vs "Confirmed") - fixed during implementation
- The `TypeCategory` enum is in `Poly.Introspection` namespace, not `Poly.Data.Modeling.TypeSystem`
- Build verification: `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` succeeds with 0 errors
- Domain rendering: `AsciiDomainRenderer.Render(domain)` works correctly
