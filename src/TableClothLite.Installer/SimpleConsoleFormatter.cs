using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

public sealed class SimpleConsoleFormatter : ConsoleFormatter
{
    public SimpleConsoleFormatter()
        : base(nameof(SimpleConsoleFormatter))
    { }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        // 예외가 있는 경우 예외 메시지 출력
        if (logEntry.Exception is not null)
        {
            textWriter.WriteLine(logEntry.Exception);
        }

        // 로그 메시지만 출력
        textWriter.WriteLine(logEntry.Formatter(logEntry.State, logEntry.Exception));
    }
}