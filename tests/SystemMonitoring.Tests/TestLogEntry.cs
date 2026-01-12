namespace Microsoft.Extensions.Logging;

internal record TestLogEntry(LogLevel LogLevel, string Message, Exception? Exception = default, EventId EventId = default);
