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
                return true;

            if (CellKind != other.CellKind) return false;
            if (CellKind == ParentCellKind.Exterior) return true;

            return CellX == other.CellX && CellY == other.CellY;
        }
    }

    private volatile Dictionary<string, PlayerPlacement> _snapshot = new(StringComparer.Ordinal);
    private long _refreshCount;
    private int _interiorCount;
    private int _cellMatchCount;

    public int Count => _snapshot.Count;

    public long RefreshCount => _refreshCount;

    public int InteriorCount => _interiorCount;

    public int CellMatchCount => _cellMatchCount;

    public bool TryGet(string playerId, out PlayerPlacement placement) =>
        _snapshot.TryGetValue(playerId, out placement);

    public void Refresh(EcsApi ecsApi)
    {
        try
        {
            var next = new Dictionary<string, PlayerPlacement>(_snapshot.Count, StringComparer.Ordinal);

            ecsApi.Query<MainCharacterComponent, TransformComponent>((ref character, ref transform) =>
            {
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
            _snapshot = next;
            Interlocked.Increment(ref _refreshCount);
        }
        catch (Exception ex)
        {

            logger.LogDebug(ex, "Voice position refresh failed; retaining the previous snapshot.");
        }
    }
}
