using System.Text;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>SCAFFOLDING (L-15b probe P2): dumps today's EF foreign keys and the SQLite schema. Deleted in the last commit.</summary>
public class L15bProbeP2Tests {
    private const string Out = "/private/tmp/claude-501/-Users-scoizzle--t3-worktrees-Poly-t3-bcf47a9d-f63f439a67cd5ea0/82139a5b-0e79-429d-9b2a-bed3055d8c0e/scratchpad/p2-";

    private const string Head = "domain Probe\nuses sqlite\nuses http\n\n";

    private const string Shapes = Head + """
        Course: entity {
          Code: Text unique
          sections: many Section
        }
        Section: entity {
          Cap: Number
          course: Course
        }
        Lead: entity {
          Name: Text
          account: Account optional
        }
        Account: entity {
          Name: Text
          parent: Account optional
          hq: owned Site optional
          parts: many owned LineItem
          notes: many Note via account
          seats: many Seat
        }
        Site: entity { City: Text }
        LineItem: entity { Qty: Number }
        Note: entity {
          Body: Text
          account: Account via notes
        }
        Seat: entity { Label: Text }
        Invoice: entity {
          Code: Text
          rows: many owned Row
        }
        Row: entity {
          Qty: Number
          invoice: Invoice
        }
        """;

    private const string PairOwned = Head + """
        Reservation: entity {
          Nights: Number
          folio: owned Folio optional
        }
        Folio: entity {
          Balance: Number default(0)
          reservation: Reservation
        }
        """;

    private const string PairOptional = Head + """
        Person: entity {
          Name: Text
          badge: Badge optional
        }
        Badge: entity {
          Code: Text
          holder: Person optional
        }
        """;

    private const string PairRequired = Head + """
        Driver: entity {
          Name: Text
          licence: Licence
        }
        Licence: entity {
          Code: Text
          driver: Driver optional
        }
        """;

    [Test]
    public async Task DumpAll() {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Poly.slnx")))
            root = Path.GetDirectoryName(root)!;
        var sb = new StringBuilder();
        foreach (var (name, poly) in new[] {
            ("shapes", Shapes), ("pair-owned", PairOwned), ("pair-optional", PairOptional), ("pair-required", PairRequired),
            ("hotel", File.ReadAllText(Path.Combine(root, "docs/probes/dogfood/hotel.poly"))),
            ("crm", File.ReadAllText(Path.Combine(root, "docs/probes/dogfood/crm.poly"))),
            ("university", File.ReadAllText(Path.Combine(root, "docs/probes/dogfood/university.poly"))),
        }) {
            sb.AppendLine("##### " + name);
            try {
                using var m = EfModel.Create(poly, name).EnsureSchema();
                sb.Append(Dump(m));
            }
            catch (Exception ex) {
                sb.AppendLine("MODEL DOES NOT BUILD: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
            }
        }
        await File.WriteAllTextAsync(Out + "dump.txt", sb.ToString());
    }

    [Test]
    public async Task Corpus() {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Poly.slnx")))
            root = Path.GetDirectoryName(root)!;
        var docs = new List<(string, string)>();
        foreach (var d in Poly.Tests.TestHelpers.GoldenDomains.Compilable)
            docs.Add(("golden/" + d.Name, Poly.Tests.Quality.GoldenPackDomains.FromGolden(d).Poly));
        foreach (var f in new[] { "docs/probes/dogfood/hotel.poly", "docs/probes/dogfood/crm.poly", "docs/probes/dogfood/university.poly", "demo/Poly.RestApi/library.poly" })
            docs.Add((f, File.ReadAllText(Path.Combine(root, f))));
        var sb = new StringBuilder();
        foreach (var (name, poly) in docs) {
            sb.AppendLine("##### " + name);
            try {
                using var m = EfModel.Create(poly, "corpus");
                if (m.BuildError is { } err) { sb.AppendLine("MODEL DOES NOT BUILD: " + err.Split('\n')[0]); continue; }
                var fks = m.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()).ToList();
                foreach (var fk in fks.OrderBy(f => f.DeclaringEntityType.Name))
                    sb.AppendLine($"{fk.DeclaringEntityType.ClrType.Name}.{fk.Properties[0].Name}->{fk.PrincipalEntityType.ClrType.Name} req={fk.IsRequired} del={fk.DeleteBehavior} uniq={fk.IsUnique}" + (fk.Properties[0].Name.EndsWith("1") ? "  <<< DUPLICATE?" : ""));
            }
            catch (Exception ex) { sb.AppendLine("COMPILE: " + ex.Message.Split('\n')[0]); }
        }
        await File.WriteAllTextAsync(Out + "corpus.txt", sb.ToString());
    }

    [Test]
    public async Task RawAndDeleteBehaviour() {
        using var m = EfModel.Create(Shapes).EnsureSchema();
        var sb = new StringBuilder();
        sb.AppendLine("PRAGMA foreign_keys = " + Scalar(m, "PRAGMA foreign_keys"));
        sb.AppendLine("raw insert Section with NULL course FK: " + Try(m, "INSERT INTO Sections (cap) VALUES (1)"));
        sb.AppendLine("raw insert Lead with NULL account FK: " + Try(m, "INSERT INTO Leads (name) VALUES ('x')"));
        sb.AppendLine("raw insert Section with a missing course id (999): " + Try(m, "INSERT INTO Sections (cap, CourseId) VALUES (1, 999)"));
        await File.WriteAllTextAsync(Out + "raw.txt", sb.ToString());
    }

    [Test]
    public async Task DeleteBehaviourToday() {
        var sb = new StringBuilder();
        foreach (var variant in new[] { "required-section", "optional-lead", "owned-many-lineitem", "owned-single-hq", "plain-many-seat" }) {
            // Raw DELETE of the principal
            using (var m = EfModel.Create(Shapes).EnsureSchema()) {
                Seed(m, variant);
                sb.AppendLine($"{variant}: raw DELETE principal: " + Try(m, Principal(variant)));
            }
            // EF remove of the principal with the dependent tracked
            using (var m = EfModel.Create(Shapes).EnsureSchema()) {
                Seed(m, variant);
                sb.AppendLine($"{variant}: EF remove principal (dependent tracked): " + EfDelete(m, variant));
                sb.AppendLine($"{variant}:   dependent rows left: " + Dependents(m, variant));
            }
        }
        await File.WriteAllTextAsync(Out + "delete.txt", sb.ToString());
    }

    private static string Principal(string v) => v switch {
        "required-section" => "DELETE FROM Courses",
        "optional-lead" or "owned-many-lineitem" or "plain-many-seat" => "DELETE FROM Accounts",
        "owned-single-hq" => "DELETE FROM Sites",
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    private static void Seed(EfModel m, string v) {
        switch (v) {
            case "required-section":
                Try(m, "INSERT INTO Courses (code) VALUES ('c1')");
                Try(m, "INSERT INTO Sections (cap, CourseId) VALUES (1, 1)");
                break;
            case "optional-lead":
                Try(m, "INSERT INTO Accounts (name) VALUES ('a')");
                Try(m, "INSERT INTO Leads (name, AccountId) VALUES ('l', 1)");
                break;
            case "owned-many-lineitem":
                Try(m, "INSERT INTO Accounts (name) VALUES ('a')");
                Try(m, "INSERT INTO LineItems (qty, AccountId) VALUES (1, 1)");
                break;
            case "owned-single-hq":
                Try(m, "INSERT INTO Sites (city) VALUES ('x')");
                Try(m, "INSERT INTO Accounts (name, HqId) VALUES ('a', 1)");
                break;
            case "plain-many-seat":
                Try(m, "INSERT INTO Accounts (name) VALUES ('a')");
                Try(m, "INSERT INTO Seats (label, AccountId) VALUES ('s', 1)");
                break;
        }
    }

    private static string Dependents(EfModel m, string v) {
        var table = v switch {
            "required-section" => "Sections", "optional-lead" => "Leads", "owned-many-lineitem" => "LineItems",
            "owned-single-hq" => "Accounts", "plain-many-seat" => "Seats", _ => throw new ArgumentOutOfRangeException(nameof(v)),
        };
        var fk = v switch {
            "required-section" => "CourseId", "optional-lead" => "AccountId", "owned-many-lineitem" => "AccountId",
            "owned-single-hq" => "HqId", "plain-many-seat" => "AccountId", _ => throw new ArgumentOutOfRangeException(nameof(v)),
        };
        return $"{Scalar(m, $"SELECT COUNT(*) FROM {table}")} rows, fk values: " + string.Join(",", Rows(m, $"SELECT {fk} FROM {table}"));
    }

    private static string EfDelete(EfModel m, string v) {
        try {
            var (principalType, dependentType, principalId) = v switch {
                "required-section" => ("Course", "Section", 1),
                "optional-lead" => ("Account", "Lead", 1),
                "owned-many-lineitem" => ("Account", "LineItem", 1),
                "owned-single-hq" => ("Site", "Account", 1),
                "plain-many-seat" => ("Account", "Seat", 1),
                _ => throw new ArgumentOutOfRangeException(nameof(v)),
            };
            var p = m.Db.Find(m.Entity(principalType).ClrType, principalId)!;
            _ = m.Db.Find(m.Entity(dependentType).ClrType, 1);
            m.Db.Remove(p);
            m.Db.SaveChanges();
            return "SaveChanges ok";
        }
        catch (Exception ex) {
            return ex.GetType().Name + ": " + ex.Message.Split('\n')[0];
        }
    }

    private static string Scalar(EfModel m, string sql) {
        using var c = m.Connection.CreateCommand();
        c.CommandText = sql;
        return Convert.ToString(c.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? "null";
    }

    private static List<string> Rows(EfModel m, string sql) {
        using var c = m.Connection.CreateCommand();
        c.CommandText = sql;
        using var r = c.ExecuteReader();
        var rows = new List<string>();
        while (r.Read())
            rows.Add("(" + string.Join("|", Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "null" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))) + ")");
        return rows;
    }

    private static string Try(EfModel m, string sql) {
        try {
            using var c = m.Connection.CreateCommand();
            c.CommandText = sql;
            return "accepted (" + c.ExecuteNonQuery() + " row)";
        }
        catch (SqliteException ex) {
            return "REFUSED: " + ex.Message;
        }
    }

    private static string Dump(EfModel m) {
        var sb = new StringBuilder();
        sb.AppendLine("PRAGMA foreign_keys = " + Scalar(m, "PRAGMA foreign_keys"));
        foreach (var fk in m.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()).OrderBy(f => f.DeclaringEntityType.Name).ThenBy(f => f.Properties[0].Name)) {
            sb.AppendLine($"EF  {fk.DeclaringEntityType.ClrType.Name}.{string.Join(",", fk.Properties.Select(p => p.Name))} -> {fk.PrincipalEntityType.ClrType.Name}"
                + $"  dep-nav={fk.DependentToPrincipal?.Name ?? "-"} prin-nav={fk.PrincipalToDependent?.Name ?? "-"} IsRequired={fk.IsRequired} IsUnique={fk.IsUnique}"
                + $" Delete={fk.DeleteBehavior} colNullable={string.Join(",", fk.Properties.Select(p => p.IsNullable))}");
        }
        foreach (var et in m.Model.GetEntityTypes().OrderBy(e => e.ClrType.Name)) {
            var table = et.GetTableName();
            var fkList = Rows(m, $"PRAGMA foreign_key_list({table})");
            if (fkList.Count > 0)
                sb.AppendLine($"SQL {table}: fk_list(id|seq|table|from|to|on_update|on_delete|match)=" + string.Join(" ", fkList));
        }
        return sb.ToString();
    }
}
