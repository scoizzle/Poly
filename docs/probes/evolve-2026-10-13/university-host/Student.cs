#nullable enable
using System;
using System.Collections.Generic;

public class Student
{
    private List<Enrollment> _enrollments;
    private Student()
    {
        this._enrollments = new List<Enrollment>();
    }
    private Student(string email, string name, string studentId, Program? program = null, IEnumerable<Enrollment>? enrollments = null)
    {
        this.Email = email;
        this.Name = name;
        this.StudentId = studentId;
        this.Program = program;
        this._enrollments = new List<Enrollment>(enrollments ?? new List<Enrollment>());
    }
    public string Email { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string StudentId { get; private set; } = default!;
    public Program? Program { get; private set; }
    public IReadOnlyList<Enrollment> Enrollments
    {
        get => this._enrollments;
    }
    private DomainResult<Program> CreateProgram(string code, string name)
    {
        var programResult = Program.Create(code, name);
        if (!programResult.IsSuccess)
        {
            return DomainResult<Program>.Failure(programResult.ErrorMessage ?? "");
        }
        var program = programResult.Value;
        this.Program = program;
        return DomainResult<Program>.Success(program);
    }
    private DomainResult<Enrollment> CreateEnrollments(DateOnly enrolledOn, string grade, Course? course)
    {
        var enrollmentResult = Enrollment.Create(enrolledOn, grade, this, course);
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
    public DomainResult Enroll(Course course)
    {
        if (this.Overloaded())
        {
            return DomainResult.Failure("'Enroll' blocked by policy 'Overloaded'.");
        }
        var create0 = this.ProbeCreate("Enrollment", "course", (object)course, "EnrolledOn", DateOnly.FromDateTime(DateTime.Today));
        if (!create0.IsSuccess)
        {
            return DomainResult.Failure(create0.ErrorMessage ?? "");
        }
        var create1 = this.CreateIn("enrollments", "course", (object)course, "EnrolledOn", DateOnly.FromDateTime(DateTime.Today));
        if (!create1.IsSuccess)
        {
            return DomainResult.Failure(create1.ErrorMessage ?? "");
        }
        var created2 = create1.Value;
        return DomainResult.Success();
    }
    public bool Overloaded() => this.Enrollments.Count >= 5L;
    public static DomainResult<Student> Create(string email, string name, string studentId, Program? program = null, IEnumerable<Enrollment>? enrollments = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            return DomainResult<Student>.Failure("'Name' is required.");
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(studentId, "^[0-9]{8}$"))
        {
            return DomainResult<Student>.Failure("'StudentId' does not match the required pattern.");
        }
        var created = new Student(email, name, studentId, program, enrollments);
        var uniqueCheck = created.EnsureUnique("Email", email);
        if (!uniqueCheck.IsSuccess)
        {
            return DomainResult<Student>.Failure(uniqueCheck.ErrorMessage ?? "");
        }
        uniqueCheck = created.EnsureUnique("StudentId", studentId);
        if (!uniqueCheck.IsSuccess)
        {
            return DomainResult<Student>.Failure(uniqueCheck.ErrorMessage ?? "");
        }
        return DomainResult<Student>.Success(created);
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
        if (relationshipName == "program")
        {
            var typed = this.CreateProgram((values.ContainsKey("Code") ? (string?)values["Code"] ?? "" : ""), (values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (relationshipName == "enrollments")
        {
            var typed = this.CreateEnrollments((values.ContainsKey("EnrolledOn") ? (DateOnly?)values["EnrolledOn"] ?? DateOnly.MinValue : DateOnly.MinValue), (values.ContainsKey("Grade") ? (string?)values["Grade"] ?? "" : ""), (values.ContainsKey("course") ? (Course?)values["course"] ?? null : null));
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