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
          receipt: one Receipt optional
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
    /// error before L-22b and boots since L-15b declared the dependent side of a one-to-one pair (#791).</summary>
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
          receipt: one owned Receipt optional
          public action Issue (note: Text) { create in receipt { Note: note } }
        }
        """,
        "/api/orders");

    /// <summary>L-22b closing round: the same two-way one-to-one without `owned` boots too (#791, L-15b).</summary>
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
          receipt: one Receipt optional
          public action Issue (note: Text) { create in receipt { Note: note } }
        }
        """,
        "/api/orders");

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

    /// <summary>P-14a (row P-14). One host with every failure the HTTP contract maps: a Range, a unique,
    /// an occupied to-one link, a Stage and a Policy failure.</summary>
    public static PackOracleDomain Failures687 { get; } = new(
        "Failures687",
        """
        domain Failures
        uses http
        uses sqlite

        Child: entity {
          Code: Text unique length(1, 8)
          Qty: Number range(1, 100)
        }

        Node: entity {
          Code: Text length(1, 8)
          next: Node optional
          Extend: action { create in next { Code: "A" } create in next { Code: "B" } }
        }

        Ticket: entity {
          Code: Text unique length(1, 8)
          Ok: Boolean
          IsOk: policy { Ok is true }
          Draft: stage {
            Go: action require IsOk { transition to Done }
            Cancel: action { transition to Closed }
          }
          Done: stage { }
          Closed: stage { }
        }
        """,
        "/api/children");

    /// <summary>P-13a (#912). A single-create action into a to-one: the module's own 409 Link only fires when the host
    /// has loaded the stored link, so the second request is the oracle. It stays out of <see cref="All"/>.</summary>
    public static PackOracleDomain NodeLink912 { get; } = new(
        "NodeLink912",
        """
        domain Links
        uses http
        uses sqlite

        Node: entity {
          Code: Text length(1, 8)
          next: Node optional
          Extend: action { create in next { Code: "A" } }
        }
        """,
        "/api/nodes");

    /// <summary>P-13a (#658). A guard through a to-one on a root entity (the shape of crm's Lead.Convert): the guard reads
    /// <c>account</c>, which is not a route parent. It stays out of <see cref="All"/>.</summary>
    public static PackOracleDomain LeadConvert658 { get; } = new(
        "LeadConvert658",
        """
        domain Leads
        uses http
        uses sqlite

        Account: entity {
          Name: Text unique length(1, 20)
        }

        Lead: entity {
          Name: Text unique length(1, 20)
          account: Account optional
          HasAccount: policy { account exists }
          New: stage {
            Attach: action { create in account { Name: "Acme" } }
            Convert: action require HasAccount { transition to Converted }
          }
          Converted: stage { }
        }
        """,
        "/api/leads");

    /// <summary>P-13b (ii) (#912): reads at depth. <c>Order</c> reaches <c>Customer.Region.Zone</c> through three links, a line's
    /// product through a quantifier body and a fan-out, and <c>Both</c> takes two products. Each link is made over HTTP by the
    /// module's own <c>create in</c> actions. It stays out of <see cref="All"/>.</summary>
    public static PackOracleDomain ReadDepth912 { get; } = new(
        "ReadDepth912",
        """
        domain Depth
        uses http
        uses sqlite

        Zone: entity {
          Code: Text unique length(1, 8)
          Label: Text
        }

        Region: entity {
          Code: Text unique length(1, 8)
          Name: Text
          zone: owned Zone optional
          MakeZone: action -> Zone { create in zone { Code: "z1"  Label: "zl" } }
        }

        Product: entity {
          Code: Text unique length(1, 8)
          Name: Text
        }

        Customer: entity {
          Code: Text unique length(1, 8)
          Name: Text
          region: owned Region optional
          Flag: Boolean default(false)
          notes: many Note
          NoteNamed: action { assign Flag to any notes where product Name is "pn" }
          MakeRegion: action -> Region { create in region { Code: "r1"  Name: "n0" } }
          AddNote: action (c: Text, p: Product) -> Note { create in notes { Code: c  product: p } }
        }

        Note: entity {
          Code: Text unique length(1, 8)
          product: Product
        }

        Order: entity {
          Code: Text unique length(1, 8)
          Note: Text optional
          Flag: Boolean default(false)
          Every: Boolean default(false)
          customer: owned Customer optional
          lines: many Line via order
          MakeCustomer: action -> Customer { create in customer { Code: "c1"  Name: "cn" } }
          InRegion: policy { customer region Name is "n0" }
          TwoHop: action require InRegion { assign Note to customer region Name }
          ThreeHop: action { assign Note to customer region zone Label }
          AddLine: action (c: Text, p: Product) -> Line { create in lines { Code: c  product: p } }
          AnyNamed: action { assign Flag to any lines where product Name is "pn" }
          AllNamed: action { assign Every to all lines where product Name is "pn" }
          NoteNamed: action { invoke customer.NoteNamed }
          FanOut: action { for lines as l invoke l.Look }
          FanTags: action { for lines as l invoke l.AnyTag }
        }

        Line: entity {
          Code: Text unique length(1, 8)
          Seen: Text optional
          Flag: Boolean default(false)
          order: Order
          product: Product
          tags: many Tag
          Look: action { assign Seen to product Name }
          ShowCustomer: action { assign Seen to order customer region Name }
          AddTag: action (c: Text, p: Product) -> Tag { create in tags { Code: c  product: p } }
          AnyTag: action { assign Flag to any tags where product Name is "pn" }
        }

        Tag: entity {
          Code: Text unique length(1, 8)
          product: Product
        }
        """,
        "/api/orders");

    /// <summary>P-13b (ii) (#912): a <c>when</c> handler whose subscriber is registered in the same request. The new item starts in
    /// <c>Open</c>, the watcher's handler runs <c>invoke region.Copy</c>, and that reads the region's zone. It stays out of
    /// <see cref="All"/>.</summary>
    public static PackOracleDomain Watch912 { get; } = new(
        "Watch912",
        """
        domain Watch
        uses http
        uses sqlite

        Zone: entity {
          Code: Text unique length(1, 8)
          Label: Text
        }

        Region: entity {
          Code: Text unique length(1, 8)
          Name: Text
          zone: owned Zone optional
          MakeZone: action -> Zone { create in zone { Code: "z1"  Label: "zl" } }
          Copy: action { assign Name to zone Label }
        }

        Watcher: entity {
          Code: Text unique length(1, 8)
          region: owned Region optional
          items: many Item
          MakeRegion: action -> Region { create in region { Code: "r1"  Name: "n0" } }
          when items Open { invoke region.Copy }
          AddItem: action -> Item { create in items { Code: "i1" } }
        }

        Item: entity {
          Code: Text unique length(1, 8)
          watcher: Watcher via items
          Open: stage { Finish: action { transition to Done } }
          Done: stage { }
        }
        """,
        "/api/watchers");

    /// <summary>P-13b (ii) (#912): one row reached through two paths. <c>Go(p)</c> adds a line that watches <c>p</c> and closes the
    /// order's own product; when <c>p</c> is that product the handler reads <c>p.Zone</c>. It stays out of <see cref="All"/>.</summary>
    public static PackOracleDomain SameRow912 { get; } = new(
        "SameRow912",
        """
        domain Alias
        uses http
        uses sqlite

        Zone: entity {
          Code: Text unique length(1, 8)
          Label: Text
        }

        Product: entity {
          Code: Text unique length(1, 8)
          Name: Text
          zone: owned Zone optional
          MakeZone: action (code: Text) -> Zone { create in zone { Code: code  Label: code } }
          Copy: action { assign Name to zone Label }
          Open: stage { Close: action { transition to Closed } }
          Closed: stage { }
        }

        Order: entity {
          Code: Text unique length(1, 8)
          product: owned Product optional
          lines: many Line via order
          MakeProduct: action -> Product { create in product { Code: "p1"  Name: "n0" } }
          Go: action (p: Product) { create in lines { Code: "l1"  product: p }  invoke product.Close }
        }

        Line: entity {
          Code: Text unique length(1, 8)
          order: Order
          product: Product
          when product Closed { invoke product.Copy }
        }
        """,
        "/api/orders");

    /// <summary>P-13b (ii) (#912): two parameters of one entity type. <c>Both</c> adds a line for each product; a new line's
    /// entry effect copies its own product's name, so a host that bound both parameters to one local answers the same name twice.
    /// It stays out of <see cref="All"/>.</summary>
    public static PackOracleDomain TwoParams912 { get; } = new(
        "TwoParams912",
        """
        domain Pair
        uses http
        uses sqlite

        Product: entity {
          Code: Text unique length(1, 8)
          Name: Text
        }

        Order: entity {
          Code: Text unique length(1, 8)
          lines: many Line via order
          Both: action (a: Product, b: Product) {
            create in lines { Code: "l1"  product: a }
            create in lines { Code: "l2"  product: b }
          }
        }

        Line: entity {
          Code: Text unique length(1, 8)
          Seen: Text optional
          order: Order
          product: Product
          Fresh: stage { entry { assign Seen to product Name } }
        }
        """,
        "/api/orders");

    /// <summary>P-13b (ii) (#912): generated locals are unique by construction. <c>Customer</c> then its <c>Region</c> and the
    /// navigation <c>CustomerRegion</c> would both name a local <c>entityCustomerRegion</c> if locals were just path
    /// concatenations; one action reads through both. It stays out of <see cref="All"/>.</summary>
    public static PackOracleDomain Names912 { get; } = new(
        "Names912",
        """
        domain Names
        uses http
        uses sqlite

        Zone: entity {
          Code: Text unique length(1, 8)
          Label: Text
        }

        Region: entity {
          Code: Text unique length(1, 8)
          zone: owned Zone optional
          MakeZone: action -> Zone { create in zone { Code: "z1"  Label: "first" } }
        }

        Customer: entity {
          Code: Text unique length(1, 8)
          region: owned Region optional
          MakeRegion: action -> Region { create in region { Code: "r1" } }
        }

        Other: entity {
          Code: Text unique length(1, 8)
          zone: Zone optional
          MakeZone: action -> Zone { create in zone { Code: "z2"  Label: "second" } }
        }

        Order: entity {
          Code: Text unique length(1, 8)
          Note: Text optional
          Second: Text optional
          customer: owned Customer optional
          customerRegion: owned Other optional
          MakeCustomer: action -> Customer { create in customer { Code: "c1" } }
          MakeOther: action -> Other { create in customerRegion { Code: "x1" } }
          Both: action {
            assign Note to customer region zone Label
            assign Second to customerRegion zone Label
          }
        }
        """,
        "/api/orders");

    /// <summary>L-08b half 2 (#683, #687): a root with one required value of each type a POST can omit. Its DSL carries no
    /// <c>uses</c> line of its own, so <see cref="RequiredOmittedSimulatorDsl"/> (the same entity without the host
    /// libraries) runs on the simulator unchanged. It stays out of <see cref="All"/>.</summary>
    public static PackOracleDomain RequiredOmitted683 { get; } = new(
        "RequiredOmitted683",
        """
        domain Required
        uses http
        uses sqlite
        uses temporal

        Grade: enum { Low, High }

        Gizmo: entity {
          Label: Text
          Qty: Number
          Active: Boolean
          Born: Date
          Tier: Grade
          Price: Decimal
          Stamp: DateTime
          Clock: Time
          Span: Duration
        }
        """,
        "/api/gizmos");

    /// <summary>L-15a (#683): <c>Order</c> has a required link and no parent, so it is an aggregate root whose
    /// <c>Create</c> answers Required unless the link is supplied, which a POST body cannot do. It stays out of
    /// <see cref="All"/>.</summary>
    public static PackOracleDomain RequiredLink683 { get; } = new(
        "RequiredLink683",
        """
        domain Shop
        uses http
        uses sqlite

        Customer: entity {
          Name: Text
        }

        Order: entity {
          Number: Text
          customer: Customer
        }
        """,
        "/api/orders");

    /// <summary>The simulator's side of <see cref="RequiredOmitted683"/>: the same entity, no host libraries.</summary>
    public const string RequiredOmittedSimulatorDsl = """
        domain Required
        uses temporal

        Grade: enum { Low, High }

        Gizmo: entity {
          Label: Text
          Qty: Number
          Active: Boolean
          Born: Date
          Tier: Grade
          Price: Decimal
          Stamp: DateTime
          Clock: Time
          Span: Duration
        }
        """;

    /// <summary>E-10a (#923): a required DateTime, Time and Duration on a creatable root. Stays out of <see cref="All"/>.</summary>
    public static PackOracleDomain Temporal923 { get; } = new(
        "Temporal923",
        """
        domain Shop
        uses temporal
        uses sqlite
        uses http

        Thing: entity {
          Name: Text unique
          Dt: DateTime
          Tm: Time
          Du: Duration
        }
        """,
        "/api/things");

    /// <summary>E-10a (#924): an enum and an entity named like C# keywords. Stays out of <see cref="All"/>.</summary>
    public static PackOracleDomain KeywordNames924 { get; } = new(
        "KeywordNames924",
        """
        domain Kw
        uses sqlite
        uses http

        event: enum { A, B }

        Thing: entity {
          Name: Text unique
          Kind: event
        }

        class: entity {
          Name: Text unique
        }
        """,
        "/api/things");

    /// <summary>P-12a (#687 row P-12): natural keys of each route-key type (Text, Number, Date), a shadow-key
    /// entity, and a keyed entity with a scalar of several types plus a stage for the body-member contract.
    /// It stays out of <see cref="All"/>.</summary>
    public static PackOracleDomain RouteKeys687 { get; } = new(
        "RouteKeys687",
        """
        domain Keys
        uses http
        uses sqlite
        uses temporal

        Grade: enum { Low, High }

        Customer: entity {
          Email: Text unique
        }

        Ticket: entity {
          Seq: Number unique
        }

        Event: entity {
          Day: Date unique
        }

        Note: entity {
          Body: Text
        }

        Profile: entity {
          Handle: Text unique
          Age: Number
          Active: Boolean
          Born: Date
          Tier: Grade
          Price: Decimal
          Draft: stage { Publish: action { transition to Live } }
          Live: stage {}
        }
        """,
        "/api/customers");

    /// <summary>P-12a fix round: an OPTIONAL unique key is still the route key. <c>Day</c> (Date) is a <c>DateOnly?</c> key, so the
    /// host must compile with it (a create of it answers 409 Unique, which this slice does not touch, so only the compile is tested). An optional unique Text key cannot be a fixture: the generated host dies at boot
    /// (<c>NullReferenceException</c> in the module's <c>Create</c> from the seed), in the module and outside this slice.
    /// Date, not DateTime or Time: a required DateTime/Time/Duration host member does not compile yet (#923).
    /// It stays out of <see cref="All"/>.</summary>
    public static PackOracleDomain OptionalKeys687 { get; } = new(
        "OptionalKeys687",
        """
        domain Optional
        uses http
        uses sqlite
        uses temporal

        Slot: entity {
          Name: Text
          Day: Date unique optional
        }
        """,
        "/api/slots");

    public static IReadOnlyList<PackOracleDomain> All { get; } =
        [Inventory588, Logistics559, Warehouse620, Inventory626, Desk658, Tally653, Fulfillment675];

    /// <summary>The PR-tier boot set (OQ-10): exactly Inventory588 and Warehouse620.</summary>
    public static IReadOnlyList<PackOracleDomain> Subset { get; } = [Inventory588, Warehouse620];

    /// <summary>Every fixture that boots.</summary>
    public static IReadOnlyList<PackOracleDomain> Full { get; } = All.Where(d => d.Boots).ToList();
}