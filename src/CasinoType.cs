using System;
using DiceKingdoms.Game;
using DiceKingdoms.Interface;
using DiceKingdoms.Network;
using DiceModders.ModCore.Tools;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DiceModders.Casino;

using Core = DiceModders.ModCore.ModCore;

/// <summary>
/// The Casino as the game's 25th building type. The game keeps its building types in a fixed list (BuildingTypes.Types, 24 entries in
/// BuildingTypeName order) and sends buildings over the network by their place in that list (BuildingSerialization). So the Casino is added
/// at the END of that list on every computer, always (whether the lobby rule is on or not), so every computer has the same list:
///  * BuildingTypes.Init postfix: append to Types and typesByName (the game calls Init when its data loads).
///  * BuildingSerialization.Awake postfix: its type -> index table includes the Casino.
///  * BuildInterface.InitBuildingButtons prefix: the build bar gets a Casino button while the rule is on.
///  * BuildData.CanBuildType postfix: only while the rule is on, only with a University (same check on every computer). How many Casinos an
///    island may have is the Building limits mod's business (its Casino number, 1 by default, while its rule is on).
/// Its name is BuildingTypeName 24, so the game's texts are "building.24.name" / ".effect" / ".lore" (texts.json, gamekey:).
/// </summary>
internal static class CasinoType
{
    internal const BuildingTypeName Name = (BuildingTypeName)24;
    internal static BuildingType Type;
    /// <summary>The Market type: the Casino borrows its shape, and its look is made from the Market each player sees (CasinoLook).</summary>
    internal static BuildingType Market;

    internal static void Install(Patcher patcher)
    {
        patcher.Patch(typeof(BuildingTypes), "Init", typeof(CasinoType), postfix: nameof(InitPostfix));
        patcher.Patch(typeof(BuildingSerialization), "Awake", typeof(CasinoType), postfix: nameof(SerializationPostfix));
        patcher.Patch(typeof(BuildInterface), "InitBuildingButtons", typeof(CasinoType), prefix: nameof(ButtonsPrefix));
        patcher.Patch(typeof(BuildData), "CanBuildType", typeof(CasinoType), postfix: nameof(CanBuildPostfix));
        // locked (the game's lock icon on the button) until the island has a University, like the buildings a technology unlocks
        patcher.Patch(typeof(BuildData), "Unlocked", typeof(CasinoType), postfix: nameof(UnlockedPostfix));
        // PoolingBuffering.Awake fills a fixed table "how many copies of each building's model to make ready" (buildingTypeFactors: one entry
        // per original type) and, at match start, reads it for EVERY type in Types: the Casino has no entry -> KeyNotFoundException, the match
        // never starts (2026-11-21). Give it the Market's number (1) before the game reads the table.
        patcher.Patch(typeof(PoolingBuffering.__c__DisplayClass4_0), "_Awake_b__0", typeof(CasinoType), prefix: nameof(PoolingPrefix));
        // one Casino per island by default where the host can limit buildings (the Building limits mod reads it; the host can change it)
        Core.Buildings.SuggestLimit(Name, 1);
        // the game's data may already be loaded (the type list made) before this mod: add it to what exists
        Core.Civilizations.GameDataLoaded += AddToExisting;
        AddToExisting();
    }

    private static void AddToExisting()
    {
        try
        {
            foreach (var types in Resources.FindObjectsOfTypeAll<BuildingTypes>()) Append(types);
            foreach (var s in Resources.FindObjectsOfTypeAll<BuildingSerialization>()) AddIndex(s);
        }
        catch (Exception e) { Plugin.L.LogWarning("casino type (existing data): " + e.Message); }
    }

    public static void InitPostfix(BuildingTypes __instance)
    {
        try { Append(__instance); }
        catch (Exception e) { Plugin.Once("init", "casino type: " + e); }
    }

    /// <summary>Makes the Casino type once (from the Market: its 4-tile shape and, for now, its look) and puts it at the end of the list.</summary>
    private static void Append(BuildingTypes types)
    {
        var list = types?.Types;
        if (list == null || list.Length == 0) return;
        foreach (var t in list) if (t != null && t.name == Name) return;   // already there
        var market = types.market;
        if (market == null) return;
        Market = market;
        if (Type == null)
        {
            Type = new BuildingType
            {
                name = Name,
                localPositions = market.localPositions,
                localPositionsInWater = market.localPositionsInWater,
                heightOverrides = market.heightOverrides,
                visualPrefab = market.visualPrefab,
                storageCapacity = new ResourceAmount(),
                cost = new ResourceAmount { wood = 3, stone = 2, gold = 2, hammer = 1 },
                tier = 2,
                bulkSize = market.bulkSize,
                health = 20,
                fortification = false,
                garrison = false,
                pillageReward = new ResourceAmount { gold = 3 },
                remainsReward = new ResourceAmount { wood = 1, stone = 1 },
                hasEffectDescription = true,
            };
        }
        var bigger = new Il2CppReferenceArray<BuildingType>(list.Length + 1);
        for (int i = 0; i < list.Length; i++) bigger[i] = list[i];
        bigger[list.Length] = Type;
        types.Types = bigger;
        var byName = types.typesByName;
        if (byName != null && !byName.ContainsKey(Name)) byName.Add(Name, Type);
        Plugin.L.LogInfo($"casino: added as building type {list.Length} of {bigger.Length}");
    }

    public static void PoolingPrefix(PoolingBuffering.__c__DisplayClass4_0 __instance)
    {
        try
        {
            var factors = __instance.buildingTypeFactors;
            if (Type != null && factors != null && !factors.ContainsKey(Type)) factors.Add(Type, 1);
        }
        catch (Exception e) { Plugin.Once("pooling", "casino (model pool): " + e); }
    }

    public static void SerializationPostfix(BuildingSerialization __instance)
    {
        try { AddIndex(__instance); }
        catch (Exception e) { Plugin.Once("serialization", "casino type (network): " + e); }
    }

    private static void AddIndex(BuildingSerialization s)
    {
        if (Type == null || s?.indexes == null || s.buildingTypes?.Types == null) return;
        var list = s.buildingTypes.Types;
        for (int i = 0; i < list.Length; i++)
            if (list[i] != null && list[i].Pointer == Type.Pointer)
            {
                if (!s.indexes.ContainsKey(Type)) s.indexes.Add(Type, i);
                return;
            }
    }

    /// <summary>The build bar: the Casino button while the rule is on (the game makes the buttons from this list when a match starts).</summary>
    public static void ButtonsPrefix(BuildInterface __instance)
    {
        try
        {
            var list = __instance.buildableBuildingTypes;
            if (list == null || Type == null) return;
            int at = -1;
            for (int i = 0; i < list.Length; i++) if (list[i] != null && list[i].Pointer == Type.Pointer) { at = i; break; }
            bool want = Casino.On;
            if (want == (at >= 0)) return;
            var changed = new Il2CppReferenceArray<BuildingType>(want ? list.Length + 1 : list.Length - 1);
            int n = 0;
            for (int i = 0; i < list.Length; i++) if (i != at) changed[n++] = list[i];
            if (want) changed[n] = Type;
            __instance.buildableBuildingTypes = changed;
        }
        catch (Exception e) { Plugin.Once("buttons", "casino button: " + e); }
    }

    /// <summary>Only while the rule is on, and only with a University. Runs on every computer at the same step (DoAction), so all agree.</summary>
    public static void CanBuildPostfix(BuildData __instance, BuildingType __0, ref bool __result)
    {
        try
        {
            if (!__result || __0 == null || __0.name != Name) return;
            if (!Casino.On) { __result = false; return; }
            var island = __instance.Island;
            if (island?.Owner == null || island.Owner.IsAI) { __result = false; return; }   // computer players do not gamble
            var grid = island.grid;
            if (grid == null || !HasUniversity(grid)) __result = false;   // only with a University (no limit of its own: see the Building limits mod)
        }
        catch (Exception e) { Plugin.Once("canbuild", "casino limit: " + e); }
    }

    /// <summary>The Casino is unlocked by a University on the island (same answer on every computer: it reads the island).</summary>
    public static void UnlockedPostfix(BuildData __instance, BuildingType __0, ref bool __result)
    {
        try
        {
            if (__0 == null || __0.name != Name) return;
            var grid = __instance.Island?.grid;
            __result = grid != null && HasUniversity(grid);
        }
        catch (Exception e) { Plugin.Once("unlocked", "casino lock: " + e); }
    }

    /// <summary>The island's Casino (building id), or -1.</summary>
    internal static int Find(IslandGrid grid)
    {
        if (grid == null) return -1;
        var e = grid.Buildings.GetEnumerator();
        while (e.MoveNext())
        {
            var b = e.Current;
            if (b.type != null && b.type.name == Name) return Building.Id.OfPosition(b.transform.position).value;
        }
        return -1;
    }

    private static bool HasUniversity(IslandGrid grid)
    {
        var e = grid.Buildings.GetEnumerator();
        while (e.MoveNext()) { var b = e.Current; if (b.type != null && b.type.name == BuildingTypeName.UNIVERSITY) return true; }
        return false;
    }

    internal static int Count(IslandGrid grid)
    {
        int n = 0;
        var e = grid.Buildings.GetEnumerator();
        while (e.MoveNext()) { var b = e.Current; if (b.type != null && b.type.name == Name) n++; }
        return n;
    }
}
