#nullable enable

using System.Runtime.CompilerServices;

namespace Poly;

public static class Activities
{
    public static readonly ActivitySource Source = new(nameof(Poly));

    public static Activity? AddEvent(
        this Activity? activity,
        [CallerMemberName] string? eventName = default)
    {
        Guard.IsNotNull(eventName);
        
        return activity?.AddEvent(new(eventName));
    }
}