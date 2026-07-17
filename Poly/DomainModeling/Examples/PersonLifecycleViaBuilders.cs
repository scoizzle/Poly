using Poly.DomainModeling;
using Poly.DomainModeling.Builders;

namespace Poly.DomainModeling.Examples;

/// <summary>
/// Demonstrates constructing the Person lifecycle using the V3 fluent builders
/// (instead of manually assembling immutable records).
///
/// Stage transitions are the observable — no Event/PublishEvent surface.
/// Subscribers use relationship-path subscriptions (when cert Stage) to react.
///
/// This is the target experience for test authors and LLM-driven construction.
/// </summary>
public static class PersonLifecycleViaBuilders {
    public static Domain Create() {
        return new DomainBuilder("PersonLifecycle")
            .PrimitiveType("Text", TypeCategory.Text)
            .PrimitiveType("Timestamp", TypeCategory.DateTime)
            .PrimitiveType("Duration", TypeCategory.Duration)

            // Demonstrate top-level .Type (Ugh sketch style)
            .Type("CertificateOfLifeBirth", v => v
                .Property("Time", "Timestamp"))

            .Type("CertificateOfDeath", v => v
                .Property("Time", "Timestamp")
                .Property("Cause", "Text"))

            .Entity("Person", person => person
                .Property("SurName", "Text")
                .Property("GivenName", "Text")
                .Property("TimeOfBirth", "Timestamp")

                // Stage transitions ARE the observable — no Event declarations needed.
                // Ownership (still works via OwnsOne for ergonomics)
                .OwnsOne("BirthCertificate", ofType: "CertificateOfLifeBirth")
                .OwnsOne("DeathCertificate", ofType: "CertificateOfDeath")

                // Relationship (newly wired)
                .HasMany("Friends", "Person")

                // Stage: Alive
                .Stage("Alive", stage => stage
                    .Requires(DomainExpression.Exists(
                        DomainExpression.Owned("BirthCertificate", DomainExpression.Property("Time"))))
                    .Requires(DomainExpression.NotExists(
                        DomainExpression.Owned("DeathCertificate", DomainExpression.Property("Time"))))

                    // Entry into Alive IS the observable — no PublishEventEffect needed.
                    // Subscribers navigate via relationship path and read instance properties directly.

                    .Action("Die")
                        .Parameter("TimeOfDeath", "Timestamp")
                        .Parameter("CauseOfDeath", "Text")

                        .Create("DeathCertificate", create => create
                            .Set("Time", DomainExpression.Parameter("TimeOfDeath"))
                            .Set("Cause", DomainExpression.Parameter("CauseOfDeath")))

                        .TransitionTo("Dead")
                )

                // Stage: Dead
                .Stage("Dead", stage => stage
                    .Requires(DomainExpression.Exists(
                        DomainExpression.Owned("DeathCertificate", DomainExpression.Property("Time"))))
                )
            )

            .Build();
    }
}