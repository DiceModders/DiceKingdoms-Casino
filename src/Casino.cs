using DiceModders.ModCore;
using DiceModders.ModCore.Tools;

namespace DiceModders.Casino;

using Core = DiceModders.ModCore.ModCore;

/// <summary>The lobby rules: the Casino on/off, the wheel's chances (in steps of 5 %, one face of 20 each) and the amounts.</summary>
internal static class Casino
{
    private static LobbyRule<bool> _on;
    private static LobbyRule<int> _jackpot, _win, _bad, _catastrophe, _stake, _cooldown, _winGold, _winEach;

    /// <summary>The wheel's numbers, in the order the settings window shows them (rule, unit shown after the value).</summary>
    internal static (LobbyRule<int> rule, string unit)[] Numbers => new[] { (_jackpot, "%"), (_win, "%"), (_bad, "%"), (_catastrophe, "%"), (_stake, ""), (_cooldown, ""), (_winGold, ""), (_winEach, "") };
    internal static LobbyRule<bool> Rule => _on;

    /// <summary>The four chances: together at most 100 % ("nothing" gets the rest).</summary>
    internal static bool IsChance(LobbyRule<int> rule) => rule == _jackpot || rule == _win || rule == _bad || rule == _catastrophe;

    /// <summary>The host's four chances added up (the lobby's current values).</summary>
    internal static int ChanceSum => (_jackpot?.Live ?? 0) + (_win?.Live ?? 0) + (_bad?.Live ?? 0) + (_catastrophe?.Live ?? 0);

    /// <summary>The rule is on for this match (the building type itself always exists, so every computer has the same type list).</summary>
    internal static bool On => Plugin.On && _on != null && _on.Value;

    internal static int JackpotPercent => _jackpot?.Value ?? 5;
    internal static int WinPercent => _win?.Value ?? 15;
    internal static int BadPercent => _bad?.Value ?? 30;
    internal static int CatastrophePercent => _catastrophe?.Value ?? 5;
    internal static int Stake => _stake?.Value ?? 3;
    internal static int Cooldown => _cooldown?.Value ?? 3;
    internal static int WinGold => _winGold?.Value ?? 6;
    internal static int WinEach => _winEach?.Value ?? 1;

    internal static void Install(Patcher patcher)
    {
        _on = Core.Lobby.Rules.Toggle("casino.on", "Casino", false,
            "Adds the Casino, a building (one per island, needs a University) where you can spin a wheel of fortune once in a while: good and bad outcomes.");
        // the numbers are edited in their own window (Settings > Lobby > Casino > Wheel settings), not in the rules list
        _jackpot = Core.Lobby.Rules.Number("casino.jackpot", "Jackpot chance (%)", 5, 0, 30, 5, "Chance of a tsunami on every other player.", () => false);
        _win = Core.Lobby.Rules.Number("casino.win", "Win chance (%)", 15, 0, 60, 5, "Chance of winning gold or some of each resource.", () => false);
        _bad = Core.Lobby.Rules.Number("casino.bad", "Bad luck chance (%)", 30, 0, 60, 5, "Chance of robbers from another island or a fire on your island.", () => false);
        _catastrophe = Core.Lobby.Rules.Number("casino.catastrophe", "Catastrophe chance (%)", 5, 0, 30, 5, "Chance of a tsunami and a meteorite on your own island.", () => false);
        _stake = Core.Lobby.Rules.Number("casino.stake", "Stake (gold)", 3, 1, 10, 1, "Gold a spin costs.", () => false);
        _cooldown = Core.Lobby.Rules.Number("casino.cooldown", "Turns between spins", 3, 1, 10, 1, "How many turns a player waits between two spins.", () => false);
        _winGold = Core.Lobby.Rules.Number("casino.winGold", "Gold win", 6, 1, 30, 1, "Gold a gold win gives.", () => false);
        _winEach = Core.Lobby.Rules.Number("casino.winEach", "Win of each resource", 1, 1, 5, 1, "How much of each resource the other win gives.", () => false);
        var heading = Core.Lobby.Groups.Get("casino", "Casino");
        foreach (var rule in new ILobbyRule[] { _on, _jackpot, _win, _bad, _catastrophe, _stake, _cooldown, _winGold, _winEach }) Group(rule, heading);
    }

    private static void Group(ILobbyRule rule, LobbyGroup heading)
    {
        if (rule is LobbyRule<bool> b) b.InGroup(heading);
        else if (rule is LobbyRule<int> i) i.InGroup(heading);
    }
}
