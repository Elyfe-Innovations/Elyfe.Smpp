using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;

namespace Common.Logging;

/// <summary>
/// Adapter to bridge Common.Logging API to Microsoft.Extensions.Logging
/// This allows legacy code using Common.Logging to work with modern Microsoft.Extensions.Logging
/// </summary>
public interface ILog
{
    bool IsTraceEnabled { get; }
    bool IsDebugEnabled { get; }
    bool IsInfoEnabled { get; }
    bool IsWarnEnabled { get; }
    bool IsErrorEnabled { get; }
    bool IsFatalEnabled { get; }

    void Trace(object message);
    void Trace(object message, Exception exception);
    void TraceFormat(string format, params object[] args);
    void TraceFormat(string format, Exception exception, params object[] args);

    void Debug(object message);
    void Debug(object message, Exception exception);
    void DebugFormat(string format, params object[] args);
    void DebugFormat(string format, Exception exception, params object[] args);

    void Info(object message);
    void Info(object message, Exception exception);
    void InfoFormat(string format, params object[] args);
    void InfoFormat(string format, Exception exception, params object[] args);

    void Warn(object message);
    void Warn(object message, Exception exception);
    void WarnFormat(string format, params object[] args);
    void WarnFormat(string format, Exception exception, params object[] args);

    void Error(object message);
    void Error(object message, Exception exception);
    void ErrorFormat(string format, params object[] args);
    void ErrorFormat(string format, Exception exception, params object[] args);

    void Fatal(object message);
    void Fatal(object message, Exception exception);
    void FatalFormat(string format, params object[] args);
    void FatalFormat(string format, Exception exception, params object[] args);
}

/// <summary>
/// LogManager that creates ILog instances backed by Microsoft.Extensions.Logging
/// </summary>
public static class LogManager
{
    private static ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    public static void SetLoggerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public static ILog GetLogger(Type type)
    {
        return new MicrosoftExtensionsLogAdapter(_loggerFactory.CreateLogger(type));
    }

    public static ILog GetLogger(string name)
    {
        return new MicrosoftExtensionsLogAdapter(_loggerFactory.CreateLogger(name));
    }
}

/// <summary>
/// Internal adapter that wraps Microsoft.Extensions.Logging.ILogger to implement Common.Logging.ILog
/// </summary>
internal class MicrosoftExtensionsLogAdapter : ILog
{
    private readonly Microsoft.Extensions.Logging.ILogger _logger;

    public MicrosoftExtensionsLogAdapter(Microsoft.Extensions.Logging.ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsTraceEnabled => _logger.IsEnabled(LogLevel.Trace);
    public bool IsDebugEnabled => _logger.IsEnabled(LogLevel.Debug);
    public bool IsInfoEnabled => _logger.IsEnabled(LogLevel.Information);
    public bool IsWarnEnabled => _logger.IsEnabled(LogLevel.Warning);
    public bool IsErrorEnabled => _logger.IsEnabled(LogLevel.Error);
    public bool IsFatalEnabled => _logger.IsEnabled(LogLevel.Critical);

    public void Trace(object message) => _logger.LogTrace("{Message}", message);
    public void Trace(object message, Exception exception) => _logger.LogTrace(exception, "{Message}", message);
    public void TraceFormat(string format, params object[] args) => _logger.LogTrace(format, args);
    public void TraceFormat(string format, Exception exception, params object[] args) => _logger.LogTrace(exception, format, args);

    public void Debug(object message) => _logger.LogDebug("{Message}", message);
    public void Debug(object message, Exception exception) => _logger.LogDebug(exception, "{Message}", message);
    public void DebugFormat(string format, params object[] args) => _logger.LogDebug(format, args);
    public void DebugFormat(string format, Exception exception, params object[] args) => _logger.LogDebug(exception, format, args);

    public void Info(object message) => _logger.LogInformation("{Message}", message);
    public void Info(object message, Exception exception) => _logger.LogInformation(exception, "{Message}", message);
    public void InfoFormat(string format, params object[] args) => _logger.LogInformation(format, args);
    public void InfoFormat(string format, Exception exception, params object[] args) => _logger.LogInformation(exception, format, args);

    public void Warn(object message) => _logger.LogWarning("{Message}", message);
    public void Warn(object message, Exception exception) => _logger.LogWarning(exception, "{Message}", message);
    public void WarnFormat(string format, params object[] args) => _logger.LogWarning(format, args);
    public void WarnFormat(string format, Exception exception, params object[] args) => _logger.LogWarning(exception, format, args);

    public void Error(object message) => _logger.LogError("{Message}", message);
    public void Error(object message, Exception exception) => _logger.LogError(exception, "{Message}", message);
    public void ErrorFormat(string format, params object[] args) => _logger.LogError(format, args);
    public void ErrorFormat(string format, Exception exception, params object[] args) => _logger.LogError(exception, format, args);

    public void Fatal(object message) => _logger.LogCritical("{Message}", message);
    public void Fatal(object message, Exception exception) => _logger.LogCritical(exception, "{Message}", message);
    public void FatalFormat(string format, params object[] args) => _logger.LogCritical(format, args);
    public void FatalFormat(string format, Exception exception, params object[] args) => _logger.LogCritical(exception, format, args);
}
