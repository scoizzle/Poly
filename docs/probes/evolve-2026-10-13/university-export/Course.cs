#nullable enable
using System;
using System.Collections.Generic;

public class Course
{
    private List<Enrollment> _enrollments;
    private Course()
    {
        this._enrollments = new List<Enrollment>();
    }
    private Course(string code, long credits, long seats, string title, IEnumerable<Enrollment>? enrollments = null)
    {
        this.Code = code;
        this.Credits = credits;
        this.Seats = seats;
        this.Title = title;
        this._enrollments = new List<Enrollment>(enrollments ?? new List<Enrollment>());
    }
    public string Code { get; private set; } = default!;
    public long Credits { get; private set; }
    public long Seats { get; private set; }
    public string Title { get; private set; } = default!;
    public IReadOnlyList<Enrollment> Enrollments
    {
        get => this._enrollments;
    }
    private DomainResult<Enrollment> CreateEnrollments(DateOnly enrolledOn, string grade, Student? student)
    {
        var enrollmentResult = Enrollment.Create(enrolledOn, grade, student, this);
        if (!enrollmentResult.IsSuccess)
        {
            return DomainResult<Enrollment>.Failure(enrollmentResult.ErrorMessage ?? "");
        }
        var enrollment = enrollmentResult.Value;
        return DomainResult<Enrollment>.Success(enrollment);
    }
    internal void AttachEnrollments(Enrollment child)
    {
        this._enrollments.Add(child);
    }
    public DomainResult AddStudent(Student student)
    {
        if (!this.HasSeats())
        {
            return DomainResult.Failure("'AddStudent' blocked by policy 'HasSeats'.");
        }
        var create0 = this.ProbeCreate("Enrollment", "student", (object)student, "EnrolledOn", DateOnly.FromDateTime(DateTime.Today));
        if (!create0.IsSuccess)
        {
            return DomainResult.Failure(create0.ErrorMessage ?? "");
        }
        var create1 = this.CreateIn("enrollments", "student", (object)student, "EnrolledOn", DateOnly.FromDateTime(DateTime.Today));
        if (!create1.IsSuccess)
        {
            return DomainResult.Failure(create1.ErrorMessage ?? "");
        }
        var created2 = create1.Value;
        return DomainResult.Success();
    }
    public bool HasSeats() => this.Enrollments.Count < this.Seats;
    public static DomainResult<Course> Create(string code, long credits, long seats, string title, IEnumerable<Enrollment>? enrollments = null)
    {
        if (string.IsNullOrEmpty(code))
        {
            return DomainResult<Course>.Failure("'Code' is required.");
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z]{2,4}[0-9]{3}$"))
        {
            return DomainResult<Course>.Failure("'Code' does not match the required pattern.");
        }
        if (credits < 1L)
        {
            return DomainResult<Course>.Failure("'Credits' must be >= 1.");
        }
        if (credits > 6L)
        {
            return DomainResult<Course>.Failure("'Credits' must be <= 6.");
        }
        if (seats < 0L)
        {
            return DomainResult<Course>.Failure("'Seats' must be >= 0.");
        }
        if (seats > 200L)
        {
            return DomainResult<Course>.Failure("'Seats' must be <= 200.");
        }
        if (string.IsNullOrEmpty(title))
        {
            return DomainResult<Course>.Failure("'Title' is required.");
        }
        var created = new Course(code, credits, seats, title, enrollments);
        return DomainResult<Course>.Success(created);
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
        if (typeName == "Program")
        {
            var typed = Program.Create((values.ContainsKey("Code") ? (string?)values["Code"] ?? "" : ""), (values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (typeName == "Student")
        {
            var typed = Student.Create((values.ContainsKey("Email") ? (string?)values["Email"] ?? "" : ""), (values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), (values.ContainsKey("StudentId") ? (string?)values["StudentId"] ?? "" : ""), (values.ContainsKey("program") ? (Program?)values["program"] ?? null : null), new List<Enrollment>());
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (typeName == "Course")
        {
            var typed = Course.Create((values.ContainsKey("Code") ? (string?)values["Code"] ?? "" : ""), (values.ContainsKey("Credits") ? (long?)values["Credits"] ?? 0L : 0L), (values.ContainsKey("Seats") ? (long?)values["Seats"] ?? 0L : 0L), (values.ContainsKey("Title") ? (string?)values["Title"] ?? "" : ""), new List<Enrollment>());
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (typeName == "Enrollment")
        {
            var typed = Enrollment.Create((values.ContainsKey("EnrolledOn") ? (DateOnly?)values["EnrolledOn"] ?? DateOnly.MinValue : DateOnly.MinValue), (values.ContainsKey("Grade") ? (string?)values["Grade"] ?? "" : ""), (values.ContainsKey("student") ? (Student?)values["student"] ?? null : null), (values.ContainsKey("course") ? (Course?)values["course"] ?? null : null));
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
        if (relationshipName == "enrollments")
        {
            var typed = this.CreateEnrollments((values.ContainsKey("EnrolledOn") ? (DateOnly?)values["EnrolledOn"] ?? DateOnly.MinValue : DateOnly.MinValue), (values.ContainsKey("Grade") ? (string?)values["Grade"] ?? "" : ""), (values.ContainsKey("student") ? (Student?)values["student"] ?? null : null));
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
        if (typeName == "Program")
        {
            var typed = Program.Create((values.ContainsKey("Code") ? (string?)values["Code"] ?? "" : ""), (values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""));
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        if (typeName == "Student")
        {
            var typed = Student.Create((values.ContainsKey("Email") ? (string?)values["Email"] ?? "" : ""), (values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), (values.ContainsKey("StudentId") ? (string?)values["StudentId"] ?? "" : ""), (values.ContainsKey("program") ? (Program?)values["program"] ?? null : null), new List<Enrollment>());
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        if (typeName == "Course")
        {
            var typed = Course.Create((values.ContainsKey("Code") ? (string?)values["Code"] ?? "" : ""), (values.ContainsKey("Credits") ? (long?)values["Credits"] ?? 0L : 0L), (values.ContainsKey("Seats") ? (long?)values["Seats"] ?? 0L : 0L), (values.ContainsKey("Title") ? (string?)values["Title"] ?? "" : ""), new List<Enrollment>());
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        if (typeName == "Enrollment")
        {
            var typed = Enrollment.Create((values.ContainsKey("EnrolledOn") ? (DateOnly?)values["EnrolledOn"] ?? DateOnly.MinValue : DateOnly.MinValue), (values.ContainsKey("Grade") ? (string?)values["Grade"] ?? "" : ""), (values.ContainsKey("student") ? (Student?)values["student"] ?? null : null), (values.ContainsKey("course") ? (Course?)values["course"] ?? null : null));
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        return DomainResult.Failure("Unknown type '" + typeName + "'.");
    }
}