#nullable enable
using System;
using System.Collections.Generic;

public enum LoanStage
{
    Active = 0,
    Overdue = 1,
    Returned = 2
}

public class Loan
{
    private List<Patron>? _patronOverdueSubscribers;
    private List<Patron>? _patronReturnedSubscribers;
    private Loan()
    { }
    private Loan(DateTime dueDate, DateTime returnedAt, string status, long timesRenewed, Book? book = null, Patron? borrower = null)
    {
        this.DueDate = dueDate;
        this.ReturnedAt = returnedAt;
        this.Status = status;
        this.TimesRenewed = timesRenewed;
        this.Book = book;
        this.Borrower = borrower;
        this.CurrentStage = LoanStage.Active;
        this.CheckedOutAt = DateTime.UtcNow;
    }
    public DateTime CheckedOutAt { get; private set; }
    public DateTime DueDate { get; private set; }
    public DateTime ReturnedAt { get; private set; }
    public string Status { get; private set; } = default!;
    public long TimesRenewed { get; private set; }
    public Book? Book { get; private set; }
    public Patron? Borrower { get; private set; }
    public LoanStage CurrentStage { get; private set; }
    private DomainResult<Book> CreateBook(string author, Genre genre, string isbn, long pages, string title)
    {
        var bookResult = Book.Create(author, genre, isbn, pages, title);
        if (!bookResult.IsSuccess)
        {
            return DomainResult<Book>.Failure(bookResult.ErrorMessage ?? "");
        }
        var book = bookResult.Value;
        this.Book = book;
        return DomainResult<Book>.Success(book);
    }
    private DomainResult<Patron> CreateBorrower(long currentBorrowCount, string email, long maxItems, string name, long outstandingFines, DateOnly? memberSince = null, PatronStatus status = PatronStatus.Active)
    {
        var patronResult = Patron.Create(currentBorrowCount, email, maxItems, name, outstandingFines, new List<Loan>(), new List<Fine>(), memberSince, status);
        if (!patronResult.IsSuccess)
        {
            return DomainResult<Patron>.Failure(patronResult.ErrorMessage ?? "");
        }
        var patron = patronResult.Value;
        this.Borrower = patron;
        return DomainResult<Patron>.Success(patron);
    }
    public DomainResult Renew()
    {
        if (this.CurrentStage != LoanStage.Active)
        {
            return DomainResult.Failure("'Renew' requires stage 'Active' on entity 'Loan'.");
        }
        this.DueDate = this.DueDate.AddDays(14L);
        this.TimesRenewed = this.TimesRenewed + 1L;
        return DomainResult.Success();
    }
    public DomainResult Return()
    {
        if (this.CurrentStage != LoanStage.Active)
        {
            return DomainResult.Failure("'Return' requires stage 'Active' on entity 'Loan'.");
        }
        this.ReturnedAt = DateTime.UtcNow;
        var previousStage0 = this.CurrentStage;
        this.CurrentStage = LoanStage.Returned;
        this.NotifyReturnedSubscribers(previousStage0);
        return DomainResult.Success();
    }
    internal void RegisterPatronOverdueSubscriber(Patron subscriber)
    {
        if (this._patronOverdueSubscribers == null)
        {
            this._patronOverdueSubscribers = new List<Patron>();
        }
        this._patronOverdueSubscribers.Add(subscriber);
    }
    internal void RegisterPatronReturnedSubscriber(Patron subscriber)
    {
        if (this._patronReturnedSubscribers == null)
        {
            this._patronReturnedSubscribers = new List<Patron>();
        }
        this._patronReturnedSubscribers.Add(subscriber);
    }
    internal void NotifyOverdueSubscribers(LoanStage previousStage)
    {
        if (this._patronOverdueSubscribers != null)
        {
            foreach (var sub in this._patronOverdueSubscribers)
            {
                sub.WhenEachLoanOverdue();
            }
        }
    }
    internal void NotifyReturnedSubscribers(LoanStage previousStage)
    {
        if (this._patronReturnedSubscribers != null)
        {
            foreach (var sub in this._patronReturnedSubscribers)
            {
                sub.WhenEachLoanReturned();
            }
        }
    }
    public static DomainResult<Loan> Create(DateTime dueDate, DateTime returnedAt, string status, long timesRenewed, Book? book = null, Patron? borrower = null)
    {
        var created = new Loan(dueDate, returnedAt, status, timesRenewed, book, borrower);
        if (borrower != null)
        {
            borrower.AttachLoans(created);
        }
        return DomainResult<Loan>.Success(created);
    }
    public DomainResult EnsureUnique(string propertyName, object value) => DomainResult.Success();
    public DomainResult<object> Create(string name, object? __job = null)
    {
        var values = new Dictionary<string, object>();
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6, string p7, object v7)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        values[p7] = v7;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> CreateIn(string name, object? __job = null)
    {
        var values = new Dictionary<string, object>();
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6, string p7, object v7)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        values[p7] = v7;
        return this.BindCreateIn(name, values);
    }
    public DomainResult ProbeCreate(string name, object? __job = null)
    {
        var values = new Dictionary<string, object>();
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6, string p7, object v7)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        values[p7] = v7;
        return this.BindProbeCreate(name, values);
    }
    private DomainResult<object> BindCreate(string typeName, Dictionary<string, object> values)
    {
        if (typeName == "Book")
        {
            var typed = Book.Create((values.ContainsKey("Author") ? (string?)values["Author"] ?? "" : ""), (values.ContainsKey("Genre") ? (Genre?)values["Genre"] ?? Genre.Fiction : Genre.Fiction), (values.ContainsKey("ISBN") ? (string?)values["ISBN"] ?? "" : ""), (values.ContainsKey("Pages") ? (long?)values["Pages"] ?? 0L : 0L), (values.ContainsKey("Title") ? (string?)values["Title"] ?? "" : ""));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (typeName == "Patron")
        {
            var typed = Patron.Create((values.ContainsKey("CurrentBorrowCount") ? (long?)values["CurrentBorrowCount"] ?? 0L : 0L), (values.ContainsKey("Email") ? (string?)values["Email"] ?? "" : ""), (values.ContainsKey("MaxItems") ? (long?)values["MaxItems"] ?? 0L : 0L), (values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), (values.ContainsKey("OutstandingFines") ? (long?)values["OutstandingFines"] ?? 0L : 0L), new List<Loan>(), new List<Fine>(), (values.ContainsKey("MemberSince") ? (DateOnly?)values["MemberSince"] ?? DateOnly.MinValue : DateOnly.MinValue), (values.ContainsKey("Status") ? (PatronStatus?)values["Status"] ?? PatronStatus.Active : PatronStatus.Active));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (typeName == "Loan")
        {
            var typed = Loan.Create((values.ContainsKey("DueDate") ? (DateTime?)values["DueDate"] ?? DateTime.MinValue : DateTime.MinValue), (values.ContainsKey("ReturnedAt") ? (DateTime?)values["ReturnedAt"] ?? DateTime.MinValue : DateTime.MinValue), (values.ContainsKey("Status") ? (string?)values["Status"] ?? "" : ""), (values.ContainsKey("TimesRenewed") ? (long?)values["TimesRenewed"] ?? 0L : 0L), (values.ContainsKey("book") ? (Book?)values["book"] ?? null : null), (values.ContainsKey("borrower") ? (Patron?)values["borrower"] ?? null : null));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (typeName == "Fine")
        {
            var typed = Fine.Create((values.ContainsKey("Amount") ? (long?)values["Amount"] ?? 0L : 0L), (values.ContainsKey("Paid") ? (bool?)values["Paid"] ?? false : false), (values.ContainsKey("Reason") ? (string?)values["Reason"] ?? "" : ""), (values.ContainsKey("patron") ? (Patron?)values["patron"] ?? null : null), (values.ContainsKey("DateIssued") ? (DateTime?)values["DateIssued"] ?? DateTime.MinValue : DateTime.MinValue));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (typeName == "PremiumPatron")
        {
            var typed = PremiumPatron.Create((values.ContainsKey("Email") ? (string?)values["Email"] ?? "" : ""), (values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), (values.ContainsKey("PriorityAccess") ? (bool?)values["PriorityAccess"] ?? false : false), (values.ContainsKey("RewardPoints") ? (long?)values["RewardPoints"] ?? 0L : 0L), (values.ContainsKey("Tier") ? (PremiumTier?)values["Tier"] ?? PremiumTier.Silver : PremiumTier.Silver));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        return DomainResult<object>.Failure("Unknown type '" + typeName + "'.");
    }
    private DomainResult<object> BindCreateIn(string relationshipName, Dictionary<string, object> values)
    {
        if (relationshipName == "book")
        {
            var typed = this.CreateBook((values.ContainsKey("Author") ? (string?)values["Author"] ?? "" : ""), (values.ContainsKey("Genre") ? (Genre?)values["Genre"] ?? Genre.Fiction : Genre.Fiction), (values.ContainsKey("ISBN") ? (string?)values["ISBN"] ?? "" : ""), (values.ContainsKey("Pages") ? (long?)values["Pages"] ?? 0L : 0L), (values.ContainsKey("Title") ? (string?)values["Title"] ?? "" : ""));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (relationshipName == "borrower")
        {
            var typed = this.CreateBorrower((values.ContainsKey("CurrentBorrowCount") ? (long?)values["CurrentBorrowCount"] ?? 0L : 0L), (values.ContainsKey("Email") ? (string?)values["Email"] ?? "" : ""), (values.ContainsKey("MaxItems") ? (long?)values["MaxItems"] ?? 0L : 0L), (values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), (values.ContainsKey("OutstandingFines") ? (long?)values["OutstandingFines"] ?? 0L : 0L), (values.ContainsKey("MemberSince") ? (DateOnly?)values["MemberSince"] ?? DateOnly.MinValue : DateOnly.MinValue), (values.ContainsKey("Status") ? (PatronStatus?)values["Status"] ?? PatronStatus.Active : PatronStatus.Active));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        return DomainResult<object>.Failure("Unknown relationship '" + relationshipName + "'.");
    }
    private DomainResult BindProbeCreate(string typeName, Dictionary<string, object> values)
    {
        if (typeName == "Book")
        {
            var typed = Book.Create((values.ContainsKey("Author") ? (string?)values["Author"] ?? "" : ""), (values.ContainsKey("Genre") ? (Genre?)values["Genre"] ?? Genre.Fiction : Genre.Fiction), (values.ContainsKey("ISBN") ? (string?)values["ISBN"] ?? "" : ""), (values.ContainsKey("Pages") ? (long?)values["Pages"] ?? 0L : 0L), (values.ContainsKey("Title") ? (string?)values["Title"] ?? "" : ""));
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        if (typeName == "Patron")
        {
            var typed = Patron.Create((values.ContainsKey("CurrentBorrowCount") ? (long?)values["CurrentBorrowCount"] ?? 0L : 0L), (values.ContainsKey("Email") ? (string?)values["Email"] ?? "" : ""), (values.ContainsKey("MaxItems") ? (long?)values["MaxItems"] ?? 0L : 0L), (values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), (values.ContainsKey("OutstandingFines") ? (long?)values["OutstandingFines"] ?? 0L : 0L), new List<Loan>(), new List<Fine>(), (values.ContainsKey("MemberSince") ? (DateOnly?)values["MemberSince"] ?? DateOnly.MinValue : DateOnly.MinValue), (values.ContainsKey("Status") ? (PatronStatus?)values["Status"] ?? PatronStatus.Active : PatronStatus.Active));
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        if (typeName == "Loan")
        {
            var typed = Loan.Create((values.ContainsKey("DueDate") ? (DateTime?)values["DueDate"] ?? DateTime.MinValue : DateTime.MinValue), (values.ContainsKey("ReturnedAt") ? (DateTime?)values["ReturnedAt"] ?? DateTime.MinValue : DateTime.MinValue), (values.ContainsKey("Status") ? (string?)values["Status"] ?? "" : ""), (values.ContainsKey("TimesRenewed") ? (long?)values["TimesRenewed"] ?? 0L : 0L), (values.ContainsKey("book") ? (Book?)values["book"] ?? null : null), (values.ContainsKey("borrower") ? (Patron?)values["borrower"] ?? null : null));
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        if (typeName == "Fine")
        {
            var typed = Fine.Create((values.ContainsKey("Amount") ? (long?)values["Amount"] ?? 0L : 0L), (values.ContainsKey("Paid") ? (bool?)values["Paid"] ?? false : false), (values.ContainsKey("Reason") ? (string?)values["Reason"] ?? "" : ""), (values.ContainsKey("patron") ? (Patron?)values["patron"] ?? null : null), (values.ContainsKey("DateIssued") ? (DateTime?)values["DateIssued"] ?? DateTime.MinValue : DateTime.MinValue));
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        if (typeName == "PremiumPatron")
        {
            var typed = PremiumPatron.Create((values.ContainsKey("Email") ? (string?)values["Email"] ?? "" : ""), (values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), (values.ContainsKey("PriorityAccess") ? (bool?)values["PriorityAccess"] ?? false : false), (values.ContainsKey("RewardPoints") ? (long?)values["RewardPoints"] ?? 0L : 0L), (values.ContainsKey("Tier") ? (PremiumTier?)values["Tier"] ?? PremiumTier.Silver : PremiumTier.Silver));
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        return DomainResult.Failure("Unknown type '" + typeName + "'.");
    }
    private void OnEntryActive()
    {
        this.CheckedOutAt = DateTime.UtcNow;
    }
    private void OnEntryOverdue()
    {
        this.CheckedOutAt = DateTime.UtcNow;
    }
}