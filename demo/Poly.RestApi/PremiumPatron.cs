#nullable enable
using System;
using System.Collections.Generic;

public class PremiumPatron {
    private PremiumPatron() {
        // EF materialization.
    }
    private PremiumPatron(string email, string name, bool priorityAccess, long rewardPoints, PremiumTier tier = PremiumTier.Silver) {
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
    public static DomainResult<PremiumPatron> Create(string email, string name, bool priorityAccess, long rewardPoints, PremiumTier tier = PremiumTier.Silver) {
        if (!System.Text.RegularExpressions.Regex.IsMatch(email, "^[^@]+@[^@]+$")) {
            return DomainResult<PremiumPatron>.Failure("'Email' does not match the required pattern.");
        }
        if (string.IsNullOrEmpty(name)) {
            return DomainResult<PremiumPatron>.Failure("'Name' is required.");
        }
        return DomainResult<PremiumPatron>.Success(new PremiumPatron(email, name, priorityAccess, rewardPoints, tier));
    }
}