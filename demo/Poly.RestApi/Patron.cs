#nullable enable
public enum PatronStage {
    Active = 0,
    Suspended = 1,
    Closed = 2
}

public class Patron {
    private readonly List<Loan> _loans;
    private readonly List<Fine> _fines;
    private Patron() {
        this._loans = new List<Loan>();
        this._fines = new List<Fine>();
    }
    private Patron(long currentBorrowCount, string email, long maxItems, string name, long outstandingFines, IEnumerable<Loan> loans, IEnumerable<Fine> fines, DateOnly? memberSince = null, PatronStatus status = PatronStatus.Active) {
        this.CurrentBorrowCount = currentBorrowCount;
        this.Email = email;
        this.MaxItems = maxItems;
        this.MemberSince = memberSince ?? DateOnly.FromDateTime(DateTime.UtcNow);
        this.Name = name;
        this.OutstandingFines = outstandingFines;
        this.Status = status;
        this._loans = new List<Loan>(loans);
        this._fines = new List<Fine>(fines);
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
    public IReadOnlyList<Loan> Loans {
        get => this._loans;
    }
    public IReadOnlyList<Fine> Fines {
        get => this._fines;
    }
    public PatronStage CurrentStage { get; private set; }
    private Loan CreateLoans(DateTime dueDate, DateTime returnedAt, string status, long timesRenewed, Book? book) {
        var loanResult = Loan.Create(dueDate, returnedAt, status, timesRenewed, book, this);
        if (!loanResult.IsSuccess) {
            throw new InvalidOperationException(loanResult.ErrorMessage);
        }
        var loan = loanResult.Value;
        this._loans.Add(loan);
        loan.RegisterPatronOverdueSubscriber(this);
        loan.RegisterPatronReturnedSubscriber(this);
        return loan;
    }
    private Fine CreateFines(long amount, bool paid, string reason, DateTime? dateIssued = null) {
        var fineResult = Fine.Create(amount, paid, reason, this, dateIssued);
        if (!fineResult.IsSuccess) {
            throw new InvalidOperationException(fineResult.ErrorMessage);
        }
        var fine = fineResult.Value;
        this._fines.Add(fine);
        fine.RegisterPatronResolvedSubscriber(this);
        return fine;
    }
    public DomainResult<Loan> CheckOut(Book book) {
        if (this.CurrentStage != PatronStage.Active) {
            return DomainResult<Loan>.Failure("'CheckOut' requires stage 'Active' on entity 'Patron'.");
        }
        if (!this.GoodStanding()) {
            return DomainResult<Loan>.Failure("'CheckOut' blocked by policy 'GoodStanding'.");
        }
        if (this.AtLimit()) {
            return DomainResult<Loan>.Failure("'CheckOut' blocked by policy 'AtLimit'.");
        }
        if (this.HasFines()) {
            return DomainResult<Loan>.Failure("'CheckOut' blocked by policy 'HasFines'.");
        }
        this.CurrentBorrowCount = this.CurrentBorrowCount + 1L;
        return DomainResult<Loan>.Success(this.CreateLoans(DateTime.MinValue, DateTime.MinValue, "", 0L, book));
    }
    public DomainResult Suspend() {
        if (this.CurrentStage != PatronStage.Active) {
            return DomainResult.Failure("'Suspend' requires stage 'Active' on entity 'Patron'.");
        }
        this.Status = PatronStatus.Suspended;
        this.CurrentBorrowCount = 0L;
        this.MaxItems = 0L;
        this.CurrentStage = PatronStage.Suspended;
        return DomainResult.Success();
    }
    public DomainResult CloseAccount() {
        if (this.CurrentStage != PatronStage.Active) {
            return DomainResult.Failure("'CloseAccount' requires stage 'Active' on entity 'Patron'.");
        }
        this.CurrentStage = PatronStage.Closed;
        return DomainResult.Success();
    }
    public DomainResult Reinstate() {
        if (this.CurrentStage != PatronStage.Suspended) {
            return DomainResult.Failure("'Reinstate' requires stage 'Suspended' on entity 'Patron'.");
        }
        if (this.HasOverdueLoans()) {
            return DomainResult.Failure("'Reinstate' blocked by policy 'HasOverdueLoans'.");
        }
        this.Status = PatronStatus.Active;
        this.MaxItems = 5L;
        this.CurrentStage = PatronStage.Active;
        return DomainResult.Success();
    }
    public bool GoodStanding() => this.Status == PatronStatus.Active;
    public bool AtLimit() => this.CurrentBorrowCount >= this.MaxItems;
    public bool HasFines() => this.OutstandingFines > 0L;
    public bool HasOverdueLoans() {
        throw new NotSupportedException("Policy 'HasOverdueLoans' requires store-aware evaluation and cannot be compiled to standalone C#.");
    }
    public bool AccountInGoodStanding() => this.Status == PatronStatus.Active && this.OutstandingFines == 0L;
    internal void WhenLoanOverdue() {
        var fineResult = Fine.Create(5L, false, "Overdue item", null);
        if (!fineResult.IsSuccess) {
            throw new InvalidOperationException(fineResult.ErrorMessage);
        }
        var fine = fineResult.Value;
        this.OutstandingFines = this.OutstandingFines + 5L;
    }
    internal void WhenLoanReturned() {
        this.CurrentBorrowCount = this.CurrentBorrowCount - 1L;
    }
    internal void WhenFineResolved() {
        this.OutstandingFines = this.OutstandingFines - 5L;
    }
    public static DomainResult<Patron> Create(long currentBorrowCount, string email, long maxItems, string name, long outstandingFines, IEnumerable<Loan> loans, IEnumerable<Fine> fines, DateOnly? memberSince = null, PatronStatus status = PatronStatus.Active) {
        if (!System.Text.RegularExpressions.Regex.IsMatch(email, "^[^@]+@[^@]+$")) {
            return DomainResult<Patron>.Failure("'Email' does not match the required pattern.");
        }
        if (maxItems < 0L) {
            return DomainResult<Patron>.Failure("'MaxItems' must be >= 0.");
        }
        if (maxItems > 20L) {
            return DomainResult<Patron>.Failure("'MaxItems' must be <= 20.");
        }
        if (string.IsNullOrEmpty(name)) {
            return DomainResult<Patron>.Failure("'Name' is required.");
        }
        return DomainResult<Patron>.Success(new Patron(currentBorrowCount, email, maxItems, name, outstandingFines, loans, fines, memberSince, status));
    }
    private void InitializeSubscriptions() {
        foreach (var target in this.Loans) {
            target.RegisterPatronOverdueSubscriber(this);
        }
        foreach (var target in this.Loans) {
            target.RegisterPatronReturnedSubscriber(this);
        }
        foreach (var target in this.Fines) {
            target.RegisterPatronResolvedSubscriber(this);
        }
    }
}