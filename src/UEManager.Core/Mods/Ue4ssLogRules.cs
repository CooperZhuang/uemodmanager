using System.Text.Json;
using UEManager.Core.Storage;

namespace UEManager.Core.Mods;

/// <summary>A line UE4SS writes at every game start that does no harm: a part of it, and why it is harmless.</summary>
public sealed record Ue4ssKnownLine(string Text, string? Why);

/// <summary>What the UE4SS page knows about UE4SS.log, from Mods\Ue4ssLog.json, which is built into this assembly.</summary>
public sealed record Ue4ssLogRules(IReadOnlyList<Ue4ssKnownLine> Known, IReadOnlyList<string> Ue4ssLines, IReadOnlyList<string> Ue4ssMods)
{
    private const string Resource = "UEManager.Core.Mods.Ue4ssLog.json";

    public static Ue4ssLogRules BuiltIn { get; } = Load();

    /// <summary>Whether a message, the line after its time stamp, is a known one.</summary>
    public bool IsKnown(string message) => Known.Any(k => message.Contains(k.Text, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a [Lua] message is UE4SS's own, not a mod's.</summary>
    public bool IsUe4ssLine(string message) => Ue4ssLines.Any(l => message.StartsWith(l, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a mod folder is one UE4SS comes with, such as BPModLoaderMod.</summary>
    public bool IsUe4ssMod(string mod) => Ue4ssMods.Contains(mod, StringComparer.OrdinalIgnoreCase);

    private static Ue4ssLogRules Load()
    {
        using var stream = typeof(Ue4ssLogRules).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"'{Resource}' is not built into this assembly.");
        return JsonSerializer.Deserialize<Ue4ssLogRules>(stream, JsonFile.Options)
            ?? throw new InvalidDataException($"'{Resource}' is empty.");
    }
}
