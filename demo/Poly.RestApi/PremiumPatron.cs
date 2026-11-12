#nullable enable
using Poly.Runtime;

public class PremiumPatron
{
    private PremiumPatron() { }
    private PremiumPatron(string name, string email, long rewardPoints, bool priorityAccess, PremiumTier tier = PremiumTier.Silver)
    {
        this.Name = name;
        this.Email = email;
        this.RewardPoints = rewardPoints;
        this.PriorityAccess = priorityAccess;
        this.Tier = tier;
    }
    public string Name { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public long RewardPoints { get; private set; }
    public PremiumTier Tier { get; private set; }
    public bool PriorityAccess { get; private set; }
    public bool IsLoyal() => this.RewardPoints >= 100L;
    public bool HasPriority() => this.PriorityAccess;
    public bool UnlimitedItems() => this.Tier == PremiumTier.Platinum || this.PriorityAccess;
    public static DomainResult<PremiumPatron> Create(string name, string email, long rewardPoints, bool priorityAccess, Poly.Runtime.IStoragePort db, PremiumTier tier = PremiumTier.Silver)
    {
        if (string.IsNullOrEmpty(name))
        {
            return DomainResult<PremiumPatron>.Failure("'Name' is required.");
        }
        if (string.IsNullOrEmpty(email))
        {
            return DomainResult<PremiumPatron>.Failure("'Email' is required.");
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(email, "^[^@]+@[^@]+\\z"))
        {
            return DomainResult<PremiumPatron>.Failure("'Email' does not match the required pattern.");
        }
        if (tier != PremiumTier.Silver && tier != PremiumTier.Gold && tier != PremiumTier.Platinum)
        {
            return DomainResult<PremiumPatron>.Failure(string.Concat("'", tier, "' is not a valid member of PremiumTier for property 'Tier'."));
        }
        if (PremiumPatron.ExistsWithEmail(db, email))
        {
            return DomainResult<PremiumPatron>.Failure("Unique constraint violated: 'Email' value is already used on another 'PremiumPatron'.");
        }
        var created = new PremiumPatron(name, email, rewardPoints, priorityAccess, tier);
        db.Record("PremiumPatron", "Email", email);
        return DomainResult<PremiumPatron>.Success(created);
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
    public static System.Boolean ExistsWithEmail(Poly.Runtime.IStoragePort db, System.String email) => db.Exists("PremiumPatron", "Email", email);
}