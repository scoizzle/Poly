#nullable enable
using System;
using System.Collections.Generic;

using Poly.Runtime;

internal enum LoanStage
{
    Active = 0,
    Overdue = 1,
    Returned = 2
}

internal class Loan : Poly.Runtime.IActionScoped
{
    private List<Patron>? _patronLoansOverdueSubscribers;
    private List<Patron>? _patronLoansReturnedSubscribers;
    private int _notifyDepth;
    private Loan() { }
    private Loan(Book? book = null, Patron? borrower = null, string? status = null, DateTime? dueDate = null, DateTime? returnedAt = null, long? timesRenewed = null)
    {
        this.Status = status;
        this.DueDate = dueDate ?? DateTime.UtcNow;
        this.ReturnedAt = returnedAt;
        this.TimesRenewed = timesRenewed;
        this.Book = book;
        this.Borrower = borrower;
        this.CurrentStage = LoanStage.Active;
        this.CheckedOutAt = DateTime.UtcNow;
    }
    public string? Status { get; private set; }
    public DateTime CheckedOutAt { get; private set; }
    public DateTime DueDate { get; private set; }
    public DateTime? ReturnedAt { get; private set; }
    public long? TimesRenewed { get; private set; }
    public Book? Book { get; private set; }
    public Patron? Borrower { get; private set; }
    public LoanStage CurrentStage { get; private set; }
    internal DomainResult Renew()
    {
        if (this.CurrentStage != LoanStage.Active)
        {
            return DomainResult.Failure(Poly.Runtime.DomainFailure.Stage("'Renew' requires stage 'Active' on entity 'Loan'.", "Loan.Renew", "Active"));
        }
        this.DueDate = this.DueDate.AddDays(14L);
        this.TimesRenewed = this.TimesRenewed + 1L;
        return DomainResult.Success();
    }
    internal DomainResult Return()
    {
        if (this.CurrentStage != LoanStage.Active)
        {
            return DomainResult.Failure(Poly.Runtime.DomainFailure.Stage("'Return' requires stage 'Active' on entity 'Loan'.", "Loan.Return", "Active"));
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
    internal static DomainResult<Loan> Create(Book? book = null, Patron? borrower = null, string? status = null, DateTime? dueDate = null, DateTime? returnedAt = null, long? timesRenewed = null)
    {
        var created = new Loan(book, borrower, status, dueDate, returnedAt, timesRenewed);
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
    public object __Snapshot()
    {
        var __state = new object?[11];
        __state[0] = this.Status;
        __state[1] = this.CheckedOutAt;
        __state[2] = this.DueDate;
        __state[3] = this.ReturnedAt;
        __state[4] = this.TimesRenewed;
        __state[5] = this.Book;
        __state[6] = this.Borrower;
        __state[7] = this.CurrentStage;
        __state[8] = (this._patronLoansOverdueSubscribers == null ? null : new List<Patron>(this._patronLoansOverdueSubscribers));
        __state[9] = (this._patronLoansReturnedSubscribers == null ? null : new List<Patron>(this._patronLoansReturnedSubscribers));
        __state[10] = this._notifyDepth;
        return __state;
    }
    public void __Restore(object state)
    {
        var __saved = (object?[])state;
        this.Status = (string?)__saved[0]!;
        this.CheckedOutAt = (DateTime)__saved[1]!;
        this.DueDate = (DateTime)__saved[2]!;
        this.ReturnedAt = (DateTime?)__saved[3]!;
        this.TimesRenewed = (long?)__saved[4]!;
        this.Book = (Book?)__saved[5]!;
        this.Borrower = (Patron?)__saved[6]!;
        this.CurrentStage = (LoanStage)__saved[7]!;
        if (__saved[8] == null)
        {
            this._patronLoansOverdueSubscribers = null;
        }
        else
        {
            if (this._patronLoansOverdueSubscribers == null)
            {
                this._patronLoansOverdueSubscribers = new List<Patron>();
            }
            ((System.Collections.IList)this._patronLoansOverdueSubscribers).Clear();
            foreach (var __item8 in(List<Patron>)__saved[8]!)
            {
                this._patronLoansOverdueSubscribers.Add(__item8);
            }
        }
        if (__saved[9] == null)
        {
            this._patronLoansReturnedSubscribers = null;
        }
        else
        {
            if (this._patronLoansReturnedSubscribers == null)
            {
                this._patronLoansReturnedSubscribers = new List<Patron>();
            }
            ((System.Collections.IList)this._patronLoansReturnedSubscribers).Clear();
            foreach (var __item9 in(List<Patron>)__saved[9]!)
            {
                this._patronLoansReturnedSubscribers.Add(__item9);
            }
        }
        this._notifyDepth = (int)__saved[10]!;
    }
}