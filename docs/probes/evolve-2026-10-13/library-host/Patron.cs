#nullable enable
using System;
using System.Collections.Generic;

public class Patron
{
    private List<Loan> _loans;
    private List<Fine> _fines;
    private Patron()
    {
        this._loans = new List<Loan>();
        this._fines = new List<Fine>();
    }
    private Patron(string email, DateOnly lastOverdue, string name, string status, IEnumerable<Loan>? loans = null, IEnumerable<Fine>? fines = null)
    {
        this.Email = email;
        this.LastOverdue = lastOverdue;
        this.Name = name;
        this.Status = status;
        this._loans = new List<Loan>(loans ?? new List<Loan>());
        this._fines = new List<Fine>(fines ?? new List<Fine>());
        this.InitializeSubscriptions();
    }
    public string Email { get; private set; } = default!;
    public DateOnly LastOverdue { get; private set; }
    public string Name { get; private set; } = default!;
    public string Status { get; private set; } = default!;
    public IReadOnlyList<Loan> Loans
    {
        get => this._loans;
    }
    public IReadOnlyList<Fine> Fines
    {
        get => this._fines;
    }
    private DomainResult<Loan> CreateLoans(DateOnly dueDate, DateOnly returnedOn, Book? book)
    {
        var loanResult = Loan.Create(dueDate, returnedOn, book);
        if (!loanResult.IsSuccess)
        {
            return DomainResult<Loan>.Failure(loanResult.ErrorMessage ?? "");
        }
        var loan = loanResult.Value;
        this._loans.Add(loan);
        loan.RegisterPatronLoansOverdueSubscriber(this);
        loan.RegisterPatronLoansReturnedSubscriber(this);
        return DomainResult<Loan>.Success(loan);
    }
    internal void AttachLoans(Loan child)
    {
        this._loans.Add(child);
        child.RegisterPatronLoansOverdueSubscriber(this);
        child.RegisterPatronLoansReturnedSubscriber(this);
    }
    private DomainResult<Fine> CreateFines(long amount, string reason)
    {
        var fineResult = Fine.Create(amount, reason);
        if (!fineResult.IsSuccess)
        {
            return DomainResult<Fine>.Failure(fineResult.ErrorMessage ?? "");
        }
        var fine = fineResult.Value;
        this._fines.Add(fine);
        return DomainResult<Fine>.Success(fine);
    }
    internal void AttachFines(Fine child)
    {
        this._fines.Add(child);
    }
    public DomainResult CheckOut(Book book)
    {
        if (this.AtLimit())
        {
            return DomainResult.Failure("'CheckOut' blocked by policy 'AtLimit'.");
        }
        var create0 = this.ProbeCreate("Loan", "book", (object)book, "DueDate", DateOnly.FromDateTime(DateTime.UtcNow).AddDays((int)14L));
        if (!create0.IsSuccess)
        {
            return DomainResult.Failure(create0.ErrorMessage ?? "");
        }
        var create1 = this.CreateIn("loans", "book", (object)book, "DueDate", DateOnly.FromDateTime(DateTime.UtcNow).AddDays((int)14L));
        if (!create1.IsSuccess)
        {
            return DomainResult.Failure(create1.ErrorMessage ?? "");
        }
        var created2 = create1.Value;
        return DomainResult.Success();
    }
    public DomainResult RenewAllActive()
    {
        var matched1 = false;
        foreach (var target0 in this.Loans)
        {
            if (!(target0.CurrentStage == LoanStage.Active))
            {
                continue;
            }
            matched1 = true;
            var result2 = target0.Renew();
            if (!result2.IsSuccess)
            {
                return DomainResult.Failure(result2.ErrorMessage ?? "");
            }
        }
        if (!matched1)
        {
            return DomainResult.Failure("for loans.Renew matched zero targets.");
        }
        return DomainResult.Success();
    }
    public DomainResult RenewAndTag()
    {
        var matched1 = false;
        foreach (var target0 in this.Loans)
        {
            if (!(target0.CurrentStage == LoanStage.Active))
            {
                continue;
            }
            matched1 = true;
            var result2 = target0.Renew();
            if (!result2.IsSuccess)
            {
                return DomainResult.Failure(result2.ErrorMessage ?? "");
            }
        }
        if (!matched1)
        {
            return DomainResult.Failure("for loans.Renew matched zero targets.");
        }
        this.Status = "renewed";
        return DomainResult.Success();
    }
    public DomainResult NotifyPatron(Message message)
    {
        var adapterResult = MailerAdapters.Send(message);
        if (!adapterResult.IsSuccess)
        {
            return adapterResult;
        }
        this.Status = "notified";
        return DomainResult.Success();
    }
    public bool AtLimit() => this.Loans.Count >= 3L;
    public bool AllOverdue()
    {
        var all1 = true;
        var sawItem2 = false;
        foreach (var item0 in this.Loans)
        {
            sawItem2 = true;
            if (!(item0.DueDate < DateOnly.FromDateTime(DateTime.UtcNow)))
            {
                all1 = false;
                break;
            }
        }
        return sawItem2 && all1;
    }
    public bool HasLoans() => this.Loans.Count != 0;
    internal void WhenEachLoanOverdue()
    {
        var create0 = this.CreateIn("fines", "Amount", 5L, "Reason", "Overdue");
        if (!create0.IsSuccess)
        {
            throw new ConstraintFailureException(create0.ErrorMessage ?? "");
        }
        var created1 = create0.Value;
    }
    internal void WhenEachLoanOverdue(Loan loan)
    {
        this.LastOverdue = loan.DueDate;
    }
    internal void WhenAllLoanReturned(LoanStage previousStage)
    {
        var linkedMatched = false;
        foreach (var linkedTarget in this.Loans)
        {
            linkedMatched = true;
            if (linkedTarget.CurrentStage != LoanStage.Returned)
            {
                return;
            }
        }
        if (!linkedMatched)
        {
            return;
        }
        if (previousStage == LoanStage.Returned)
        {
            return;
        }
        {
            this.Status = "allReturned";
        }
    }
    public static DomainResult<Patron> Create(string email, DateOnly lastOverdue, string name, string status, IEnumerable<Loan>? loans = null, IEnumerable<Fine>? fines = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            return DomainResult<Patron>.Failure("'Name' is required.");
        }
        var created = new Patron(email, lastOverdue, name, status, loans, fines);
        var uniqueCheck = created.EnsureUnique("Email", email);
        if (!uniqueCheck.IsSuccess)
        {
            return DomainResult<Patron>.Failure(uniqueCheck.ErrorMessage ?? "");
        }
        return DomainResult<Patron>.Success(created);
    }
    private void InitializeSubscriptions()
    {
        foreach (var target in this.Loans)
        {
            target.RegisterPatronLoansOverdueSubscriber(this);
        }
        foreach (var target in this.Loans)
        {
            target.RegisterPatronLoansReturnedSubscriber(this);
        }
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
        if (relationshipName == "loans")
        {
            var typed = this.CreateLoans((values.ContainsKey("DueDate") ? (DateOnly?)values["DueDate"] ?? DateOnly.MinValue : DateOnly.MinValue), (values.ContainsKey("ReturnedOn") ? (DateOnly?)values["ReturnedOn"] ?? DateOnly.MinValue : DateOnly.MinValue), (values.ContainsKey("book") ? (Book?)values["book"] ?? null : null));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (relationshipName == "fines")
        {
            var typed = this.CreateFines((values.ContainsKey("Amount") ? (long?)values["Amount"] ?? 0L : 0L), (values.ContainsKey("Reason") ? (string?)values["Reason"] ?? "" : ""));
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
}