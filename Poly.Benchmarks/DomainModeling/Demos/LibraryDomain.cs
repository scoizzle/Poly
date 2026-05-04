using System.Linq;

using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.Effects.Mutations;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Benchmarks.DomainModeling.Demos;

internal static class LibraryDomain {
    public static Domain Build() {
        var domain = new Domain("Library Management System");

        // Primitives
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var intType = new Primitive(domain, "int", TypeCategory.Integer);
        var boolType = new Primitive(domain, "bool", TypeCategory.Primitive);
        var instantType = new Primitive(domain, "instant", TypeCategory.Instant);
        var dateType = new Primitive(domain, "date", TypeCategory.Temporal);
        var isbnType = new Primitive(domain, "isbn", TypeCategory.Text);
        var emailType = new Primitive(domain, "email", TypeCategory.Text);
        var phoneType = new Primitive(domain, "phone", TypeCategory.Text);
        var decimalType = new Primitive(domain, "decimal", TypeCategory.HighPrecision);

        domain.AddType(stringType);
        domain.AddType(intType);
        domain.AddType(boolType);
        domain.AddType(instantType);
        domain.AddType(dateType);
        domain.AddType(isbnType);
        domain.AddType(emailType);
        domain.AddType(phoneType);
        domain.AddType(decimalType);

        // Entities with inheritance
        var person = new Entity(domain, "Person");
        person.AddProperty(new Property(domain, "FirstName", stringType));
        person.AddProperty(new Property(domain, "LastName", stringType));
        person.AddProperty(new Property(domain, "PhoneNumber", phoneType));
        person.AddProperty(new Property(domain, "Email", emailType));
        domain.AddType(person);

        var member = new Entity(domain, "Member", person);
        member.AddProperty(new Property(domain, "MemberId", stringType));
        member.AddProperty(new Property(domain, "JoinDate", instantType));
        member.AddProperty(new Property(domain, "IsActive", boolType));
        member.AddProperty(new Property(domain, "MaxBooksAllowed", intType));
        domain.AddType(member);

        var librarian = new Entity(domain, "Librarian", person);
        librarian.AddProperty(new Property(domain, "EmployeeId", stringType));
        librarian.AddProperty(new Property(domain, "HireDate", instantType));
        librarian.AddProperty(new Property(domain, "Role", stringType));
        domain.AddType(librarian);

        var book = new Entity(domain, "Book");
        book.AddProperty(new Property(domain, "ISBN", isbnType));
        book.AddProperty(new Property(domain, "Title", stringType));
        book.AddProperty(new Property(domain, "Author", stringType));
        book.AddProperty(new Property(domain, "Publisher", stringType));
        book.AddProperty(new Property(domain, "PublicationYear", intType));
        book.AddProperty(new Property(domain, "TotalCopies", intType));
        book.AddProperty(new Property(domain, "AvailableCopies", intType));
        book.AddProperty(new Property(domain, "ShelfLocation", stringType));
        domain.AddType(book);

        var loan = new Entity(domain, "Loan");
        loan.AddProperty(new Property(domain, "LoanDate", instantType));
        loan.AddProperty(new Property(domain, "DueDate", dateType));
        loan.AddProperty(new Property(domain, "ReturnDate", instantType));
        loan.AddProperty(new Property(domain, "IsReturned", boolType));
        loan.AddProperty(new Property(domain, "RenewalCount", intType));
        domain.AddType(loan);

        var reservation = new Entity(domain, "Reservation");
        reservation.AddProperty(new Property(domain, "ReservationDate", instantType));
        reservation.AddProperty(new Property(domain, "ExpiryDate", dateType));
        reservation.AddProperty(new Property(domain, "Status", stringType));
        reservation.AddProperty(new Property(domain, "Position", intType));
        domain.AddType(reservation);

        var fine = new Entity(domain, "Fine");
        fine.AddProperty(new Property(domain, "Amount", decimalType));
        fine.AddProperty(new Property(domain, "Reason", stringType));
        fine.AddProperty(new Property(domain, "IssuedDate", instantType));
        fine.AddProperty(new Property(domain, "IsPaid", boolType));
        domain.AddType(fine);

        var category = new Entity(domain, "Category");
        category.AddProperty(new Property(domain, "Name", stringType));
        category.AddProperty(new Property(domain, "Description", stringType));
        domain.AddType(category);

        // Loan Stages
        var activeStage = new Stage(domain, "Active");
        var overdueStage = new Stage(domain, "Overdue") { Parent = activeStage };
        var returnedStage = new Stage(domain, "Returned");
        var renewedStage = new Stage(domain, "Renewed") { Parent = activeStage };
        var lostStage = new Stage(domain, "Lost") { Parent = activeStage };

        loan.AddStage(activeStage);
        loan.AddStage(overdueStage);
        loan.AddStage(returnedStage);
        loan.AddStage(renewedStage);
        loan.AddStage(lostStage);

        // Reservation Stages
        var waitingStage = new Stage(domain, "Waiting");
        var readyStage = new Stage(domain, "Ready") { Parent = waitingStage };
        var fulfilledStage = new Stage(domain, "Fulfilled");
        var cancelledStage = new Stage(domain, "Cancelled");
        var expiredStage = new Stage(domain, "Expired");

        reservation.AddStage(waitingStage);
        reservation.AddStage(readyStage);
        reservation.AddStage(fulfilledStage);
        reservation.AddStage(cancelledStage);
        reservation.AddStage(expiredStage);

        // Book Stages
        var availableStage = new Stage(domain, "Available");
        var outOfStockStage = new Stage(domain, "OutOfStock") { Parent = availableStage };
        var reservedStage = new Stage(domain, "Reserved") { Parent = availableStage };
        var archivedStage = new Stage(domain, "Archived");

        book.AddStage(availableStage);
        book.AddStage(outOfStockStage);
        book.AddStage(reservedStage);
        book.AddStage(archivedStage);

        // Events
        var bookCheckedOut = new Event(domain, "BookCheckedOut");
        bookCheckedOut.AddProperty(new Property(domain, "BookTitle", stringType));
        bookCheckedOut.AddProperty(new Property(domain, "MemberName", stringType));
        loan.AddEvent(bookCheckedOut);
        domain.AddType(bookCheckedOut);

        var bookReturned = new Event(domain, "BookReturned");
        bookReturned.AddProperty(new Property(domain, "ReturnDate", instantType));
        bookReturned.AddProperty(new Property(domain, "Condition", stringType));
        loan.AddEvent(bookReturned);
        domain.AddType(bookReturned);

        var reservationReady = new Event(domain, "ReservationReady");
        reservationReady.AddProperty(new Property(domain, "BookTitle", stringType));
        reservationReady.AddProperty(new Property(domain, "MemberName", stringType));
        reservation.AddEvent(reservationReady);
        domain.AddType(reservationReady);

        var fineIssued = new Event(domain, "FineIssued");
        fineIssued.AddProperty(new Property(domain, "Amount", decimalType));
        fineIssued.AddProperty(new Property(domain, "Reason", stringType));
        fine.AddEvent(fineIssued);
        domain.AddType(fineIssued);

        var bookAdded = new Event(domain, "BookAdded");
        bookAdded.AddProperty(new Property(domain, "ISBN", isbnType));
        bookAdded.AddProperty(new Property(domain, "Title", stringType));
        book.AddEvent(bookAdded);
        domain.AddType(bookAdded);

        // Actions with effects

        // CheckoutBook - Loan, from none (initial action)
        var checkoutBook = new DomainAction(domain, "CheckoutBook", loan);
        var checkoutBookParam = new Property(domain, "BookTitle", stringType);
        checkoutBook.AddParameter(checkoutBookParam);
        checkoutBook.AddParameter(new Property(domain, "MemberName", stringType));
        checkoutBook.AddEffect(new StageTransition(domain) { TargetStage = activeStage });
        var publishBookCheckedOut = new PublishEvent(domain) { Event = bookCheckedOut };
        publishBookCheckedOut.BindProperty(bookCheckedOut.RequireProperty("BookTitle"), checkoutBookParam);
        checkoutBook.AddEffect(publishBookCheckedOut);
        loan.AddAction(checkoutBook);

        // ReturnBook - Loan, from Active/Overdue/Renewed -> Returned
        var returnBook = new DomainAction(domain, "ReturnBook", loan);
        var conditionParam = new Property(domain, "Condition", stringType);
        returnBook.AddParameter(conditionParam);
        returnBook.AddEffect(new StageTransition(domain) { TargetStage = returnedStage });
        var publishBookReturned = new PublishEvent(domain) { Event = bookReturned };
        publishBookReturned.BindProperty(bookReturned.RequireProperty("ReturnDate"), loan.RequireProperty("ReturnDate"));
        publishBookReturned.BindProperty(bookReturned.RequireProperty("Condition"), conditionParam);
        returnBook.AddEffect(publishBookReturned);
        loan.AddAction(returnBook);
        activeStage.AddAction(returnBook);
        overdueStage.AddAction(returnBook);
        renewedStage.AddAction(returnBook);

        // RenewLoan - Loan, from Active/Overdue -> Renewed
        var renewLoan = new DomainAction(domain, "RenewLoan", loan);
        renewLoan.AddEffect(new StageTransition(domain) { TargetStage = renewedStage });
        loan.AddAction(renewLoan);
        activeStage.AddAction(renewLoan);
        overdueStage.AddAction(renewLoan);

        // ReportLost - Loan, from Active/Overdue/Renewed -> Lost
        var reportLost = new DomainAction(domain, "ReportLost", loan);
        reportLost.AddEffect(new StageTransition(domain) { TargetStage = lostStage });
        loan.AddAction(reportLost);
        activeStage.AddAction(reportLost);
        overdueStage.AddAction(reportLost);
        renewedStage.AddAction(reportLost);

        // ReserveBook - Reservation, from none
        var reserveBook = new DomainAction(domain, "ReserveBook", reservation);
        reservation.AddAction(reserveBook);

        // CancelReservation - Reservation, from Waiting/Ready -> Cancelled
        var cancelReservation = new DomainAction(domain, "CancelReservation", reservation);
        cancelReservation.AddEffect(new StageTransition(domain) { TargetStage = cancelledStage });
        reservation.AddAction(cancelReservation);
        waitingStage.AddAction(cancelReservation);
        readyStage.AddAction(cancelReservation);

        // FulfillReservation - Reservation, from Ready -> Fulfilled, triggers CheckoutBook
        var fulfillReservation = new DomainAction(domain, "FulfillReservation", reservation);
        fulfillReservation.AddEffect(new StageTransition(domain) { TargetStage = fulfilledStage });
        var invokeCheckout = new InvokeAction(domain) { TargetAction = checkoutBook };
        fulfillReservation.AddEffect(invokeCheckout);
        reservation.AddAction(fulfillReservation);
        readyStage.AddAction(fulfillReservation);

        // MarkAsReady - Reservation, from Waiting -> Ready
        var markAsReady = new DomainAction(domain, "MarkAsReady", reservation);
        markAsReady.AddEffect(new StageTransition(domain) { TargetStage = readyStage });
        var publishReservationReady = new PublishEvent(domain) { Event = reservationReady };
        markAsReady.AddEffect(publishReservationReady);
        reservation.AddAction(markAsReady);
        waitingStage.AddAction(markAsReady);

        // IssueFine - Fine, from none
        var issueFine = new DomainAction(domain, "IssueFine", fine);
        var amountParam = new Property(domain, "Amount", decimalType);
        var reasonParam = new Property(domain, "Reason", stringType);
        issueFine.AddParameter(amountParam);
        issueFine.AddParameter(reasonParam);
        var publishFineIssued = new PublishEvent(domain) { Event = fineIssued };
        publishFineIssued.BindProperty(fineIssued.RequireProperty("Amount"), amountParam);
        publishFineIssued.BindProperty(fineIssued.RequireProperty("Reason"), reasonParam);
        issueFine.AddEffect(publishFineIssued);
        fine.AddAction(issueFine);

        // PayFine - Fine, from none
        var payFine = new DomainAction(domain, "PayFine", fine);
        fine.AddAction(payFine);

        // AddBook - Book, from none
        var addBook = new DomainAction(domain, "AddBook", book);
        addBook.AddEffect(new StageTransition(domain) { TargetStage = availableStage });
        var publishBookAdded = new PublishEvent(domain) { Event = bookAdded };
        publishBookAdded.BindProperty(bookAdded.RequireProperty("ISBN"), book.RequireProperty("ISBN"));
        publishBookAdded.BindProperty(bookAdded.RequireProperty("Title"), book.RequireProperty("Title"));
        addBook.AddEffect(publishBookAdded);
        book.AddAction(addBook);

        // ArchiveBook - Book, from Available/OutOfStock/Reserved -> Archived
        var archiveBook = new DomainAction(domain, "ArchiveBook", book);
        archiveBook.AddEffect(new StageTransition(domain) { TargetStage = archivedStage });
        book.AddAction(archiveBook);
        availableStage.AddAction(archiveBook);
        outOfStockStage.AddAction(archiveBook);
        reservedStage.AddAction(archiveBook);

        // Relationships
        var memberLoans = new Relationship(domain, "MemberLoans", member, loan, RelationshipCardinality.OneToMany, true);
        domain.AddRelationship(memberLoans);
        member.AddRelationship(memberLoans);

        var bookLoans = new Relationship(domain, "BookLoans", book, loan, RelationshipCardinality.OneToMany, true);
        domain.AddRelationship(bookLoans);
        book.AddRelationship(bookLoans);

        var librarianLoans = new Relationship(domain, "LibrarianLoans", librarian, loan, RelationshipCardinality.OneToMany, false);
        domain.AddRelationship(librarianLoans);
        librarian.AddRelationship(librarianLoans);

        var memberReservations = new Relationship(domain, "MemberReservations", member, reservation, RelationshipCardinality.OneToMany, true);
        domain.AddRelationship(memberReservations);
        member.AddRelationship(memberReservations);

        var bookReservations = new Relationship(domain, "BookReservations", book, reservation, RelationshipCardinality.OneToMany, true);
        domain.AddRelationship(bookReservations);
        book.AddRelationship(bookReservations);

        var memberFines = new Relationship(domain, "MemberFines", member, fine, RelationshipCardinality.OneToMany, true);
        domain.AddRelationship(memberFines);
        member.AddRelationship(memberFines);

        var bookCategories = new Relationship(domain, "BookCategories", book, category, RelationshipCardinality.ManyToMany, false);
        domain.AddRelationship(bookCategories);
        book.AddRelationship(bookCategories);

        var loanFines = new Relationship(domain, "LoanFines", loan, fine, RelationshipCardinality.OneToMany, true);
        domain.AddRelationship(loanFines);
        loan.AddRelationship(loanFines);

        // Policies

        // MaxBooksPolicy - Member cannot have more than MaxBooksAllowed active loans
        var maxBooksPolicy = new Policy(domain, "MaxBooksPolicy") { AggregationStrategy = PolicyAggregationStrategy.All };
        maxBooksPolicy.AddRule(new PropertyRule(domain, "MaxBooksAllowedCheck", member.RequireProperty("MaxBooksAllowed"), new RequiredConstraint()));
        member.AddPolicy(maxBooksPolicy);

        // RequireValidISBN - Book must have ISBN
        var requireValidIsbn = new Policy(domain, "RequireValidISBN") { AggregationStrategy = PolicyAggregationStrategy.All };
        requireValidIsbn.AddRule(new PropertyRule(domain, "ISBNRequired", book.RequireProperty("ISBN"), new RequiredConstraint()));
        book.AddPolicy(requireValidIsbn);

        // ActiveMembershipRequired - Member must have IsActive=true to checkout
        var activeMembershipPolicy = new Policy(domain, "ActiveMembershipRequired") { AggregationStrategy = PolicyAggregationStrategy.All };
        activeMembershipPolicy.AddRule(new PropertyRule(domain, "IsActiveCheck", member.RequireProperty("IsActive"), new RequiredConstraint()));
        member.AddPolicy(activeMembershipPolicy);

        return domain;
    }
}