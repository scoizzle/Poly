#nullable enable
using System;
using System.Collections.Generic;

public class PremiumPatron
{
    private PremiumPatron()
    { }
    private PremiumPatron(string email, string name, bool priorityAccess, long rewardPoints, PremiumTier tier = PremiumTier.Silver)
    {
        this.Email = email;
        this.Name = name;
        this.PriorityAccess = priorityAccess;
        this.RewardPoints = rewardPoints;
        this.Tier = tier;
    }
    public string Email { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public bool PriorityAccess { get; private set; }
    public long RewardPoints { get; private set; }
    public PremiumTier Tier { get; private set; }
    public bool IsLoyal() => this.RewardPoints >= 100L;
    public bool HasPriority() => this.PriorityAccess;
    public bool UnlimitedItems() => this.Tier == PremiumTier.Platinum || this.PriorityAccess;
    public static DomainResult<PremiumPatron> Create(string email, string name, bool priorityAccess, long rewardPoints, PremiumTier tier = PremiumTier.Silver)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(email, "^[^@]+@[^@]+$"))
        {
            return DomainResult<PremiumPatron>.Failure("'Email' does not match the required pattern.");
        }
        if (string.IsNullOrEmpty(name))
        {
            return DomainResult<PremiumPatron>.Failure("'Name' is required.");
        }
        var created = new PremiumPatron(email, name, priorityAccess, rewardPoints, tier);
        return DomainResult<PremiumPatron>.Success(created);
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
    private DomainResult<object> BindCreateIn(string relationshipName, Dictionary<string, object> values) => DomainResult<object>.Failure("Unknown relationship '" + relationshipName + "'.");
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
}