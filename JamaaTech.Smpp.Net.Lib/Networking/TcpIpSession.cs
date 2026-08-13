/************************************************************************
 * Copyright (C) 2007 Jamaa Technologies
 *
 * This file is part of Jamaa SMPP Library.
 *
 * Jamaa SMPP Library is free software. You can redistribute it and/or modify
 * it under the terms of the Microsoft Reciprocal License (Ms-RL)
 *
 * You should have received a copy of the Microsoft Reciprocal License
 * along with Jamaa SMPP Library; See License.txt for more details.
 *
 * Author: Benedict J. Tesha
 * benedict.tesha@jamaatech.com, www.jamaatech.com
 *
 ************************************************************************/

using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using JamaaTech.Smpp.Net.Lib.Compat;
using JamaaTech.Smpp.Net.Lib.Logging;
using Microsoft.Extensions.Logging;

namespace JamaaTech.Smpp.Net.Lib.Networking
{
    /// <summary>
    /// Encapsulates an open TCP/IP session with a remote host.
    /// </summary>
    public class TcpIpSession
    {
        private static readonly ILogger Logger = SmppLog.For(typeof(TcpIpSession));

        #region Variables
        /// <summary>
        /// The underlying socket used for sending and receiving data
        /// </summary>
        protected Socket vSocket;
        /// <summary>
        /// The type of session this instance represents
        /// </summary>
        private SessionType vSessionType;
        /// <summary>
        /// True if the connection is open or false otherwise
        /// </summary>
        private bool vIsAlive;
        /// <summary>
        /// Lock this variable when access the vIsAlive variable
        /// </summary>
        private object vSyncRoot;
        /// <summary>
        /// Properties for this instance
        /// </summary>
        private TcpIpSessionProperties vProperties;
        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new instance of TcpIpSession
        /// </summary>
        /// <param name="socket">A TCP/IP socket to be used by for sending and receiving data</param>
        /// <param name="sessionType">The type of session this instance represents</param>
        internal TcpIpSession(Socket socket, SessionType sessionType)
        {
            if (socket == null) { throw new ArgumentNullException(nameof(socket)); }
            vSocket = socket;
            vSessionType = sessionType;
            vSyncRoot = new object();
        }
        #endregion

        #region Events
        public event EventHandler<TcpIpSessionClosedEventArgs> SessionClosed;

        public event EventHandler<TcpIpSessionExceptionEventArgs> SessionException;
        #endregion

        #region Properties
        public TcpIpSessionProperties Properties
        {
            get { return vProperties; }
        }

        public SessionType SessionType
        {
            get { return vSessionType; }
        }

        public bool IsAlive
        {
            get { lock (vSyncRoot) { return vIsAlive; } }
        }

        #endregion

        #region Methods
        #region Helper Methods
        /// <summary>
        /// Handles exceptions thrown during a call to <see cref="OpenClientSession"/> method
        /// </summary>
        /// <param name="exception">The exception to handle</param>
        private static void HandleConnectionException(SocketException exception)
        {
            /*
             * This method checks for conditions that are likely to occur
             * when connecting to a remote host.
             * Such conditions might only be temporary and due to the following situations:
             * - The remote host has no specified port in a listening state
             * - The specified host is not reachable
             * - The network is down
             * - The route to the destination network is not known
             * - The route to the destination host is not known
             * - Connection request to the remote host timed out
             */
            switch (exception.SocketErrorCode)
            {
                case SocketError.NetworkDown: //The network is down
                case SocketError.NetworkUnreachable: //The network is unreachable
                case SocketError.TimedOut: //Connection timed out
                case SocketError.ConnectionRefused: //No specified port is in a listening state on the remote machine
                case SocketError.HostDown: //The remote host is down
                case SocketError.HostUnreachable: //The remote host is unreachable
                case SocketError.HostNotFound: //Host not found, no such host is known
                    //Wrap this exception in a TcpIpConnectionException exception and throw
                    throw new TcpIpConnectionException(exception);
                default:
                    //For any other error code not listed, throw TcpIpException exception
                    TcpIpException.WrapAndThrow(exception);
                    break;
            }
        }
        /// <summary>
        /// Handles exceptions thrown during a call to methods/properties other than <see cref="OpenClientSession"/>
        /// method
        /// </summary>
        /// <param name="exception"></param>
        private void HandleException(SocketException exception)
        {
            /*
             * This method should be called not more than once per session.
             * This rule is kept deliberately to prevent duplicate events
             * from multiple threads encountering exceptions almost at the same time.
             */
            lock (vSyncRoot)
            {
                if (!vIsAlive) { WrapAndThrow(exception); }
                vIsAlive = false;
            }
            /*
             * If an exception is thrown during sending or receiving data operation
             * this session cannot be guaranteed to be stable enough to serve subsequent requests
             * as some infromation might have been lost when exception was thrown.
             * 
             * We therefore shutdown this session so that any subsequent requests are denied.
             */
            TerminateSession(SessionCloseReason.ExceptionThrown, exception); //Shutdown this session to release claimed resources
            RaiseSessionExceptionEvent(exception);
            WrapAndThrow(exception);

        }

        private void WrapAndThrow(SocketException exception)
        {
            /*
             * This code checks for error codes that a likely to be caused
             * by a failure in connection to a remote host.
             * Such situations will render the session incapable to serve
             * any subsequent requests.
             */
            switch (exception.SocketErrorCode)
            {
                case SocketError.NetworkDown: //Network is down
                case SocketError.NetworkReset: //Connection broken due to failure in connection
                case SocketError.ConnectionAborted: //Connection was aborted by software in the host machine
                case SocketError.ConnectionReset: //Remote host forcibly closed the connection
                case SocketError.NotConnected: //Socket is not connected
                case SocketError.HostDown: //Remote host is down
                case SocketError.Disconnecting: //Remote party has called a graceful shutdown
                    //Wrap this exception in a new exception
                    throw new TcpIpSessionClosedException(exception);
                default:
                    //Rethrow the original exception
                    TcpIpException.WrapAndThrow(exception);
                    break;
            }
        }
        /// <summary>
        /// Creates a TCP/IP socket with default options
        /// </summary>
        /// <returns>A TCP/IP socket with default properties</returns>
        private static Socket CreateClientSocket()
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            //Setting default properties
            socket.NoDelay = true; //Disable Nagle algorithm
            //Assign a new LingerOption: LingerState returns a copy on some runtimes,
            //so mutating it in place silently does nothing.
            socket.LingerState = new LingerOption(false, 0);
            socket.SendBufferSize = 1024; //Set send buffer size to 1KiB
            socket.ReceiveBufferSize = 4 * 1024; //Set receive buffer size to 4KiB
            socket.SendTimeout = 0; //Sending operation should never timeout
            socket.ReceiveTimeout = 0; //Receive operation should never timeout
            return socket; //return the socket
        }
        /// <summary>
        /// Check if this session can be used, that is if the connection is alive otherwise an TcpIpConnectionException is thrown
        /// </summary>
        private void CheckSession()
        {
            lock (vSyncRoot)
            {
                if (!vIsAlive) { throw new TcpIpSessionClosedException("TCP/IP session cannot send or receive data because it has been shutdown"); }
            }
        }

        private void RaiseSessionClosedEvent(SessionCloseReason reason, Exception ex)
        {
            if (SessionClosed == null) { return; }
            var delegates = SessionClosed.GetInvocationList();
            foreach (EventHandler<TcpIpSessionClosedEventArgs> del in delegates)
            {
                if (del == null) continue;
                Task.Run(() =>
                {
                    try
                    {
                        del.Invoke(this, new TcpIpSessionClosedEventArgs(reason, ex));
                    }
                    catch (Exception eventEx)
                    {
                        // Log the exception but don't let it propagate to avoid crashing the application
                        Logger.LogError(eventEx, "Exception in TcpIpSession SessionClosed event handler");
                    }
                });
            }
        }

        private void RaiseSessionExceptionEvent(Exception ex)
        {
            if (SessionException == null) { return; }
            foreach (EventHandler<TcpIpSessionExceptionEventArgs> del in SessionException.GetInvocationList())
            {
                Task.Run(() =>
                {
                    try
                    {
                        del.Invoke(this, new TcpIpSessionExceptionEventArgs(ex));
                    }
                    catch (Exception eventEx)
                    {
                        // Log the exception but don't let it propagate to avoid crashing the application
                        Logger.LogError(eventEx, "Exception in TcpIpSession SessionException event handler");
                    }
                });
            }
        }

        #endregion

        #region Public Methods
        public void Send(byte[] buffer)
        {
            CheckSession();
            try { vSocket.Send(buffer); }
            catch (SocketException ex)
            { HandleException(ex); }
        }

        public void Send(byte[] buffer, int start, int length)
        {
            CheckSession();
            try { vSocket.Send(buffer, start, length, SocketFlags.None); }
            catch (SocketException ex)
            { HandleException(ex); }
        }

        public Task SendAsync(byte[] buffer, CancellationToken cancellationToken = default)
        {
            if (buffer == null) { throw new ArgumentNullException(nameof(buffer)); }
            return SendAsync(new ReadOnlyMemory<byte>(buffer), cancellationToken);
        }

        public Task SendAsync(byte[] buffer, int start, int length, CancellationToken cancellationToken = default)
        {
            if (buffer == null) { throw new ArgumentNullException(nameof(buffer)); }
            return SendAsync(new ReadOnlyMemory<byte>(buffer, start, length), cancellationToken);
        }

        public async Task SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            CheckSession();
            try
            {
                //Send returns once it has handed the whole buffer to the socket, but a
                //partial send is possible, so keep going until the buffer is drained.
                while (!buffer.IsEmpty)
                {
                    int sent = await vSocket
                        .SendAsync(buffer, SocketFlags.None, cancellationToken)
                        .ConfigureAwait(false);

                    if (sent <= 0) { break; }
                    buffer = buffer.Slice(sent);
                }
            }
            catch (SocketException ex)
            { HandleException(ex); }
        }

        public Task<int> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken = default)
        {
            if (buffer == null) { throw new ArgumentNullException(nameof(buffer)); }
            return ReceiveAsync(new Memory<byte>(buffer), cancellationToken);
        }

        public Task<int> ReceiveAsync(byte[] buffer, int start, int length, CancellationToken cancellationToken = default)
        {
            if (buffer == null) { throw new ArgumentNullException(nameof(buffer)); }
            return ReceiveAsync(new Memory<byte>(buffer, start, length), cancellationToken);
        }

        public async Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            CheckSession();
            try
            {
                int received = await vSocket
                    .ReceiveAsync(buffer, SocketFlags.None, cancellationToken)
                    .ConfigureAwait(false);

                //If underlying socket returns zero bytes, close this session
                //as this indicates a closed socket connection by the remote host.
                if (received == 0)
                {
                    lock (vSyncRoot)
                    {
                        if (!vIsAlive) { return received; }
                        vIsAlive = false;
                    }
                    TerminateSession(SessionCloseReason.SocketShutdown, null);
                }
                return received;
            }
            catch (SocketException ex)
            { HandleException(ex); throw; }
        }

        public int Receive(byte[] buffer)
        {
            CheckSession();
            try { return vSocket.Receive(buffer); }
            catch (SocketException ex)
            { HandleException(ex); throw; }
        }

        public int Receive(byte[] buffer, int start, int length)
        {
            CheckSession();
            try
            {
                int received = vSocket.Receive(buffer, start, length, SocketFlags.None);
                //If underlying socket returns zero bytes, close this session
                //as this indicates a closed socket connection by the remote host.
                if (received == 0)
                {
                    lock (vSyncRoot)
                    {
                        if (!vIsAlive) { return received; }
                        vIsAlive = false;
                    }
                    TerminateSession(SessionCloseReason.SocketShutdown, null);
                }
                return received;
            }
            catch (SocketException ex)
            { HandleException(ex); throw; }
        }

        public void EndSession()
        {
            lock (vSyncRoot)
            {
                if (!vIsAlive) { return; }
                TerminateSession(SessionCloseReason.EndSessionCalled, null);
                vIsAlive = false;
            }
        }

        private void TerminateSession(SessionCloseReason reason, Exception exception)
        {
            //shutdown socket
            vSocket.Shutdown(SocketShutdown.Both);
            //disconnect socket
            vSocket.Disconnect(false);
            //Close to release any resources claimed by the socket instance
            vSocket.Close();
            RaiseSessionClosedEvent(reason, exception);
        }

        /// <summary>
        /// Establishes a TCP/IP session with a remote host. The host is specified by an IP address and a port number
        /// </summary>
        /// <param name="ipAddress">The IP address of the remote host</param>
        /// <param name="port">The port number of the remote host</param>
        /// <returns>A TCP/IP session</returns>
        public static TcpIpSession OpenClientSession(IPAddress ipAddress, int port)
        {
            Socket socket = CreateClientSocket(); //Creates a socket to be used as client
            try { socket.Connect(ipAddress, port); } //Connect to the server of the specified ipAddress on the specified port
            catch (SocketException ex) { HandleConnectionException(ex); }
            TcpIpSession session = new TcpIpSession(socket, SessionType.Client);
            session.vProperties = new TcpIpSessionProperties(socket);
            session.vIsAlive = true;
            return session;
        }

        public static TcpIpSession OpenClientSession(EndPoint endPoint)
        {
            Socket socket = CreateClientSocket(); //Creates a socket to be used for client connection
            try { socket.Connect(endPoint); }
            catch (SocketException ex) { HandleConnectionException(ex); }
            TcpIpSession session = new TcpIpSession(socket, SessionType.Client);
            session.vProperties = new TcpIpSessionProperties(socket);
            session.vIsAlive = true;
            return session;
        }

        public static TcpIpSession OpenClientSession(IPAddress[] addresses, int port)
        {
            Socket socket = CreateClientSocket(); //Creates a socket to be used for client connection
            try { socket.Connect(addresses, port); }
            catch (SocketException ex) { HandleConnectionException(ex); }
            TcpIpSession session = new TcpIpSession(socket, SessionType.Client);
            session.vProperties = new TcpIpSessionProperties(socket);
            session.vIsAlive = true;
            return session;
        }

        public static TcpIpSession OpenClientSession(string hostName, int port)
        {
            Socket socket = CreateClientSocket(); //Creates a socket to be used as client
            try { socket.Connect(hostName, port); } //Connect to the specified host on the specified port
            catch (SocketException ex) { HandleConnectionException(ex); }
            TcpIpSession session = new TcpIpSession(socket, SessionType.Client);
            session.vProperties = new TcpIpSessionProperties(socket);
            session.vIsAlive = true;
            return session;
        }

        /// <summary>
        /// Establishes a TCP/IP session with the remote host at <paramref name="endPoint"/>.
        /// </summary>
        public static async Task<TcpIpSession> OpenClientSessionAsync(EndPoint endPoint,
            CancellationToken cancellationToken = default)
        {
            if (endPoint == null) { throw new ArgumentNullException(nameof(endPoint)); }

            Socket socket = CreateClientSocket(); //Creates a socket to be used for client connection
            try { await SocketCompat.ConnectAsync(socket, endPoint, cancellationToken).ConfigureAwait(false); }
            catch (SocketException ex) { HandleConnectionException(ex); }
            TcpIpSession session = new TcpIpSession(socket, SessionType.Client);
            session.vProperties = new TcpIpSessionProperties(socket);
            session.vIsAlive = true;
            return session;
        }

        /// <inheritdoc cref="OpenClientSessionAsync(EndPoint, CancellationToken)"/>
        public static Task<TcpIpSession> OpenClientSessionAsync(IPAddress ipAddress, int port,
            CancellationToken cancellationToken = default)
        {
            if (ipAddress == null) { throw new ArgumentNullException(nameof(ipAddress)); }
            return OpenClientSessionAsync(new IPEndPoint(ipAddress, port), cancellationToken);
        }

        /// <inheritdoc cref="OpenClientSessionAsync(EndPoint, CancellationToken)"/>
        public static Task<TcpIpSession> OpenClientSessionAsync(string hostName, int port,
            CancellationToken cancellationToken = default)
        {
            if (hostName == null) { throw new ArgumentNullException(nameof(hostName)); }
            return OpenClientSessionAsync(new DnsEndPoint(hostName, port), cancellationToken);
        }
        #endregion
        #endregion
    }
}
