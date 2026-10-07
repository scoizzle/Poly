#nullable enable
using System;
using System.Collections.Generic;

public enum PatronStage
{
    Active = 0,
    Suspended = 1,
    Closed = 2
}

public class Patron
{
    private List<Loan> _loans;
    private List<Fine> _fines;
    private Patron()
    {
        this._loans = new List<Loan>();
        this._fines = new List<Fine>();
    }
    private Patron(long currentBorrowCount, string email, long maxItems, string name, long outstandingFines, IEnumerable<Loan>? loans = null, IEnumerable<Fine>? fines = null, DateOnly? memberSince = null, PatronStatus status = PatronStatus.Active)
    {
        this.CurrentBorrowCount = currentBorrowCount;
        this.Email = email;
        this.MaxItems = maxItems;
        this.MemberSince = memberSince ?? DateOnly.FromDateTime(DateTime.Today);
        this.Name = name;
        this.OutstandingFines = outstandingFines;
        this.Status = status;
        this._loans = new List<Loan>(loans ?? new List<Loan>());
        this._fines = new List<Fine>(fines ?? new List<Fine>());
        this.CurrentStage = PatronStage.Active;
        this.InitializeSubscriptions();
    }
    public long CurrentBorrowCount { get; private set; }
    public string Email { get; private set; } = default!;
    public long MaxItems { get; private set; }
    public DateOnly MemberSince { get; private set; }
    public string Name { get; private set; } = default!;
    public long OutstandingFines { get; private set; }
    public PatronStatus Status { get; private set; }
    public IReadOnlyList<Loan> Loans
    {
        get => this._loans;
    }
    public IReadOnlyList<Fine> Fines
    {
        get => this._fines;
    }
    public PatronStage CurrentStage { get; private set; }
    private DomainResult<Loan> CreateLoans(DateTime dueDate, DateTime returnedAt, string status, long timesRenewed, Book? book)
    {
        var loanResult = Loan.Create(dueDate, returnedAt, status, timesRenewed, book, this);
        if (!loanResult.IsSuccess)
        {
            return DomainResult<Loan>.Failure(loanResult.ErrorMessage ?? "");
        }
        var loan = loanResult.Value;
        return DomainResult<Loan>.Success(loan);
    }
    internal void AttachLoans(Loan child)
    {
        this._loans.Add(child);
        child.RegisterPatronOverdueSubscriber(this);
        child.RegisterPatronReturnedSubscriber(this);
    }
    private DomainResult<Fine> CreateFines(long amount, bool paid, string reason, DateTime? dateIssued = null)
    {
        var fineResult = Fine.Create(amount, paid, reason, this, dateIssued);
        if (!fineResult.IsSuccess)
        {
            return DomainResult<Fine>.Failure(fineResult.ErrorMessage ?? "");
        }
        var fine = fineResult.Value;
        return DomainResult<Fine>.Success(fine);
    }
    internal void AttachFines(Fine child)
    {
        this._fines.Add(child);
        child.RegisterPatronResolvedSubscriber(this);
    }
    public DomainResult<Loan> CheckOut(Book book)
    {
        if (this.CurrentStage != PatronStage.Active)
        {
            return DomainResult<Loan>.Failure("'CheckOut' requires stage 'Active' on entity 'Patron'.");
        }
        if (!this.GoodStanding())
        {
            return DomainResult<Loan>.Failure("'CheckOut' blocked by policy 'GoodStanding'.");
        }
        if (this.AtLimit())
        {
            return DomainResult<Loan>.Failure("'CheckOut' blocked by policy 'AtLimit'.");
        }
        if (this.HasFines())
        {
            return DomainResult<Loan>.Failure("'CheckOut' blocked by policy 'HasFines'.");
        }
        var create0 = this.ProbeCreate("Loan");
        if (!create0.IsSuccess)
        {
            return DomainResult<Loan>.Failure(create0.ErrorMessage ?? "");
        }
        this.CurrentBorrowCount = this.CurrentBorrowCount + 1L;
        var create1 = this.CreateIn("loans");
        if (!create1.IsSuccess)
        {
            return DomainResult<Loan>.Failure(create1.ErrorMessage ?? "");
        }
        var created2 = create1.Value;
        return DomainResult<Loan>.Success((Loan)created2);
    }
    public DomainResult Suspend()
    {
        if (this.CurrentStage != PatronStage.Active)
        {
            return DomainResult.Failure("'Suspend' requires stage 'Active' on entity 'Patron'.");
        }
        this.Status = PatronStatus.Suspended;
        this.CurrentBorrowCount = 0L;
        this.CurrentStage = PatronStage.Suspended;
        {
            var assignValue0 = 0L;
            if (assignValue0 < 0L)
            {
                return DomainResult.Failure("'MaxItems' must be >= 0.");
            }
            if (assignValue0 > 20L)
            {
                return DomainResult.Failure("'MaxItems' must be <= 20.");
            }
            this.MaxItems = assignValue0;
        }
        return DomainResult.Success();
    }
    public DomainResult CloseAccount()
    {
        if (this.CurrentStage != PatronStage.Active)
        {
            return DomainResult.Failure("'CloseAccount' requires stage 'Active' on entity 'Patron'.");
        }
        this.CurrentStage = PatronStage.Closed;
        return DomainResult.Success();
    }
    public DomainResult Reinstate()
    {
        if (this.CurrentStage != PatronStage.Suspended)
        {
            return DomainResult.Failure("'Reinstate' requires stage 'Suspended' on entity 'Patron'.");
        }
        if (this.HasOverdueLoans())
        {
            return DomainResult.Failure("'Reinstate' blocked by policy 'HasOverdueLoans'.");
        }
        this.Status = PatronStatus.Active;
        {
            var assignValue0 = 5L;
            if (assignValue0 < 0L)
            {
                return DomainResult.Failure("'MaxItems' must be >= 0.");
            }
            if (assignValue0 > 20L)
            {
                return DomainResult.Failure("'MaxItems' must be <= 20.");
            }
            this.MaxItems = assignValue0;
        }
        this.CurrentStage = PatronStage.Active;
        return DomainResult.Success();
    }
    public bool GoodStanding() => this.Status == PatronStatus.Active;
    public bool AtLimit() => this.CurrentBorrowCount >= this.MaxItems;
    public bool HasFines() => this.OutstandingFines > 0L;
    public bool HasOverdueLoans()
    {
        var any1 = false;
        foreach (var item0 in this.Loans)
        {
            if (item0.Status == "Overdue")
            {
                any1 = true;
                break;
            }
        }
        return any1;
    }
    public bool AccountInGoodStanding() => this.Status == PatronStatus.Active && this.OutstandingFines == 0L;
    internal void WhenEachLoanOverdue()
    {
        var create0 = this.Create("Fine", "Amount", 5L, "Reason", "Overdue item");
        if (!create0.IsSuccess)
        {
            throw new InvalidOperationException(create0.ErrorMessage ?? "");
        }
        var created1 = create0.Value;
        this.OutstandingFines = this.OutstandingFines + 5L;
    }
    internal void WhenEachLoanReturned()
    {
        this.CurrentBorrowCount = this.CurrentBorrowCount - 1L;
    }
    internal void WhenEachFineResolved()
    {
        this.OutstandingFines = this.OutstandingFines - 5L;
    }
    public static DomainResult<Patron> Create(long currentBorrowCount, string email, long maxItems, string name, long outstandingFines, IEnumerable<Loan>? loans = null, IEnumerable<Fine>? fines = null, DateOnly? memberSince = null, PatronStatus status = PatronStatus.Active)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(email, "^[^@]+@[^@]+$"))
        {
            return DomainResult<Patron>.Failure("'Email' does not match the required pattern.");
        }
        if (maxItems < 0L)
        {
            return DomainResult<Patron>.Failure("'MaxItems' must be >= 0.");
        }
        if (maxItems > 20L)
        {
            return DomainResult<Patron>.Failure("'MaxItems' must be <= 20.");
        }
        if (string.IsNullOrEmpty(name))
        {
            return DomainResult<Patron>.Failure("'Name' is required.");
        }
        var created = new Patron(currentBorrowCount, email, maxItems, name, outstandingFines, loans, fines, memberSince, status);
        return DomainResult<Patron>.Success(created);
    }
    private void InitializeSubscriptions()
    {
        foreach (var target in this.Loans)
        {
            target.RegisterPatronOverdueSubscriber(this);
        }
        foreach (var target in this.Loans)
        {
            target.RegisterPatronReturnedSubscriber(this);
        }
        foreach (var target in this.Fines)
        {
            target.RegisterPatronResolvedSubscriber(this);
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
            var typed = Loan.Create((values.ContainsKey("DueDate") ? (DateTime?)values["DueDate"] ?? DateTime.MinValue : DateTime.MinValue), (values.ContainsKey("ReturnedAt") ? (DateTime?)values["ReturnedAt"] ?? DateTime.MinValue : DateTime.MinValue), (values.ContainsKey("Status") ? (string?)values["Status"] ?? "" : ""), (values.ContainsKey("TimesRenewed") ? (long?)values["TimesRenewed"] ?? 0L : 0L), (values.ContainsKey("book") ? (Book?)values["book"] ?? null : null), this);
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
            var typed = Fine.Create((values.ContainsKey("Amount") ? (long?)values["Amount"] ?? 0L : 0L), (values.ContainsKey("Paid") ? (bool?)values["Paid"] ?? false : false), (values.ContainsKey("Reason") ? (string?)values["Reason"] ?? "" : ""), this, (values.ContainsKey("DateIssued") ? (DateTime?)values["DateIssued"] ?? DateTime.MinValue : DateTime.MinValue));
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
        if (relationshipName == "loans")
        {
            var typed = this.CreateLoans((values.ContainsKey("DueDate") ? (DateTime?)values["DueDate"] ?? DateTime.MinValue : DateTime.MinValue), (values.ContainsKey("ReturnedAt") ? (DateTime?)values["ReturnedAt"] ?? DateTime.MinValue : DateTime.MinValue), (values.ContainsKey("Status") ? (string?)values["Status"] ?? "" : ""), (values.ContainsKey("TimesRenewed") ? (long?)values["TimesRenewed"] ?? 0L : 0L), (values.ContainsKey("book") ? (Book?)values["book"] ?? null : null));
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
            var typed = this.CreateFines((values.ContainsKey("Amount") ? (long?)values["Amount"] ?? 0L : 0L), (values.ContainsKey("Paid") ? (bool?)values["Paid"] ?? false : false), (values.ContainsKey("Reason") ? (string?)values["Reason"] ?? "" : ""), (values.ContainsKey("DateIssued") ? (DateTime?)values["DateIssued"] ?? DateTime.MinValue : DateTime.MinValue));
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
    private void OnEntrySuspended()
    {
        var assignValue0 = 0L;
        if (assignValue0 < 0L)
        {
            throw new InvalidOperationException("'MaxItems' must be >= 0.");
        }
        if (assignValue0 > 20L)
        {
            throw new InvalidOperationException("'MaxItems' must be <= 20.");
        }
        this.MaxItems = assignValue0;
    }
    private void OnExitSuspended()
    {
        var assignValue0 = 5L;
        if (assignValue0 < 0L)
        {
            throw new InvalidOperationException("'MaxItems' must be >= 0.");
        }
        if (assignValue0 > 20L)
        {
            throw new InvalidOperationException("'MaxItems' must be <= 20.");
        }
        this.MaxItems = assignValue0;
    }
}