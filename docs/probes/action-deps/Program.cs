using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var failures = new List<string>();

Check("threaded", ThreadedParameters());
Check("locator", Locator());
Check("activated-handler", ActivatedHandler());
Check("instance-field", InstanceField());
Check("ambient", Ambient());
Check("save-is-late", SaveIsLate());

if (failures.Count > 0)
{
    foreach (var failure in failures)
        Console.Error.WriteLine(failure);
    return 1;
}

Console.WriteLine("six shapes observed");
return 0;

void Check(string name, string? error)
{
    if (error is null)
        Console.WriteLine($"{name} ok");
    else
        failures.Add($"{name}: {error}");
}

static string? ThreadedParameters()
{
    using var db = Open();
    var first = Widget.Create("A", db);
    if (!first.IsSuccess)
        return "first create failed";
    var issued = new Lot().Issue("A", db);
    if (issued.IsSuccess)
        return "second create succeeded";
    if (issued.ErrorMessage != Widget.Duplicate)
        return issued.ErrorMessage;
    return null;
}

static string? Locator()
{
    using var connection = OpenConnection();
    var services = new ServiceCollection();
    services.AddDbContext<LabDbContext>(options => options.UseSqlite(connection));
    using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();
    scope.ServiceProvider.GetRequiredService<LabDbContext>().Database.EnsureCreated();
    var first = Widget.CreateFrom(scope.ServiceProvider, "A");
    if (!first.IsSuccess)
        return "first create failed";
    var second = Widget.CreateFrom(scope.ServiceProvider, "A");
    if (second.IsSuccess)
        return "locator create succeeded";
    if (second.ErrorMessage != Widget.Duplicate)
        return second.ErrorMessage;
    return null;
}

static string? ActivatedHandler()
{
    using var connection = OpenConnection();
    var services = new ServiceCollection();
    services.AddDbContext<LabDbContext>(options => options.UseSqlite(connection));
    services.AddScoped<IssueHandler>();
    using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
    db.Database.EnsureCreated();
    var seeded = Widget.Create("A", db);
    if (!seeded.IsSuccess)
        return "seed failed";
    var handler = scope.ServiceProvider.GetRequiredService<IssueHandler>();
    var issued = handler.Issue("A");
    if (issued.IsSuccess)
        return "handler create succeeded";
    if (issued.ErrorMessage != Widget.Duplicate)
        return issued.ErrorMessage;
    return null;
}

static string? InstanceField()
{
    using var db = Open();
    var parent = new Parent { Db = db };
    var seeded = parent.IssueCopyingField("A");
    if (!seeded.IsSuccess)
        return "seed failed";
    var copied = parent.IssueCopyingField("A");
    if (copied.IsSuccess)
        return "copied field did not see the duplicate";
    var forgotten = parent.IssueForgettingField("B");
    if (!forgotten.IsSuccess)
        return "a child that did not receive the field should not have failed the check";
    return null;
}

static string? Ambient()
{
    using var db = Open();
    Storage.Current.Value = db;
    var first = Widget.CreateAmbient("A");
    var second = Widget.CreateAmbient("A");
    Storage.Current.Value = null;
    if (!first.IsSuccess || second.IsSuccess || second.ErrorMessage != Widget.Duplicate)
        return "ambient check did not fail the duplicate";
    try
    {
        Widget.CreateAmbient("C");
        return "cleared ambient still created";
    }
    catch (InvalidOperationException)
    {
        return null;
    }
}

static string? SaveIsLate()
{
    using var db = Open();
    db.Widgets.Add(new Widget("A"));
    db.Widgets.Add(new Widget("A"));
    try
    {
        db.SaveChanges();
        return "sqlite accepted two widgets with Code A";
    }
    catch (DbUpdateException)
    {
        return null;
    }
}

static LabDbContext Open()
{
    var connection = OpenConnection();
    var db = new LabDbContext(new DbContextOptionsBuilder<LabDbContext>().UseSqlite(connection).Options);
    db.Database.EnsureCreated();
    return db;
}

static SqliteConnection OpenConnection()
{
    var connection = new SqliteConnection("Data Source=:memory:");
    connection.Open();
    return connection;
}

sealed class LabDbContext(DbContextOptions<LabDbContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>().HasIndex(widget => widget.Code).IsUnique();
    }
}

sealed class Widget
{
    public const string Duplicate =
        "Unique constraint violated: 'Code' value is already used on another 'Widget'.";

    public int Id { get; private set; }
    public string Code { get; private set; } = "";

    public Widget(string code) { Code = code; }

    public static bool ExistsWithCode(LabDbContext db, string code) =>
        db.Widgets.Local.Any(widget => widget.Code == code)
        || db.Widgets.Any(widget => widget.Code == code);

    public static DomainResult<Widget> Create(string code, LabDbContext db)
    {
        if (ExistsWithCode(db, code))
            return DomainResult<Widget>.Failure(Duplicate);
        var created = new Widget(code);
        db.Widgets.Add(created);
        return DomainResult<Widget>.Success(created);
    }

    public static DomainResult<Widget> CreateFrom(IServiceProvider services, string code)
    {
        var db = services.GetRequiredService<LabDbContext>();
        return Create(code, db);
    }

    public static DomainResult<Widget> CreateAmbient(string code)
    {
        var db = Storage.Current.Value
            ?? throw new InvalidOperationException("No storage context.");
        return Create(code, db);
    }
}

sealed class Lot
{
    public DomainResult Issue(string code, LabDbContext db) => Results.Of(Widget.Create(code, db));
}

sealed class IssueHandler(LabDbContext db)
{
    public DomainResult Issue(string code) => Results.Of(Widget.Create(code, db));
}

sealed class Parent
{
    public LabDbContext? Db { get; set; }

    public DomainResult IssueCopyingField(string code) =>
        new Child { Db = Db }.Create(code);

    public DomainResult IssueForgettingField(string code) =>
        new Child().Create(code);
}

sealed class Child
{
    public LabDbContext? Db { get; set; }

    public DomainResult Create(string code)
    {
        if (Db is null)
            return DomainResult.Success();
        return Results.Of(Widget.Create(code, Db));
    }
}

static class Results
{
    public static DomainResult Of<T>(DomainResult<T> result) where T : class =>
        result.IsSuccess ? DomainResult.Success() : DomainResult.Failure(result.ErrorMessage ?? "");
}

static class Storage
{
    public static readonly AsyncLocal<LabDbContext?> Current = new();
}

sealed class DomainResult
{
    public bool IsSuccess { get; private init; }
    public string? ErrorMessage { get; private init; }
    public static DomainResult Success() => new() { IsSuccess = true };
    public static DomainResult Failure(string message) => new() { ErrorMessage = message };
}

sealed class DomainResult<T> where T : class
{
    public bool IsSuccess { get; private init; }
    public string? ErrorMessage { get; private init; }
    public T? Value { get; private init; }
    public static DomainResult<T> Success(T value) => new() { IsSuccess = true, Value = value };
    public static DomainResult<T> Failure(string message) => new() { ErrorMessage = message };
}
