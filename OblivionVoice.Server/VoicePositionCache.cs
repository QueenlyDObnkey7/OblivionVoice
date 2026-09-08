using System.Numerics;
using Microsoft.Extensions.Logging;
using OblivionVoice.Common;
using ReadyM.Relay.Common.Oblivion.ECS.Components;
using ReadyM.Relay.Common.Oblivion.ECS.Values;
using ReadyM.Relay.Server.Sdk.Ecs;

namespace OblivionVoice.Server;

public sealed class VoicePositionCache(ILogger logger)
{
    public readonly record struct PlayerPlacement(
        Vector3 Position,
        ParentCellKind CellKind,
        int CellX,
        int CellY,
        VoiceEnvironment Environment)
    {

        public bool SharesSpaceWith(in PlayerPlacement other)
        {
            if (CellKind == ParentCellKind.Unknown || other.CellKind == ParentCellKind.Unknown)
                return false;

            if (CellKind != other.CellKind) return false;
            if (CellKind == ParentCellKind.Exterior) return true;

            return CellX == other.CellX && CellY == other.CellY;
        }
    }

    private sealed record Snapshot(Dictionary<string, PlayerPlacement> Players, long RefreshedAt);
    private static readonly Dictionary<string, PlayerPlacement> Empty = new(StringComparer.Ordinal);
    private volatile Snapshot _snapshot = new(Empty, 0);
    private long _refreshCount;
    private int _interiorCount;
    private int _cellMatchCount;

    public int Count => GetSnapshot().Count;

    public long RefreshCount => _refreshCount;

    public int InteriorCount => _interiorCount;

    public int CellMatchCount => _cellMatchCount;

    public IReadOnlyDictionary<string, PlayerPlacement> GetSnapshot()
    {
        var snapshot = _snapshot;
        return Environment.TickCount64 - snapshot.RefreshedAt <= 1000 ? snapshot.Players : Empty;
    }

    public bool TryGet(string playerId, out PlayerPlacement placement) =>
        GetSnapshot().TryGetValue(playerId, out placement);

    public void Refresh(EcsApi ecsApi)
    {
        try
        {
            var next = new Dictionary<string, PlayerPlacement>(_snapshot.Players.Count, StringComparer.Ordinal);

            ecsApi.Query<MainCharacterComponent, TransformComponent>((ref character, ref transform) =>
            {
                if (!float.IsFinite(transform.Position.X) || !float.IsFinite(transform.Position.Y) || !float.IsFinite(transform.Position.Z)) return;
                next[character.PlayerId.ToString()] = new PlayerPlacement(
                    transform.Position, ParentCellKind.Unknown, 0, 0, VoiceEnvironment.Outdoor);
            });

            var interiors = 0;
            var cellMatches = 0;

            ecsApi.Query<MainCharacterComponent, ParentCellComponent>((ref character, ref cell) =>
            {
                var key = character.PlayerId.ToString();
                if (!next.TryGetValue(key, out var existing)) return;

                cellMatches++;
                if (cell.Kind == ParentCellKind.Interior) interiors++;

                next[key] = existing with
                {
                    CellKind = cell.Kind,
                    CellX = cell.X,
                    CellY = cell.Y,

                    Environment = cell.Kind == ParentCellKind.Interior
                        ? VoiceEnvironment.Room
                        : VoiceEnvironment.Outdoor
                };
            });

            _interiorCount = interiors;
            _cellMatchCount = cellMatches;
            _snapshot = new(next, Environment.TickCount64);
            Interlocked.Increment(ref _refreshCount);
        }
        catch (Exception ex)
        {

            logger.LogDebug(ex, "Voice position refresh failed; previous snapshot expires after one second.");
        }
    }
}
