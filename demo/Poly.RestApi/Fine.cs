#nullable enable
using System;
using System.Collections.Generic;

public enum FineStage {
    Unpaid = 0,
    Resolved = 1
}

public class Fine {
    private List<Patron>? _resolvedSubscribers;
    private Fine() {
        // EF materialization.
    }
    private Fine(long amount, bool paid, string reason, Patron? patron, DateTime? dateIssued = null) {
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
    public DomainResult Pay() {
        if (this.CurrentStage != FineStage.Unpaid) {
            return DomainResult.Failure("'Pay' requires stage 'Unpaid' on entity 'Fine'.");
        }
        if (this.Amount <= 0L) {
            this.Paid = true;
        }
        else {
            this.Paid = true;
        }
        this.Paid = true;
        this.CurrentStage = FineStage.Resolved;
        this.NotifyResolvedSubscribers();
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
        this.NotifyResolvedSubscribers();
        return DomainResult.Success();
    }
    internal void RegisterPatronResolvedSubscriber(Patron subscriber) {
        if (this._resolvedSubscribers == null) {
            this._resolvedSubscribers = new List<Patron>();
        }
        this._resolvedSubscribers.Add(subscriber);
    }
    internal void NotifyResolvedSubscribers() {
        if (this._resolvedSubscribers != null) {
            foreach (var sub in this._resolvedSubscribers) {
                sub.WhenFineResolved();
            }
        }
    }
    public static DomainResult<Fine> Create(long amount, bool paid, string reason, Patron? patron, DateTime? dateIssued = null) => DomainResult<Fine>.Success(new Fine(amount, paid, reason, patron, dateIssued));
}