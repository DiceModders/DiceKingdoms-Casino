using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace DiceModders.Casino;

/// <summary>Runs work in a later frame (not the current one). A hidden object exists only while something waits.</summary>
internal static class Later
{
    private static readonly List<(int frame, Action work)> Pending = new();
    private static GameObject _go;

    internal static void Run(Action work)
    {
        Pending.Add((Time.frameCount, work));
        if (_go == null)
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<CasinoLater>()) ClassInjector.RegisterTypeInIl2Cpp<CasinoLater>();
            _go = new GameObject("DiceModders Casino Later") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _go.AddComponent<CasinoLater>();
        }
        _go.SetActive(true);
    }

    internal static void Tick()
    {
        int now = Time.frameCount;
        var due = Pending.FindAll(p => p.frame < now);
        Pending.RemoveAll(p => p.frame < now);
        foreach (var (_, work) in due)
        {
            try { work(); } catch (Exception e) { Plugin.Once("later", "casino (later): " + e); }
        }
        if (Pending.Count == 0 && _go != null) _go.SetActive(false);
    }
}

internal sealed class CasinoLater : MonoBehaviour
{
    public CasinoLater(IntPtr ptr) : base(ptr) { }
    private void Update() => Later.Tick();
}
