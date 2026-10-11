using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace BlockRealm.Net
{
    /// <summary>A reliable, ordered stream of packets between two peers. Polled from the game thread.</summary>
    public interface IConnection : IDisposable
    {
        bool IsOpen { get; }
        string Description { get; }
        /// <summary>Queues a serialized packet. Never blocks.</summary>
        void Send(byte[] payload);
        /// <summary>Dequeues the next received packet if there is one.</summary>
        bool TryReceive(out byte[] payload);
        void Close();
    }

    /// <summary>In-process connection pair, used for single player and tests: same code path as the network, no sockets.</summary>
    public sealed class LoopbackConnection : IConnection
    {
        private readonly ConcurrentQueue<byte[]> _inbox = new ConcurrentQueue<byte[]>();
        private LoopbackConnection _peer;
        private volatile bool _open = true;
        private readonly string _name;

        private LoopbackConnection(string name) { _name = name; }

        public static void CreatePair(out LoopbackConnection a, out LoopbackConnection b)
        {
            a = new LoopbackConnection("loopback-a");
            b = new LoopbackConnection("loopback-b");
            a._peer = b; b._peer = a;
        }

        public bool IsOpen { get { return _open && _peer != null && _peer._open; } }
        public string Description { get { return _name; } }

        public void Send(byte[] payload)
        {
            if (!IsOpen) return;
            if (payload.Length > PacketLimits.MaxFrameBytes) return;
            _peer._inbox.Enqueue(payload);
        }

        public bool TryReceive(out byte[] payload) { return _inbox.TryDequeue(out payload); }
        public void Close() { _open = false; }
        public void Dispose() { Close(); }
    }

    /// <summary>TCP connection with a reader thread and a writer thread. Frames are [uint32 length][payload].</summary>
    public sealed class TcpConnection : IConnection
    {
        private const long MaxQueuedBytes = 32L * 1024 * 1024;

        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly ConcurrentQueue<byte[]> _inbox = new ConcurrentQueue<byte[]>();
        private readonly BlockingCollectionLite _outbox = new BlockingCollectionLite();
        private volatile bool _open = true;
        private long _queuedBytes;
        private readonly string _description;

        public TcpConnection(TcpClient client)
        {
            _client = client;
            _client.NoDelay = true;
            _stream = client.GetStream();
            _description = client.Client.RemoteEndPoint != null ? client.Client.RemoteEndPoint.ToString() : "tcp";
            Thread reader = new Thread(ReadLoop) { IsBackground = true, Name = "net-read" };
            Thread writer = new Thread(WriteLoop) { IsBackground = true, Name = "net-write" };
            reader.Start(); writer.Start();
        }

        public bool IsOpen { get { return _open; } }
        public string Description { get { return _description; } }

        public static TcpConnection Connect(string host, int port, int timeoutMs)
        {
            TcpClient client = new TcpClient();
            IAsyncResult ar = client.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
            {
                client.Close();
                throw new IOException("connection to " + host + ":" + port + " timed out");
            }
            client.EndConnect(ar);
            return new TcpConnection(client);
        }

        public void Send(byte[] payload)
        {
            if (!_open) return;
            if (payload.Length > PacketLimits.MaxFrameBytes) return;
            long queued = Interlocked.Add(ref _queuedBytes, payload.Length);
            if (queued > MaxQueuedBytes) { Close(); return; }       // peer is not keeping up
            _outbox.Add(payload);
        }

        public bool TryReceive(out byte[] payload) { return _inbox.TryDequeue(out payload); }

        private void ReadLoop()
        {
            byte[] header = new byte[4];
            try
            {
                while (_open)
                {
                    if (!ReadFully(header, 4)) break;
                    int len = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
                    if (len <= 0 || len > PacketLimits.MaxFrameBytes) break;     // hostile or corrupt peer
                    byte[] body = new byte[len];
                    if (!ReadFully(body, len)) break;
                    _inbox.Enqueue(body);
                }
            }
            catch (Exception) { }
            Close();
        }

        private bool ReadFully(byte[] buf, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n = _stream.Read(buf, got, count - got);
                if (n <= 0) return false;
                got += n;
            }
            return true;
        }

        private void WriteLoop()
        {
            try
            {
                byte[] payload;
                while (_open && _outbox.Take(out payload))
                {
                    Interlocked.Add(ref _queuedBytes, -payload.Length);
                    int len = payload.Length;
                    byte[] frame = new byte[4 + len];
                    frame[0] = (byte)len; frame[1] = (byte)(len >> 8); frame[2] = (byte)(len >> 16); frame[3] = (byte)(len >> 24);
                    Buffer.BlockCopy(payload, 0, frame, 4, len);
                    _stream.Write(frame, 0, frame.Length);
                }
            }
            catch (Exception) { }
            Close();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _outbox.Complete();
            try { _client.Close(); } catch (Exception) { }
        }

        public void Dispose() { Close(); }
    }

    /// <summary>Minimal blocking queue (BlockingCollection is not available on every Unity target).</summary>
    internal sealed class BlockingCollectionLite
    {
        private readonly ConcurrentQueue<byte[]> _q = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _items = new SemaphoreSlim(0);
        private volatile bool _done;

        public void Add(byte[] item) { _q.Enqueue(item); _items.Release(); }

        public bool Take(out byte[] item)
        {
            while (true)
            {
                _items.Wait();
                if (_q.TryDequeue(out item)) return true;
                if (_done) { item = null; return false; }
            }
        }

        public void Complete() { _done = true; _items.Release(); }
    }

    /// <summary>Accepts TCP clients on a background thread.</summary>
    public sealed class TcpGameListener : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly ConcurrentQueue<IConnection> _accepted = new ConcurrentQueue<IConnection>();
        private volatile bool _running;
        public int Port { get; private set; }

        public TcpGameListener(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);
        }

        public void Start()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _running = true;
            Thread t = new Thread(AcceptLoop) { IsBackground = true, Name = "net-accept" };
            t.Start();
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient c = _listener.AcceptTcpClient();
                    _accepted.Enqueue(new TcpConnection(c));
                }
                catch (Exception) { if (!_running) break; }
            }
        }

        public bool TryAccept(out IConnection connection) { return _accepted.TryDequeue(out connection); }

        public void Dispose()
        {
            _running = false;
            try { _listener.Stop(); } catch (Exception) { }
        }
    }
}
