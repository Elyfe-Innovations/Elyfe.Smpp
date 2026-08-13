using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JamaaTech.Smpp.Net.Lib.Logging;

/// <summary>
/// Ambient <see cref="ILoggerFactory"/> for the parts of the library that are constructed
/// internally and so have no constructor a caller can inject into. Types that <em>are</em>
/// caller-constructed should take an <see cref="ILoggerFactory"/> instead and fall back here.
/// </summary>
/// <remarks>
/// The default is <see cref="NullLoggerFactory"/>: a library must not format log messages
/// nobody has asked for. Call <see cref="SetLoggerFactory"/> once during application startup.
/// </remarks>
public static class SmppLog
{
  private static ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

  // Bumped on every SetLoggerFactory so loggers handed out earlier — including the
  // static readonly fields initialized at type load — pick the new factory up.
  private static int _generation;

  internal static ILoggerFactory Factory => _loggerFactory;

  internal static int Generation => Volatile.Read(ref _generation);

  /// <summary>
  /// Routes the library's diagnostics to <paramref name="loggerFactory"/>. Loggers already
  /// handed out by <see cref="For(Type)"/> switch over to it.
  /// </summary>
  public static void SetLoggerFactory(ILoggerFactory loggerFactory)
  {
    _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    Interlocked.Increment(ref _generation);
  }

  /// <summary>Gets a logger for <typeparamref name="T"/> bound to the ambient factory.</summary>
  public static ILogger For<T>() => For(typeof(T));

  /// <summary>Gets a logger for <paramref name="type"/> bound to the ambient factory.</summary>
  public static ILogger For(Type type)
  {
    if (type == null) throw new ArgumentNullException(nameof(type));
    return new AmbientLogger(TypeNameHelper(type));
  }

  private static string TypeNameHelper(Type type)
  {
    var name = type.FullName ?? type.Name;
    // Strip the arity suffix so generic types log under a readable category.
    var tick = name.IndexOf('`');
    return tick < 0 ? name : name.Substring(0, tick);
  }

  /// <summary>
  /// An <see cref="ILogger"/> that resolves through whichever factory is current, so a
  /// logger captured in a static field before <see cref="SetLoggerFactory"/> still works.
  /// </summary>
  private sealed class AmbientLogger : ILogger
  {
    private readonly string _categoryName;
    private ILogger _inner = NullLogger.Instance;
    private int _generation = -1;

    internal AmbientLogger(string categoryName) => _categoryName = categoryName;

    private ILogger Current
    {
      get
      {
        var generation = Generation;
        // Benign race: concurrent callers may each build a logger for the same
        // generation, and every one of them is equivalent.
        if (Volatile.Read(ref _generation) != generation)
        {
          _inner = Factory.CreateLogger(_categoryName);
          Volatile.Write(ref _generation, generation);
        }

        return _inner;
      }
    }

    public IDisposable BeginScope<TState>(TState state) => Current.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => Current.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
      Func<TState, Exception, string> formatter)
      => Current.Log(logLevel, eventId, state, exception, formatter);
  }
}
