using System.Reflection;
using Poly.Reflection;

namespace Poly;

public static class Instrumentation
{
    public static readonly ActivitySource Source = new(
        nameof(Poly),
        Assembly.GetExecutingAssembly().GetAssemblyVersionString()
        );

    public static Activity? StartActivity(
        [CallerMemberName] string? activityName = default,
        ActivityKind activityKind = ActivityKind.Internal)
    {
        Guard.IsNotNull(activityName);

        return Source.StartActivity(activityName, activityKind)?.Start();
    }

    public static void StartActivity(
        Action action,
        [CallerMemberName] string? activityName = default,
        ActivityKind activityKind = ActivityKind.Internal)
    {
        using var _ = StartActivity(activityName, activityKind);

        action();
    }

    public static T StartActivity<T>(
        Func<T> action,
        [CallerMemberName] string? activityName = default,
        ActivityKind activityKind = ActivityKind.Internal)
    {
        using var _ = StartActivity(activityName, activityKind);

        return action();
    }

    public static Activity? AddEvent(
        [CallerMemberName] string? eventName = default)
    {
        Guard.IsNotNull(eventName);

        return Activity.Current?.AddEvent(new(eventName));
    }
}