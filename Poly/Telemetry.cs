namespace Poly;

internal record struct TelemetryEventScopeTracer(
    string File,
    string Method,
    int Line,
    long EntryTimestamp) : IDisposable
{
    public void Dispose()
    {
        var duration = Stopwatch.GetElapsedTime(EntryTimestamp);
        var telemetryEvent = new TelemetryEvent(Method, duration);

        Telemetry.Report(in telemetryEvent);
    }
}

public record struct TelemetryEvent(string Method, TimeSpan Duration);

public static class Telemetry
{
    public static IDisposable BeginEvent(
        [CallerFilePath] string? file = default,
        [CallerMemberName] string? method = default,
        [CallerLineNumber] int? line = default)
    {
        var scopeTracer = new TelemetryEventScopeTracer(
            file ?? "<unknown>",
            method ?? "<unknown>",
            line ?? 0,
            Stopwatch.GetTimestamp());

        return scopeTracer;
    }

    public static void Report(in TelemetryEvent telemetryEvent)
    {
        LogEntry logEntry = new(LogLevel.Trace, $"{telemetryEvent.Method:40} {telemetryEvent.Duration.Microseconds}us");

        Log.Write(in logEntry);
    }
}