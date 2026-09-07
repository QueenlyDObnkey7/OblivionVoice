using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using OblivionVoice.Common;

namespace OblivionVoice.Client.Net;

public sealed class UdpVoiceClient(ILogger logger) : IDisposable
{

    public event Action<string, ushort, VoiceMode, VoiceEnvironment, ReadOnlyMemory<byte>>? OpusFrameReceived;

    public event Action? Connected;

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private IPEndPoint? _server;
    private Task? _receiveLoop;
    private Task? _keepAliveLoop;
    private ushort _sequence;
    private bool _debugEnabled;
    private bool _debugLogPackets;
    private long _malformedPackets;

    public bool IsConnected { get; private set; }

    public async Task ConnectAsync(VoiceClientSettings settings)
    {
        DisposeSocket();

        _debugEnabled = settings.DebugEnabled;
        _debugLogPackets = settings.DebugEnabled && settings.DebugLogPackets;

        var addresses = await Dns.GetHostAddressesAsync(settings.Host);
        var address = addresses.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork)
                      ?? addresses.FirstOrDefault()
                      ?? throw new InvalidOperationException($"Could not resolve voice host '{settings.Host}'.");

        _server = new IPEndPoint(address, settings.Port);
        _udp = new UdpClient(0);

        if (OperatingSystem.IsWindows())
        {
            try
            {
                const int SIO_UDP_CONNRESET = -1744830452;
                _udp.Client.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not disable UDP connection-reset reporting.");
            }
        }

        _cts = new CancellationTokenSource();
        _receiveLoop = Task.Run(() => ReceiveLoop(_cts.Token));
        _keepAliveLoop = Task.Run(() => KeepAliveLoop(_cts.Token));

        var tokenBytes = Encoding.UTF8.GetBytes(settings.SessionToken);
        if (tokenBytes.Length > ushort.MaxValue)
            throw new InvalidOperationException("Voice session token is too long.");

        var hello = new byte[3 + tokenBytes.Length];
        hello[0] = VoiceUdpProtocol.Hello;
        BitConverter.GetBytes((ushort)tokenBytes.Length).CopyTo(hello, 1);
        tokenBytes.CopyTo(hello, 3);
        await _udp.SendAsync(hello, _server);
        logger.LogInformation("OblivionVoice UDP hello sent to {Endpoint}", _server);
    }

    public async Task SendOpusAsync(VoiceMode mode, ReadOnlyMemory<byte> opusFrame)
    {
        if (!IsConnected || _udp == null || _server == null || opusFrame.Length == 0) return;

        var packet = new byte[4 + opusFrame.Length];
        packet[0] = VoiceUdpProtocol.Audio;
        BitConverter.GetBytes(_sequence++).CopyTo(packet, 1);
        packet[3] = (byte)mode;
        opusFrame.Span.CopyTo(packet.AsSpan(4));
        await _udp.SendAsync(packet, _server);

        if (_debugLogPackets)
            logger.LogInformation("[VoiceDebug] UDP TX seq={Sequence} mode={Mode} bytes={Bytes}", (ushort)(_sequence - 1), mode, packet.Length);
    }

    private async Task ReceiveLoop(CancellationToken token)
    {
        if (_udp == null) return;

        try
        {
            while (!token.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try { result = await _udp.ReceiveAsync(token); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Voice UDP receive failed.");
                    continue;
                }

                try
                {
                    HandlePacket(result);
                }
                catch (Exception ex)
                {
                    _malformedPackets++;

                    if (_malformedPackets <= 5 || _malformedPackets % 500 == 0)
                    {
                        logger.LogWarning(
                            ex,
                            "Dropped malformed voice packet from {Endpoint} ({Bytes} bytes). Total dropped: {Count}.",
                            result.RemoteEndPoint,
                            result.Buffer.Length,
                            _malformedPackets);
                    }
                }
            }
        }
        catch (Exception ex)
        {

            logger.LogError(ex, "OblivionVoice UDP receive loop terminated unexpectedly.");
            Console.WriteLine($"[OblivionVoice] UDP RECEIVE LOOP DIED: {ex}");
        }
    }

    private void HandlePacket(UdpReceiveResult result)
    {
        if (result.Buffer.Length == 0) return;

        if (result.Buffer[0] == VoiceUdpProtocol.HelloAccepted)
        {
            IsConnected = true;
            logger.LogInformation("OblivionVoice UDP session accepted.");
            Connected?.Invoke();
            return;
        }

        if (result.Buffer[0] != VoiceUdpProtocol.Audio || result.Buffer.Length < 8) return;

        var speakerLength = BitConverter.ToUInt16(result.Buffer, 1);
        const int speakerStart = 3;
        var bodyStart = speakerStart + speakerLength;

        if (speakerLength == 0 || result.Buffer.Length <= bodyStart + 4) return;

        var speakerId = Encoding.UTF8.GetString(result.Buffer, speakerStart, speakerLength);
        var sequence = BitConverter.ToUInt16(result.Buffer, bodyStart);

        var rawMode = result.Buffer[bodyStart + 2];
        var mode = Enum.IsDefined((VoiceMode)rawMode) ? (VoiceMode)rawMode : VoiceMode.Normal;

        var rawEnvironment = result.Buffer[bodyStart + 3];
        var environment = Enum.IsDefined((VoiceEnvironment)rawEnvironment)
            ? (VoiceEnvironment)rawEnvironment
            : VoiceEnvironment.Outdoor;

        var opusStart = bodyStart + 4;
        var payload = new byte[result.Buffer.Length - opusStart];
        Buffer.BlockCopy(result.Buffer, opusStart, payload, 0, payload.Length);

        if (_debugLogPackets)
            logger.LogInformation("[VoiceDebug] UDP RX speaker={Speaker} seq={Sequence} mode={Mode} env={Environment} bytes={Bytes}", speakerId, sequence, mode, environment, result.Buffer.Length);

        OpusFrameReceived?.Invoke(speakerId, sequence, mode, environment, payload);
    }

    private async Task KeepAliveLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), token); }
            catch (OperationCanceledException) { break; }
            if (_udp == null || _server == null) continue;
            try { await _udp.SendAsync(new byte[] { VoiceUdpProtocol.KeepAlive }, _server); }
            catch { }
        }
    }

    private void DisposeSocket()
    {
        try
        {
            if (_udp != null && _server != null)
                _udp.Send(new byte[] { VoiceUdpProtocol.Disconnect }, 1, _server);
        }
        catch { }
        try { _cts?.Cancel(); } catch { }
        try { _udp?.Dispose(); } catch { }
        _cts?.Dispose();
        _udp = null;
        _server = null;
        _cts = null;
        IsConnected = false;
    }

    public void Dispose() => DisposeSocket();
}
