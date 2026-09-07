namespace OblivionVoice.Client.Net;

internal static class VoiceUdpProtocol
{
    public const byte Hello = 1;
    public const byte HelloAccepted = 2;
    public const byte Audio = 3;
    public const byte KeepAlive = 4;
    public const byte Disconnect = 5;
}
