using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using DiceModders.ModCore.Tools;

namespace DiceModders.Casino;

using Core = DiceModders.ModCore.ModCore;   // inside DiceModders.* the bare name would be the namespace
using Ui = DiceModders.UiKit.UiKit;

/// <summary>
/// DiceKingdoms Casino: a lobby rule (off by default) that adds the Casino, a new building with a wheel of fortune.
/// The building type is added to the game's own type list on every computer; everything reacts to the game's own steps.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
[BepInDependency("DiceModders.ModCore")]
[BepInDependency("DiceModders.UiKit")]
[BepInProcess("Dice-Kingdoms.exe")]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "org.dicemodders.dicekingdoms.casino";
    public const string Name = "DiceKingdoms Casino";
    public const string Version = "1.0.2";

    internal static ManualLogSource L;
    /// <summary>The mod's own switch ([General] Enabled).</summary>
    internal static bool On;

    private static readonly HashSet<string> Logged = new();

    public override void Load()
    {
        Tx.Load();
        L = Log;
        var patcher = new Patcher(Guid, Log);
        Casino.Install(patcher);
        CasinoType.Install(patcher);
        CasinoLook.Install(patcher);
        CasinoPanel.Install(patcher);
        Wheel.Install(patcher);
        var ui = Ui.CreateScope(Guid);
        WheelWindow.Install(ui, patcher);
        SettingsWindow.Install(ui);
        patcher.Summary();

        Core.Mods.Switch(Config, affectsSimulation: true, description: "Turns the Casino rule of this mod on or off.")
            .WhenOn(() => { On = true; WheelWindow.Refresh(); }, () => { On = false; WheelWindow.Refresh(); });
    }

    /// <summary>Logs a problem once per key.</summary>
    internal static void Once(string key, string message)
    {
        if (Logged.Add(key)) L?.LogError(message);
    }
}
