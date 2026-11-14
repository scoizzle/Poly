using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-15b: the generated EF model says what the language says. A required link's foreign key is required and its
/// principal cannot be deleted while a dependent references it (Restrict); an owned part goes with its owner
/// (Cascade); an optional link stays as it was (optional, ClientSetNull). The EF model, read in-process from the
/// generated DbContext, is the authority; the database rows prove the schema EF derives from it.
/// </summary>
public class DbContextNavigationRequirednessTests {
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
        Room: entity { Label: Text }
        Booking: entity {
          Note: Text
          room: Room
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

    private const string OwnedPair = Head + """
        Reservation: entity {
          Nights: Number
          folio: owned Folio optional
        }
        Folio: entity {
          Balance: Number default(0)
          reservation: Reservation
        }
        """;

    private const string OptionalPair = Head + """
        Person: entity {
          Name: Text
          badge: Badge optional
        }
        Badge: entity {
          Code: Text
          holder: Person optional
        }
        """;

    private const string RequiredFirstPair = Head + """
        Driver: entity {
          Name: Text
          licence: Licence
        }
        Licence: entity {
          Code: Text
          driver: Driver optional
        }
        """;

    private const string RequiredSecondPair = Head + """
        Owner: entity {
          Name: Text
          stamp: Stamp optional
        }
        Stamp: entity {
          Code: Text
          owner: Owner
        }
        """;

    private const string OwnedPairOptionalBackReference = Head + """
        Reservation: entity {
          Nights: Number
          folio: owned Folio optional
        }
        Folio: entity {
          Balance: Number default(0)
          reservation: Reservation optional
        }
        """;

    private static async Task<EfModel> Build(string poly, string name = "Probe") {
        var model = EfModel.Create(poly, name);
        await Assert.That(model.BuildError).IsNull();
        return model;
    }

    // ── the EF model, row by row ─────────────────────────────────────────────────────────────────────

    [Test]
    public async Task RequiredToOne_ForeignKeyIsRequired_AndRestricts() {
        using var m = await Build(Shapes);
        var fk = m.Fk("Section", "Course");
        await Assert.That(fk.IsRequired).IsTrue();
        await Assert.That(fk.DeleteBehavior).IsEqualTo(DeleteBehavior.Restrict);
        await Assert.That(fk.Properties.Single().IsNullable).IsFalse();
    }

    [Test]
    public async Task RequiredToOne_WithoutACollectionEnd_ForeignKeyIsRequired_AndRestricts() {
        using var m = await Build(Shapes);
        var fk = m.Fk("Booking", "Room");
        await Assert.That(fk.IsRequired).IsTrue();
        await Assert.That(fk.DeleteBehavior).IsEqualTo(DeleteBehavior.Restrict);
    }

    [Test]
    public async Task OptionalToOne_ForeignKeyIsOptional_AsBefore() {
        using var m = await Build(Shapes);
        var fk = m.Fk("Lead", "Account");
        await Assert.That(fk.IsRequired).IsFalse();
        await Assert.That(fk.DeleteBehavior).IsEqualTo(DeleteBehavior.ClientSetNull);
        await Assert.That(fk.Properties.Single().IsNullable).IsTrue();
    }

    [Test]
    public async Task OptionalSelfReference_ForeignKeyIsOptional_AsBefore() {
        using var m = await Build(Shapes);
        var fk = m.Fk("Account", "Account");
        await Assert.That(fk.IsRequired).IsFalse();
        await Assert.That(fk.DeleteBehavior).IsEqualTo(DeleteBehavior.ClientSetNull);
    }

    [Test]
    public async Task OwnedSinglePart_WithoutBackNavigation_IsUnchanged_KeyOnTheOwner() {
        // The key sits on the owner, so cascading would delete the owner with its part: it stays optional, ClientSetNull.
        using var m = await Build(Shapes);
        var fk = m.Fk("Account", "Site");
        await Assert.That(fk.IsRequired).IsFalse();
        await Assert.That(fk.DeleteBehavior).IsEqualTo(DeleteBehavior.ClientSetNull);
        await Assert.That(fk.Properties.Single().Name).IsEqualTo("HqId");
    }

    [Test]
    public async Task OwnedMany_WithoutBackNavigation_CascadesFromTheOwner() {
        using var m = await Build(Shapes);
        var fk = m.Fk("LineItem", "Account");
        await Assert.That(fk.DeleteBehavior).IsEqualTo(DeleteBehavior.Cascade);
        // The language states no requiredness for a part with no back-navigation: the column stays nullable.
        await Assert.That(fk.IsRequired).IsFalse();
    }

    [Test]
    public async Task OwnedMany_WithRequiredBackNavigation_IsRequired_AndCascades() {
        using var m = await Build(Shapes);
        var fk = m.Fk("Row", "Invoice");
        await Assert.That(fk.IsRequired).IsTrue();
        await Assert.That(fk.DeleteBehavior).IsEqualTo(DeleteBehavior.Cascade);
    }

    [Test]
    public async Task PlainMany_Via_RequiredBackNavigation_IsRequired_AndRestricts() {
        using var m = await Build(Shapes);
        var fk = m.Fk("Note", "Account");
        await Assert.That(fk.IsRequired).IsTrue();
        await Assert.That(fk.DeleteBehavior).IsEqualTo(DeleteBehavior.Restrict);
    }

    [Test]
    public async Task PlainMany_WithoutBackNavigation_IsOptional_AsBefore() {
        using var m = await Build(Shapes);
        var fk = m.Fk("Seat", "Account");
        await Assert.That(fk.IsRequired).IsFalse();
        await Assert.That(fk.DeleteBehavior).IsEqualTo(DeleteBehavior.ClientSetNull);
    }

    [Test]
    public async Task OneToOne_OwnedPair_TheRequiredPartHoldsTheKey_AndCascades() {
        using var m = await Build(OwnedPair);
        var fk = m.Fk("Folio", "Reservation");
        await Assert.That(fk.IsRequired).IsTrue();
        await Assert.That(fk.IsUnique).IsTrue();
        await Assert.That(fk.DeleteBehavior).IsEqualTo(DeleteBehavior.Cascade);
    }

    [Test]
    public async Task OneToOne_OwnedPair_WithOptionalBackReference_ThePartStillHoldsTheKey() {
        using var m = await Build(OwnedPairOptionalBackReference);
        var fk = m.Fk("Folio", "Reservation");
        await Assert.That(fk.IsRequired).IsFalse();
        await Assert.That(fk.DeleteBehavior).IsEqualTo(DeleteBehavior.Cascade);
    }

    [Test]
    public async Task OneToOne_TheRequiredSideIsDependent_WhenDeclaredFirst() {
        using var m = await Build(RequiredFirstPair);
        var fk = m.Fk("Driver", "Licence");
        await Assert.That(fk.IsRequired).IsTrue();
        await Assert.That(fk.DeleteBehavior).IsEqualTo(DeleteBehavior.Restrict);
    }

    [Test]
    public async Task OneToOne_TheRequiredSideIsDependent_WhenDeclaredSecond() {
        using var m = await Build(RequiredSecondPair);
        var fk = m.Fk("Stamp", "Owner");
        await Assert.That(fk.IsRequired).IsTrue();
        await Assert.That(fk.DeleteBehavior).IsEqualTo(DeleteBehavior.Restrict);
    }

    [Test]
    public async Task OneToOne_BothOptional_TheFirstDeclaredIsDependent() {
        using var m = await Build(OptionalPair);
        var fk = m.Fk("Person", "Badge");
        await Assert.That(fk.IsRequired).IsFalse();
        await Assert.That(fk.DeleteBehavior).IsEqualTo(DeleteBehavior.ClientSetNull);
    }

    [Test]
    public async Task ShippedHotel_OneToOnePair_BuildsAndTheFolioHoldsTheKey() {
        // Hotel's Reservation.folio / Folio.reservation pair did not build before (#791: EF could not pick a dependent side).
        var hotel = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "docs/probes/dogfood/hotel.poly"));
        using var m = await Build(hotel, "hotel");
        var fk = m.Fk("Folio", "Reservation");
        await Assert.That(fk.IsRequired).IsTrue();
        await Assert.That(fk.DeleteBehavior).IsEqualTo(DeleteBehavior.Cascade);
    }

    // ── fail closed ──────────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ManyToMany_DecidesNoDeleteBehavior() {
        const string Dsl = """
            domain Test

            Student: entity {
              Name: Text
              courses: many Course
            }

            Course: entity {
              Title: Text
              students: many Student
            }
            """;
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(new PolyDslParser(Dsl).Parse());
        var storage = new StorageAnalyzer(result.Root, result.Analysis).Analyze();
        var navigations = storage.Entities.SelectMany(e => e.CollectionNavigations.Concat(e.ReferenceNavigations)).ToList();
        await Assert.That(navigations.Count).IsEqualTo(2);
        await Assert.That(navigations.All(n => n.OnDelete is null && !n.ConfiguresForeignKey)).IsTrue();
    }

    // ── the database, row by row ─────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Schema_RequiredLinkIsNotNull_OptionalLinkIsNullable() {
        using var m = (await Build(Shapes)).EnsureSchema();
        await Assert.That(Scalar(m, "SELECT \"notnull\" FROM pragma_table_info('Sections') WHERE name = 'CourseId'")).IsEqualTo("1");
        await Assert.That(Scalar(m, "SELECT \"notnull\" FROM pragma_table_info('Leads') WHERE name = 'AccountId'")).IsEqualTo("0");
        await Assert.That(Scalar(m, "SELECT \"notnull\" FROM pragma_table_info('Accounts') WHERE name = 'HqId'")).IsEqualTo("0");
        await Assert.That(Scalar(m, "SELECT \"notnull\" FROM pragma_table_info('Rows') WHERE name = 'InvoiceId'")).IsEqualTo("1");
    }

    [Test]
    public async Task Schema_DeleteActionsFollowTheRule() {
        using var m = (await Build(Shapes)).EnsureSchema();
        await Assert.That(Scalar(m, "SELECT on_delete FROM pragma_foreign_key_list('Sections')")).IsEqualTo("RESTRICT");
        await Assert.That(Scalar(m, "SELECT on_delete FROM pragma_foreign_key_list('LineItems')")).IsEqualTo("CASCADE");
        await Assert.That(Scalar(m, "SELECT on_delete FROM pragma_foreign_key_list('Rows')")).IsEqualTo("CASCADE");
        await Assert.That(Scalar(m, "SELECT on_delete FROM pragma_foreign_key_list('Leads')")).IsEqualTo("NO ACTION");
    }

    [Test]
    public async Task Database_EnforcesForeignKeys_ByDefault() {
        // The generated host opens "Data Source=<domain>.db" with no foreign-key keyword; the bundled SQLite compiles
        // enforcement in, so ON DELETE is a rule the database applies, not a declaration.
        using var m = (await Build(Shapes)).EnsureSchema();
        await Assert.That(Scalar(m, "PRAGMA foreign_keys")).IsEqualTo("1");
    }

    [Test]
    public async Task Database_RefusesANullRequiredForeignKey_AcceptsANullOptionalOne() {
        using var m = (await Build(Shapes)).EnsureSchema();
        await Assert.That(Exec(m, "INSERT INTO Sections (cap) VALUES (1)")).Contains("NOT NULL constraint failed");
        await Assert.That(Exec(m, "INSERT INTO Leads (name) VALUES ('x')")).IsNull();
    }

    [Test]
    public async Task DeletingAnOwner_RemovesItsOwnedParts() {
        using var m = (await Build(Shapes)).EnsureSchema();
        Exec(m, "INSERT INTO Accounts (name) VALUES ('a')");
        Exec(m, "INSERT INTO LineItems (qty, AccountId) VALUES (1, 1)");
        Exec(m, "INSERT INTO Invoices (code) VALUES ('i')");
        Exec(m, "INSERT INTO Rows (qty, InvoiceId) VALUES (1, 1)");

        RemoveWithDependent(m, "Account", 1, "LineItem");
        RemoveWithDependent(m, "Invoice", 1, "Row");

        await Assert.That(Scalar(m, "SELECT COUNT(*) FROM LineItems")).IsEqualTo("0");
        await Assert.That(Scalar(m, "SELECT COUNT(*) FROM Rows")).IsEqualTo("0");
    }

    [Test]
    public async Task DeletingAnOwner_InTheDatabase_RemovesItsOwnedParts() {
        using var m = (await Build(Shapes)).EnsureSchema();
        Exec(m, "INSERT INTO Accounts (name) VALUES ('a')");
        Exec(m, "INSERT INTO LineItems (qty, AccountId) VALUES (1, 1)");
        await Assert.That(Exec(m, "DELETE FROM Accounts")).IsNull();
        await Assert.That(Scalar(m, "SELECT COUNT(*) FROM LineItems")).IsEqualTo("0");
    }

    [Test]
    public async Task DeletingThePrincipalOfARequiredLink_IsRefused() {
        using var m = (await Build(Shapes)).EnsureSchema();
        Exec(m, "INSERT INTO Courses (code) VALUES ('c')");
        Exec(m, "INSERT INTO Sections (cap, CourseId) VALUES (1, 1)");

        await Assert.That(Exec(m, "DELETE FROM Courses")).Contains("FOREIGN KEY constraint failed");
        await Assert.That(() => RemoveWithDependent(m, "Course", 1, "Section")).ThrowsException();
        await Assert.That(Scalar(m, "SELECT COUNT(*) FROM Sections")).IsEqualTo("1");
        await Assert.That(Scalar(m, "SELECT CourseId FROM Sections")).IsEqualTo("1");
    }

    [Test]
    public async Task DeletingThePrincipalOfAnOptionalLink_NullsTheKey_AsBefore() {
        using var m = (await Build(Shapes)).EnsureSchema();
        Exec(m, "INSERT INTO Accounts (name) VALUES ('a')");
        Exec(m, "INSERT INTO Leads (name, AccountId) VALUES ('l', 1)");

        RemoveWithDependent(m, "Account", 1, "Lead");

        await Assert.That(Scalar(m, "SELECT COUNT(*) FROM Leads")).IsEqualTo("1");
        await Assert.That(Scalar(m, "SELECT AccountId IS NULL FROM Leads")).IsEqualTo("1");
    }

    [Test]
    public async Task DeletingTheReservation_RemovesItsFolio() {
        using var m = (await Build(OwnedPair)).EnsureSchema();
        Exec(m, "INSERT INTO Reservations (nights) VALUES (2)");
        Exec(m, "INSERT INTO Folios (balance, ReservationId) VALUES (0, 1)");
        await Assert.That(Exec(m, "INSERT INTO Folios (balance) VALUES (0)")).Contains("NOT NULL constraint failed");
        await Assert.That(Exec(m, "DELETE FROM Reservations")).IsNull();
        await Assert.That(Scalar(m, "SELECT COUNT(*) FROM Folios")).IsEqualTo("0");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static string RepoRoot() {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Poly.slnx")))
            root = Path.GetDirectoryName(root)!;
        return root;
    }

    private static string Scalar(EfModel m, string sql) {
        using var c = m.Connection.CreateCommand();
        c.CommandText = sql;
        return Convert.ToString(c.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? "null";
    }

    /// <summary>Null when the statement ran; otherwise SQLite's message.</summary>
    private static string? Exec(EfModel m, string sql) {
        try {
            using var c = m.Connection.CreateCommand();
            c.CommandText = sql;
            c.ExecuteNonQuery();
            return null;
        }
        catch (SqliteException ex) {
            return ex.Message;
        }
    }

    /// <summary>Loads the principal and its dependent through EF, removes the principal and saves.</summary>
    private static void RemoveWithDependent(EfModel m, string principal, int id, string dependent) {
        _ = m.Db.Find(m.Entity(dependent).ClrType, id);
        m.Db.Remove(m.Db.Find(m.Entity(principal).ClrType, id)!);
        m.Db.SaveChanges();
    }
}
