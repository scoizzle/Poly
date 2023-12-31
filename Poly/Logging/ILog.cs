using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Poly.Reflection;
using Poly.Serialization;

namespace Poly;

public enum LogLevel
{
    None,
    Critical,
    Warning,
    Information,
    Event,
    Debug,
    Trace    
}

public sealed class LogMessage : IDisposable
{
    private LogMessage(
        LogLevel level,
        string message,
        object properties)
    {
        Level = level;
        Message = message;
        Properties = properties;
    }

    public LogLevel Level { get; private set; }

    public string Message { get; private set; }

    public object? Properties { get; private set; }

    public void Dispose()
    {
        Return(this);
    }

    private static readonly ConcurrentQueue<LogMessage> _pool = new();

    public static LogMessage Rent(LogLevel logLevel, string message, object properties)
    {
        if (_pool.TryDequeue(out var result))
        {
            result.Level = logLevel;
            result.Message = message;
            result.Properties = properties;

            return result;
        }

        return new LogMessage(logLevel, message, properties);
    }

    public static void Return(LogMessage message)
    {
        Guard.IsNotNull(message);

        message.Level = LogLevel.None;
        message.Message = string.Empty;
        message.Properties = default;

        _pool.Enqueue(message);
    }
}

public interface ILogListener
{
    public Task HandleAsync(LogMessage message, CancellationToken cancellationToken = default);
}

public class LogToConsoleListener : ILogListener, IDisposable
{
    readonly Channel<StringBuilder> _channel = Channel.CreateUnbounded<StringBuilder>(new UnboundedChannelOptions { AllowSynchronousContinuations = false });
    readonly CancellationTokenSource _cancellationSource = new();

    public LogToConsoleListener() => _ = PrintToConsole(_cancellationSource.Token);

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        _cancellationSource.Cancel();
    }

    public async Task HandleAsync(LogMessage message, CancellationToken cancellationToken = default)
    {
        if (_cancellationSource.IsCancellationRequested)
            ThrowHelper.ThrowObjectDisposedException(string.Empty);

        var builder = new StringBuilder()
            .Append(message.Level).Append('\t').Append(message.Message).AppendLine();

        if (message.Properties is not null) 
        {
            var typeInterface = TypeInterfaceRegistry.Get(message.Properties.GetType());
            Guard.IsNotNull(typeInterface);

            var writer = new JsonWriter { Text = builder };
            
            typeInterface!.SerializeObject(writer, message.Properties);
        }
            
        await _channel.Writer.WriteAsync(builder, cancellationToken);
    }

    private async Task PrintToConsole(CancellationToken cancellationToken)
    {
        var reader = _channel.Reader;

        while (!cancellationToken.IsCancellationRequested)
        {
            var dataAvailable = await reader.WaitToReadAsync(cancellationToken);

            if (!dataAvailable) {
                Dispose();
                return;
            }

            while (reader.TryRead(out var builder))
            {
                Console.WriteLine(builder.ToString());
            }
        }
    }
}

public class _Log 
{
    readonly IList<ILogListener> _listeners;

    public _Log() { 
        _listeners = new List<ILogListener>();
    }

    public bool TryRegisterListener<TListener>(TListener? listener = default) where TListener : ILogListener, new()
    {
        if (_listeners.OfType<TListener>().Any())
            return false;

        listener ??= new();

        _listeners.Add(listener);
        return true;
    }

    Task HandleMessage(
        LogMessage message, 
        CancellationToken cancellationToken = default)
    {
        return Parallel.ForEachAsync(
            _listeners,
            cancellationToken,
            (listener, cancellationToken) => new(listener.HandleAsync(message, cancellationToken).IgnoreErrors())
        );
    }

    private async void OnEventRecordDisposing(DisposableEventRecord record)
    {
        using var message = LogMessage.Rent(LogLevel.Event, record.Name, new { Duration = record.Stopwatch.Elapsed });

        await HandleMessage(message);
    }
    
    public record struct DisposableEventRecord(
        _Log Log,
        string Name) : IDisposable
    {
        public readonly Stopwatch Stopwatch = Stopwatch.StartNew();

        public readonly void Dispose()
        {
            Stopwatch.Stop();

            Log.OnEventRecordDisposing(this);
        }
    }

    public static void Test([CallerMemberName] string? eventName = default)
    {
        Guard.IsNotNull(eventName);
    }

    public DisposableEventRecord EventStarted([CallerMemberName] string? eventName = default)
    {
        Guard.IsNotNullOrEmpty(eventName);

        return new(this, eventName);
    }
}