using Microsoft.EntityFrameworkCore;
using Poly.Generated;

static class MainApp
{
    static void Main()
    {
        var dbPath = Path.Combine(AppContext.BaseDirectory, "uni-test.db");
        if (File.Exists(dbPath)) File.Delete(dbPath);
        var options = new DbContextOptionsBuilder<RegistrationDbContext>().UseSqlite($"Data Source={dbPath}").Options;
        using var db = new RegistrationDbContext(options);
        db.Database.EnsureCreated();
        var p = Program.Create("CS", "Computer Science").Value;
        var s1 = Student.Create("dup@example.com", "Ada", "12345678", p, new List<Enrollment>()).Value;
        var s2 = Student.Create("dup@example.com", "Eve", "87654321", p, new List<Enrollment>()).Value;
        db.AddRange(s1, s2);
        db.SaveChanges();
        Console.WriteLine($"students with same email saved: {db.Students.Count()}");
        db.Database.OpenConnection();
        using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='index' AND tbl_name='Students';";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) Console.WriteLine($"index: {reader.GetString(0)}");
    }
}
