using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using OblivionVoice.Common;

namespace OblivionVoice.Server;

public sealed class UdpVoiceRelay(
    VoiceConfigLoader configLoader,
    VoiceSessionTokenService tokenService,
    VoicePositionCache positions,
    ILogger logger) : IDisposable
{
    private sealed class ClientState
    {
        public required string PlayerId { get; init; }
        public required IPEndPoint EndPoint { get; set; }
        public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
        public long RateWindowSecond { get; set; }
        public int AudioPacketsThisWindow { get; set; }
        public byte[] PlayerIdBytes { get; init; } = [];
    }

    private readonly ConcurrentDictionary<string, ClientState> _clientsByPlayer = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<IPEndPoint, ClientState> _clientsByEndpoint = new();

    private UdpClient? _udp;
    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private Task? _cleanupLoop;

    private long _forwardedPackets;
    private long _culledPackets;

    public void Start()
    {
        var config = configLoader.Current;
        if (!config.Enabled)
        {
            logger.LogInformation("OblivionVoice UDP relay is disabled by config.");
            return;
        }
        if (_udp != null) return;

        var bindAddress = IPAddress.Parse(config.Network.BindAddress);
        _udp = new UdpClient(new IPEndPoint(bindAddress, config.Network.Port));
        _socket = _udp.Client;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                const int SIO_UDP_CONNRESET = -1744830452;
                _socket.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not disable UDP connection-reset reporting.");
            }
        }

        _cts = new CancellationTokenSource();
        _receiveLoop = Task.Run(() => ReceiveLoop(_cts.Token));
        _cleanupLoop = Task.Run(() => CleanupLoop(_cts.Token));

        logger.LogInformation(
            "OblivionVoice UDP relay listening on {Address}:{Port}/udp. Proximity routing: {Routing}.",
            config.Network.BindAddress,
            config.Network.Port,
            config.Proximity.ServerSideRouting ? "on" : "OFF (broadcasting to everyone)");
    }

    private async Task ReceiveLoop(CancellationToken token)
    {
        if (_udp == null) return;

        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udp.ReceiveAsync(token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "OblivionVoice UDP receive failure.");
                continue;
            }

            if (result.Buffer.Length == 0 || result.Buffer.Length > configLoader.Current.Network.MaxPacketBytes)
                continue;

            try
            {
                switch (result.Buffer[0])
                {
                    case VoiceUdpProtocol.Hello:
                        await HandleHello(result);
                        break;
                    case VoiceUdpProtocol.Audio:
                        HandleAudio(result);
                        break;
                    case VoiceUdpProtocol.KeepAlive:
                        Touch(result.RemoteEndPoint);
                        break;
                    case VoiceUdpProtocol.Disconnect:
                        RemoveEndpoint(result.RemoteEndPoint);
                        break;
                }
            }
            catch (Exception ex)
            {

                logger.LogDebug(ex, "Dropped malformed voice packet from {Endpoint}.", result.RemoteEndPoint);
            }
        }
    }

    private async Task HandleHello(UdpReceiveResult result)
    {

        if (result.Buffer.Length < 3) return;
        var tokenLength = BitConverter.ToUInt16(result.Buffer, 1);
        if (tokenLength == 0 || tokenLength > 256 || result.Buffer.Length != 3 + tokenLength) return;

        var token = Encoding.UTF8.GetString(result.Buffer, 3, tokenLength);
        if (!tokenService.TryConsume(token, out var session) || session == null)
        {
            logger.LogWarning("Rejected invalid/expired voice token from {Endpoint}", result.RemoteEndPoint);
            return;
        }

        if (_clientsByPlayer.TryGetValue(session.PlayerId, out var previous))
            _clientsByEndpoint.TryRemove(previous.EndPoint, out _);

        var state = new ClientState
        {
            PlayerId = session.PlayerId,
            EndPoint = result.RemoteEndPoint,
            LastSeen = DateTimeOffset.UtcNow,
            PlayerIdBytes = Encoding.UTF8.GetBytes(session.PlayerId)
        };

        _clientsByPlayer[session.PlayerId] = state;
        _clientsByEndpoint[result.RemoteEndPoint] = state;

        await _udp!.SendAsync(new byte[] { VoiceUdpProtocol.HelloAccepted }, result.RemoteEndPoint);
        logger.LogInformation("Authenticated voice endpoint for player {PlayerId} at {Endpoint}", session.PlayerId, result.RemoteEndPoint);

        if (configLoader.Current.Debug.Enabled)
            logger.LogInformation("[VoiceDebug] UDP authenticated player={PlayerId} endpoint={Endpoint} activeClients={Count}", session.PlayerId, result.RemoteEndPoint, _clientsByPlayer.Count);
    }

    private void HandleAudio(UdpReceiveResult result)
    {

        if (result.Buffer.Length <= 4) return;
        if (!_clientsByEndpoint.TryGetValue(result.RemoteEndPoint, out var speaker)) return;

        speaker.LastSeen = DateTimeOffset.UtcNow;
        if (!AllowAudioPacket(speaker)) return;

        var config = configLoader.Current;

        var mode = (VoiceMode)result.Buffer[3];

        var range = mode switch
        {
            VoiceMode.Whisper => config.Proximity.WhisperMeters,
            VoiceMode.Shout => config.Proximity.ShoutMeters,
            _ => config.Proximity.NormalMeters
        };

        range = MathF.Min(range, config.Proximity.MaximumReceiveMeters);
        var rangeUnits = range * config.Proximity.WorldUnitsPerMeter;
        var rangeUnitsSquared = rangeUnits * rangeUnits;

        var routing = config.Proximity.ServerSideRouting;
        VoicePositionCache.PlayerPlacement speakerPlacement = default;
        var haveSpeakerPosition = false;

        if (routing)
            haveSpeakerPosition = positions.TryGet(speaker.PlayerId, out speakerPlacement);

        var environment = haveSpeakerPosition
            ? speakerPlacement.Environment
            : VoiceEnvironment.Outdoor;

        var bodyLength = result.Buffer.Length - 1;
        var packetLength = 1 + 2 + speaker.PlayerIdBytes.Length + bodyLength + 1;

        var outgoing = new byte[packetLength];

        outgoing[0] = VoiceUdpProtocol.Audio;
        BitConverter.TryWriteBytes(outgoing.AsSpan(1, 2), (ushort)speaker.PlayerIdBytes.Length);
        speaker.PlayerIdBytes.CopyTo(outgoing, 3);

        var bodyStart = 3 + speaker.PlayerIdBytes.Length;

        Buffer.BlockCopy(result.Buffer, 1, outgoing, bodyStart, 3);
        outgoing[bodyStart + 3] = (byte)environment;
        Buffer.BlockCopy(result.Buffer, 4, outgoing, bodyStart + 4, result.Buffer.Length - 4);

        var payload = new ReadOnlyMemory<byte>(outgoing, 0, packetLength);
        var sent = 0;
        var culled = 0;

        foreach (var listener in _clientsByEndpoint.Values)
        {
            if (listener.PlayerId == speaker.PlayerId && !config.Debug.LoopbackMicrophone)
                continue;

            if (routing && haveSpeakerPosition)
            {
                if (!positions.TryGet(listener.PlayerId, out var listenerPlacement))
                {

                }
                else if (!speakerPlacement.SharesSpaceWith(listenerPlacement))
                {
                    culled++;
                    continue;
                }
                else if (Vector3DistanceSquared(speakerPlacement.Position, listenerPlacement.Position) > rangeUnitsSquared)
                {
                    culled++;
                    continue;
                }
            }

            try
            {

                var pending = _socket!.SendToAsync(payload, SocketFlags.None, listener.EndPoint);

                if (pending.IsCompleted)
                    _ = pending.Result;
                else
                    ObserveSend(pending, listener.PlayerId);

                sent++;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Voice frame forward failed for {PlayerId}", listener.PlayerId);
            }
        }

        Interlocked.Add(ref _forwardedPackets, sent);
        Interlocked.Add(ref _culledPackets, culled);

        if (config.Debug.Enabled && config.Debug.LogPackets)
        {
            logger.LogInformation(
                "[VoiceDebug] UDP forward speaker={PlayerId} mode={Mode} env={Environment} cell={Kind}:{X},{Y} range={Range}m sent={Sent} culled={Culled}",
                speaker.PlayerId,
                mode,
                environment,
                haveSpeakerPosition ? speakerPlacement.CellKind.ToString() : "none",
                haveSpeakerPosition ? speakerPlacement.CellX : 0,
                haveSpeakerPosition ? speakerPlacement.CellY : 0,
                range,
                sent,
                culled);
        }
    }

    private async void ObserveSend(ValueTask<int> pending, string playerId)
    {
        try { await pending; }
        catch (Exception ex) { logger.LogDebug(ex, "Voice frame forward failed for {PlayerId}", playerId); }
    }

    private static float Vector3DistanceSquared(System.Numerics.Vector3 a, System.Numerics.Vector3 b)
    {

        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private bool AllowAudioPacket(ClientState state)
    {
        var second = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (state)
        {
            if (state.RateWindowSecond != second)
            {
                state.RateWindowSecond = second;
                state.AudioPacketsThisWindow = 0;
            }
            state.AudioPacketsThisWindow++;
            return state.AudioPacketsThisWindow <= configLoader.Current.Network.MaxAudioPacketsPerSecond;
        }
    }

    private void Touch(IPEndPoint endpoint)
    {
        if (_clientsByEndpoint.TryGetValue(endpoint, out var client))
            client.LastSeen = DateTimeOffset.UtcNow;
    }

    private void RemoveEndpoint(IPEndPoint endpoint)
    {
        if (!_clientsByEndpoint.TryRemove(endpoint, out var client)) return;

        _clientsByPlayer.TryRemove(client.PlayerId, out _);

        if (configLoader.Current.Debug.Enabled)
            logger.LogInformation("[VoiceDebug] UDP disconnected player={PlayerId} endpoint={Endpoint} activeClients={Count}", client.PlayerId, endpoint, _clientsByPlayer.Count);
    }

    private async Task CleanupLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), token); }
            catch (OperationCanceledException) { break; }

            var config = configLoader.Current;
            var cutoff = DateTimeOffset.UtcNow.AddSeconds(-config.Network.ClientTimeoutSeconds);

            foreach (var pair in _clientsByPlayer)
            {
                if (pair.Value.LastSeen >= cutoff) continue;
                if (!_clientsByPlayer.TryRemove(pair.Key, out var stale)) continue;

                _clientsByEndpoint.TryRemove(stale.EndPoint, out _);
                logger.LogInformation("Removed stale voice client {PlayerId}", pair.Key);
            }

            if (config.Debug.Enabled && config.Debug.LogClientStats)
            {
                logger.LogInformation(
                    "[VoiceDebug] Relay: clients={Clients} endpoints={Endpoints} forwarded={Forwarded} culled={Culled} positions={Positions}",
                    _clientsByPlayer.Count,
                    _clientsByEndpoint.Count,
                    Interlocked.Read(ref _forwardedPackets),
                    Interlocked.Read(ref _culledPackets),
                    positions.Count);

                ServerStartupTrace.Write(
                    "RELAY STATS",
                    $"clients={_clientsByPlayer.Count}",
                    $"endpoints={_clientsByEndpoint.Count}",
                    $"interiors={positions.InteriorCount}",
                    $"forwarded={Interlocked.Read(ref _forwardedPackets)}",
                    $"culled={Interlocked.Read(ref _culledPackets)}",
                    $"positions={positions.Count}");
            }
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _udp?.Dispose(); } catch { }
        _cts?.Dispose();
        _udp = null;
        _socket = null;
        _clientsByPlayer.Clear();
        _clientsByEndpoint.Clear();
    }
}
