using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Evolution;

namespace Poly.DomainModeling.Examples.Demos;

/// <summary>
/// Library Management System domain built with the V3 evolution API.
///
/// This mirrors the V2 LibraryDomain demo but uses only V3 types and the
/// <see cref="DomainFactory"/> / <see cref="EvolutionBuilder"/> fluent API.
/// </summary>
public static class LibraryDomain {
    public static Domain Build() =>
        DomainFactory.Create("Library Management System", builder =>
            builder
                // Additional primitives beyond the canonical 9
                .AddPrimitiveType("ISBN", Poly.Introspection.TypeCategory.Text)
                .AddPrimitiveType("Email", Poly.Introspection.TypeCategory.Text)
                .AddPrimitiveType("Phone", Poly.Introspection.TypeCategory.Text)
                .AddPrimitiveType("Decimal", Poly.Introspection.TypeCategory.HighPrecision)

                // Person entity
                .AddEntity("Person")
                .AddPropertyToEntity("Person", new("FirstName", new("Text"), []))
                .AddPropertyToEntity("Person", new("LastName", new("Text"), []))
                .AddPropertyToEntity("Person", new("PhoneNumber", new("Phone"), []))
                .AddPropertyToEntity("Person", new("Email", new("Email"), []))

                // Member inherits from Person
                .AddEntity("Member")
                .SetEntityParent("Member", "Person")
                .AddPropertyToEntity("Member", new("MemberId", new("Text"), []))
                .AddPropertyToEntity("Member", new("JoinDate", new("DateTime"), []))
                .AddPropertyToEntity("Member", new("IsActive", new("Boolean"), []))
                .AddPropertyToEntity("Member", new("MaxBooksAllowed", new("Number"), []))

                // Librarian inherits from Person
                .AddEntity("Librarian")
                .SetEntityParent("Librarian", "Person")
                .AddPropertyToEntity("Librarian", new("EmployeeId", new("Text"), []))
                .AddPropertyToEntity("Librarian", new("HireDate", new("DateTime"), []))
                .AddPropertyToEntity("Librarian", new("Role", new("Text"), []))

                // Book entity
                .AddEntity("Book")
                .AddPropertyToEntity("Book", new("ISBN", new("ISBN"), []))
                .AddPropertyToEntity("Book", new("Title", new("Text"), []))
                .AddPropertyToEntity("Book", new("Author", new("Text"), []))
                .AddPropertyToEntity("Book", new("Publisher", new("Text"), []))
                .AddPropertyToEntity("Book", new("PublicationYear", new("Number"), []))
                .AddPropertyToEntity("Book", new("TotalCopies", new("Number"), []))
                .AddPropertyToEntity("Book", new("AvailableCopies", new("Number"), []))
                .AddPropertyToEntity("Book", new("ShelfLocation", new("Text"), []))

                // Loan entity
                .AddEntity("Loan")
                .AddPropertyToEntity("Loan", new("LoanDate", new("DateTime"), []))
                .AddPropertyToEntity("Loan", new("DueDate", new("Date"), []))
                .AddPropertyToEntity("Loan", new("ReturnDate", new("DateTime"), []))
                .AddPropertyToEntity("Loan", new("IsReturned", new("Boolean"), []))
                .AddPropertyToEntity("Loan", new("RenewalCount", new("Number"), []))

                // Reservation entity
                .AddEntity("Reservation")
                .AddPropertyToEntity("Reservation", new("ReservationDate", new("DateTime"), []))
                .AddPropertyToEntity("Reservation", new("ExpiryDate", new("Date"), []))
                .AddPropertyToEntity("Reservation", new("Status", new("Text"), []))
                .AddPropertyToEntity("Reservation", new("Position", new("Number"), []))

                // Fine entity
                .AddEntity("Fine")
                .AddPropertyToEntity("Fine", new("Amount", new("Decimal"), []))
                .AddPropertyToEntity("Fine", new("Reason", new("Text"), []))
                .AddPropertyToEntity("Fine", new("IssuedDate", new("DateTime"), []))
                .AddPropertyToEntity("Fine", new("IsPaid", new("Boolean"), []))

                // Category entity
                .AddEntity("Category")
                .AddPropertyToEntity("Category", new("Name", new("Text"), []))
                .AddPropertyToEntity("Category", new("Description", new("Text"), []))

                // Loan stages
                .AddStage("Loan", "Active")
                .AddStage("Loan", "Overdue", "Active")
                .AddStage("Loan", "Renewed", "Active")
                .AddStage("Loan", "Returned")
                .AddStage("Loan", "Lost", "Active")

                // Reservation stages
                .AddStage("Reservation", "Waiting")
                .AddStage("Reservation", "Ready", "Waiting")
                .AddStage("Reservation", "Fulfilled")
                .AddStage("Reservation", "Cancelled")
                .AddStage("Reservation", "Expired")

                // Book stages
                .AddStage("Book", "Available")
                .AddStage("Book", "OutOfStock", "Available")
                .AddStage("Book", "Reserved", "Available")
                .AddStage("Book", "Archived")

                // Relationships
                .AddRelationship("MemberLoans", "Member", "Loan",
                    RelationshipCardinality.OneToMany, sourceOwnsTarget: true)
                .AddRelationship("BookLoans", "Book", "Loan",
                    RelationshipCardinality.OneToMany, sourceOwnsTarget: true)
                .AddRelationship("LibrarianLoans", "Librarian", "Loan",
                    RelationshipCardinality.OneToMany, sourceOwnsTarget: false)
                .AddRelationship("MemberReservations", "Member", "Reservation",
                    RelationshipCardinality.OneToMany, sourceOwnsTarget: true)
                .AddRelationship("BookReservations", "Book", "Reservation",
                    RelationshipCardinality.OneToMany, sourceOwnsTarget: true)
                .AddRelationship("MemberFines", "Member", "Fine",
                    RelationshipCardinality.OneToMany, sourceOwnsTarget: true)
                .AddRelationship("BookCategories", "Book", "Category",
                    RelationshipCardinality.ManyToMany, sourceOwnsTarget: false)
                .AddRelationship("LoanFines", "Loan", "Fine",
                    RelationshipCardinality.OneToMany, sourceOwnsTarget: true)

                // Policies
                .AddPolicyToEntity("Member", "ActiveMembershipRequired",
                    DomainExpression.Equal(DomainExpression.Property("IsActive"), DomainExpression.Literal(true)))
                .AddPolicyToEntity("Book", "RequireValidISBN",
                    DomainExpression.NotEqual(DomainExpression.Property("ISBN"), DomainExpression.Literal("")))
        );
}