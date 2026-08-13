using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace JamaaTech.Smpp.Net.Lib.Compat;

/// <summary>
/// Socket APIs that exist on net5+ but not on netstandard2.1, kept in one place so the
/// call sites stay free of conditional compilation.
/// </summary>
internal static class SocketCompat
{
  /// <summary>
  /// Connects to <paramref name="remoteEndPoint"/>, honouring <paramref name="cancellationToken"/>.
  /// </summary>
  public static async Task ConnectAsync(Socket socket, EndPoint remoteEndPoint,
    CancellationToken cancellationToken)
  {
    if (socket == null) throw new ArgumentNullException(nameof(socket));
    if (remoteEndPoint == null) throw new ArgumentNullException(nameof(remoteEndPoint));

#if NET5_0_OR_GREATER
    await socket.ConnectAsync(remoteEndPoint, cancellationToken).ConfigureAwait(false);
#else
    cancellationToken.ThrowIfCancellationRequested();

    var connect = socket.ConnectAsync(remoteEndPoint);
    if (!cancellationToken.CanBeCanceled)
    {
      await connect.ConfigureAwait(false);
      return;
    }

    // A pending connect cannot be aborted other than by tearing the socket down, which
    // is what net5's own cancellable overload does.
    var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    using (cancellationToken.Register(state => ((TaskCompletionSource<bool>)state).TrySetResult(true), cancelled))
    {
      if (await Task.WhenAny(connect, cancelled.Task).ConfigureAwait(false) != connect)
      {
        try { socket.Dispose(); }
        catch (ObjectDisposedException) { }
        throw new OperationCanceledException(cancellationToken);
      }
    }

    await connect.ConfigureAwait(false);
#endif
  }
}
