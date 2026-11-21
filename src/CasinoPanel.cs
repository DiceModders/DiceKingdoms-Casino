using System;
using DiceKingdoms.Interface;
using DiceModders.ModCore.Tools;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace DiceModders.Casino;

using Core = DiceModders.ModCore.ModCore;

/// <summary>
/// While a Casino is selected, the build bar's research button (the flask next to the clean-tile tool; it opens the tech tree) becomes the wheel
/// button: a small wheel, "Wheel of fortune", opens the wheel window. Deselected, it is the research button again.
/// <para>
/// When the Casino is selected comes from the game itself: BuildingInformation shows the selected building's panel (specializedPanels, by building
/// type, filled in BuildInterface.InitBuildingButtons; the game has them for the Market and the University) and hides it again, every frame. The Casino
/// gets an empty panel there whose OnEnable / OnDisable swap the button; nothing is polled.
/// </para>
/// </summary>
internal static class CasinoPanel
{
    private static SpecializedPanel _sensor;      // empty: the game switches it on while a Casino is selected
    private static BuildInterface _build;
    private static ButtonIcon _swapped;           // the research button while it is the wheel button
    private static Sprite _savedIcon;
    private static Il2CppSystem.Action _savedClick;
    private static StructuredText _savedTip;
    private static Sprite _icon;
    private static Wheel.Outcome[] _iconFaces;
    private static Action _click;                 // kept alive: the game holds only the il2cpp delegate
    private static Il2CppSystem.Action _nativeClick;

    internal static void Install(Patcher patcher)
    {
        // a postfix: the game has just added the Market's and the University's panels; the Casino's comes after them
        patcher.Patch(typeof(BuildInterface), "InitBuildingButtons", typeof(CasinoPanel), postfix: nameof(ButtonsPostfix));
        // the game writes the research tooltip again whenever the stored resources change
        patcher.Patch(typeof(BuildInterface), "RefreshButtons", typeof(CasinoPanel), postfix: nameof(RefreshPostfix));
        Core.Texts.LanguageChanged += () => { if (_swapped != null) SetTip(_swapped); };
    }

    public static void ButtonsPostfix(BuildInterface __instance)
    {
        try { Add(__instance); }
        catch (Exception e) { Plugin.Once("panel", "casino panel: " + e); }
    }

    public static void RefreshPostfix(BuildInterface __instance)
    {
        try { if (_swapped != null && _build != null && __instance.Pointer == _build.Pointer) SetTip(_swapped); }
        catch (Exception e) { Plugin.Once("panel-refresh", "casino button tooltip: " + e.Message); }
    }

    private static void Add(BuildInterface build)
    {
        var info = build?.buildingInformation;
        if (info == null || CasinoType.Type == null) return;
        _build = build;
        if (_sensor == null)   // Unity check: also true when the old one went with its scene
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<CasinoSelected>()) ClassInjector.RegisterTypeInIl2Cpp<CasinoSelected>();
            // made under a switched-off holder: its scripts wake up (Awake, OnEnable) only when the game first shows it for a selected Casino
            var holder = new GameObject("DM Casino holder");
            holder.SetActive(false);
            try
            {
                var go = new GameObject("Casino");
                go.transform.SetParent(holder.transform, false);
                var rect = go.AddComponent<RectTransform>();
                _sensor = go.AddComponent<SpecializedPanel>();
                _sensor.rectTransform = rect;   // SpecializedPanel.Awake fills it, AddSpecializedPanel needs it before that
                go.AddComponent<CasinoSelected>();
                info.AddSpecializedPanel(CasinoType.Type, _sensor);   // moves it next to the game's panels, switched off
            }
            finally { Object.Destroy(holder); }
        }
        else info.AddSpecializedPanel(CasinoType.Type, _sensor);
    }

    /// <summary>A Casino was selected (true) or deselected (false).</summary>
    internal static void Selected(bool on)
    {
        try { if (on) SwapIn(); else SwapOut(); }
        catch (Exception e) { Plugin.Once("panel-swap", "casino button: " + e); }
    }

    private static void SwapIn()
    {
        if (_swapped != null || _build == null) return;
        var button = _build.researchButton != null ? _build.researchButton.TryCast<ButtonIcon>() : null;
        if (button == null) return;
        _savedIcon = button.Icon;
        _savedClick = button.onClick;
        var tip = button.GetComponent<HasToolTip>();
        _savedTip = tip != null ? tip.text : null;

        _click ??= Click;
        _nativeClick ??= DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(_click);
        button.onClick = _nativeClick;
        button.Icon = Icon();
        SetTip(button);
        _swapped = button;
    }

    private static void SwapOut()
    {
        var button = _swapped;
        _swapped = null;
        if (button == null) return;   // Unity check: gone with its scene, nothing to give back
        button.onClick = _savedClick;
        button.Icon = _savedIcon;
        var tip = button.GetComponent<HasToolTip>();
        if (tip != null && _savedTip != null) tip.text = _savedTip;
        _savedClick = null; _savedIcon = null; _savedTip = null;
    }

    private static void Click()
    {
        try { WheelWindow.Open(); }
        catch (Exception e) { Plugin.L.LogWarning("casino button: " + e.Message); }
        try { EventSystem.current?.SetSelectedGameObject(null); } catch { }   // Unity keeps a clicked button selected otherwise
    }

    private static void SetTip(ButtonIcon button)
    {
        var tip = button.GetComponent<HasToolTip>();
        if (tip == null) return;
        var args = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object>(1);
        args[0] = (Il2CppSystem.String)Tx.T("window.title");
        tip.text = new StructuredText(Il2CppSystem.Runtime.CompilerServices.FormattableStringFactory.Create("{0}", args));
    }

    /// <summary>A small wheel (the faces of this match's wheel).</summary>
    private static Sprite Icon()
    {
        var faces = Wheel.Faces;
        if (_icon == null || _iconFaces != faces)
        {
            var texture = WheelArt.Disc(96, faces);
            _icon = Sprite.Create(texture, new Rect(0f, 0f, 96f, 96f), new Vector2(0.5f, 0.5f), 100f);
            _icon.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _iconFaces = faces;
        }
        return _icon;
    }
}

/// <summary>On the Casino's empty panel: the game switches that panel on while a Casino is selected and off again.</summary>
internal sealed class CasinoSelected : MonoBehaviour
{
    public CasinoSelected(IntPtr ptr) : base(ptr) { }
    public void OnEnable() => CasinoPanel.Selected(true);
    public void OnDisable() => CasinoPanel.Selected(false);
}
