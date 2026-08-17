using Microsoft.Extensions.Logging;

namespace LlmHarness.Api.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public FileLoggerProvider(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(
            new FileStream(
                filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(categoryName, _sync, _writer, () => _disposed);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }

    private sealed class FileLogger(
        string categoryName,
        object sync,
        StreamWriter writer,
        Func<bool> isDisposed) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || isDisposed())
            {
                return;
            }

            var message = formatter(state, exception);
            var line = $"{DateTimeOffset.UtcNow:O} [{logLevel}] {categoryName}: {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lock (sync)
            {
                if (!isDisposed())
                {
                    writer.WriteLine(line);
                }
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
