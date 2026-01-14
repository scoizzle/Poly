namespace Poly.Introspection;

public static class MemberExtensions {
    /// <summary>
    /// Filters members by name.
    /// </summary>
    /// <typeparam name="T">The type of member.</typeparam>
    /// <param name="members">The members to filter.</param>
    /// <param name="name">The name to match.</param>
    /// <returns>Members with the specified name.</returns>
    public static IEnumerable<T> WithName<T>(this IEnumerable<T> members, string name)
        where T : ITypeMember {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        
        return members.Where(m => m.Name == name);
    }
}