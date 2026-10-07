#nullable enable
using System;
using System.Collections.Generic;

public enum FineStage
{
    Unpaid = 0,
    Resolved = 1
}

public class Fine
{
    private List<Patron>? _patronResolvedSubscribers;
    private Fine()
    { }
    private Fine(long amount, bool paid, string reason, Patron? patron = null, DateTime? dateIssued = null)
    {
        this.Amount = amount;
        this.DateIssued = dateIssued ?? DateTime.UtcNow;
        this.Paid = paid;
        this.Reason = reason;
        this.Patron = patron;
        this.CurrentStage = FineStage.Unpaid;
    }
    public long Amount { get; private set; }
    public DateTime DateIssued { get; private set; }
    public bool Paid { get; private set; }
    public string Reason { get; private set; } = default!;
    public Patron? Patron { get; private set; }
    public FineStage CurrentStage { get; private set; }
    private DomainResult<Patron> CreatePatron(long currentBorrowCount, string email, long maxItems, string name, long outstandingFines, DateOnly? memberSince = null, PatronStatus status = PatronStatus.Active)
    {
        var patronResult = Patron.Create(currentBorrowCount, email, maxItems, name, outstandingFines, new List<Loan>(), new List<Fine>(), memberSince, status);
        if (!patronResult.IsSuccess)
        {
            return DomainResult<Patron>.Failure(patronResult.ErrorMessage ?? "");
        }
        var patron = patronResult.Value;
        this.Patron = patron;
        return DomainResult<Patron>.Success(patron);
    }
    public DomainResult Pay()
    {
        if (this.CurrentStage != FineStage.Unpaid)
        {
            return DomainResult.Failure("'Pay' requires stage 'Unpaid' on entity 'Fine'.");
        }
        if (this.Amount <= 0L)
        {
            this.Paid = true;
        }
        else
        {
            this.Paid = true;
        }
        var previousStage0 = this.CurrentStage;
        this.CurrentStage = FineStage.Resolved;
        {
            this.Paid = true;
            this.NotifyResolvedSubscribers(previousStage0);
        }
        return DomainResult.Success();
    }
    public DomainResult Waive()
    {
        if (this.CurrentStage != FineStage.Unpaid)
        {
            return DomainResult.Failure("'Waive' requires stage 'Unpaid' on entity 'Fine'.");
        }
        var assignValue0 = 0L;
        this.Amount = assignValue0;
        this.Paid = true;
        var previousStage1 = this.CurrentStage;
        this.CurrentStage = FineStage.Resolved;
        {
            this.Paid = true;
            this.NotifyResolvedSubscribers(previousStage1);
        }
        return DomainResult.Success();
    }
    internal void RegisterPatronResolvedSubscriber(Patron subscriber)
    {
        if (this._patronResolvedSubscribers == null)
        {
            this._patronResolvedSubscribers = new List<Patron>();
        }
        this._patronResolvedSubscribers.Add(subscriber);
    }
    internal void NotifyResolvedSubscribers(FineStage previousStage)
    {
        if (this._patronResolvedSubscribers != null)
        {
            foreach (var sub in this._patronResolvedSubscribers)
            {
                sub.WhenEachFineResolved();
            }
        }
    }
    public static DomainResult<Fine> Create(long amount, bool paid, string reason, Patron? patron = null, DateTime? dateIssued = null)
    {
        var created = new Fine(amount, paid, reason, patron, dateIssued);
        if (patron != null)
        {
            patron.AttachFines(created);
        }
        return DomainResult<Fine>.Success(created);
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
        if (relationshipName == "patron")
        {
            var typed = this.CreatePatron((values.ContainsKey("CurrentBorrowCount") ? (long?)values["CurrentBorrowCount"] ?? 0L : 0L), (values.ContainsKey("Email") ? (string?)values["Email"] ?? "" : ""), (values.ContainsKey("MaxItems") ? (long?)values["MaxItems"] ?? 0L : 0L), (values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), (values.ContainsKey("OutstandingFines") ? (long?)values["OutstandingFines"] ?? 0L : 0L), (values.ContainsKey("MemberSince") ? (DateOnly?)values["MemberSince"] ?? DateOnly.MinValue : DateOnly.MinValue), (values.ContainsKey("Status") ? (PatronStatus?)values["Status"] ?? PatronStatus.Active : PatronStatus.Active));
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
    private void OnEntryResolved()
    {
        this.Paid = true;
    }
}