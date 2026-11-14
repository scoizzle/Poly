namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-04a: the one domain shape every initial-stage entry test uses. A <c>Holder</c> owns <c>Kid</c>s (and a
/// <c>Leaf</c> collection); the <c>Kid</c>'s initial stage <c>Start</c> carries the entry block under test, so one
/// template covers every entry form on the same two hosts. A bare <c>Create</c> of the <c>Kid</c> and the holder's
/// <c>Make</c> action (a <c>create in kids</c>) are the two doors.
/// </summary>
internal static class InitialEntryDomains {
    /// <summary>
    /// <paramref name="entry"/> is the body of <c>Start: stage { entry { … } }</c>. <paramref name="kidMembers"/>
    /// and <paramref name="holderMembers"/> add members to the two entities.
    /// <c>Cap</c> is a plain defaulted property, so an entry can assign a run-time value past a range that analysis
    /// does not see (<c>assign Score to Cap</c> breaks <c>range(0, 10)</c>).
    /// </summary>
    public static string Dsl(string entry, string kidMembers = "", string holderMembers = "", string entities = "") => $$"""
        domain Entry
        Holder: entity {
          Total: Number range(0, 5) default(0)
          kids: many Kid
          leaves: many Leaf
          Make: action { create in kids { } }
          Bump: action { assign Total to Total + 1 }
          {{holderMembers}}
        }
        Leaf: entity {
          Amount: Number range(0, 10) default(1)
          owner: Holder via leaves optional
        }
        Kid: entity {
          holder: Holder via kids optional
          Cap: Number default(100)
          Score: Number range(0, 10) default(0)
          Count: Number default(0)
          Never: policy { Count > 100 }
          Fails: action require Never { assign Count to Count + 1 }
          Works: action { assign Count to Count + 1 }
          {{kidMembers}}
          Start: stage { entry { {{entry}} } }
        }
        {{entities}}
        """;
}