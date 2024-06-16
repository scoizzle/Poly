namespace Poly;

public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
    None = 6
}

public record struct LogEntry(
    LogLevel Level,
    DateTime EventDateTime,
    FormattableString Message,
    Exception? Exception = default)
{
    public LogEntry(LogLevel level, FormattableString message) : this(level, DateTime.UtcNow, message) { }
}

public static class Log
{
    internal delegate void LogEntryHandler(in LogEntry entry);
    private static LogLevel s_currentLogLevel = LogLevel.None;
    private static LogEntryHandler s_entryHandler { get; set; } = static (in LogEntry _) => { };

    static Log()
    {
        var envLogLevel = Environment.GetEnvironmentVariable("LOGGING__LOGLEVEL__POLY");

        if (Enum.TryParse<LogLevel>(envLogLevel, out var loggingLevel))
            s_currentLogLevel = loggingLevel;
    }

    public static void WriteToConsole()
    {
        s_entryHandler = ConsoleEntryHandler;
    }

    public static void SetLogLevel(LogLevel level)
    {
        s_currentLogLevel = level;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write(in LogEntry entry)
    {
        if (s_currentLogLevel > entry.Level)
            return;

        s_entryHandler(in entry);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write(LogLevel level, FormattableString message)
    {
        LogEntry logEntry = new(level, DateTime.UtcNow, message);

        Write(in logEntry);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write(LogLevel level, FormattableString message, DateTime eventDateTime)
    {
        LogEntry logEntry = new(level, DateTime.UtcNow, message);

        Write(in logEntry);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write(LogLevel level, FormattableString message, Exception? exception)
    {
        LogEntry logEntry = new(level, DateTime.UtcNow, message, exception);

        Write(in logEntry);
    }

    public static void Trace(FormattableString message) => Write(LogLevel.Trace, message);
    public static void Debug(FormattableString message) => Write(LogLevel.Debug, message);
    public static void Info(FormattableString message) => Write(LogLevel.Information, message);
    public static void Warning(FormattableString message) => Write(LogLevel.Warning, message);
    public static void Error(FormattableString message, Exception? error = default) => Write(LogLevel.Error, message, error);
    public static void Error(Exception error) => Write(LogLevel.Error, $"", error);
    public static void Critical(FormattableString message) => Write(LogLevel.Critical, message);

    private static void ConsoleEntryHandler(in LogEntry logEntry)
    {
        StringBuilder builder = new();

        string levelDisplay = logEntry.Level switch
        {
            LogLevel.Trace => "[TRACE] ",
            LogLevel.Debug => "[DEBUG] ",
            LogLevel.Information => "[INFO]  ",
            LogLevel.Warning => "[WARN]  ",
            LogLevel.Error => "[ERROR] ",
            LogLevel.Critical => "[CRIT]  ",
            _ => throw new InvalidOperationException(),
        };

        Span<char> dateTimeDisplay = stackalloc char[42];
        if (!logEntry.EventDateTime.TryFormat(dateTimeDisplay.Slice(1), out var charsWritten, "s"))
            throw new Exception();

        dateTimeDisplay[0] = '[';
        dateTimeDisplay[40] = ']';
        dateTimeDisplay[41] = ' ';

        builder.Append(levelDisplay);
        builder.Append(dateTimeDisplay);
        builder.AppendFormat(logEntry.Message.Format, logEntry.Message.GetArguments());

        Console.WriteLine(builder.ToString());
    }
}