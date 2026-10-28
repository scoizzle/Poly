namespace Poly.Demo.Shared.Services;

public sealed record PolySample(string Name, string Source);

public static class PolyIdeSamples {
    public const string Catalog = """
        domain Catalog
        uses temporal
        uses storage
        uses sqlite
        uses http

        Item: entity {
          Name: Text
          Qty: Number
          Bump: action { assign Qty to Qty + 1 }
        }
        """;

    public const string Orders = """
        domain Orders
        uses temporal
        uses storage

        Customer: entity {
          Name: Text
          Email: Text unique
          orders: many Order
        }

        Order: entity {
          Total: Number range(0, )
          Status: Text
          PositiveTotal: policy { Total > 0 }

          Draft: stage {
            Submit: action
              require PositiveTotal
            {
              transition to Active
            }
          }
          Active: stage {
            entry { assign Status to "active" }
            exit  { assign Status to "archived" }
          }
          Done: stage { }
        }
        """;

    public const string Smoke = """
        domain Parking

        Permit: entity {
          Plate: Text
        }
        """;

    public static IReadOnlyList<PolySample> All { get; } = [
        new("Catalog · sqlite + http", Catalog),
        new("Orders · round-trip", Orders),
        new("Smoke · smallest domain", Smoke),
    ];
}