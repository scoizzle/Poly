#nullable enable
using System;
using System.Collections.Generic;

using Poly.Runtime;

public enum FineStage
{
    Unpaid = 0,
    Resolved = 1
}

public class Fine
{
    private List<Patron>? _patronFinesResolvedSubscribers;
    private int _notifyDepth;
    private Fine() { }
    private Fine(string reason, long amount = 0L, Patron? patron = null, DateTime? dateIssued = null, bool? paid = null)
    {
        this.Reason = reason;
        this.Amount = amount;
        this.DateIssued = dateIssued ?? DateTime.UtcNow;
        this.Paid = paid;
        this.Patron = patron;
        this.CurrentStage = FineStage.Unpaid;
    }
    public long Amount { get; private set; }
    public string Reason { get; private set; } = default!;
    public DateTime DateIssued { get; private set; }
    public bool? Paid { get; private set; }
    public Patron? Patron { get; private set; }
    public FineStage CurrentStage { get; private set; }
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
        this.CurrentStage = FineStage.Resolved;
        this.Paid = true;
        this.NotifyResolvedSubscribers();
        return DomainResult.Success();
    }
    public DomainResult Waive()
    {
        if (this.CurrentStage != FineStage.Unpaid)
        {
            return DomainResult.Failure("'Waive' requires stage 'Unpaid' on entity 'Fine'.");
        }
        this.Amount = 0L;
        this.Paid = true;
        this.CurrentStage = FineStage.Resolved;
        this.Paid = true;
        this.NotifyResolvedSubscribers();
        return DomainResult.Success();
    }
    internal void RegisterPatronFinesResolvedSubscriber(Patron subscriber)
    {
        if (this._patronFinesResolvedSubscribers == null)
        {
            this._patronFinesResolvedSubscribers = new List<Patron>();
        }
        foreach (var existing in this._patronFinesResolvedSubscribers)
        {
            if (object.ReferenceEquals(existing, subscriber))
            {
                return;
            }
        }
        this._patronFinesResolvedSubscribers.Add(subscriber);
    }
    internal void NotifyResolvedSubscribers()
    {
        this.EnterNotifyCascade("Resolved");
        try
        {
            if (this._patronFinesResolvedSubscribers != null)
            {
                foreach (var sub in this._patronFinesResolvedSubscribers)
                {
                    sub.WhenEachFineResolved();
                }
            }
        }
        finally
        {
            this.ExitNotifyCascade();
        }
    }
    public static DomainResult<Fine> Create(string reason, long amount = 0L, Patron? patron = null, DateTime? dateIssued = null, bool? paid = null)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return DomainResult<Fine>.Failure(Poly.Runtime.DomainFailure.Required("'Reason' is required.", "Fine.Reason"));
        }
        var created = new Fine(reason, amount, patron, dateIssued, paid);
        if (patron != null)
        {
            patron.AttachFines(created);
        }
        return DomainResult<Fine>.Success(created);
    }
    private void EnterNotifyCascade(string stageName)
    {
        if (this._notifyDepth >= 16)
        {
            throw new System.InvalidOperationException(string.Concat("Subscription cascade on entity 'Fine' exceeded max depth (16) while notifying '", stageName, "'."));
        }
        this._notifyDepth = this._notifyDepth + 1;
    }
    private void ExitNotifyCascade()
    {
        this._notifyDepth = this._notifyDepth - 1;
    }
}