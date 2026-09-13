// Inert offline SDK stub. These tests never load the SDK, UE4SS or native game functions.
// The complete client is compiled against the real installed SDK in the release build.
namespace OblivionMp.Sdk.Entities.Player;
public readonly struct ReadyMainCharacter
{
    public ReadyMainCharacter() { }
    public bool IsValid { get; init; }
    public bool IsDead { get; init; }
    public float Hp { get; init; } = 100;
    public bool InDialogue { get; init; }
    public Guid PlayerId { get; init; } = Guid.NewGuid();
}
