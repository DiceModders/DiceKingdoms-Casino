using System;
using DiceKingdoms.Interface;
using DiceModders.ModCore;
using DiceModders.ModCore.Tools;
using DiceModders.UiKit;
using DiceModders.UiKit.Custom;
using DiceModders.UiKit.Native;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.UI;

namespace DiceModders.Casino;

using Core = DiceModders.ModCore.ModCore;

/// <summary>
/// The Casino window (top bar, while the rule is on): the wheel with its 20 faces, the odds, the spin button and what is going on. The wheel turns
/// when its face is known (the disasters phase after the spin). It redraws on the game's phase changes, a spin and a result; the wheel's motion is
/// the only thing that runs every frame, and only while it turns.
/// </summary>
internal static class WheelWindow
{
    private static UiScope _ui;
    private static TopBarButtonHandle _button;
    private static UiWindow _window;
    private static UiImage _wheel, _pointer;
    private static UiButton _spin;
    private static UiText _status, _result;
    private static Texture2D _wheelTexture, _pointerTexture;
    private static Wheel.Outcome[] _texturedFaces;
    private static float _angle;            // where the wheel stands (degrees, Unity z)
    private static int _shownFace = -1;     // the face the text below the wheel talks about
    private static BuildInterface _build;   // the game's build interface while the build phase is shown (it holds your BuildData)

    internal static void Install(UiScope ui, Patcher patcher)
    {
        _ui = ui;
        _ui?.WhenReady(s => { _button = s.Native.TopBar.AddIconButton("gold", Tx.T("window.tooltip"), Toggle, 22); Refresh(); });
        Core.Texts.LanguageChanged += () => { _button?.SetTooltip(Tx.T("window.tooltip")); if (_window != null) { _window.Close(); Toggle(); } };
        Core.Match.Started += () => { _shownFace = -1; _angle = 0f; Refresh(); };
        Core.Match.Ended += Refresh;
        Core.Match.PhaseEntered += _ => { Refresh(); Redraw(); };
        Wheel.LocalResult += Show;
        patcher.Patch(typeof(BuildInterface), "OnEnable", typeof(WheelWindow), postfix: nameof(BuildShown));
        patcher.Patch(typeof(BuildInterface), "OnDisable", typeof(WheelWindow), postfix: nameof(BuildHidden));
        // a clicked Casino shows its own panel with a wheel button (CasinoPanel); that button opens this window
    }

    public static void BuildShown(BuildInterface __instance) { _build = __instance; Redraw(); }
    public static void BuildHidden(BuildInterface __instance)
    {
        if (_build != null && __instance != null && _build.Pointer == __instance.Pointer) _build = null;
        Redraw();
    }

    internal static void Refresh()
    {
        bool show = Casino.On && Core.Match.Running;
        _button?.SetVisible(show);
        if (!show) _window?.Close();
    }

    private static void Toggle()
    {
        if (_window != null) { _window.Close(); return; }
        Open();
    }

    internal static void Open()
    {
        if (_window != null || !Casino.On) return;
        var faces = Wheel.Faces;
        if (_wheelTexture == null || _texturedFaces != faces)
        {
            if (_wheelTexture != null) UnityEngine.Object.Destroy(_wheelTexture);   // the window is closed here: the old picture is not shown anywhere
            _wheelTexture = WheelArt.Disc(384, faces);
            _texturedFaces = faces;
        }
        if (_pointerTexture == null) _pointerTexture = WheelArt.Pointer(48, 28);   // Unity check: a destroyed texture reads as null
        _window = _ui?.Custom.Window(w =>
        {
            w.Title(Tx.T("window.title")).Size(460f, 700f).Position(60f, 80f).Draggable().Closable();
            w.OnClose(() => { _window = null; _wheel = _pointer = null; _spin = null; _status = _result = null; Wheel.Release(); });   // closed while turning: the disasters must not wait
            _pointer = Height(w.Body.Image(_pointerTexture), 26f);
            var tip = PictureOf(_pointer);
            if (tip != null) tip.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);   // swings from its top, like a flap on a real wheel
            _wheel = Height(w.Body.Image(_wheelTexture), 270f);
            WheelArt.AddIcons(Picture(), faces, name => _ui?.Custom.Theme.Icon(name), 19f);
            Turn(_angle);
            _result = w.Body.Paragraph("");
            w.Body.Separator();
            w.Body.Paragraph(Tx.T("window.odds",
                Hex(WheelArt.Jackpot), Hex(WheelArt.Win), Hex(WheelArt.Nothing), Hex(WheelArt.Bad), Hex(WheelArt.Catastrophe),
                Wheel.Percent(Wheel.Outcome.Jackpot), Wheel.Percent(Wheel.Outcome.Gold, Wheel.Outcome.Each), Casino.WinGold, Casino.WinEach,
                Wheel.Percent(Wheel.Outcome.Nothing), Wheel.Percent(Wheel.Outcome.Robbers, Wheel.Outcome.Fire), Wheel.Percent(Wheel.Outcome.Catastrophe)));
            w.Body.Separator();
            _spin = w.Body.Button(Tx.T("window.spin", Wheel.Stake), Spin);
            _status = w.Body.Paragraph("");
        });
        Redraw();
    }

    private static UiImage Height(UiImage image, float height)
    {
        var layout = image?.Root?.GetComponent<LayoutElement>();
        if (layout != null) layout.preferredHeight = height;
        return image;
    }

    private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

    private static void Spin()
    {
        try
        {
            var data = _build?.Data;
            var grid = data?.Island?.grid;
            if (data == null || grid == null || Core.Match.Phase != MatchPhase.Build) return;
            int casino = CasinoType.Find(grid);
            if (casino < 0) return;
            bool ok = Core.BuildActions.Plan(data, Wheel.SpinAction, casino);
            _build?.RefreshButtons();
            Plugin.L.LogInfo("spin: " + (ok ? "made" : "refused"));
        }
        catch (Exception e) { Plugin.L.LogWarning("spin: " + e.Message); }
        Redraw();
    }

    /// <summary>The local player's face came: open the window and let the wheel turn to it.</summary>
    private static void Show(int face, Wheel.Outcome outcome)
    {
        Open();
        _shownFace = -1;
        if (_result != null) _result.Value = Tx.T("window.turning");
        var picture = Picture();
        if (picture == null) { _shownFace = face; Redraw(); Wheel.Release(); return; }
        // clockwise from the top: face i covers [i*step, (i+1)*step) degrees; the pointer is at the top. 3 to 5 full turns, then the face.
        float step = 360f / Wheel.Faces.Length;
        float start = _angle % 360f;
        int rounds = 3 + (int)((uint)Environment.TickCount % 3u);
        // a wheel turned by z degrees (Unity: counter-clockwise) shows at the top the point at clockwise angle z, so it must end at
        // z = centre of the face (mod 360); it turns clockwise, so z goes down
        float target = start - (rounds * 360f + Mod(start - (face * step + step / 2f), 360f));
        WheelSpin.Start(picture, PictureOf(_pointer), step, start, target, 7f, a => _angle = a, () => { _shownFace = face; Redraw(); Wheel.Release(); });
    }

    private static float Mod(float a, float m) { a %= m; return a < 0f ? a + m : a; }

    private static GameObject Picture() => PictureOf(_wheel);

    /// <summary>A UiImage's picture (its first child; the root is the layout box).</summary>
    private static GameObject PictureOf(UiImage image) =>
        image?.Root != null && image.Root.childCount > 0 ? image.Root.GetChild(0).gameObject : null;

    private static void Turn(float angle)
    {
        var picture = Picture();
        if (picture != null) picture.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    private static void Redraw()
    {
        if (_window == null) return;
        var island = Core.Players.Local?.Island;
        bool hasCasino = CasinoType.Find(island?.grid) >= 0;
        var (pending, nextTurn, face, faceTurn) = Wheel.StateFor(island);
        int turn = Core.Match.Turn;
        int gold = island?.statistics?.Storage.stored.gold ?? 0;
        bool canSpin = hasCasino && !pending && turn >= nextTurn && gold >= Wheel.Stake && _build != null && Core.Match.Phase == MatchPhase.Build;
        _spin?.Interactable(canSpin);

        if (_status != null)
            _status.Value = !hasCasino ? Tx.T("window.noCasino")
                : pending ? Tx.T("window.pending")
                : turn < nextTurn ? Tx.T("window.cooldown", nextTurn - turn)
                : Core.Match.Phase != MatchPhase.Build ? Tx.T("window.buildOnly")
                : gold < Wheel.Stake ? Tx.T("window.noGold", Wheel.Stake)
                : Tx.T("window.ready", Wheel.Cooldown);

        if (_result != null && _shownFace >= 0) _result.Value = Tx.T("result." + Wheel.Faces[_shownFace].ToString().ToLowerInvariant());
        else if (_result != null && !WheelSpin.Running) _result.Value = "";
    }
}

/// <summary>
/// Turns the wheel with an ease-out, then stops and disables itself (it only runs while the wheel turns). The pointer is kicked aside each time
/// a line between two faces passes under it and falls back, like the flap of a real wheel of fortune.
/// </summary>
internal sealed class WheelSpin : MonoBehaviour
{
    public WheelSpin(IntPtr ptr) : base(ptr) { }

    private float _from, _to, _time, _duration, _step, _kick;
    private int _line;
    private Transform _pointer;
    private Action<float> _onAngle;
    private Action _onDone;
    internal static bool Running;

    internal static void Start(GameObject target, GameObject pointer, float step, float from, float to, float seconds, Action<float> onAngle, Action onDone)
    {
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<WheelSpin>()) ClassInjector.RegisterTypeInIl2Cpp<WheelSpin>();
        var spin = target.GetComponent<WheelSpin>() ?? target.AddComponent<WheelSpin>();
        spin._from = from;
        spin._to = to;
        spin._time = 0f;
        spin._duration = Mathf.Max(0.1f, seconds);
        spin._step = Mathf.Max(1f, step);
        spin._line = Line(from, spin._step);
        spin._kick = 0f;
        spin._pointer = pointer != null ? pointer.transform : null;
        spin._onAngle = onAngle;
        spin._onDone = onDone;
        spin.enabled = true;
        Running = true;
    }

    // turned by z, the wheel shows at the top its point at clockwise angle z; the lines between faces are at k * step,
    // so a line passes under the pointer whenever z / step crosses a whole number
    private static int Line(float angle, float step) => Mathf.FloorToInt(angle / step);

    public void Update()
    {
        float dt = Time.unscaledDeltaTime;
        _time += dt;
        float t = Mathf.Clamp01(_time / _duration);
        float eased = 1f - Mathf.Pow(1f - t, 4f);   // fast at first, a long slow end
        float angle = Mathf.Lerp(_from, _to, eased);
        transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        _onAngle?.Invoke(angle);

        int line = Line(angle, _step);
        if (line != _line) { _line = line; _kick = 1f; }   // a line passed: the flap jumps aside
        else _kick = Mathf.Max(0f, _kick - dt * 7f);       // and falls back
        if (_pointer != null) _pointer.localRotation = Quaternion.Euler(0f, 0f, 26f * Mathf.SmoothStep(0f, 1f, _kick));

        if (t < 1f) return;
        Finish();
    }

    /// <summary>The window was closed while the wheel turned: still finish, so the held disasters run.</summary>
    public void OnDestroy() => Finish();

    private void Finish()
    {
        if (!Running && _onDone == null) return;
        if (_pointer != null) _pointer.localRotation = Quaternion.identity;
        enabled = false;
        Running = false;
        var done = _onDone;
        _onDone = null;
        try { done?.Invoke(); } catch (Exception e) { Plugin.L.LogWarning("wheel: " + e.Message); }
    }
}