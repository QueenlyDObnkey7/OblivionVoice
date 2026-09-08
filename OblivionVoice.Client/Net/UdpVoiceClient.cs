using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using OblivionVoice.Common;

namespace OblivionVoice.Client.Net;

public sealed class UdpVoiceClient(ILogger logger) : IDisposable
{
    private sealed class Connection(UdpClient socket, IPEndPoint server)
    {
        public readonly UdpClient Socket = socket;
        public readonly IPEndPoint Server = server;
        public readonly CancellationTokenSource Cancellation = new();
        public long LastReply = Environment.TickCount64;
        public volatile bool Accepted;
    }

    public event Action<string, ushort, VoiceMode, VoiceEnvironment, ReadOnlyMemory<byte>>? OpusFrameReceived;
    public event Action? Connected;
    private Connection? _connection;
    private int _generation;
    private ushort _sequence;
    private bool _debugLogPackets;
    private long _malformedPackets;

    public bool IsConnected
    {
        get
        {
            var connection = Volatile.Read(ref _connection);
            return connection is { Accepted: true } &&
                Environment.TickCount64 - Interlocked.Read(ref connection.LastReply) < 15000;
        }
    }

    public async Task ConnectAsync(VoiceClientSettings settings)
    {
        DisposeSocket();
        var generation = Volatile.Read(ref _generation);
        _debugLogPackets = settings.DebugEnabled && settings.DebugLogPackets;
        var addresses = await Dns.GetHostAddressesAsync(settings.Host).WaitAsync(TimeSpan.FromSeconds(5));
        if (generation != Volatile.Read(ref _generation)) return;
        var address = addresses.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault()
            ?? throw new InvalidOperationException($"Could not resolve voice host '{settings.Host}'.");
        var server = new IPEndPoint(address, settings.Port);
        var socket = new UdpClient(address.AddressFamily);
        socket.Connect(server);
        var connection = new Connection(socket, server);
        Interlocked.Exchange(ref _connection, connection);
        if (generation != Volatile.Read(ref _generation))
        {
            Close(connection);
            return;
        }

        var tokenBytes = Encoding.UTF8.GetBytes(settings.SessionToken);
        if (tokenBytes.Length is 0 or > 256)
        {
            Close(connection);
            throw new InvalidOperationException("Voice session token length is invalid.");
        }
        var hello = new byte[3 + tokenBytes.Length];
        hello[0] = VoiceUdpProtocol.Hello;
        BitConverter.TryWriteBytes(hello.AsSpan(1, 2), (ushort)tokenBytes.Length);
        tokenBytes.CopyTo(hello, 3);
        _ = ReceiveLoop(connection);
        _ = KeepAliveLoop(connection);
        try
        {
            await socket.SendAsync(hello.AsMemory(), connection.Cancellation.Token);
            logger.LogInformation("Voice authentication started at {Endpoint}.", server);
        }
        catch
        {
            Close(connection);
            throw;
        }
    }

    public async Task SendOpusAsync(VoiceMode mode, ReadOnlyMemory<byte> opusFrame)
    {
        var connection = Volatile.Read(ref _connection);
        if (!IsConnected || connection == null || opusFrame.Length == 0) return;
        var packet = new byte[4 + opusFrame.Length];
        packet[0] = VoiceUdpProtocol.Audio;
        BitConverter.TryWriteBytes(packet.AsSpan(1, 2), _sequence++);
        packet[3] = (byte)mode;
        opusFrame.Span.CopyTo(packet.AsSpan(4));
        try { await connection.Socket.SendAsync(packet.AsMemory(), connection.Cancellation.Token); }
        catch (Exception ex)
        {
            if (!connection.Cancellation.IsCancellationRequested)
                logger.LogDebug(ex, "Voice send failed; requesting recovery.");
            Close(connection);
        }
    }

    private async Task ReceiveLoop(Connection connection)
    {
        try
        {
            while (!connection.Cancellation.IsCancellationRequested)
            {
                var result = await connection.Socket.ReceiveAsync(connection.Cancellation.Token);
                if (!ReferenceEquals(connection, Volatile.Read(ref _connection))) break;
                if (!result.RemoteEndPoint.Equals(connection.Server)) continue;
                try { HandlePacket(connection, result.Buffer); }
                catch (Exception ex)
                {
                    var count = Interlocked.Increment(ref _malformedPackets);
                    if (count <= 5 || count % 500 == 0)
                        logger.LogWarning(ex, "Dropped malformed voice packet. Count={Count}", count);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { logger.LogDebug(ex, "Voice receive stopped; requesting recovery."); }
        finally { Close(connection); }
    }

    private void HandlePacket(Connection connection, byte[] packet)
    {
        if (packet.Length == 1 && packet[0] == VoiceUdpProtocol.HelloAccepted)
        {
            Interlocked.Exchange(ref connection.LastReply, Environment.TickCount64);
            if (!connection.Accepted)
            {
                connection.Accepted = true;
                logger.LogInformation("Voice connected.");
                Connected?.Invoke();
            }
            return;
        }
        if (!connection.Accepted) return;
        if (packet.Length == 1 && packet[0] == VoiceUdpProtocol.KeepAlive)
        {
            Interlocked.Exchange(ref connection.LastReply, Environment.TickCount64);
            return;
        }
        if (packet.Length < 8 || packet[0] != VoiceUdpProtocol.Audio) return;
        var speakerLength = BitConverter.ToUInt16(packet, 1);
        var bodyStart = 3 + speakerLength;
        if (speakerLength == 0 || packet.Length <= bodyStart + 4) return;
        var mode = (VoiceMode)packet[bodyStart + 2];
        if (!Enum.IsDefined(mode)) return;
        var environment = (VoiceEnvironment)packet[bodyStart + 3];
        if (!Enum.IsDefined(environment)) return;
        var speakerId = Encoding.UTF8.GetString(packet, 3, speakerLength);
        var sequence = BitConverter.ToUInt16(packet, bodyStart);
        Interlocked.Exchange(ref connection.LastReply, Environment.TickCount64);
        if (_debugLogPackets)
            logger.LogInformation("Voice RX speaker={Speaker} sequence={Sequence}", speakerId, sequence);
        OpusFrameReceived?.Invoke(speakerId, sequence, mode, environment, packet.AsMemory(bodyStart + 4));
    }

    private async Task KeepAliveLoop(Connection connection)
    {
        try
        {
            while (!connection.Cancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), connection.Cancellation.Token);
                var timeout = connection.Accepted ? 15000 : 8000;
                if (Environment.TickCount64 - Interlocked.Read(ref connection.LastReply) >= timeout)
                {
                    logger.LogWarning("Voice connection timed out; a fresh session will be requested.");
                    break;
                }
                await connection.Socket.SendAsync(new byte[] { VoiceUdpProtocol.KeepAlive }.AsMemory(), connection.Cancellation.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { logger.LogDebug(ex, "Voice heartbeat stopped."); }
        finally { Close(connection); }
    }

    private void Close(Connection connection)
    {
        Interlocked.CompareExchange(ref _connection, null, connection);
        connection.Accepted = false;
        try { connection.Cancellation.Cancel(); } catch { }
        try { connection.Socket.Dispose(); } catch { }
    }

    private void DisposeSocket()
    {
        Interlocked.Increment(ref _generation);
        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection == null) return;
        try { connection.Socket.Send(new byte[] { VoiceUdpProtocol.Disconnect }, 1); } catch { }
        Close(connection);
    }

    public void Dispose() => DisposeSocket();
}
