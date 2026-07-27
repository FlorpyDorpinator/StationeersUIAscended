using System;
using System.Collections.Generic;
using ImGuiNET;
using StationeersUIMod.UI.Hud;
using UnityEngine;

namespace StationeersUIMod.Windows
{
    /// <summary>
    /// Renders a list of <see cref="HudProp"/> descriptors as ImGui widgets — the one place
    /// that knows how each <see cref="HudPropKind"/> looks on screen, so every panel and the
    /// click-to-edit popup can expose their knobs without writing widget code of their own.
    ///
    /// The drawer stays deliberately ignorant of the document: it only ever calls the prop's
    /// boxed Get/Set and raises begin/commit/cancel callbacks so the CALLER can bracket an edit
    /// for undo without retaining a dead snapshot after an activation that changed nothing. Getting
    /// that bracketing right is the whole point — <see cref="DrawAll"/>'s doc comment spells out
    /// exactly when each callback fires, because undo granularity lives or dies on the timing.
    /// </summary>
    public static class HudPropDrawer
    {
        // The nine anchor names never change, and combos are drawn every frame while the popup
        // is open — build the string array once rather than re-allocating it per frame per prop.
        private static readonly string[] AnchorNames = Enum.GetNames(typeof(HudAnchor));

        // Flag sets mirror the existing HUD colour wheel so a colour ref edited here feels
        // identical to one edited in the F9 palette list.
        private const ImGuiColorEditFlags WheelFlags = ImGuiColorEditFlags.AlphaBar
                                                      | ImGuiColorEditFlags.AlphaPreviewHalf
                                                      | ImGuiColorEditFlags.PickerHueWheel;
        private const ImGuiColorEditFlags SwatchFlags = ImGuiColorEditFlags.AlphaPreviewHalf;

        /// <summary>
        /// Draw every prop in order. <paramref name="onBeginEdit"/> fires ONCE at the start of an
        /// edit gesture — on <c>IsItemActivated</c> for the drag/slide/type widgets, or immediately
        /// before <c>Set</c> for one-shot picks (a checkbox toggle, a combo selection). It is the
        /// caller's cue to snapshot the pre-edit state. <paramref name="onCommitted"/> fires when the
        /// gesture completes — on <c>IsItemDeactivatedAfterEdit</c> for the continuous widgets, or
        /// immediately after <c>Set</c> for the one-shots — the cue to push that snapshot onto the
        /// undo stack and mark the document dirty. <c>onChanged</c> reports that the gesture really
        /// mutated its property, including when a tab switch prevents normal deactivation.
        /// <c>onCancelled</c> drops an activation snapshot
        /// when a continuous item deactivates without editing. A slider dragged for a second is ONE
        /// undo step, not sixty; a checkbox is one step; get either bracket wrong and undo shatters
        /// into per-frame noise.
        /// </summary>
        public static void DrawAll(List<HudProp> props, Action onBeginEdit, Action onCommitted,
            Action onCancelled = null, Action onChanged = null)
            => Draw(props, null, onBeginEdit, onCommitted, onCancelled, onChanged);

        public static void DrawGroup(List<HudProp> props, HudPropGroup group,
            Action onBeginEdit, Action onCommitted, Action onCancelled = null,
            Action onChanged = null)
            => Draw(props, group, onBeginEdit, onCommitted, onCancelled, onChanged);

        public static bool HasGroup(List<HudProp> props, HudPropGroup group)
        {
            if (props == null) return false;
            for (int i = 0; i < props.Count; i++)
                if (props[i] != null && props[i].Group == group) return true;
            return false;
        }

        private static void Draw(List<HudProp> props, HudPropGroup? only,
            Action onBeginEdit, Action onCommitted, Action onCancelled, Action onChanged)
        {
            if (props == null) return;
            for (int i = 0; i < props.Count; i++)
            {
                HudProp p = props[i];
                if (p == null || (only.HasValue && p.Group != only.Value)) continue;
                // Labels repeat across elements (every Box has a "Fill", every Readout an "Anchor"),
                // so the visible label carries a per-index "##hp" suffix — same text, unique ImGui id.
                string key = !string.IsNullOrEmpty(p.StableId)
                    ? p.StableId : p.Group + "_" + p.Label;
                string sfx = "##hp_" + key;

                switch (p.Kind)
                {
                    case HudPropKind.Bool: DrawBool(p, sfx, onBeginEdit, onCommitted, onChanged); break;
                    case HudPropKind.Float: DrawFloat(p, sfx, onBeginEdit, onCommitted, onCancelled, onChanged); break;
                    case HudPropKind.Int: DrawInt(p, sfx, onBeginEdit, onCommitted, onCancelled, onChanged); break;
                    case HudPropKind.Text: DrawText(p, sfx, onBeginEdit, onCommitted, onCancelled, onChanged); break;
                    case HudPropKind.Enum: DrawEnum(p, sfx, onBeginEdit, onCommitted, onChanged); break;
                    case HudPropKind.Anchor: DrawAnchor(p, sfx, onBeginEdit, onCommitted, onChanged); break;
                    case HudPropKind.TierMask: DrawTier(p, key, onBeginEdit, onCommitted, onChanged); break;
                    case HudPropKind.ColorRef: DrawColorRef(p, key, onBeginEdit, onCommitted, onCancelled, onChanged); break;
                    case HudPropKind.Points: DrawPoints(p); break;
                    case HudPropKind.TabGroup:
                        DrawTabGroup(p, key, onBeginEdit, onCommitted, onCancelled, onChanged); break;
                    // A page never draws itself — only its owning TabGroup draws it.
                    case HudPropKind.TabPage: break;
                    case HudPropKind.Header:
                        ImGui.Spacing();
                        ImGui.Separator();
                        ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.93f, 1f), p.Label);
                        break;
                    // A footnote to the row above: no separator, no accent, no value.
                    case HudPropKind.Note:
                        ImGui.TextDisabled(p.Label);
                        Tooltip(p);
                        break;
                    // No undo bracket on purpose — a button carries no property to snapshot, and
                    // whatever it does is responsible for its own bracketing.
                    case HudPropKind.Button:
                        if (ImGui.Button(p.Label + sfx) && p.Action != null) p.Action();
                        Tooltip(p);
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ containers

        /// <summary>A NESTED tab bar: one tab per child <see cref="HudPropKind.TabPage"/>, and only
        /// the selected page's props are drawn. Folds a long repeated block (one page per sense,
        /// per body part…) into a compact section instead of a scrolling wall of rows. ImGui keeps
        /// the selected tab itself, so nothing has to be stored on the element.
        ///
        /// Children are drawn with NO group filter — the TabGroup's own Group already decided which
        /// inspector tab the whole block belongs to, so a page's props must not be filtered again.
        /// The undo callbacks pass straight through, so an edit inside a page brackets exactly like
        /// a top-level one.</summary>
        private static void DrawTabGroup(HudProp p, string key, Action begin, Action commit,
            Action cancel, Action changed)
        {
            if (p.Children == null || p.Children.Count == 0) return;
            // EndTabBar is only legal when Begin returned true (same contract as the popup's own bar).
            if (!ImGui.BeginTabBar("##tg_" + key)) return;
            for (int i = 0; i < p.Children.Count; i++)
            {
                HudProp page = p.Children[i];
                if (page == null) continue;
                // Caption is the page label; the "##" suffix keeps ids unique when two pages share text.
                if (ImGui.BeginTabItem(page.Label + "##tgp_" + key + "_" + i))
                {
                    // A PAGE SWITCH IS AN INTERRUPTION, exactly like the outer inspector tab switch
                    // that HudEditorWindow.DrawElementPropTab already guards: the previous page's
                    // active InputText/slider stops being submitted, so ImGui can never report its
                    // deactivation and EndContinuous never fires. Without this flush the pending undo
                    // snapshot is stranded — the next gesture reuses it (two edits collapse into one
                    // undo) and, if an undo lands meanwhile, the stale document is written back over
                    // the profile. Commit before the new page can start a gesture.
                    int prev;
                    if (!_tabPage.TryGetValue(key, out prev)) _tabPage[key] = i;
                    else if (prev != i) { _tabPage[key] = i; commit?.Invoke(); }

                    Draw(page.Children, null, begin, commit, cancel, changed);
                    ImGui.EndTabItem();
                }
            }
            ImGui.EndTabBar();
        }

        /// <summary>Last-drawn page index per TabGroup, so a page switch can be detected and the
        /// in-flight edit flushed. Value types keyed by prop id — holds no scene or document
        /// reference, so it is inert across an F6 reload (a stale int only names a tab index).</summary>
        private static readonly Dictionary<string, int> _tabPage = new Dictionary<string, int>();

        // ------------------------------------------------------------------ scalar widgets

        private static void DrawBool(HudProp p, string sfx, Action begin, Action commit,
            Action changed)
        {
            bool v = (bool)p.Get();
            // A checkbox is one-shot: it only reports on the frame it flips, so the whole
            // gesture — begin, set, commit — collapses into that single true return.
            if (ImGui.Checkbox(p.Label + sfx, ref v))
            {
                begin?.Invoke();
                p.Set(v);
                changed?.Invoke();
                commit?.Invoke();
            }
            Tooltip(p);
        }

        private static void DrawFloat(HudProp p, string sfx, Action begin, Action commit,
            Action cancel, Action noteChanged)
        {
            float v = (float)p.Get();
            bool changed = ImGui.SliderFloat(p.Label + sfx, ref v, p.Min, p.Max);
            // SliderFloat may change its value on the same mouse-down frame that activates it.
            // Capture the document after ImGui establishes the item state but BEFORE applying the
            // returned value, otherwise Undo starts at the click position instead of the true old
            // value.
            BeginContinuous(begin);
            if (changed)
            {
                p.Set(v);
                noteChanged?.Invoke();
            }
            EndContinuous(commit, cancel);
            Tooltip(p);
        }

        private static void DrawInt(HudProp p, string sfx, Action begin, Action commit,
            Action cancel, Action noteChanged)
        {
            int v = (int)p.Get();
            bool changed = ImGui.SliderInt(p.Label + sfx, ref v, (int)p.Min, (int)p.Max);
            BeginContinuous(begin);
            if (changed)
            {
                p.Set(v);
                noteChanged?.Invoke();
            }
            EndContinuous(commit, cancel);
            Tooltip(p);
        }

        private static void DrawText(HudProp p, string sfx, Action begin, Action commit,
            Action cancel, Action noteChanged)
        {
            string v = (string)p.Get() ?? "";
            // "Text" in the label means a free-form body (an element's Text field) that wants room to
            // breathe; anything else (an icon name, an align token) is a one-liner.
            bool multiline = p.Label.IndexOf("Text", StringComparison.Ordinal) >= 0;
            bool changed;
            if (multiline)
            {
                changed = ImGui.InputTextMultiline(p.Label + sfx, ref v, 256u, new Vector2(0f, 54f));
            }
            else
            {
                changed = ImGui.InputText(p.Label + sfx, ref v, 256u);
            }
            BeginContinuous(begin);
            if (changed)
            {
                p.Set(v);
                noteChanged?.Invoke();
            }
            EndContinuous(commit, cancel);
            Tooltip(p);
        }

        // ------------------------------------------------------------------ combos

        private static void DrawEnum(HudProp p, string sfx, Action begin, Action commit,
            Action changed)
        {
            string[] names = p.EnumNames;
            if (names == null || names.Length == 0) return;
            int cur = (int)p.Get();
            string preview = cur >= 0 && cur < names.Length ? names[cur] : "";
            if (ImGui.BeginCombo(p.Label + sfx, preview))
            {
                for (int n = 0; n < names.Length; n++)
                {
                    if (!ImGui.Selectable(names[n], n == cur) || n == cur) continue;
                    begin?.Invoke();
                    p.Set(n);
                    changed?.Invoke();
                    commit?.Invoke();
                }
                ImGui.EndCombo();
            }
            Tooltip(p);
        }

        private static void DrawAnchor(HudProp p, string sfx, Action begin, Action commit,
            Action changed)
        {
            int cur = (int)p.Get();
            string preview = cur >= 0 && cur < AnchorNames.Length ? AnchorNames[cur] : "";
            if (ImGui.BeginCombo(p.Label + sfx, preview))
            {
                for (int n = 0; n < AnchorNames.Length; n++)
                {
                    if (!ImGui.Selectable(AnchorNames[n], n == cur) || n == cur) continue;
                    begin?.Invoke();
                    p.Set(n);
                    changed?.Invoke();
                    commit?.Invoke();
                }
                ImGui.EndCombo();
            }
            Tooltip(p);
        }

        private static void DrawTier(HudProp p, string key, Action begin, Action commit,
            Action changed)
        {
            // Two boxes, Bare and Suited — the human player's two HUD modes. The Robot bit still
            // exists in the data model (append-only enum) but is deliberately NOT surfaced: a robot
            // gets its OWN profile, so an author only ever thinks in Bare/Suited here (FlorpyDorp,
            // 2026-07-19). Existing Robot bits are preserved untouched — we simply never toggle them.
            int mask = (int)p.Get();
            bool bare = (mask & (int)HudTierMask.Bare) != 0;
            // "Suited" means "shows in the live/powered HUD". Robot is part of that live state, so
            // the one visible checkbox drives Suited AND Robot together — keeping the hidden Robot
            // bit in lockstep so it can never strand an element in a bare+robot-only state.
            const HudTierMask Live = HudTierMask.Suited | HudTierMask.Robot;
            bool suited = (mask & (int)Live) != 0;

            if (ImGui.Checkbox("Bare##hp_" + key + "_B", ref bare)) SetTier(p, mask, HudTierMask.Bare, bare, begin, commit, changed);
            ImGui.SameLine();
            if (ImGui.Checkbox("Suited##hp_" + key + "_S", ref suited)) SetTier(p, mask, Live, suited, begin, commit, changed);
            ImGui.SameLine();
            ImGui.TextDisabled(p.Label);
            Tooltip(p);
        }

        private static void SetTier(HudProp p, int mask, HudTierMask bit, bool on, Action begin,
            Action commit, Action changed)
        {
            int next = on ? mask | (int)bit : mask & ~(int)bit;
            begin?.Invoke();
            p.Set(next);
            changed?.Invoke();
            commit?.Invoke();
        }

        // ------------------------------------------------------------------ colour reference

        private static void DrawColorRef(HudProp p, string key, Action begin, Action commit,
            Action cancel, Action noteChanged)
        {
            string cur = (string)p.Get() ?? "";
            Color fallback = p.ColorFallback != null ? p.ColorFallback() : Color.white;
            // The swatch always shows the RESOLVED colour, so a palette-named ref visibly tracks the
            // palette while a "#hex" literal shows its own value — one glance tells you which you have.
            Color resolved = HudPalette.Resolve(cur, fallback);
            ImGui.ColorButton("##sw_" + key, ToV4(resolved), SwatchFlags, new Vector2(18f, 18f));
            ImGui.SameLine();

            bool isPalette = HudPalette.IsPaletteName(cur);
            string preview = isPalette ? cur : "Custom colour";
            if (ImGui.BeginCombo(p.Label + "##hp_" + key, preview))
            {
                // Picking "Custom" hands the ref off to the wheel below by writing the current colour
                // as a literal; picking a palette name stores the NAME so it keeps tracking the palette.
                if (ImGui.Selectable("Custom colour", !isPalette))
                {
                    if (isPalette)
                    {
                        begin?.Invoke();
                        p.Set(HudPalette.ToHexRef(resolved));
                        noteChanged?.Invoke();
                        commit?.Invoke();
                    }
                }
                for (int n = 0; n < HudPalette.All.Count; n++)
                {
                    string name = HudPalette.All[n].Name;
                    if (ImGui.Selectable(name, string.Equals(name, cur, StringComparison.Ordinal)))
                    {
                        if (!string.Equals(name, cur, StringComparison.Ordinal))
                        {
                            begin?.Invoke();
                            p.Set(name);
                            noteChanged?.Invoke();
                            commit?.Invoke();
                        }
                    }
                }
                ImGui.EndCombo();
            }
            Tooltip(p);

            // Re-read: a "Custom" pick this frame flips the ref to a literal, and we want its wheel to
            // appear immediately rather than a frame late.
            cur = (string)p.Get() ?? "";
            if (!HudPalette.IsPaletteName(cur))
            {
                Color c = HudPalette.Resolve(cur, fallback);
                Vector4 v = ToV4(c);
                bool didChange = ImGui.ColorEdit4("##cw_" + key, ref v, WheelFlags);
                BeginContinuous(begin);
                if (didChange)
                {
                    p.Set(HudPalette.ToHexRef(new Color(v.x, v.y, v.z, v.w)));
                    noteChanged?.Invoke();
                }
                EndContinuous(commit, cancel);
            }
        }

        // ------------------------------------------------------------------ points

        private static void DrawPoints(HudProp p)
        {
            // Point lists are shaped on the canvas with the Draw tool, not typed here — there is no
            // sane textual widget for a polyline, so this row is purely a signpost.
            ImGui.TextDisabled(p.Label);
            ImGui.SameLine();
            ImGui.TextDisabled("(edit points with the Draw tool)");
        }

        // ------------------------------------------------------------------ shared helpers

        /// <summary>Undo bracket for the continuous widgets (sliders, drags, text, the colour wheel):
        /// begin on the frame the widget is grabbed, commit on the frame it is released after a real
        /// edit — so the whole drag is a single undo step.</summary>
        private static void BeginContinuous(Action begin)
        {
            if (ImGui.IsItemActivated()) begin?.Invoke();
        }

        private static void EndContinuous(Action commit, Action cancel)
        {
            if (ImGui.IsItemDeactivatedAfterEdit()) commit?.Invoke();
            else if (ImGui.IsItemDeactivated()) cancel?.Invoke();
        }

        private static void Tooltip(HudProp p)
        {
            if (!string.IsNullOrEmpty(p.Help) && ImGui.IsItemHovered()) ImGui.SetTooltip(p.Help);
        }

        private static Vector4 ToV4(Color c) { return new Vector4(c.r, c.g, c.b, c.a); }
    }
}
