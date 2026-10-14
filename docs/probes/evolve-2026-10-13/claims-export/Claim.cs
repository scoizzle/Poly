#nullable enable
using System;
using System.Collections.Generic;

public enum ClaimStage
{
    Submitted = 0,
    UnderReview = 1,
    Approved = 2,
    Rejected = 3,
    Paid = 4
}

public class Claim
{
    private List<Note> _notes;
    private Claim()
    {
        this._notes = new List<Note>();
    }
    private Claim(long amount, string refNo, Claimant? claimant = null, Assessor? assessor = null, IEnumerable<Note>? notes = null)
    {
        this.Amount = amount;
        this.RefNo = refNo;
        this.Claimant = claimant;
        this.Assessor = assessor;
        this._notes = new List<Note>(notes ?? new List<Note>());
        this.CurrentStage = ClaimStage.Submitted;
        this.Status = "submitted";
    }
    public long Amount { get; private set; }
    public string RefNo { get; private set; } = default!;
    public string Status { get; private set; } = default!;
    public Claimant? Claimant { get; private set; }
    public Assessor? Assessor { get; private set; }
    public IReadOnlyList<Note> Notes
    {
        get => this._notes;
    }
    public ClaimStage CurrentStage { get; private set; }
    private DomainResult<Claimant> CreateClaimant(string name)
    {
        var claimantResult = Claimant.Create(name, new List<Claim>());
        if (!claimantResult.IsSuccess)
        {
            return DomainResult<Claimant>.Failure(claimantResult.ErrorMessage ?? "");
        }
        var claimant = claimantResult.Value;
        this.Claimant = claimant;
        return DomainResult<Claimant>.Success(claimant);
    }
    private DomainResult<Assessor> CreateAssessor(string name)
    {
        var assessorResult = Assessor.Create(name, new List<Note>());
        if (!assessorResult.IsSuccess)
        {
            return DomainResult<Assessor>.Failure(assessorResult.ErrorMessage ?? "");
        }
        var assessor = assessorResult.Value;
        this.Assessor = assessor;
        return DomainResult<Assessor>.Success(assessor);
    }
    private DomainResult<Note> CreateNotes(string body)
    {
        var noteResult = Note.Create(body);
        if (!noteResult.IsSuccess)
        {
            return DomainResult<Note>.Failure(noteResult.ErrorMessage ?? "");
        }
        var note = noteResult.Value;
        this._notes.Add(note);
        return DomainResult<Note>.Success(note);
    }
    internal void AttachNotes(Note child)
    {
        this._notes.Add(child);
    }
    public DomainResult Review()
    {
        if (this.CurrentStage != ClaimStage.Submitted)
        {
            return DomainResult.Failure("'Review' requires stage 'Submitted' on entity 'Claim'.");
        }
        this.CurrentStage = ClaimStage.UnderReview;
        return DomainResult.Success();
    }
    public DomainResult Approve()
    {
        if (this.CurrentStage != ClaimStage.UnderReview)
        {
            return DomainResult.Failure("'Approve' requires stage 'UnderReview' on entity 'Claim'.");
        }
        if (!this.HasNotes())
        {
            return DomainResult.Failure("'Approve' blocked by policy 'HasNotes'.");
        }
        if (this.Assessor == null)
        {
            return DomainResult.Failure("'LogReview' requires a linked 'assessor' on entity 'Claim'.");
        }
        var invoke0 = this.Assessor.LogReview("approved");
        if (!invoke0.IsSuccess)
        {
            return DomainResult.Failure(invoke0.ErrorMessage ?? "");
        }
        this.CurrentStage = ClaimStage.Approved;
        return DomainResult.Success();
    }
    public DomainResult Reject()
    {
        if (this.CurrentStage != ClaimStage.UnderReview)
        {
            return DomainResult.Failure("'Reject' requires stage 'UnderReview' on entity 'Claim'.");
        }
        if (this.Assessor == null)
        {
            return DomainResult.Failure("'LogReview' requires a linked 'assessor' on entity 'Claim'.");
        }
        var invoke0 = this.Assessor.LogReview("rejected");
        if (!invoke0.IsSuccess)
        {
            return DomainResult.Failure(invoke0.ErrorMessage ?? "");
        }
        this.CurrentStage = ClaimStage.Rejected;
        return DomainResult.Success();
    }
    public DomainResult Pay()
    {
        if (this.CurrentStage != ClaimStage.Approved)
        {
            return DomainResult.Failure("'Pay' requires stage 'Approved' on entity 'Claim'.");
        }
        this.CurrentStage = ClaimStage.Paid;
        return DomainResult.Success();
    }
    public bool TooLarge() => this.Amount > 100000L;
    public bool HasNotes() => this.Notes.Count != 0;
    public bool NoNotes() => !(this.Notes.Count != 0);
    public static DomainResult<Claim> Create(long amount, string refNo, Claimant? claimant = null, Assessor? assessor = null, IEnumerable<Note>? notes = null)
    {
        if (amount < 0L)
        {
            return DomainResult<Claim>.Failure("'Amount' must be >= 0.");
        }
        var created = new Claim(amount, refNo, claimant, assessor, notes);
        var uniqueCheck = created.EnsureUnique("RefNo", refNo);
        if (!uniqueCheck.IsSuccess)
        {
            return DomainResult<Claim>.Failure(uniqueCheck.ErrorMessage ?? "");
        }
        if (claimant != null)
        {
            claimant.AttachClaims(created);
        }
        return DomainResult<Claim>.Success(created);
    }
    public DomainResult EnsureUnique(string propertyName, object value) => DomainResult.Success();
    public DomainResult<object> Create(string name, object? __job = null)
    {
        var values = new Dictionary<string, object>();
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6, string p7, object v7)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        values[p7] = v7;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> CreateIn(string name, object? __job = null)
    {
        var values = new Dictionary<string, object>();
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6, string p7, object v7)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        values[p7] = v7;
        return this.BindCreateIn(name, values);
    }
    public DomainResult ProbeCreate(string name, object? __job = null)
    {
        var values = new Dictionary<string, object>();
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6, string p7, object v7)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        values[p7] = v7;
        return this.BindProbeCreate(name, values);
    }
    private DomainResult<object> BindCreate(string typeName, Dictionary<string, object> values)
    {
        if (typeName == "Claimant")
        {
            var typed = Claimant.Create((values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), new List<Claim>());
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (typeName == "Claim")
        {
            var typed = Claim.Create((values.ContainsKey("Amount") ? (long?)values["Amount"] ?? 0L : 0L), (values.ContainsKey("RefNo") ? (string?)values["RefNo"] ?? "" : ""), (values.ContainsKey("claimant") ? (Claimant?)values["claimant"] ?? null : null), (values.ContainsKey("assessor") ? (Assessor?)values["assessor"] ?? null : null), new List<Note>());
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (typeName == "Assessor")
        {
            var typed = Assessor.Create((values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), new List<Note>());
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (typeName == "Note")
        {
            var typed = Note.Create((values.ContainsKey("Body") ? (string?)values["Body"] ?? "" : ""));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        return DomainResult<object>.Failure("Unknown type '" + typeName + "'.");
    }
    private DomainResult<object> BindCreateIn(string relationshipName, Dictionary<string, object> values)
    {
        if (relationshipName == "claimant")
        {
            var typed = this.CreateClaimant((values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (relationshipName == "assessor")
        {
            var typed = this.CreateAssessor((values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (relationshipName == "notes")
        {
            var typed = this.CreateNotes((values.ContainsKey("Body") ? (string?)values["Body"] ?? "" : ""));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        return DomainResult<object>.Failure("Unknown relationship '" + relationshipName + "'.");
    }
    private DomainResult BindProbeCreate(string typeName, Dictionary<string, object> values)
    {
        if (typeName == "Claimant")
        {
            var typed = Claimant.Create((values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), new List<Claim>());
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        if (typeName == "Claim")
        {
            var typed = Claim.Create((values.ContainsKey("Amount") ? (long?)values["Amount"] ?? 0L : 0L), (values.ContainsKey("RefNo") ? (string?)values["RefNo"] ?? "" : ""), (values.ContainsKey("claimant") ? (Claimant?)values["claimant"] ?? null : null), (values.ContainsKey("assessor") ? (Assessor?)values["assessor"] ?? null : null), new List<Note>());
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        if (typeName == "Assessor")
        {
            var typed = Assessor.Create((values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), new List<Note>());
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        if (typeName == "Note")
        {
            var typed = Note.Create((values.ContainsKey("Body") ? (string?)values["Body"] ?? "" : ""));
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        return DomainResult.Failure("Unknown type '" + typeName + "'.");
    }
    private void OnEntrySubmitted()
    {
        this.Status = "submitted";
    }
}