using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using DiceKingdoms.Game;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Tools = DiceModders.ModCore.Tools;

namespace DiceModders.Casino;

using Core = DiceModders.ModCore.ModCore;

/// <summary>
/// The Casino's look. One model per building style (models/casino-&lt;style&gt;.dkmesh.gz, embedded): the five civilizations, Japan and the Europe reskin.
/// The game asks <c>PlayerSkins.GetBuildingVisualPrefab</c> for a building's prefab; for the Casino this postfix (after every other mod's) asks the same
/// player's skins for the MARKET they would see (their civilization, a reskin or a civilization mod), picks the style from that market's meshes and returns
/// a copy of that market prefab with the Casino model in it. So materials, player colours, the burnt look, fires and the 4-tile shape stay the game's.
/// The copies get their own pictures (taken in the game, cached) for the build bar and the other screens.
/// </summary>
internal static class CasinoLook
{
    internal static readonly string[] Styles = { "europe", "middle-east", "viking", "ancient", "maya", "japan", "reskin" };
    private static readonly string[] Tokens = { "europe", "middle-east", "viking", "ancient", "maya" };

    private static readonly Dictionary<IntPtr, BuildingVisual> Copies = new();   // by the market prefab they were made from
    private static readonly Dictionary<string, Mesh> Meshes = new();
    private static readonly Dictionary<string, Mesh> SlotMeshes = new();
    private static Mesh _empty;
    private static int _loadedFrame = -1;
    private static bool _inside;

    internal static void Install(Tools.Patcher patcher)
    {
        try
        {
            var target = AccessTools.Method(typeof(PlayerSkins), "GetBuildingVisualPrefab");
            // last, after ModCore's civilization skins and every civilization mod: the Casino look is decided on top of theirs
            patcher.Harmony.Patch(target, postfix: new HarmonyMethod(typeof(CasinoLook), nameof(LookPostfix)) { priority = Priority.Last });
        }
        catch (Exception e) { Plugin.L.LogError("casino look hook: " + e); }
        Core.Civilizations.WhenGameDataLoaded(() =>
        {
            _loadedFrame = Time.frameCount;
            Later.Run(Prepare);   // next frame: every civilization mod has made its copies by then
        });
    }

    /// <summary>The copies and their pictures for every civilization, made at the main menu so the build bar has them in the first match.</summary>
    private static void Prepare()
    {
        try
        {
            var reg = Resources.FindObjectsOfTypeAll<DiceKingdoms.Interface.SkinRegistry>();
            if (reg.Length == 0 || CasinoType.Type == null) return;
            var ids = new List<(int id, int look)> { (0, 0), (1, 1), (2, 2), (3, 3), (4, 4) };
            foreach (var c in Core.Civilizations.All) ids.Add((c.Id, c.SkinsLike));
            foreach (var (id, look) in ids)
            {
                try
                {
                    // like the game's own reader: a civilization mod's player gets the look-alike's skins carrying the real number
                    var skins = new PlayerSkins((DiceKingdoms.Interface.Civilization)look, reg[0]);
                    if (id != look) skins.civilization = (DiceKingdoms.Interface.Civilization)id;
                    skins.GetBuildingVisualPrefab(CasinoType.Type);
                }
                catch (Exception e) { Plugin.Once("prepare" + id, "casino look for civilization " + id + ": " + e.Message); }
            }
            Plugin.L.LogInfo("casino looks ready: " + Copies.Count);
        }
        catch (Exception e) { Plugin.Once("prepare", "casino looks: " + e); }
    }

    public static void LookPostfix(PlayerSkins __instance, BuildingType __0, ref BuildingVisual __result)
    {
        if (_inside || __0 == null || __0.name != CasinoType.Name || __instance == null || CasinoType.Market == null) return;
        if (_loadedFrame < 0 || Time.frameCount <= _loadedFrame) return;   // while the civilization mods copy every building: leave it to them
        _inside = true;
        try
        {
            var market = __instance.GetBuildingVisualPrefab(CasinoType.Market);
            if (market == null) return;
            if (Copies.TryGetValue(market.Pointer, out var copy) && copy != null) { __result = copy; return; }
            copy = Make(market);
            if (copy != null) { Copies[market.Pointer] = copy; __result = copy; }
        }
        catch (Exception e) { Plugin.Once("look", "casino look: " + e); }
        finally { _inside = false; }
    }

    private static BuildingVisual Make(BuildingVisual market)
    {
        string style = StyleOf(market);
        var mesh = MeshOf(style);
        if (mesh == null) return null;
        var copy = Tools.Prefabs.Copy(market);   // keeps the game's naming ("Market Viking"): pooling expects it
        int filled = 0, blanked = 0;
        foreach (var mf in copy.GetComponentsInChildren<MeshFilter>(true))
        {
            if (GroupOf(mf.gameObject.name) == null) continue;
            var parent = mf.transform.parent;
            bool on = parent != null && parent.gameObject.activeSelf && !(mf.sharedMesh != null && mf.sharedMesh.name == "custom-empty");
            if (on) { mf.sharedMesh = ForSlots(mesh, style, mf.GetComponent<MeshRenderer>()); filled++; }
            else { mf.sharedMesh = Empty; blanked++; }
        }
        foreach (var lod in copy.GetComponentsInChildren<LODGroup>(true))
            try { lod.RecalculateBounds(); } catch { }
        Plugin.L.LogInfo($"casino look '{style}' made from '{market.gameObject.name}' ({filled} meshes, {blanked} blanked)");

        var first = copy.icons != null && copy.icons.Length > 0 ? copy.icons[0] : null;
        int w = first != null ? Mathf.RoundToInt(first.rect.width) : 256, h = first != null ? Mathf.RoundToInt(first.rect.height) : 256;
        var target = copy;
        Tools.IconRenderer.PlayerColours(Plugin.Guid + " casino-" + style, copy.gameObject, Tools.IconView.Building, w, h, sprites =>
        {
            if (target == null || sprites == null) return;
            int n = target.icons != null ? target.icons.Length : sprites.Length;
            var icons = new Il2CppReferenceArray<Sprite>(n);
            for (int i = 0; i < n; i++) icons[i] = i < sprites.Length ? sprites[i] : (target.icons != null ? target.icons[i] : null);
            target.icons = icons;
        });
        return copy;
    }

    /// <summary>The style of a market prefab: Japan's and the reskin's models are named "custom-...", the game's by their group ("market-viking-mesh_LOD0").</summary>
    private static string StyleOf(BuildingVisual market)
    {
        string token = null; bool custom = false, japan = false;
        foreach (var mf in market.GetComponentsInChildren<MeshFilter>(true))
        {
            var group = GroupOf(mf.gameObject.name);
            if (group == null) continue;
            var parent = mf.transform.parent;
            if (parent == null || !parent.gameObject.activeSelf) continue;
            var m = mf.sharedMesh != null ? mf.sharedMesh.name : "";
            if (m == "custom-empty") continue;
            if (m.StartsWith("custom-jp-", StringComparison.Ordinal)) japan = true;
            else if (m.StartsWith("custom-", StringComparison.Ordinal)) custom = true;
            if (token == null) foreach (var t in Tokens) if (group.EndsWith("-" + t, StringComparison.Ordinal)) { token = t; break; }
        }
        if (japan) return "japan";
        token ??= "europe";
        if (custom && token == "europe") return "reskin";
        return token;
    }

    private static Mesh MeshOf(string style)
    {
        if (Meshes.TryGetValue(style, out var m) && m != null) return m;
        var asm = typeof(CasinoLook).Assembly;
        string res = "models/casino-" + style + ".dkmesh.gz";
        if (asm.GetManifestResourceInfo(res) == null) { Plugin.Once("model:" + style, "casino model missing: " + res); return null; }
        byte[] gz;
        using (var s = asm.GetManifestResourceStream(res))
        using (var copy = new MemoryStream()) { s.CopyTo(copy); gz = copy.ToArray(); }
        // ModCore keeps the parsed model on disk under this version: with the mod's version alone a new model in the same version was never read
        // (the old one came from the cache). The model's own fingerprint makes every changed model count as new.
        string version = Plugin.Version + "+" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(gz)).Substring(0, 12);
        m = Tools.Meshes.LoadDkmesh("casino-" + style, () => new GZipStream(new MemoryStream(gz), CompressionMode.Decompress), version, out _, out _);
        m.name = "casino-" + style;
        m.hideFlags = HideFlags.DontUnloadUnusedAsset;
        Meshes[style] = m;
        return m;
    }

    /// <summary>One material: the mesh as it is. Several: the model in the Colored-Paper slot, the other slots empty.</summary>
    private static Mesh ForSlots(Mesh mesh, string style, MeshRenderer r)
    {
        var mats = r != null ? r.sharedMaterials : null;
        if (mats == null || mats.Length <= 1) return mesh;
        int slot = 0;
        for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name.StartsWith("Colored-Paper", StringComparison.Ordinal)) { slot = i; break; }
        string key = style + "/" + slot + "/" + mats.Length;
        if (SlotMeshes.TryGetValue(key, out var c) && c != null) return c;
        c = UnityEngine.Object.Instantiate(mesh);
        c.name = mesh.name + "#" + key;
        c.hideFlags = HideFlags.DontUnloadUnusedAsset;
        var tris = mesh.triangles;
        c.subMeshCount = mats.Length;
        for (int i = 0; i < mats.Length; i++) c.SetTriangles(i == slot ? tris : new int[0], i);
        SlotMeshes[key] = c;
        return c;
    }

    private static Mesh Empty
    {
        get
        {
            if (_empty == null) { _empty = new Mesh { name = "custom-empty" }; _empty.hideFlags = HideFlags.DontUnloadUnusedAsset; }
            return _empty;
        }
    }

    /// <summary>"market-viking-mesh_LOD1" -> "market-viking"; null for other objects.</summary>
    private static string GroupOf(string name)
    {
        int i = name.IndexOf("-mesh_LOD", StringComparison.Ordinal);
        return i > 0 ? name.Substring(0, i) : null;
    }
}
