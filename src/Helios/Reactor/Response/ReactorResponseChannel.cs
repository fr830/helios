﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Helios.Exceptions;
using Helios.Net;
using Helios.Ops;
using Helios.Topology;
using Helios.Util.Collections;

namespace Helios.Reactor.Response
{
    /// <summary>
    /// Wraps a remote endpoint which connected <see cref="IReactor"/> instance inside a <see cref="IConnection"/> object
    /// </summary>
    public abstract class ReactorResponseChannel : IConnection
    {
        protected ICircularBuffer<NetworkData> UnreadMessages = new ConcurrentCircularBuffer<NetworkData>(100);
        private readonly ReactorBase _reactor;
        internal readonly Socket Socket;

        protected ReactorResponseChannel(ReactorBase reactor, Socket outboundSocket, NetworkEventLoop eventLoop) : this(reactor, outboundSocket, (IPEndPoint)outboundSocket.RemoteEndPoint, eventLoop)
        {
            
        }

        protected ReactorResponseChannel(ReactorBase reactor, Socket outboundSocket, IPEndPoint endPoint, NetworkEventLoop eventLoop)
        {
            _reactor = reactor;
            Socket = outboundSocket;
            Local = reactor.LocalEndpoint.ToNode(reactor.Transport);
            RemoteHost = NodeBuilder.FromEndpoint(endPoint);
            NetworkEventLoop = eventLoop;
        }


        public event ReceivedDataCallback Receive
        {
            add { NetworkEventLoop.Receive = value; }
            // ReSharper disable once ValueParameterNotUsed
            remove { NetworkEventLoop.Receive = null; }
        }

        public event ConnectionEstablishedCallback OnConnection
        {
            add { NetworkEventLoop.Connection = value; }
            // ReSharper disable once ValueParameterNotUsed
            remove { NetworkEventLoop.Connection = null; }
        }
        public event ConnectionTerminatedCallback OnDisconnection
        {
            add { NetworkEventLoop.Disconnection = value; }
            // ReSharper disable once ValueParameterNotUsed
            remove { NetworkEventLoop.Disconnection = null; }
        }

        public event ExceptionCallback OnError
        {
            add { NetworkEventLoop.SetExceptionHandler(value, this); }
            // ReSharper disable once ValueParameterNotUsed
            remove { NetworkEventLoop.SetExceptionHandler(null, this); }
        }

        public IEventLoop EventLoop { get { return NetworkEventLoop; } }

        public NetworkEventLoop NetworkEventLoop { get; private set; }

        public DateTimeOffset Created { get; private set; }
        public INode RemoteHost { get; private set; }
        public INode Local { get; private set; }
        public TimeSpan Timeout { get { return TimeSpan.FromSeconds(Socket.ReceiveTimeout); } }
        public TransportType Transport { get{ if(Socket.ProtocolType == ProtocolType.Tcp){ return TransportType.Tcp; } return TransportType.Udp; } }
        public bool Blocking { get { return Socket.Blocking; } set { Socket.Blocking = value; } }
        public bool WasDisposed { get; private set; }
        public bool Receiving { get { return _reactor.IsActive; } }
        public bool IsOpen()
        {
            return Socket.Connected;
        }

        public int Available { get { return Socket.Available; } }
        public Task<bool> OpenAsync()
        {
            Open();
            return Task.Run(() => true);
        }

        public abstract void Configure(IConnectionConfig config);

        public void Open()
        {
            if (NetworkEventLoop.Connection != null)
            {
                NetworkEventLoop.Connection(RemoteHost, this);
            }
        }

        public void BeginReceive()
        {
            if (NetworkEventLoop.Receive == null) throw new NullReferenceException("Receive cannot be null");

            BeginReceiveInternal();
        }

        public void BeginReceive(ReceivedDataCallback callback)
        {
            Receive += callback;
            foreach (var msg in UnreadMessages.DequeueAll())
            {
                var msg1 = msg;
                NetworkEventLoop.Receive(msg1, this);
            }

            BeginReceiveInternal();
        }

        protected abstract void BeginReceiveInternal();


        /// <summary>
        /// Method is called directly by the <see cref="ReactorBase"/> implementation to send data to this <see cref="IConnection"/>.
        /// 
        /// Can also be called by the socket itself if this reactor doesn't use <see cref="ReactorProxyResponseChannel"/>.
        /// </summary>
        /// <param name="data">The data to pass directly to the recipient</param>

        internal virtual void OnReceive(NetworkData data)
        {
            if (NetworkEventLoop.Receive != null)
            {
                NetworkEventLoop.Receive(data, this);
            }
            else
            {
               UnreadMessages.Enqueue(data);
            }
        }

        public void StopReceive()
        {
            StopReceiveInternal();
            NetworkEventLoop.Receive = null;
        }


        protected abstract void StopReceiveInternal();

        public void Close()
        {
            _reactor.CloseConnection(this);

            if (NetworkEventLoop.Disconnection != null)
            {
                NetworkEventLoop.Disconnection(new HeliosConnectionException(ExceptionType.Closed), this);
            }
        }

        public virtual void Send(NetworkData payload)
        {
            _reactor.Send(payload.Buffer, RemoteHost);
        }

        public virtual async Task SendAsync(NetworkData payload)
        {
            await Task.Run(() => _reactor.Send(payload.Buffer, RemoteHost));
        }

        #region IDisposable members

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void Dispose(bool disposing)
        {
            if (!WasDisposed)
            {
                
                if (disposing)
                {
                    Close();
                    if (Socket != null)
                    {
                        ((IDisposable)Socket).Dispose();
                    }
                }
            }
            WasDisposed = true;
        }

        #endregion
    }
}