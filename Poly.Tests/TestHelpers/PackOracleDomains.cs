namespace Poly.Tests.TestHelpers;

/// <summary>One fixture the <see cref="PackOracle"/> compiles and boots. <paramref name="Poly"/> is the
/// "Minimal reproduction" block of the named issue, verbatim. <paramref name="ListPath"/> is a GET
/// route that returns a JSON array once the host is up. A fixture with
/// <paramref name="Boots"/> false never reaches the oracle; <paramref name="NotBootingReason"/> names why.</summary>
public sealed record PackOracleDomain(
    string Name,
    string Poly,
    string ListPath,
    bool Boots = true,
    string? NotBootingReason = null) {
    public override string ToString() => Name;
}

/// <summary>The oracle's fixture catalog. <see cref="Subset"/> is the PR-tier boot set (OQ-10, #677):
/// exactly two domains. <see cref="Full"/> is every fixture that boots.</summary>
public static class PackOracleDomains {
    /// <summary>#588 Minimal reproduction. Product and Order roots, stages, enum default.</summary>
    public static PackOracleDomain Inventory588 { get; } = new(
        "Inventory588",
        """
        domain Inventory
        uses sqlite
        uses http

        Category: enum { Tools, Toys, Food }

        Product: entity {
          Sku: Text unique length(3, 12) pattern("^[A-Z0-9]+$")
          Name: Text length(1, 50)
          Price: Number range(0, 10000)
          Stock: Number range(0, 100000) default(0)
          Cat: Category default(Tools)
          Note: Text optional length(0, 100)
        }

        Order: entity {
          Qty: Number range(1, 100)
          Total: Number range(0, 1000000)
          Draft: stage { Submit: action { transition to Placed } }
          Placed: stage {}
        }
        """,
        "/api/orders");

    /// <summary>#559 Minimal reproduction. It has no <c>uses http</c>, so polyc emits no Program.cs and
    /// there is nothing to boot. It stays in the catalog so the issue is named, not forgotten.</summary>
    public static PackOracleDomain Logistics559 { get; } = new(
        "Logistics559",
        """
        domain Logistics
        uses sqlite
        Shipment: entity {
          Reference: Text unique length(4, 20)
          WeightKg: Number range(1, 40000)
          Declared: Decimal range(0, 1000000)
        }
        """,
        "/api/shipments",
        Boots: false,
        NotBootingReason: "#559: the reproduction has no 'uses http', so no Program.cs is emitted and there is no host to boot");

    /// <summary>#620 Minimal reproduction. A failing action whose earlier effect is persisted.</summary>
    public static PackOracleDomain Warehouse620 { get; } = new(
        "Warehouse620",
        """
        domain Logistics
        uses http
        uses sqlite

        Warehouse: entity table("WAREHOUSES") {
          Code: Text unique length(3, 8) column("WH_CODE", "VARCHAR(8)")
          Capacity: Number range(0, 100000)
          Used: Number range(0, 100000) default(0)
          Bad: action (units: Number) { assign Capacity to Capacity + 1 assign Used to units }
        }
        """,
        "/api/warehouses");

    /// <summary>#626 Minimal reproduction. A stage-guarded one-shot action under concurrency.</summary>
    public static PackOracleDomain Inventory626 { get; } = new(
        "Inventory626",
        """
        domain Inventory
        uses sqlite
        uses http
        Order: entity {
          Qty: Number range(1, 100)
          Total: Number range(0, 1000000)
          Draft: stage { Submit: action { transition to Placed } }
          Placed: stage { Ship: action { transition to Shipped } }
          Shipped: stage {}
        }
        """,
        "/api/orders");

    /// <summary>#658 Minimal reproduction. A policy over a to-one navigation.</summary>
    public static PackOracleDomain Desk658 { get; } = new(
        "Desk658",
        """
        domain Desk
        uses http
        uses sqlite

        User: entity {
          Email: Text unique
        }

        Project: entity {
          Key: Text unique
          tasks: many Task
          FileTask: action (who: User) {
            create in tasks { assignee: who }
          }
        }

        Task: entity {
          assignee: User
          Assigned: policy { assignee exists }
          Todo: stage {
            Triage: action require Assigned { transition to Done }
          }
          Done: stage { }
        }
        """,
        "/api/users");

    /// <summary>#653 Minimal reproduction. A read-modify-write counter.</summary>
    public static PackOracleDomain Tally653 { get; } = new(
        "Tally653",
        """
        domain Counter
        uses http
        uses sqlite

        Tally: entity {
          Name: Text unique length(1, 20)
          N: Number default(0) range(0, 1000000)
          Bump: action { assign N to N + 1 }
        }
        """,
        "/api/tallies");

    /// <summary>#675 (Q13 ruling) designated initial stage. <c>Draft</c> is declared first, so <c>Open</c>
    /// is only initial because the header says so. The unique <c>Code</c> gives the POST a Location and the
    /// created row its own read route, clear of #589's unserialised synthetic id.</summary>
    public static PackOracleDomain Fulfillment675 { get; } = new(
        "Fulfillment675",
        """
        domain Fulfillment
        uses sqlite
        uses http

        Order: entity initial(Open) {
          Code: Text unique length(3, 8)
          Draft: stage {}
          Open: stage {}
        }
        """,
        "/api/orders");

    /// <summary>L-22b (#604), A11 owner ruling. A public entity navigates to an internal entity: the navigation
    /// prints internal and the pack maps it explicitly, so the app boots and the nested route reads the lines.</summary>
    public static PackOracleDomain PublicOrderInternalLine604 { get; } = new(
        "PublicOrderInternalLine604",
        """
        domain Shop
        uses sqlite
        uses http

        OrderLine: entity {
          Qty: Number range(1, 100)
        }

        public entity Order {
          Code: Text unique length(3, 8)
          Total: Number default(0)
          lines: many OrderLine
          public action AddLine (qty: Number) { create in lines { Qty: qty } }
        }
        """,
        "/api/orders");

    /// <summary>L-22b: an owned many navigation to an internal entity whose own side declares the inverse (<c>via</c> on the
    /// target). The line is an aggregate child, so the nested route reads it back.</summary>
    public static PackOracleDomain PublicOrderOwnedInternalLine604 { get; } = new(
        "PublicOrderOwnedInternalLine604",
        """
        domain Shop
        uses sqlite
        uses http

        OrderLine: entity {
          Qty: Number range(1, 100)
          order: Order via lines
        }

        public entity Order {
          Code: Text unique length(3, 8)
          lines: many owned OrderLine
          public action AddLine (qty: Number) { create in lines { Qty: qty } }
        }
        """,
        "/api/orders");

    /// <summary>L-22b: a one-to-one navigation from a public entity to an internal one.</summary>
    public static PackOracleDomain PublicOrderInternalReceipt604 { get; } = new(
        "PublicOrderInternalReceipt604",
        """
        domain Shop
        uses sqlite
        uses http

        Receipt: entity {
          Note: Text
        }

        public entity Order {
          Code: Text unique length(3, 8)
          receipt: one Receipt
          public action Issue (note: Text) { create in receipt { Note: note } }
        }
        """,
        "/api/orders");

    /// <summary>L-22b, reverse shape: the public entity is the child; its singular navigation to the internal parent
    /// is the one that prints internal, and it pairs with the parent's public collection.</summary>
    public static PackOracleDomain PublicLineInternalOrder604 { get; } = new(
        "PublicLineInternalOrder604",
        """
        domain Shop
        uses sqlite
        uses http

        Order: entity {
          Code: Text unique length(3, 8)
          lines: many owned Line
          AddLine: action (qty: Number) { create in lines { Qty: qty } }
        }

        public entity Line {
          Qty: Number range(1, 100)
          order: Order via lines
        }
        """,
        "/api/orders");

    /// <summary>L-22b F4: a BIDIRECTIONAL owned one-to-one from a public entity to an internal one. It was an analysis
    /// error before L-22b and is now the same boot failure as every all-public bidirectional one-to-one (#791).</summary>
    public static PackOracleDomain PublicOrderInternalReceiptBidirectional604 { get; } = new(
        "PublicOrderInternalReceiptBidirectional604",
        """
        domain Shop
        uses sqlite
        uses http

        Receipt: entity {
          Note: Text
          order: Order via receipt
        }

        public entity Order {
          Code: Text unique length(3, 8)
          receipt: one owned Receipt
          public action Issue (note: Text) { create in receipt { Note: note } }
        }
        """,
        "/api/orders",
        Boots: false,
        NotBootingReason: "#791: EF cannot determine the dependent side of a bidirectional one-to-one");

    /// <summary>L-22b closing round: the same two-way one-to-one without `owned` also fails to boot (#791).</summary>
    public static PackOracleDomain PublicOrderInternalReceiptTwoWay604 { get; } = new(
        "PublicOrderInternalReceiptTwoWay604",
        """
        domain Shop
        uses sqlite
        uses http

        Receipt: entity {
          Note: Text
          order: Order via receipt
        }

        public entity Order {
          Code: Text unique length(3, 8)
          receipt: one Receipt
          public action Issue (note: Text) { create in receipt { Note: note } }
        }
        """,
        "/api/orders",
        Boots: false,
        NotBootingReason: "#791: EF cannot determine the dependent side of a two-way one-to-one");

    /// <summary>L-22 (#604): the same domain with the target marked public. It boots and serves the list route.</summary>
    public static PackOracleDomain PublicOrderPublicLine604 { get; } = new(
        "PublicOrderPublicLine604",
        """
        domain Shop
        uses sqlite
        uses http

        public entity OrderLine {
          Qty: Number range(1, 100)
        }

        public entity Order {
          Code: Text unique length(3, 8)
          Total: Number default(0)
          lines: many OrderLine
          public action Reset { assign Total to 0 }
        }
        """,
        "/api/orders");

    public static IReadOnlyList<PackOracleDomain> All { get; } =
        [Inventory588, Logistics559, Warehouse620, Inventory626, Desk658, Tally653, Fulfillment675];

    /// <summary>The PR-tier boot set (OQ-10): exactly Inventory588 and Warehouse620.</summary>
    public static IReadOnlyList<PackOracleDomain> Subset { get; } = [Inventory588, Warehouse620];

    /// <summary>Every fixture that boots.</summary>
    public static IReadOnlyList<PackOracleDomain> Full { get; } = All.Where(d => d.Boots).ToList();
}