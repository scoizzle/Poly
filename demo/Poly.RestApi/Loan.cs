#nullable enable
using System;
using System.Collections.Generic;

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
    private Loan(DateTime dueDate, DateTime returnedAt, string status, long timesRenewed, Book? book, Patron? borrower) {
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
    public string Status { get; private set; } = default!;
    public long TimesRenewed { get; private set; }
    public Book? Book { get; private set; }
    public Patron? Borrower { get; private set; }
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
    public static DomainResult<Loan> Create(DateTime dueDate, DateTime returnedAt, string status, long timesRenewed, Book? book, Patron? borrower) => DomainResult<Loan>.Success(new Loan(dueDate, returnedAt, status, timesRenewed, book, borrower));
}