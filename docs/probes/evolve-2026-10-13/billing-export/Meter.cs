#nullable enable
using System;
using System.Collections.Generic;

public class Meter
{
    private List<Reading> _readings;
    private Meter()
    {
        this._readings = new List<Reading>();
    }
    private Meter(long offPeakRate, long peakRate, long rate, string serial, IEnumerable<Reading>? readings = null)
    {
        this.OffPeakRate = offPeakRate;
        this.PeakRate = peakRate;
        this.Rate = rate;
        this.Serial = serial;
        this._readings = new List<Reading>(readings ?? new List<Reading>());
    }
    public long OffPeakRate { get; private set; }
    public long PeakRate { get; private set; }
    public long Rate { get; private set; }
    public string Serial { get; private set; } = default!;
    public IReadOnlyList<Reading> Readings
    {
        get => this._readings;
    }
    private DomainResult<Reading> CreateReadings(long amount, DateTime recordedAt, long usage, DateTime? createdAt = null, string status = "new")
    {
        var readingResult = Reading.Create(amount, recordedAt, usage, new List<Note>(), createdAt, status);
        if (!readingResult.IsSuccess)
        {
            return DomainResult<Reading>.Failure(readingResult.ErrorMessage ?? "");
        }
        var reading = readingResult.Value;
        this._readings.Add(reading);
        return DomainResult<Reading>.Success(reading);
    }
    internal void AttachReadings(Reading child)
    {
        this._readings.Add(child);
    }
    public DomainResult<Reading> AddReading(long usage)
    {
        var create0 = this.ProbeCreate("Reading", "Usage", usage, "RecordedAt", DateTime.UtcNow);
        if (!create0.IsSuccess)
        {
            return DomainResult<Reading>.Failure(create0.ErrorMessage ?? "");
        }
        var create1 = this.CreateIn("readings", "Usage", usage, "RecordedAt", DateTime.UtcNow);
        if (!create1.IsSuccess)
        {
            return DomainResult<Reading>.Failure(create1.ErrorMessage ?? "");
        }
        var created2 = create1.Value;
        return DomainResult<Reading>.Success((Reading)created2);
    }
    public DomainResult BillAll()
    {
        var matched1 = false;
        foreach (var target0 in this.Readings)
        {
            if (!target0.Unbilled())
            {
                continue;
            }
            matched1 = true;
            var result2 = target0.Charge(this.Rate, this.OffPeakRate);
            if (!result2.IsSuccess)
            {
                return DomainResult.Failure(result2.ErrorMessage ?? "");
            }
        }
        if (!matched1)
        {
            return DomainResult.Failure("for readings.Charge matched zero targets.");
        }
        return DomainResult.Success();
    }
    public static DomainResult<Meter> Create(long offPeakRate, long peakRate, long rate, string serial, IEnumerable<Reading>? readings = null)
    {
        if (offPeakRate < 0L)
        {
            return DomainResult<Meter>.Failure("'OffPeakRate' must be >= 0.");
        }
        if (peakRate < 0L)
        {
            return DomainResult<Meter>.Failure("'PeakRate' must be >= 0.");
        }
        if (rate < 0L)
        {
            return DomainResult<Meter>.Failure("'Rate' must be >= 0.");
        }
        var created = new Meter(offPeakRate, peakRate, rate, serial, readings);
        var uniqueCheck = created.EnsureUnique("Serial", serial);
        if (!uniqueCheck.IsSuccess)
        {
            return DomainResult<Meter>.Failure(uniqueCheck.ErrorMessage ?? "");
        }
        return DomainResult<Meter>.Success(created);
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
        if (typeName == "Meter")
        {
            var typed = Meter.Create((values.ContainsKey("OffPeakRate") ? (long?)values["OffPeakRate"] ?? 0L : 0L), (values.ContainsKey("PeakRate") ? (long?)values["PeakRate"] ?? 0L : 0L), (values.ContainsKey("Rate") ? (long?)values["Rate"] ?? 0L : 0L), (values.ContainsKey("Serial") ? (string?)values["Serial"] ?? "" : ""), new List<Reading>());
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (typeName == "Reading")
        {
            var typed = Reading.Create((values.ContainsKey("Amount") ? (long?)values["Amount"] ?? 0L : 0L), (values.ContainsKey("RecordedAt") ? (DateTime?)values["RecordedAt"] ?? DateTime.MinValue : DateTime.MinValue), (values.ContainsKey("Usage") ? (long?)values["Usage"] ?? 0L : 0L), new List<Note>(), (values.ContainsKey("CreatedAt") ? (DateTime?)values["CreatedAt"] ?? DateTime.MinValue : DateTime.MinValue), (values.ContainsKey("Status") ? (string?)values["Status"] ?? "" : ""));
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
        if (relationshipName == "readings")
        {
            var typed = this.CreateReadings((values.ContainsKey("Amount") ? (long?)values["Amount"] ?? 0L : 0L), (values.ContainsKey("RecordedAt") ? (DateTime?)values["RecordedAt"] ?? DateTime.MinValue : DateTime.MinValue), (values.ContainsKey("Usage") ? (long?)values["Usage"] ?? 0L : 0L), (values.ContainsKey("CreatedAt") ? (DateTime?)values["CreatedAt"] ?? DateTime.MinValue : DateTime.MinValue), (values.ContainsKey("Status") ? (string?)values["Status"] ?? "" : ""));
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
        if (typeName == "Meter")
        {
            var typed = Meter.Create((values.ContainsKey("OffPeakRate") ? (long?)values["OffPeakRate"] ?? 0L : 0L), (values.ContainsKey("PeakRate") ? (long?)values["PeakRate"] ?? 0L : 0L), (values.ContainsKey("Rate") ? (long?)values["Rate"] ?? 0L : 0L), (values.ContainsKey("Serial") ? (string?)values["Serial"] ?? "" : ""), new List<Reading>());
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        if (typeName == "Reading")
        {
            var typed = Reading.Create((values.ContainsKey("Amount") ? (long?)values["Amount"] ?? 0L : 0L), (values.ContainsKey("RecordedAt") ? (DateTime?)values["RecordedAt"] ?? DateTime.MinValue : DateTime.MinValue), (values.ContainsKey("Usage") ? (long?)values["Usage"] ?? 0L : 0L), new List<Note>(), (values.ContainsKey("CreatedAt") ? (DateTime?)values["CreatedAt"] ?? DateTime.MinValue : DateTime.MinValue), (values.ContainsKey("Status") ? (string?)values["Status"] ?? "" : ""));
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
}