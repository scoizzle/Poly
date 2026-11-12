#nullable enable
using System;
using System.Collections.Generic;

using Poly.DomainModeling.Runtime;
using Poly.Runtime;

public enum PatronStage
{
    Active = 0,
    Suspended = 1,
    Closed = 2
}

public class Patron
{
    private List<Loan> _loans = new List<Loan>();
    private List<Fine> _fines = new List<Fine>();
    private Patron() { }
    private Patron(string name, string email, long maxItems = 0L, IEnumerable<Loan>? loans = null, IEnumerable<Fine>? fines = null, DateOnly? memberSince = null, PatronStatus status = PatronStatus.Active, long currentBorrowCount = 0L, long outstandingFines = 0L)
    {
        this.Name = name;
        this.Email = email;
        this.MaxItems = maxItems;
        this.MemberSince = memberSince ?? DateOnly.FromDateTime(DateTime.UtcNow);
        this.Status = status;
        this.CurrentBorrowCount = currentBorrowCount;
        this.OutstandingFines = outstandingFines;
        this._loans = new List<Loan>(loans ?? new List<Loan>());
        this._fines = new List<Fine>(fines ?? new List<Fine>());
        this.CurrentStage = PatronStage.Active;
        this.InitializeSubscriptions();
    }
    public string Name { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public DateOnly MemberSince { get; private set; }
    public PatronStatus Status { get; private set; }
    public long MaxItems { get; private set; }
    public long CurrentBorrowCount { get; private set; }
    public long OutstandingFines { get; private set; }
    public IReadOnlyList<Loan> Loans
    {
        get => this._loans;
    }
    public IReadOnlyList<Fine> Fines
    {
        get => this._fines;
    }
    public PatronStage CurrentStage { get; private set; }
    public DomainResult<Loan> CreateLoans(Book? book = null, string? status = null, DateTime? dueDate = null, DateTime? returnedAt = null, long? timesRenewed = null)
    {
        var loanResult = Loan.Create(book, this, status, dueDate, returnedAt, timesRenewed);
        if (!loanResult.IsSuccess)
        {
            return DomainResult<Loan>.Failure(loanResult.Error!);
        }
        var loan = loanResult.Value;
        return DomainResult<Loan>.Success(loan);
    }
    internal void AttachLoans(Loan child)
    {
        this._loans.Add(child);
        child.RegisterPatronLoansOverdueSubscriber(this);
        child.RegisterPatronLoansReturnedSubscriber(this);
    }
    internal void AttachFines(Fine child)
    {
        this._fines.Add(child);
        child.RegisterPatronFinesResolvedSubscriber(this);
    }
    public DomainResult<Loan> CheckOut(Book book)
    {
        if (this.CurrentStage != PatronStage.Active)
        {
            return DomainResult<Loan>.Failure(Poly.Runtime.DomainFailure.Stage("'CheckOut' requires stage 'Active' on entity 'Patron'.", "Patron.CheckOut", "Active"));
        }
        if (!this.GoodStanding())
        {
            return DomainResult<Loan>.Failure(Poly.Runtime.DomainFailure.Policy("'CheckOut' blocked by policy 'GoodStanding'.", "Patron.CheckOut", "GoodStanding"));
        }
        if (this.AtLimit())
        {
            return DomainResult<Loan>.Failure(Poly.Runtime.DomainFailure.Policy("'CheckOut' blocked by policy 'AtLimit'.", "Patron.CheckOut", "AtLimit"));
        }
        if (this.HasFines())
        {
            return DomainResult<Loan>.Failure(Poly.Runtime.DomainFailure.Policy("'CheckOut' blocked by policy 'HasFines'.", "Patron.CheckOut", "HasFines"));
        }
        this.CurrentBorrowCount = this.CurrentBorrowCount + 1L;
        var create0 = this.CreateLoans();
        if (!create0.IsSuccess)
        {
            return DomainResult<Loan>.Failure(create0.Error!.At("Patron.CheckOut"));
        }
        var created1 = create0.Value;
        return DomainResult<Loan>.Success(created1);
    }
    public DomainResult Suspend()
    {
        if (this.CurrentStage != PatronStage.Active)
        {
            return DomainResult.Failure(Poly.Runtime.DomainFailure.Stage("'Suspend' requires stage 'Active' on entity 'Patron'.", "Patron.Suspend", "Active"));
        }
        this.Status = PatronStatus.Suspended;
        this.CurrentBorrowCount = 0L;
        this.CurrentStage = PatronStage.Suspended;
        this.Status = PatronStatus.Suspended;
        this.MaxItems = 0L;
        return DomainResult.Success();
    }
    public DomainResult CloseAccount()
    {
        if (this.CurrentStage != PatronStage.Active)
        {
            return DomainResult.Failure(Poly.Runtime.DomainFailure.Stage("'CloseAccount' requires stage 'Active' on entity 'Patron'.", "Patron.CloseAccount", "Active"));
        }
        this.CurrentStage = PatronStage.Closed;
        this.Status = PatronStatus.Closed;
        return DomainResult.Success();
    }
    public DomainResult Reinstate()
    {
        if (this.CurrentStage != PatronStage.Suspended)
        {
            return DomainResult.Failure(Poly.Runtime.DomainFailure.Stage("'Reinstate' requires stage 'Suspended' on entity 'Patron'.", "Patron.Reinstate", "Suspended"));
        }
        if (this.HasOverdueLoans())
        {
            return DomainResult.Failure(Poly.Runtime.DomainFailure.Policy("'Reinstate' blocked by policy 'HasOverdueLoans'.", "Patron.Reinstate", "HasOverdueLoans"));
        }
        this.Status = PatronStatus.Active;
        this.MaxItems = 5L;
        this.CurrentStage = PatronStage.Active;
        this.Status = PatronStatus.Active;
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
        var create0 = Fine.Create("Overdue item", 5L);
        if (!create0.IsSuccess)
        {
            throw new ConstraintFailureException(create0.ErrorMessage ?? "");
        }
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
    public static DomainResult<Patron> Create(string name, string email, Poly.Runtime.IStoragePort db, long maxItems = 0L, IEnumerable<Loan>? loans = null, IEnumerable<Fine>? fines = null, DateOnly? memberSince = null, PatronStatus status = PatronStatus.Active, long currentBorrowCount = 0L, long outstandingFines = 0L)
    {
        if (string.IsNullOrEmpty(name))
        {
            return DomainResult<Patron>.Failure(Poly.Runtime.DomainFailure.Required("'Name' is required.", "Patron.Name"));
        }
        if (string.IsNullOrEmpty(email))
        {
            return DomainResult<Patron>.Failure(Poly.Runtime.DomainFailure.Required("'Email' is required.", "Patron.Email"));
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(email, "^[^@]+@[^@]+\\z"))
        {
            return DomainResult<Patron>.Failure(Poly.Runtime.DomainFailure.Pattern("'Email' does not match the required pattern.", "Patron.Email"));
        }
        if (status != PatronStatus.Active && status != PatronStatus.Suspended && status != PatronStatus.Closed)
        {
            return DomainResult<Patron>.Failure(Poly.Runtime.DomainFailure.Enum(string.Concat("'", status, "' is not a valid member of PatronStatus for property 'Status'."), "Patron.Status"));
        }
        if (maxItems < 0L)
        {
            return DomainResult<Patron>.Failure(Poly.Runtime.DomainFailure.Range("'MaxItems' must be >= 0.", "Patron.MaxItems"));
        }
        if (maxItems > 20L)
        {
            return DomainResult<Patron>.Failure(Poly.Runtime.DomainFailure.Range("'MaxItems' must be <= 20.", "Patron.MaxItems"));
        }
        if (Patron.ExistsWithEmail(db, email))
        {
            return DomainResult<Patron>.Failure(Poly.Runtime.DomainFailure.Unique("Unique constraint violated: 'Email' value is already used on another 'Patron'.", "Patron.Email"));
        }
        var created = new Patron(name, email, maxItems, loans, fines, memberSince, status, currentBorrowCount, outstandingFines);
        db.Record("Patron", "Email", email);
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
        foreach (var target in this.Fines)
        {
            target.RegisterPatronFinesResolvedSubscriber(this);
        }
    }
    public DomainResult EnsureUnique(string propertyName, object value)
    {
        if (value == null)
        {
            return DomainResult.Success();
        }
        if (propertyName == "Email" && object.Equals(this.Email, value))
        {
            return DomainResult.Success();
        }
        return DomainResult.Failure(Poly.Runtime.DomainFailure.Constraint("A unique assign needs the storage port.", "Patron"));
    }
    public static System.Boolean ExistsWithEmail(Poly.Runtime.IStoragePort db, System.String email) => db.Exists("Patron", "Email", email);
}