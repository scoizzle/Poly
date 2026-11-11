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
    private List<Loan> _loans = new List<Loan>();
    private List<Fine> _fines = new List<Fine>();
    private Patron() { }
    private Patron(string email, string name, long maxItems = 0L, IEnumerable<Loan>? loans = null, IEnumerable<Fine>? fines = null, long currentBorrowCount = 0L, DateOnly? memberSince = null, long outstandingFines = 0L, PatronStatus status = PatronStatus.Active)
    {
        this.Email = email;
        this.Name = name;
        this.MaxItems = maxItems;
        this.CurrentBorrowCount = currentBorrowCount;
        this.MemberSince = memberSince ?? DateOnly.FromDateTime(DateTime.UtcNow);
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
    public DomainResult<Loan> CreateLoans(Book? book = null, DateTime? dueDate = null, DateTime? returnedAt = null, string? status = null, long? timesRenewed = null)
    {
        var loanResult = Loan.Create(book, this, dueDate, returnedAt, status, timesRenewed);
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
        this.CurrentBorrowCount = this.CurrentBorrowCount + 1L;
        var create0 = this.CreateLoans();
        if (!create0.IsSuccess)
        {
            return DomainResult<Loan>.Failure(create0.ErrorMessage ?? "");
        }
        var created1 = create0.Value;
        return DomainResult<Loan>.Success(created1);
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
        this.Status = PatronStatus.Suspended;
        this.MaxItems = 0L;
        return DomainResult.Success();
    }
    public DomainResult CloseAccount()
    {
        if (this.CurrentStage != PatronStage.Active)
        {
            return DomainResult.Failure("'CloseAccount' requires stage 'Active' on entity 'Patron'.");
        }
        this.CurrentStage = PatronStage.Closed;
        this.Status = PatronStatus.Closed;
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
    public static DomainResult<Patron> Create(string email, string name, Poly.DomainModeling.Runtime.IStoragePort db, long maxItems = 0L, IEnumerable<Loan>? loans = null, IEnumerable<Fine>? fines = null, long currentBorrowCount = 0L, DateOnly? memberSince = null, long outstandingFines = 0L, PatronStatus status = PatronStatus.Active)
    {
        if (string.IsNullOrEmpty(email))
        {
            return DomainResult<Patron>.Failure("'Email' is required.");
        }
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
        if (status != PatronStatus.Active && status != PatronStatus.Suspended && status != PatronStatus.Closed)
        {
            return DomainResult<Patron>.Failure(string.Concat("'", status, "' is not a valid member of PatronStatus for property 'Status'."));
        }
        if (Patron.ExistsWithEmail(db, email))
        {
            return DomainResult<Patron>.Failure("Unique constraint violated: 'Email' value is already used on another 'Patron'.");
        }
        var created = new Patron(email, name, maxItems, loans, fines, currentBorrowCount, memberSince, outstandingFines, status);
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
        return DomainResult.Failure("A unique assign needs the storage port.");
    }
    public static System.Boolean ExistsWithEmail(Poly.DomainModeling.Runtime.IStoragePort db, System.String email) => db.Exists("Patron", "Email", email);
}