using System;
using System.Collections.Generic;
using DiceKingdoms.Game;
using DiceModders.ModCore;
using DiceModders.ModCore.Tools;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace DiceModders.Casino;

using Core = DiceModders.ModCore.ModCore;

/// <summary>
/// The wheel of fortune. Lock-step: every computer runs the same steps with the same numbers.
///  1. Build phase of turn T: the spin is one of ModCore's build actions (code 1), like a market purchase: the stake goes at the click on your
///     screen and on every other computer when the build phase ends. The island now has a spin waiting.
///  2. Disasters phase of turn T + 1: the host makes the turn's disaster list (DisastersPhase.RandomOccurrences). Every island always gets a
///     Starvation entry with a random seed of the host's; the wheel's face is taken from the spinner's Starvation seed, so nobody can know it
///     before. The host adds the disasters the face asks for to the list, and the game sends the list to everybody (its own message).
///  3. Every computer receives that list (DisastersPhase.ExecuteOccurrences), works out the same face from the same seed, pays the winnings
///     and shows the wheel to the spinner. The disasters then play like any other.
/// </summary>
internal static class Wheel
{
    internal const int SpinAction = 1;       // ModCore build action code of the Casino
    internal static int Stake => Casino.Stake;          // gold per spin (lobby rule)
    internal static int Cooldown => Casino.Cooldown;    // turns between two spins (lobby rule)

    internal enum Outcome { Nothing, Gold, Each, Robbers, Fire, Jackpot, Catastrophe }

    private static Outcome[] _faces;
    private static string _facesFor;

    /// <summary>
    /// The 20 faces, clockwise from the top, made from the lobby rules (every computer has the same rules, so the same wheel): one face per 5 %.
    /// Each kind is spread evenly round the wheel; the jackpot and the catastrophe sit on opposite sides. Wins alternate gold / gold / each,
    /// bad luck alternates robbers / fire. If the chances add up to more than 100 %, the biggest kinds give faces back first.
    /// </summary>
    internal static Outcome[] Faces
    {
        get
        {
            string key = $"{Casino.JackpotPercent}|{Casino.WinPercent}|{Casino.BadPercent}|{Casino.CatastrophePercent}";
            if (_faces != null && key == _facesFor) return _faces;
            _facesFor = key;
            return _faces = Build(Casino.JackpotPercent / 5, Casino.WinPercent / 5, Casino.BadPercent / 5, Casino.CatastrophePercent / 5);
        }
    }

    private static Outcome[] Build(int jackpot, int win, int bad, int catastrophe)
    {
        const int n = 20;
        int[] counts = { Math.Max(0, jackpot), Math.Max(0, win), Math.Max(0, bad), Math.Max(0, catastrophe) };   // order: jackpot, win, bad, catastrophe
        while (counts[0] + counts[1] + counts[2] + counts[3] > n)
        {
            int big = 2;   // ties: bad, win, catastrophe, jackpot give back first
            foreach (int c in new[] { 1, 3, 0 }) if (counts[c] > counts[big]) big = c;
            counts[big]--;
        }
        int nothing = n - counts[0] - counts[1] - counts[2] - counts[3];
        var items = new List<(double pos, int order, Outcome what)>();
        void Spread(int count, double offset, int order, Func<int, Outcome> what)
        {
            for (int k = 0; k < count; k++) items.Add((((k + 0.5) * n / count + offset) % n, order, what(k)));
        }
        Spread(counts[3], n / 2.0, 0, _ => Outcome.Catastrophe);
        Spread(counts[0], 0.0, 1, _ => Outcome.Jackpot);
        Spread(counts[1], 2.5, 2, k => k % 3 == 1 ? Outcome.Each : Outcome.Gold);
        Spread(counts[2], 1.2, 3, k => k % 2 == 0 ? Outcome.Robbers : Outcome.Fire);
        Spread(nothing, 0.5, 4, _ => Outcome.Nothing);
        items.Sort((a, b) => a.pos != b.pos ? a.pos.CompareTo(b.pos) : a.order.CompareTo(b.order));
        var faces = new Outcome[n];
        for (int i = 0; i < n; i++) faces[i] = items[i].what;
        return faces;
    }

    /// <summary>The chance of each kind in % (5 per face), from the faces really on the wheel.</summary>
    internal static int Percent(params Outcome[] kinds)
    {
        int count = 0;
        foreach (var f in Faces) if (Array.IndexOf(kinds, f) >= 0) count++;
        return count * 100 / Faces.Length;
    }
    private sealed class IslandState
    {
        public int LastSpin = -100;   // build turn of the last spin
        public int Pending = -1;      // build turn of a spin whose face is not known yet
        public int Face = -1;         // the last face
        public int FaceTurn = -1;     // the disasters phase it came in
    }
    private static readonly Dictionary<IntPtr, IslandState> States = new();   // by IslandGrid

    /// <summary>The local player's face just came: (face index, outcome).</summary>
    internal static event Action<int, Outcome> LocalResult;

    internal static void Install(Patcher patcher)
    {
        Core.BuildActions.Register(SpinAction, "Casino (spin)", Spin);
        patcher.Patch(typeof(DisastersPhase), "RandomOccurrences", typeof(Wheel), postfix: nameof(RandomPostfix));
        patcher.Patch(typeof(DisastersPhase), "ExecuteOccurrences", typeof(Wheel), prefix: nameof(ExecutePrefix));
        Core.Match.Started += States.Clear;
        Core.Match.Ended += States.Clear;
    }

    private static IslandState StateOf(IslandGrid grid)
    {
        if (!States.TryGetValue(grid.Pointer, out var s)) States[grid.Pointer] = s = new IslandState();
        return s;
    }

    /// <summary>A spin from an earlier turn waits for this disasters phase (normally the next turn's; a later one if that phase never came).</summary>
    private static bool Due(IslandState state, int turn) => state.Pending >= 0 && state.Pending < turn;

    /// <summary>The face a seed gives (spread out, so it does not follow what the Starvation itself does with the seed).</summary>
    internal static int FaceOf(uint seed)
    {
        uint h = seed * 2654435761u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return (int)(h % (uint)Faces.Length);
    }

    // ------------------------------------------------------------------ 1. the spin (a build action, every computer)

    private static bool Spin(BuildData data, int casinoId)
    {
        if (!Casino.On) return false;
        var island = data.Island;
        var grid = island?.grid;
        var stats = island?.statistics;
        var game = data.GameState;
        if (grid == null || stats == null || game == null || island.Owner == null || island.Owner.IsAI) return false;
        if (!CasinoType.IsCasino(grid, casinoId)) return false;   // any of the island's Casinos (the host may allow more than one); one wheel per island
        int turn = game.TurnCounter;
        var state = StateOf(grid);
        if (state.Pending >= 0 || turn - state.LastSpin < Cooldown) return false;
        if (stats.Storage.stored.gold < Stake) return false;
        stats.RemoveResource(new ResourceAmount { gold = Stake });
        state.LastSpin = turn;
        state.Pending = turn;
        Plugin.L.LogInfo($"turn {turn}: {island.Owner.UserName} spun the wheel ({Stake} gold); it turns in the next disasters phase");
        return true;
    }

    // ------------------------------------------------------------------ 2. the host adds the disasters a face asks for

    public static void RandomPostfix(DisastersPhase __instance, ref Il2CppReferenceArray<DisasterOccurrence> __result)
    {
        try
        {
            if (!Casino.On || __result == null || States.Count == 0) return;
            var game = __instance.GameState;
            var disasters = __instance.disasters;
            if (game == null || disasters == null) return;
            int turn = game.TurnCounter;
            var added = new List<DisasterOccurrence>();
            foreach (var p in Core.Players.All)
            {
                var owner = p.Game;
                var grid = p.Island?.grid;
                if (owner == null || grid == null || !States.TryGetValue(grid.Pointer, out var state) || !Due(state, turn)) continue;
                if (!SeedOf(__result, disasters, owner, out uint seed)) continue;
                uint k = 1;
                switch (Faces[FaceOf(seed)])
                {
                    case Outcome.Jackpot:   // a tsunami on every other player (not team-mates, not the defeated)
                        foreach (var other in Others(owner)) added.Add(new DisasterOccurrence(disasters.tsunami, owner, other, Next(seed, k++)));
                        break;
                    case Outcome.Robbers:   // robbers from another island steal from you (they get what you lose)
                        var others = Others(owner);
                        if (others.Count > 0) added.Add(new DisasterOccurrence(disasters.robbers, others[(int)((seed >> 7) % (uint)others.Count)], owner, Next(seed, k++)));
                        break;
                    case Outcome.Fire:
                        added.Add(new DisasterOccurrence(disasters.fire, owner, owner, Next(seed, k++)));
                        break;
                    case Outcome.Catastrophe:
                        added.Add(new DisasterOccurrence(disasters.tsunami, owner, owner, Next(seed, k++)));
                        added.Add(new DisasterOccurrence(disasters.meteorite, owner, owner, Next(seed, k++)));
                        break;
                }
            }
            if (added.Count == 0) return;
            var list = new Il2CppReferenceArray<DisasterOccurrence>(__result.Length + added.Count);
            for (int i = 0; i < __result.Length; i++) list[i] = __result[i];
            for (int i = 0; i < added.Count; i++) list[__result.Length + i] = added[i];
            __result = list;
            Plugin.L.LogInfo($"turn {turn}: the wheel added {added.Count} disaster(s)");
        }
        catch (Exception e) { Plugin.Once("random", "casino (disasters): " + e); }
    }

    private static uint Next(uint seed, uint k) => seed * 747796405u + k * 2891336453u;

    private static List<Player> Others(Player owner)
    {
        var list = new List<Player>();
        foreach (var p in Core.Players.All)
        {
            var other = p.Game;
            if (other == null || other.Pointer == owner.Pointer || other.IsDefeated || !Team.Different(other, owner)) continue;
            list.Add(other);
        }
        return list;
    }

    /// <summary>The seed of the island's Starvation entry (every island gets one every turn).</summary>
    private static bool SeedOf(Il2CppReferenceArray<DisasterOccurrence> list, Disasters disasters, Player owner, out uint seed)
    {
        seed = 0;
        IntPtr starvation = disasters.starvation?.Pointer ?? IntPtr.Zero;
        foreach (var o in list)
        {
            if (o == null || o.Disaster == null || o.Disaster.Pointer != starvation) continue;
            var target = o.Target;
            if (target != null && target.Pointer == owner.Pointer) { seed = o.Seed; return true; }
        }
        return false;
    }

    // ------------------------------------------------------------------ 3. every computer: the face, the winnings, the wheel

    // the game's disaster list held on the spinner's computer until the wheel has stopped (see ExecutePrefix)
    private static DisastersPhase _heldPhase;
    private static Il2CppReferenceArray<DisasterOccurrence> _heldList;
    private static Il2CppSystem.Action _heldEnd;
    private static bool _replaying;

    /// <summary>
    /// Every computer, when the disaster list arrives: the face, the winnings. On the spinner's computer the game's disasters then wait until the
    /// wheel has stopped (at game speed 3 a fire burnt a building before the wheel showed why, 2026-11-21): this only delays what that screen shows
    /// (the game waits for every player to finish watching the disasters anyway); the state changes are the same and in the same order.
    /// </summary>
    public static bool ExecutePrefix(DisastersPhase __instance, Il2CppReferenceArray<DisasterOccurrence> __0, Il2CppSystem.Action __1)
    {
        if (_replaying) return true;
        int localFace = -1;
        var localOutcome = Outcome.Nothing;
        try
        {
            if (!Casino.On || __0 == null || States.Count == 0) return true;
            var game = __instance.GameState;
            var disasters = __instance.disasters;
            if (game == null || disasters == null) return true;
            int turn = game.TurnCounter;
            foreach (var p in Core.Players.All)
            {
                var owner = p.Game;
                var island = p.Island;
                var grid = island?.grid;
                if (owner == null || grid == null || !States.TryGetValue(grid.Pointer, out var state) || !Due(state, turn)) continue;
                state.Pending = -1;
                if (!SeedOf(__0, disasters, owner, out uint seed)) continue;
                int face = FaceOf(seed);
                var outcome = Faces[face];
                state.Face = face;
                state.FaceTurn = turn;
                var stats = island.statistics;
                if (outcome == Outcome.Gold) stats?.AddResource(new ResourceAmount { gold = Casino.WinGold });
                else if (outcome == Outcome.Each) { int e = Casino.WinEach; stats?.AddResource(new ResourceAmount { food = e, wood = e, stone = e, gold = e, hammer = e, military = e }); }
                Plugin.L.LogInfo($"turn {turn}: {p.Name}'s wheel stopped on face {face}: {outcome}");
                if (p.IsLocal) { localFace = face; localOutcome = outcome; }
            }
        }
        catch (Exception e) { Plugin.Once("execute", "casino (wheel): " + e); }
        if (localFace < 0 || LocalResult == null) return true;
        // hold the game's disasters, show the wheel, let them run when it stops (Release)
        _heldPhase = __instance; _heldList = __0; _heldEnd = __1;
        try { LocalResult(localFace, localOutcome); }
        catch (Exception e) { Plugin.Once("show", "casino (wheel window): " + e); Release(); }
        return false;   // held (or, if the wheel could not be shown, already run by Release)
    }

    /// <summary>The wheel has stopped (or could not be shown): the game's disasters run now.</summary>
    internal static void Release()
    {
        var phase = _heldPhase;
        if (phase == null) return;
        var list = _heldList; var end = _heldEnd;
        _heldPhase = null; _heldList = null; _heldEnd = null;
        if (!Core.Match.Running) return;   // the match ended while the wheel turned (the window closed with it): these disasters belong to no match now
        _replaying = true;
        try { phase.ExecuteOccurrences(list, end); }
        catch (Exception e) { Plugin.L.LogError("casino: the held disasters could not run: " + e); }
        finally { _replaying = false; }
    }

    internal static bool Holding => _heldPhase != null;
    // ------------------------------------------------------------------ for the window

    internal static (bool pending, int nextTurn, int face, int faceTurn) StateFor(Island island)
    {
        if (island?.grid == null || !States.TryGetValue(island.grid.Pointer, out var s)) return (false, 0, -1, -1);
        return (s.Pending >= 0, s.LastSpin + Cooldown, s.Face, s.FaceTurn);
    }
}
