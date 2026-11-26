using System;
using System.Collections.Generic;
using System.Globalization;
using DiceModders.ModCore;
using DiceModders.UiKit;
using DiceModders.UiKit.Custom;
using DiceModders.UiKit.Native;
using UnityEngine;
using UnityEngine.UI;

namespace DiceModders.Casino;

using Core = DiceModders.ModCore.ModCore;

/// <summary>
/// The Casino's settings in their own window (Settings > Lobby > Casino > Casino settings), so the rules list stays short: the on/off tick, the
/// chances and the amounts with - / + and "Default", and a picture of the wheel those chances make. The host edits; everybody else sees the host's
/// values. They are lobby rules (hidden in the rules list), so ModCore sends them to everybody and freezes them for the match. Redraws when one changes.
/// </summary>
internal static class SettingsWindow
{
    private static readonly Color Changed = new(1f, 0.82f, 0.45f, 1f);
    private static readonly Color Normal = new(0.97f, 0.94f, 0.89f, 1f);

    private static UiScope _ui;
    private static UiWindow _win;
    private static UiImage _wheel;
    private static UiToggle _on;
    private static UiText _status, _sum;
    private static Texture2D _texture;
    private static Wheel.Outcome[] _drawn;
    private static readonly List<Action> Refreshers = new();
    private static readonly List<UiButton> EditButtons = new();

    internal static void Install(UiScope ui)
    {
        _ui = ui;
        foreach (var (rule, _) in Casino.Numbers) rule.Changed += _ => Refresh();
        Casino.Rule.Changed += _ => Refresh();
        Core.Lobby.Changed += Refresh;   // the host can change, a match can start: who may edit changes with it
        Core.Match.Started += Refresh;   // the rules are fixed for the match: nobody edits them now
        Core.Match.Ended += Refresh;
        Tx.Block(ui, () => new SettingsBlockSpec
        {
            Id = Plugin.Guid + ".lobby",
            Page = SettingsPage.Lobby,
            Title = Tx.T("settings.block"),
            Order = 40,
            Build = block => block.AddButton(Tx.T("settings.title"), Tx.T("settings.open"), Toggle),
        });
    }

    internal static void Toggle() { if (_win != null) _win.Close(); else Open(); }

    private static void Open()
    {
        _drawn = null;   // a new window: draw the wheel and its icons again
        _win = _ui?.Custom.Window(w =>
        {
            w.Title(Tx.T("settings.title")).Size(560f, 780f).Position(100f, 40f).Draggable().Closable();
            w.OnClose(() => { _win = null; _wheel = null; _on = null; _status = _sum = null; Refreshers.Clear(); EditButtons.Clear(); });
            w.Body.Spacing(4f);
            _on = w.Body.Toggle(Casino.Rule.Title, Casino.Rule.Live, v =>
            {
                if (!Casino.Rule.TrySet(v) && _status != null) _status.Value = Tx.T("settings.hostOnly");
                Refresh();   // snaps back when the change was refused
            });
            var about = Casino.Rule.Description;
            if (!string.IsNullOrEmpty(about)) w.Body.Paragraph(about);
            w.Body.Separator();
            _status = w.Body.Paragraph("");
            _wheel = w.Body.Image(null);
            var layout = _wheel?.Root?.GetComponent<LayoutElement>();
            if (layout != null) layout.preferredHeight = 200f;
            _sum = w.Body.Paragraph("");
            w.Body.Separator();
            foreach (var (rule, unit) in Casino.Numbers) Stepper(w.Body, rule, unit);
            w.Body.Separator();
            var all = w.Body.Button(Tx.T("settings.allDefault"), () => { foreach (var (rule, _) in Casino.Numbers) rule.TrySet(rule.Default); });
            if (all != null) EditButtons.Add(all);
        });
        Refresh();
    }

    /// <summary>"label   [-] value [+] [Default]"</summary>
    private static void Stepper(UiContainer c, LobbyRule<int> rule, string unit)
    {
        UiText value = null;
        UiButton plus = null;
        c.Row(r =>
        {
            r.Spacing(4f);
            r.Text(rule.Title)?.FillWidth();
            var minus = r.Button("-", () => Step(rule, -rule.Step))?.FixedW(36f);
            value = r.Text("")?.FixedW(70f);
            plus = r.Button("+", () => Step(rule, rule.Step))?.FixedW(36f);
            var reset = r.Button(Tx.T("settings.default"), () => rule.TrySet(rule.Default))?.FixedW(90f);
            foreach (var b in new[] { minus, plus, reset }) if (b != null) EditButtons.Add(b);
        })?.Height(32f);
        Refreshers.Add(() =>
        {
            if (value == null) return;
            value.Value = rule.Live.ToString(CultureInfo.InvariantCulture) + unit;
            value.Color(rule.Live == rule.Default ? Normal : Changed);
            // a chance can only go up while the four together stay at 100 % or less
            if (plus != null && Casino.IsChance(rule) && Casino.ChanceSum + rule.Step > 100) plus.Interactable(false);
        });
    }

    private static void Step(LobbyRule<int> rule, int delta)
    {
        int next = Math.Clamp(rule.Live + delta, rule.Min, rule.Max);
        if (delta > 0 && Casino.IsChance(rule))
        {
            int room = 100 - (Casino.ChanceSum - rule.Live);   // what is left for this chance
            if (next > room) next = Math.Max(rule.Live, room - room % Math.Max(1, rule.Step));
            if (next == rule.Live) { if (_status != null) _status.Value = Tx.T("settings.full"); return; }
        }
        if (!rule.TrySet(next) && _status != null) _status.Value = Tx.T("settings.hostOnly");
    }

    private static void Refresh()
    {
        if (_win == null) return;
        bool edit = Casino.Rule.Editable;
        _on?.Set(Casino.Rule.Live).Interactable(edit);
        foreach (var b in EditButtons) b?.Interactable(edit);
        foreach (var r in Refreshers) r();   // after: a full 100 % turns the chances' + off
        if (_status != null)
            _status.Value = (Casino.Rule.Live ? "" : Tx.T("settings.ruleOff") + " ") + (edit ? Tx.T("settings.host") : Tx.T("settings.readonly"))
                + (Casino.ChanceSum > 100 ? " " + Tx.T("settings.over", Casino.ChanceSum) : "");

        // the wheel these chances make (5 % a face; "nothing" gets the rest; over 100 % the biggest kinds give faces back)
        var faces = Wheel.Faces;
        if (_texture == null || _drawn != faces)
        {
            var old = _texture;
            _texture = WheelArt.Disc(256, faces);
            _drawn = faces;
            _wheel?.SetTexture(_texture);
            if (old != null) UnityEngine.Object.Destroy(old);   // a picture per click on a chance would pile up otherwise
            var picture = _wheel?.Root != null && _wheel.Root.childCount > 0 ? _wheel.Root.GetChild(0).gameObject : null;
            WheelArt.AddIcons(picture, faces, name => _ui?.Custom.Theme.Icon(name), 13f);
        }
        else if (_wheel != null && _wheel.Texture != _texture) _wheel.SetTexture(_texture);
        if (_sum != null)
            _sum.Value = Tx.T("settings.sum",
                Wheel.Percent(Wheel.Outcome.Jackpot), Wheel.Percent(Wheel.Outcome.Gold, Wheel.Outcome.Each), Wheel.Percent(Wheel.Outcome.Nothing),
                Wheel.Percent(Wheel.Outcome.Robbers, Wheel.Outcome.Fire), Wheel.Percent(Wheel.Outcome.Catastrophe));
    }
}
