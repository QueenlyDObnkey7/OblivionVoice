namespace OblivionVoice.Common;

public enum VoiceEnvironment : byte
{

    Outdoor = 0,

    Room = 1,

    Cave = 2,

    Mine = 3,

    Ruin = 4,

    Hall = 5
}

public static class VoiceEnvironmentClassifier
{

    private static readonly string[] AyleidRuinPrefixes =
    [
        "Anga", "Anutwyll", "Arpenia", "Atatar", "Bawn", "Belda", "Beldaburo",
        "Ceyatatar", "Culotte", "Elenglynn", "Fanacas", "Fanacasecul", "Garlas",
        "Hame", "Hrotanda", "Kemen", "Lindai", "Lipsand", "Mackamentain", "Malada",
        "Miscarcand", "Morahame", "Moranda", "Nagastani", "Narfinsel", "Nenalata",
        "Nenyond", "Ninendava", "Niryastare", "Nonungalo", "Nornal", "Nornalhorst",
        "Ondo", "Piukanda", "Rielle", "Sedor", "Sercen", "Silorn", "Talwinque",
        "Telepe", "Trumbe", "Veyond", "Vilverin", "Welke", "Wendelbek", "Wendir"
    ];

    private static readonly (string Token, VoiceEnvironment Environment)[] Tokens =
    [

        ("Chapel",      VoiceEnvironment.Hall),
        ("Cathedral",   VoiceEnvironment.Hall),
        ("GreatHall",   VoiceEnvironment.Hall),
        ("CountyHall",  VoiceEnvironment.Hall),
        ("Palace",      VoiceEnvironment.Hall),
        ("Arena",       VoiceEnvironment.Hall),
        ("ArcaneUniversity", VoiceEnvironment.Hall),
        ("CloudRuler",  VoiceEnvironment.Hall),

        ("Cavern",      VoiceEnvironment.Cave),
        ("Cave",        VoiceEnvironment.Cave),
        ("Grotto",      VoiceEnvironment.Cave),
        ("Caverns",     VoiceEnvironment.Cave),

        ("Mine",        VoiceEnvironment.Mine),

        ("Fort",        VoiceEnvironment.Ruin),
        ("Sewer",       VoiceEnvironment.Ruin),
        ("Subterrane",  VoiceEnvironment.Ruin),
        ("Dungeon",     VoiceEnvironment.Ruin),
        ("Crypt",       VoiceEnvironment.Ruin),
        ("Catacomb",    VoiceEnvironment.Ruin),
        ("Tomb",        VoiceEnvironment.Ruin),
        ("Undercroft",  VoiceEnvironment.Ruin),
        ("Mausoleum",   VoiceEnvironment.Ruin),
        ("Sanctuary",   VoiceEnvironment.Ruin),
        ("Citadel",     VoiceEnvironment.Ruin),
        ("Ruins",       VoiceEnvironment.Ruin),
        ("Tower",       VoiceEnvironment.Ruin),
        ("Barracks",    VoiceEnvironment.Ruin),

        ("House",       VoiceEnvironment.Room),
        ("Basement",    VoiceEnvironment.Room),
        ("Upstairs",    VoiceEnvironment.Room),
        ("Inn",         VoiceEnvironment.Room),
        ("Lodge",       VoiceEnvironment.Room),
        ("Shack",       VoiceEnvironment.Room),
        ("Store",       VoiceEnvironment.Room),
        ("Farm",        VoiceEnvironment.Room),
        ("Stable",      VoiceEnvironment.Room)
    ];

    public static VoiceEnvironment Classify(string? cellId, bool isExterior)
    {
        if (isExterior) return VoiceEnvironment.Outdoor;

        if (string.IsNullOrWhiteSpace(cellId)) return VoiceEnvironment.Room;

        foreach (var (token, environment) in Tokens)
        {
            if (cellId.Contains(token, StringComparison.OrdinalIgnoreCase))
                return environment;
        }

        foreach (var prefix in AyleidRuinPrefixes)
        {
            if (cellId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return VoiceEnvironment.Ruin;
        }

        return VoiceEnvironment.Room;
    }
}
