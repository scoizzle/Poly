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
    private List<Patron>? _patronLoansOverdueSubscribers;
    private int _notifyDepth;
    private Loan()
    { }
    private Loan(DateOnly dueDate, DateOnly returnedOn, Book? book = null)
    {
        this.DueDate = dueDate;
        this.ReturnedOn = returnedOn;
        this.Book = book;
        this.CurrentStage = LoanStage.Active;
        this.Status = "active";
    }
    public DateOnly DueDate { get; private set; }
    public DateOnly ReturnedOn { get; private set; }
    public string Status { get; private set; } = default!;
    public Book? Book { get; private set; }
    public LoanStage CurrentStage { get; private set; }
    private DomainResult<Book> CreateBook(string isbn, string title)
    {
        var bookResult = Book.Create(isbn, title);
        if (!bookResult.IsSuccess)
        {
            return DomainResult<Book>.Failure(bookResult.ErrorMessage ?? "");
        }
        var book = bookResult.Value;
        this.Book = book;
        return DomainResult<Book>.Success(book);
    }
    public DomainResult Return()
    {
        if (this.CurrentStage == LoanStage.Active)
        {
            this.ReturnedOn = DateOnly.FromDateTime(DateTime.Today);
            this.CurrentStage = LoanStage.Returned;
            return DomainResult.Success();
        }
        if (this.CurrentStage == LoanStage.Overdue)
        {
            this.ReturnedOn = DateOnly.FromDateTime(DateTime.Today);
            this.CurrentStage = LoanStage.Returned;
            return DomainResult.Success();
        }
        return DomainResult.Failure("'Return' is not valid for the current stage on entity 'Loan'.");
    }
    public DomainResult Renew()
    {
        if (this.CurrentStage != LoanStage.Active)
        {
            return DomainResult.Failure("'Renew' requires stage 'Active' on entity 'Loan'.");
        }
        this.DueDate = this.DueDate.AddDays((int)7L);
        return DomainResult.Success();
    }
    internal void RegisterPatronLoansOverdueSubscriber(Patron subscriber)
    {
        if (this._patronLoansOverdueSubscribers == null)
        {
            this._patronLoansOverdueSubscribers = new List<Patron>();
        }
        this._patronLoansOverdueSubscribers.Add(subscriber);
    }
    internal void NotifyOverdueSubscribers(LoanStage previousStage)
    {
        this.EnterNotifyCascade("Overdue");
        try
        {
            if (this._patronLoansOverdueSubscribers != null)
            {
                foreach (var sub in this._patronLoansOverdueSubscribers)
                {
                    sub.WhenEachLoanOverdue();
                    sub.WhenEachLoanOverdue(this);
                }
            }
        }
        finally
        {
            this.ExitNotifyCascade();
        }
    }
    public static DomainResult<Loan> Create(DateOnly dueDate, DateOnly returnedOn, Book? book = null)
    {
        var created = new Loan(dueDate, returnedOn, book);
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
            var typed = Book.Create((values.ContainsKey("Isbn") ? (string?)values["Isbn"] ?? "" : ""), (values.ContainsKey("Title") ? (string?)values["Title"] ?? "" : ""));
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
            var typed = Patron.Create((values.ContainsKey("Email") ? (string?)values["Email"] ?? "" : ""), (values.ContainsKey("LastOverdue") ? (DateOnly?)values["LastOverdue"] ?? DateOnly.MinValue : DateOnly.MinValue), (values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), (values.ContainsKey("Status") ? (string?)values["Status"] ?? "" : ""), new List<Loan>(), new List<Fine>());
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
            var typed = Loan.Create((values.ContainsKey("DueDate") ? (DateOnly?)values["DueDate"] ?? DateOnly.MinValue : DateOnly.MinValue), (values.ContainsKey("ReturnedOn") ? (DateOnly?)values["ReturnedOn"] ?? DateOnly.MinValue : DateOnly.MinValue), (values.ContainsKey("book") ? (Book?)values["book"] ?? null : null));
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
            var typed = Fine.Create((values.ContainsKey("Amount") ? (long?)values["Amount"] ?? 0L : 0L), (values.ContainsKey("Reason") ? (string?)values["Reason"] ?? "" : ""));
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
            var typed = this.CreateBook((values.ContainsKey("Isbn") ? (string?)values["Isbn"] ?? "" : ""), (values.ContainsKey("Title") ? (string?)values["Title"] ?? "" : ""));
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
            var typed = Book.Create((values.ContainsKey("Isbn") ? (string?)values["Isbn"] ?? "" : ""), (values.ContainsKey("Title") ? (string?)values["Title"] ?? "" : ""));
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        if (typeName == "Patron")
        {
            var typed = Patron.Create((values.ContainsKey("Email") ? (string?)values["Email"] ?? "" : ""), (values.ContainsKey("LastOverdue") ? (DateOnly?)values["LastOverdue"] ?? DateOnly.MinValue : DateOnly.MinValue), (values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), (values.ContainsKey("Status") ? (string?)values["Status"] ?? "" : ""), new List<Loan>(), new List<Fine>());
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        if (typeName == "Loan")
        {
            var typed = Loan.Create((values.ContainsKey("DueDate") ? (DateOnly?)values["DueDate"] ?? DateOnly.MinValue : DateOnly.MinValue), (values.ContainsKey("ReturnedOn") ? (DateOnly?)values["ReturnedOn"] ?? DateOnly.MinValue : DateOnly.MinValue), (values.ContainsKey("book") ? (Book?)values["book"] ?? null : null));
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        if (typeName == "Fine")
        {
            var typed = Fine.Create((values.ContainsKey("Amount") ? (long?)values["Amount"] ?? 0L : 0L), (values.ContainsKey("Reason") ? (string?)values["Reason"] ?? "" : ""));
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        return DomainResult.Failure("Unknown type '" + typeName + "'.");
    }
    private void EnterNotifyCascade(string stageName)
    {
        if (this._notifyDepth >= 16)
        {
            throw new System.InvalidOperationException(string.Concat("Subscription cascade on entity 'Loan' exceeded max depth (16) while notifying '", stageName, "'."));
        }
        this._notifyDepth = this._notifyDepth + 1;
    }
    private void ExitNotifyCascade()
    {
        this._notifyDepth = this._notifyDepth - 1;
    }
    private void OnEntryActive()
    {
        this.Status = "active";
    }
}