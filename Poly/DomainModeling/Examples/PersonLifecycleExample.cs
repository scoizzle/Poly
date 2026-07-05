using Poly.DomainModeling;
using Poly.DomainModeling.Effects;

namespace Poly.DomainModeling.Examples;

/// <summary>
/// Concrete construction of the Person lifecycle from the original Ugh sketch,
/// using the current V3 Core immutable model.
/// 
/// Policies are expressed directly with DomainExpression (no PolicyExpression layer).
/// This is the target shape the V3 builders should make ergonomic.
/// </summary>
public static class PersonLifecycleExample {
    public static Domain Create() {
        // --- Value Types / Owned Documents ---
        var birthCertificateType = new ValueType(
            "BirthCertificate",
            [
                new Property("Time", new DomainTypeReference("Timestamp"), []),
            ],
            []
        );

        var deathCertificateType = new ValueType(
            "DeathCertificate",
            [
                new Property("Time", new DomainTypeReference("Timestamp"), []),
                new Property("Cause", new DomainTypeReference("Text"), []),
            ],
            []
        );

        // --- Events ---
        var bornEvent = new Event(
            "Born",
            [
                new Property("TimeOfBirth", new DomainTypeReference("Timestamp"), []),
            ],
            []
        );

        var diedEvent = new Event(
            "Died",
            [
                new Property("TimeOfDeath", new DomainTypeReference("Timestamp"), []),
                new Property("Cause", new DomainTypeReference("Text"), []),
                new Property("LifeSpan", new DomainTypeReference("Duration"), []),
            ],
            []
        );

        // --- Person Entity ---
        var person = new Entity(
            "Person",
            [
                new Property("SurName", new DomainTypeReference("Text"), []),
                new Property("GivenName", new DomainTypeReference("Text"), []),
                new Property("TimeOfBirth", new DomainTypeReference("Timestamp"), []),
            ],
            [
                new DomainTypeReference("Born"),
                new DomainTypeReference("Died"),
            ],
            [], // Actions defined per stage below
            [], // Entity-level policies
            [
                // === Alive Stage ===
                new Stage(
                    "Alive",
                    Parent: null,
                    Actions: [], // Populated below
                    Policies:
                    [
                        new Policy("HasBirthCertificate",
                            DomainExpression.Exists(
                                DomainExpression.Owned("BirthCertificate", DomainExpression.Property("Time"))
                            )
                        ),
                        new Policy("NoDeathCertificate",
                            DomainExpression.NotExists(
                                DomainExpression.Owned("DeathCertificate", DomainExpression.Property("Time"))
                            )
                        ),
                    ],
                    OnEntryEffects:
                    [
                        new PublishEventEffect(
                            new DomainTypeReference("Born"),
                            [
                                new PropertyBinding(
                                    "TimeOfBirth",
                                    DomainExpression.Owned("BirthCertificate", DomainExpression.Property("Time"))
                                ),
                            ]
                        ),
                    ],
                    OnExitEffects: []
                ),

                // === Dead Stage ===
                new Stage(
                    "Dead",
                    Parent: null,
                    Actions: [],
                    Policies:
                    [
                        new Policy("HasDeathCertificate",
                            DomainExpression.Exists(
                                DomainExpression.Owned("DeathCertificate", DomainExpression.Property("Time"))
                            )
                        ),
                    ],
                    OnEntryEffects:
                    [
                        new PublishEventEffect(
                            new DomainTypeReference("Died"),
                            [
                                new PropertyBinding(
                                    "TimeOfDeath",
                                    DomainExpression.Owned("DeathCertificate", DomainExpression.Property("Time"))
                                ),
                                new PropertyBinding(
                                    "Cause",
                                    DomainExpression.Owned("DeathCertificate", DomainExpression.Property("Cause"))
                                ),
                                new PropertyBinding(
                                    "LifeSpan",
                                    DomainExpression.Subtract(
                                        DomainExpression.Owned("DeathCertificate", DomainExpression.Property("Time")),
                                        DomainExpression.Property("TimeOfBirth")
                                    )
                                ),
                            ]
                        ),
                    ],
                    OnExitEffects: []
                ),
            ]
        );

        // --- Die Action (available in Alive) ---
        var dieAction = new Action(
            "Die",
            Result: InvocationResult.Void,
            Parameters:
            [
                new Property("TimeOfDeath", new DomainTypeReference("Timestamp"), []),
                new Property("CauseOfDeath", new DomainTypeReference("Text"), []),
            ],
            Effects:
            [
                new CreateEntityInstance(
                    new DomainTypeReference("DeathCertificate"),
                    [
                        new PropertyBinding(
                            "Time",
                            DomainExpression.Parameter("TimeOfDeath")
                        ),
                        new PropertyBinding(
                            "Cause",
                            DomainExpression.Parameter("CauseOfDeath")
                        ),
                    ]
                ),
                new StageTransitionEffect(new StageReference("Dead")),
            ],
            Policies: []
        );

        // Note: In a more complete model we would attach the action to the Alive stage.
        // For now this demonstrates the core immutable structures.

        var domain = new Domain(
            "PersonLifecycle",
            [
                new PrimitiveType("Text", TypeCategory.Text, []),
                new PrimitiveType("Timestamp", TypeCategory.Temporal, []),
                new PrimitiveType("Duration", TypeCategory.Temporal, []),
                birthCertificateType,
                deathCertificateType,
                bornEvent,
                diedEvent,
                person,
            ],
            []
        );

        return domain;
    }
}