#nullable enable
using System;
using System.Collections.Generic;

public enum EnrollmentStage
{
    Enrolled = 0,
    Completed = 1,
    Dropped = 2
}

public class Enrollment
{
    private Enrollment()
    { }
    private Enrollment(DateOnly enrolledOn, string grade, Student? student = null, Course? course = null)
    {
        this.EnrolledOn = enrolledOn;
        this.Grade = grade;
        this.Student = student;
        this.Course = course;
        this.CurrentStage = EnrollmentStage.Enrolled;
    }
    public DateOnly EnrolledOn { get; private set; }
    public string Grade { get; private set; } = default!;
    public Student? Student { get; private set; }
    public Course? Course { get; private set; }
    public EnrollmentStage CurrentStage { get; private set; }
    private DomainResult<Student> CreateStudent(string email, string name, string studentId, Program? program)
    {
        var studentResult = Student.Create(email, name, studentId, program, new List<Enrollment>());
        if (!studentResult.IsSuccess)
        {
            return DomainResult<Student>.Failure(studentResult.ErrorMessage ?? "");
        }
        var student = studentResult.Value;
        this.Student = student;
        return DomainResult<Student>.Success(student);
    }
    private DomainResult<Course> CreateCourse(string code, long credits, long seats, string title)
    {
        var courseResult = Course.Create(code, credits, seats, title, new List<Enrollment>());
        if (!courseResult.IsSuccess)
        {
            return DomainResult<Course>.Failure(courseResult.ErrorMessage ?? "");
        }
        var course = courseResult.Value;
        this.Course = course;
        return DomainResult<Course>.Success(course);
    }
    public DomainResult Drop()
    {
        if (this.CurrentStage != EnrollmentStage.Enrolled)
        {
            return DomainResult.Failure("'Drop' requires stage 'Enrolled' on entity 'Enrollment'.");
        }
        this.CurrentStage = EnrollmentStage.Dropped;
        return DomainResult.Success();
    }
    public DomainResult Complete(string grade)
    {
        if (this.CurrentStage != EnrollmentStage.Enrolled)
        {
            return DomainResult.Failure("'Complete' requires stage 'Enrolled' on entity 'Enrollment'.");
        }
        this.Grade = grade;
        this.CurrentStage = EnrollmentStage.Completed;
        return DomainResult.Success();
    }
    public bool SeniorProgram() => this.Student != null && (this.Student!.Program != null && this.Student!.Program!.Code == "CS");
    public static DomainResult<Enrollment> Create(DateOnly enrolledOn, string grade, Student? student = null, Course? course = null)
    {
        var created = new Enrollment(enrolledOn, grade, student, course);
        if (student != null)
        {
            student.AttachEnrollments(created);
        }
        if (course != null)
        {
            course.AttachEnrollments(created);
        }
        return DomainResult<Enrollment>.Success(created);
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
        if (relationshipName == "student")
        {
            var typed = this.CreateStudent((values.ContainsKey("Email") ? (string?)values["Email"] ?? "" : ""), (values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), (values.ContainsKey("StudentId") ? (string?)values["StudentId"] ?? "" : ""), (values.ContainsKey("program") ? (Program?)values["program"] ?? null : null));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (relationshipName == "course")
        {
            var typed = this.CreateCourse((values.ContainsKey("Code") ? (string?)values["Code"] ?? "" : ""), (values.ContainsKey("Credits") ? (long?)values["Credits"] ?? 0L : 0L), (values.ContainsKey("Seats") ? (long?)values["Seats"] ?? 0L : 0L), (values.ContainsKey("Title") ? (string?)values["Title"] ?? "" : ""));
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