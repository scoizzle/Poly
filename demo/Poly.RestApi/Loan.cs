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
    private List<Patron>? _patronLoansReturnedSubscribers;
    private int _notifyDepth;
    private Loan() { }
    private Loan(Book? book = null, Patron? borrower = null, DateTime? dueDate = null, DateTime? returnedAt = null, string? status = null, long? timesRenewed = null)
    {
        this.DueDate = dueDate ?? DateTime.UtcNow;
        this.ReturnedAt = returnedAt;
        this.Status = status;
        this.TimesRenewed = timesRenewed;
        this.Book = book;
        this.Borrower = borrower;
        this.CurrentStage = LoanStage.Active;
        this.CheckedOutAt = DateTime.UtcNow;
    }
    public DateTime CheckedOutAt { get; private set; }
    public DateTime DueDate { get; private set; }
    public DateTime? ReturnedAt { get; private set; }
    public string? Status { get; private set; }
    public long? TimesRenewed { get; private set; }
    public Book? Book { get; private set; }
    public Patron? Borrower { get; private set; }
    public LoanStage CurrentStage { get; private set; }
    public DomainResult Renew()
    {
        if (this.CurrentStage != LoanStage.Active)
        {
            return DomainResult.Failure("'Renew' requires stage 'Active' on entity 'Loan'.");
        }
        this.DueDate = this.DueDate.AddDays(14L);
        this.TimesRenewed = this.TimesRenewed + 1L;
        return DomainResult.Success();
    }
    public DomainResult Return()
    {
        if (this.CurrentStage != LoanStage.Active)
        {
            return DomainResult.Failure("'Return' requires stage 'Active' on entity 'Loan'.");
        }
        this.ReturnedAt = DateTime.UtcNow;
        this.CurrentStage = LoanStage.Returned;
        this.NotifyReturnedSubscribers();
        return DomainResult.Success();
    }
    internal void RegisterPatronLoansOverdueSubscriber(Patron subscriber)
    {
        if (this._patronLoansOverdueSubscribers == null)
        {
            this._patronLoansOverdueSubscribers = new List<Patron>();
        }
        foreach (var existing in this._patronLoansOverdueSubscribers)
        {
            if (object.ReferenceEquals(existing, subscriber))
            {
                return;
            }
        }
        this._patronLoansOverdueSubscribers.Add(subscriber);
    }
    internal void RegisterPatronLoansReturnedSubscriber(Patron subscriber)
    {
        if (this._patronLoansReturnedSubscribers == null)
        {
            this._patronLoansReturnedSubscribers = new List<Patron>();
        }
        foreach (var existing in this._patronLoansReturnedSubscribers)
        {
            if (object.ReferenceEquals(existing, subscriber))
            {
                return;
            }
        }
        this._patronLoansReturnedSubscribers.Add(subscriber);
    }
    internal void NotifyOverdueSubscribers()
    {
        this.EnterNotifyCascade("Overdue");
        try
        {
            if (this._patronLoansOverdueSubscribers != null)
            {
                foreach (var sub in this._patronLoansOverdueSubscribers)
                {
                    sub.WhenEachLoanOverdue();
                }
            }
        }
        finally
        {
            this.ExitNotifyCascade();
        }
    }
    internal void NotifyReturnedSubscribers()
    {
        this.EnterNotifyCascade("Returned");
        try
        {
            if (this._patronLoansReturnedSubscribers != null)
            {
                foreach (var sub in this._patronLoansReturnedSubscribers)
                {
                    sub.WhenEachLoanReturned();
                }
            }
        }
        finally
        {
            this.ExitNotifyCascade();
        }
    }
    public static DomainResult<Loan> Create(Book? book = null, Patron? borrower = null, DateTime? dueDate = null, DateTime? returnedAt = null, string? status = null, long? timesRenewed = null)
    {
        var created = new Loan(book, borrower, dueDate, returnedAt, status, timesRenewed);
        if (borrower != null)
        {
            borrower.AttachLoans(created);
        }
        return DomainResult<Loan>.Success(created);
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
}