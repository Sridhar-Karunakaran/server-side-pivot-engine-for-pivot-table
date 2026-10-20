using Microsoft.Extensions.Logging;

namespace PivotController.Configuration.Services
{
    /// <summary>
    /// Wraps the console logger with a runtime-adjustable minimum level, making
    /// General:MinimumLogLevel observable in the local console (spec §1.3): messages below
    /// the configured level are suppressed immediately — no restart required.
    /// </summary>
    public class MinimumLevelLoggerProvider : ILoggerProvider
    {
        private volatile int _minimumLevel;

        public MinimumLevelLoggerProvider()
        {
            _minimumLevel = (int)LogLevel.Information;
        }

        public LogLevel MinimumLevel
        {
            get => (LogLevel)_minimumLevel;
            set => _minimumLevel = (int)value;
        }

        public ILogger CreateLogger(string categoryName) => new FilteringLogger(this, categoryName);

        public void Dispose() { }

        private sealed class FilteringLogger : ILogger
        {
            private readonly MinimumLevelLoggerProvider _owner;
            private readonly string _categoryName;

            public FilteringLogger(MinimumLevelLoggerProvider owner, string categoryName)
            {
                _owner = owner;
                _categoryName = categoryName;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= _owner.MinimumLevel;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                // Drop messages below the configured General:MinimumLogLevel before the
                // framework's console provider can write them.
                if (!IsEnabled(logLevel))
                {
                    return;
                }
                Console.WriteLine($": {_categoryName}[{eventId}] {formatter(state, exception)}"
                    + (exception != null ? Environment.NewLine + exception : string.Empty));
            }
        }
    }
}