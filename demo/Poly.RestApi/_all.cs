#nullable enable
using System;
using System.Collections.Generic;

public enum Genre {
    Fiction = 0,
    NonFiction = 1,
    Reference = 2
}

public enum PatronStatus {
    Active = 0,
    Suspended = 1,
    Closed = 2
}

public enum FineStatus {
    Unpaid = 0,
    Resolved = 1
}

public enum PremiumTier {
    Silver = 0,
    Gold = 1,
    Platinum = 2
}

public record DomainResult {
    private DomainResult(bool isSuccess, string? errorMessage) {
        this.IsSuccess = isSuccess;
        this.ErrorMessage = errorMessage;
    }
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public static DomainResult Success() => new DomainResult(true, null);
    public static DomainResult Failure(string message) => new DomainResult(false, message);
}

public record DomainResult<T> {
    private DomainResult(bool isSuccess, T value, string? errorMessage) {
        this.IsSuccess = isSuccess;
        this.Value = value;
        this.ErrorMessage = errorMessage;
    }
    public bool IsSuccess { get; }
    public T Value { get; }
    public string? ErrorMessage { get; }
    public static DomainResult<T> Success(T value) => new DomainResult<T>(true, value, null);
    public static DomainResult<T> Failure(string message) => new DomainResult<T>(false, default!, message);
}

public class Book {
    private Book() {
        // EF materialization.
    }
    private Book(string author, Genre genre, string isbn, long pages, string title) {
        this.Author = author;
        this.Genre = genre;
        this.ISBN = isbn;
        this.Pages = pages;
        this.Title = title;
    }
    public bool IsDeleted { get; private set; }
    public string Author { get; private set; }
    public Genre Genre { get; private set; }
    public string ISBN { get; private set; }
    public long Pages { get; private set; }
    public string Title { get; private set; }
    public static DomainResult<Book> Create(string author, Genre genre, string isbn, long pages, string title) {
        if (string.IsNullOrEmpty(author)) {
            return DomainResult<Book>.Failure("'Author' is required.");
        }
        if (isbn.Length < 10L) {
            return DomainResult<Book>.Failure("'ISBN' must be at least 10 characters.");
        }
        if (isbn.Length > 17L) {
            return DomainResult<Book>.Failure("'ISBN' must be at most 17 characters.");
        }
        if (pages < 1L) {
            return DomainResult<Book>.Failure("'Pages' must be >= 1.");
        }
        if (pages > 10000L) {
            return DomainResult<Book>.Failure("'Pages' must be <= 10000.");
        }
        if (string.IsNullOrEmpty(title)) {
            return DomainResult<Book>.Failure("'Title' is required.");
        }
        return DomainResult<Book>.Success(new Book(author, genre, isbn, pages, title));
    }
}

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
    private Patron(long currentBorrowCount, string email, long maxItems, string name, long outstandingFines, IEnumerable<Loan> loans, IEnumerable<Fine> fines) {
        this.CurrentBorrowCount = currentBorrowCount;
        this.Email = email;
        this.MaxItems = maxItems;
        this.MemberSince = DateOnly.FromDateTime(DateTime.UtcNow);
        this.Name = name;
        this.OutstandingFines = outstandingFines;
        this.Status = PatronStatus.Active;
        this._loans = new List<Loan>(loans);
        this._fines = new List<Fine>(fines);
        this.CurrentStage = PatronStage.Active;
        this.InitializeSubscriptions();
    }
    public bool IsDeleted { get; private set; }
    public long CurrentBorrowCount { get; private set; }
    public string Email { get; private set; }
    public long MaxItems { get; private set; }
    public DateOnly MemberSince { get; private set; }
    public string Name { get; private set; }
    public long OutstandingFines { get; private set; }
    public PatronStatus Status { get; private set; }
    public IReadOnlyList<Loan> Loans {
        get => this._loans;
    }
    public IReadOnlyList<Fine> Fines {
        get => this._fines;
    }
    public PatronStage CurrentStage { get; private set; }
    private Loan CreateLoans(DateTime checkedOutAt, DateTime dueDate, DateTime returnedAt, string status, long timesRenewed, Book book) {
        var loanResult = Loan.Create(checkedOutAt, dueDate, returnedAt, status, timesRenewed, book, this);
        if (!loanResult.IsSuccess) {
            throw new InvalidOperationException(loanResult.ErrorMessage);
        }
        var loan = loanResult.Value;
        this._loans.Add(loan);
        loan.RegisterPatronOverdueSubscriber(this);
        loan.RegisterPatronReturnedSubscriber(this);
        return loan;
    }
    private Fine CreateFines(long amount, bool paid, string reason) {
        var fineResult = Fine.Create(amount, paid, reason, this);
        if (!fineResult.IsSuccess) {
            throw new InvalidOperationException(fineResult.ErrorMessage);
        }
        var fine = fineResult.Value;
        this._fines.Add(fine);
        fine.RegisterPatronPaidSubscriber(this);
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
        return DomainResult<Loan>.Success(this.CreateLoans(DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, "", 0L, book));
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
        var fineResult = Fine.Create(5L, false, "Overdue item", this);
        if (!fineResult.IsSuccess) {
            throw new InvalidOperationException(fineResult.ErrorMessage);
        }
        var fine = fineResult.Value;
        this.OutstandingFines = this.OutstandingFines + 5L;
    }
    internal void WhenLoanReturned() {
        this.CurrentBorrowCount = this.CurrentBorrowCount - 1L;
    }
    internal void WhenFinePaid() {
        this.OutstandingFines = this.OutstandingFines - 5L;
    }
    public static DomainResult<Patron> Create(long currentBorrowCount, string email, long maxItems, string name, long outstandingFines, IEnumerable<Loan> loans, IEnumerable<Fine> fines) {
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
        return DomainResult<Patron>.Success(new Patron(currentBorrowCount, email, maxItems, name, outstandingFines, loans, fines));
    }
    private void InitializeSubscriptions() {
        foreach (var target in this.Loans) {
            target.RegisterPatronOverdueSubscriber(this);
        }
        foreach (var target in this.Loans) {
            target.RegisterPatronReturnedSubscriber(this);
        }
        foreach (var target in this.Fines) {
            target.RegisterPatronPaidSubscriber(this);
        }
    }
}

public enum LoanStage {
    Active = 0,
    Overdue = 1,
    Returned = 2
}

public class Loan {
    private List<Patron>? _overdueSubscribers;
    private List<Patron>? _returnedSubscribers;
    private Loan() {
        // EF materialization.
    }
    private Loan(DateTime checkedOutAt, DateTime dueDate, DateTime returnedAt, string status, long timesRenewed, Book book, Patron borrower) {
        this.CheckedOutAt = checkedOutAt;
        this.DueDate = dueDate;
        this.ReturnedAt = returnedAt;
        this.Status = status;
        this.TimesRenewed = timesRenewed;
        this.Book = book;
        this.Borrower = borrower;
        this.CurrentStage = LoanStage.Active;
        this.CheckedOutAt = DateTime.UtcNow;
    }
    public bool IsDeleted { get; private set; }
    public DateTime CheckedOutAt { get; private set; }
    public DateTime DueDate { get; private set; }
    public DateTime ReturnedAt { get; private set; }
    public string Status { get; private set; }
    public long TimesRenewed { get; private set; }
    public Book Book { get; private set; }
    public Patron Borrower { get; private set; }
    public LoanStage CurrentStage { get; private set; }
    public DomainResult Renew() {
        if (this.CurrentStage != LoanStage.Active) {
            return DomainResult.Failure("'Renew' requires stage 'Active' on entity 'Loan'.");
        }
        this.DueDate = this.DueDate.AddDays(14L);
        this.TimesRenewed = this.TimesRenewed + 1L;
        return DomainResult.Success();
    }
    public DomainResult Return() {
        if (this.CurrentStage != LoanStage.Active) {
            return DomainResult.Failure("'Return' requires stage 'Active' on entity 'Loan'.");
        }
        this.ReturnedAt = DateTime.UtcNow;
        this.CurrentStage = LoanStage.Returned;
        this.NotifyReturnedSubscribers();
        return DomainResult.Success();
    }
    internal void RegisterPatronOverdueSubscriber(Patron subscriber) {
        if (this._overdueSubscribers == null) {
            this._overdueSubscribers = new List<Patron>();
        }
        this._overdueSubscribers.Add(subscriber);
    }
    internal void NotifyOverdueSubscribers() {
        if (this._overdueSubscribers != null) {
            foreach (var sub in this._overdueSubscribers) {
                sub.WhenLoanOverdue();
            }
        }
    }
    internal void RegisterPatronReturnedSubscriber(Patron subscriber) {
        if (this._returnedSubscribers == null) {
            this._returnedSubscribers = new List<Patron>();
        }
        this._returnedSubscribers.Add(subscriber);
    }
    internal void NotifyReturnedSubscribers() {
        if (this._returnedSubscribers != null) {
            foreach (var sub in this._returnedSubscribers) {
                sub.WhenLoanReturned();
            }
        }
    }
    public static DomainResult<Loan> Create(DateTime checkedOutAt, DateTime dueDate, DateTime returnedAt, string status, long timesRenewed, Book book, Patron borrower) => DomainResult<Loan>.Success(new Loan(checkedOutAt, dueDate, returnedAt, status, timesRenewed, book, borrower));
}

public enum FineStage {
    Unpaid = 0,
    Resolved = 1
}

public class Fine {
    private List<Patron>? _paidSubscribers;
    private Fine() {
        // EF materialization.
    }
    private Fine(long amount, bool paid, string reason, Patron patron) {
        this.Amount = amount;
        this.DateIssued = DateTime.UtcNow;
        this.Paid = paid;
        this.Reason = reason;
        this.Patron = patron;
        this.CurrentStage = FineStage.Unpaid;
    }
    public bool IsDeleted { get; private set; }
    public long Amount { get; private set; }
    public DateTime DateIssued { get; private set; }
    public bool Paid { get; private set; }
    public string Reason { get; private set; }
    public Patron Patron { get; private set; }
    public FineStage CurrentStage { get; private set; }
    public DomainResult Pay() {
        if (this.CurrentStage != FineStage.Unpaid) {
            return DomainResult.Failure("'Pay' requires stage 'Unpaid' on entity 'Fine'.");
        }
        if (this.Amount <= 0L) {
            this.Paid = true;
            this.IsDeleted = true;
        }
        else {
            this.Paid = true;
        }
        this.Paid = true;
        this.CurrentStage = FineStage.Resolved;
        return DomainResult.Success();
    }
    public DomainResult Waive() {
        if (this.CurrentStage != FineStage.Unpaid) {
            return DomainResult.Failure("'Waive' requires stage 'Unpaid' on entity 'Fine'.");
        }
        this.Amount = 0L;
        this.Paid = true;
        this.Paid = true;
        this.CurrentStage = FineStage.Resolved;
        return DomainResult.Success();
    }
    internal void RegisterPatronPaidSubscriber(Patron subscriber) {
        if (this._paidSubscribers == null) {
            this._paidSubscribers = new List<Patron>();
        }
        this._paidSubscribers.Add(subscriber);
    }
    internal void NotifyPaidSubscribers() {
        if (this._paidSubscribers != null) {
            foreach (var sub in this._paidSubscribers) {
                sub.WhenFinePaid();
            }
        }
    }
    public static DomainResult<Fine> Create(long amount, bool paid, string reason, Patron patron) => DomainResult<Fine>.Success(new Fine(amount, paid, reason, patron));
}

public class PremiumPatron {
    private PremiumPatron() {
        // EF materialization.
    }
    private PremiumPatron(string email, string name, bool priorityAccess, long rewardPoints) {
        this.Email = email;
        this.Name = name;
        this.PriorityAccess = priorityAccess;
        this.RewardPoints = rewardPoints;
        this.Tier = PremiumTier.Silver;
    }
    public bool IsDeleted { get; private set; }
    public string Email { get; private set; }
    public string Name { get; private set; }
    public bool PriorityAccess { get; private set; }
    public long RewardPoints { get; private set; }
    public PremiumTier Tier { get; private set; }
    public bool IsLoyal() => this.RewardPoints >= 100L;
    public bool HasPriority() => this.PriorityAccess;
    public bool UnlimitedItems() => this.Tier == PremiumTier.Platinum || this.PriorityAccess;
    public static DomainResult<PremiumPatron> Create(string email, string name, bool priorityAccess, long rewardPoints) {
        if (!System.Text.RegularExpressions.Regex.IsMatch(email, "^[^@]+@[^@]+$")) {
            return DomainResult<PremiumPatron>.Failure("'Email' does not match the required pattern.");
        }
        if (string.IsNullOrEmpty(name)) {
            return DomainResult<PremiumPatron>.Failure("'Name' is required.");
        }
        return DomainResult<PremiumPatron>.Success(new PremiumPatron(email, name, priorityAccess, rewardPoints));
    }
}