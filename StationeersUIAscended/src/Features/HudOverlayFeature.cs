using System;
using Assets.Scripts;
using Assets.Scripts.Atmospherics;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Clothing;
using Assets.Scripts.Objects.Entities;
using Assets.Scripts.Objects.Items;
using ImGuiNET;
using StationeersUIAscended.Core;
using StationeersUIAscended.Overlay;
using UnityEngine;

namespace StationeersUIAscended.Features
{
    /// <summary>
    /// Phase 6 — the visor HUD (proposal §12, Figure 1): two-hand boxes bottom-center
    /// (never a hotbar), a slim top status strip, compact vitals, day/time readout, and a
    /// contextual reticle panel. Purely additive: vanilla panels are optionally hidden via
    /// the game's own SetUIPanelVisibility, never destroyed.
    /// </summary>
    public sealed class HudOverlayFeature
    {
        private bool _vanillaHidden;

        public void Draw()
        {
            if (!UIAConfig.HudEnabled.Value) { RestoreVanillaIfNeeded(); return; }
            var human = Guards.LocalHuman;
            if (human == null) { RestoreVanillaIfNeeded(); return; }

            SyncVanillaVisibility();

            bool helmetOn = !UIAConfig.HardcoreGating.Value || HasHelmet(human);
            bool sensorsOn = !UIAConfig.HardcoreGating.Value || HasActiveSensors(human);

            var dl = ImGui.GetBackgroundDrawList();
            float scale = UIAConfig.HudScale.Value;

            if (UIAConfig.HudHandBoxes.Value) DrawHands(dl, human, scale);
            if (UIAConfig.HudStatusStrip.Value && helmetOn) DrawStatusStrip(dl, human, scale);
            if (UIAConfig.HudVitals.Value && helmetOn) DrawVitals(dl, human, scale);
            if (UIAConfig.HudClock.Value && helmetOn) DrawClock(dl, scale);
            if (UIAConfig.HudContextPanel.Value && sensorsOn) DrawContextPanel(dl, scale);
            if (UIAConfig.HudVisorArcs.Value) DrawVisorArcs(dl);
        }

        // ---------- hardcore gating ----------

        private static bool HasHelmet(Human human)
        {
            try { return human.HelmetSlot?.Get() != null; } catch { return false; }
        }

        private static bool HasActiveSensors(Human human)
        {
            try
            {
                var lenses = human.GlassesSlot?.Get<SensorLenses>();
                return lenses != null && lenses.InteractOnOff != null && lenses.InteractOnOff.State == 1;
            }
            catch { return false; }
        }

        // ---------- two-hand boxes ----------

        private void DrawHands(ImDrawListPtr dl, Human human, float scale)
        {
            float w = 240f * scale, h = 74f * scale, gap = 24f * scale;
            float y = Screen.height - h - 18f * scale;
            float cx = Screen.width * 0.5f;

            Slot active = InventoryManager.ActiveHandSlot;
            DrawHandBox(dl, human.LeftHandSlot, "LEFT HAND",
                new Vector2(cx - gap * 0.5f - w, y), new Vector2(w, h), human.LeftHandSlot == active, scale);
            DrawHandBox(dl, human.RightHandSlot, "RIGHT HAND",
                new Vector2(cx + gap * 0.5f, y), new Vector2(w, h), human.RightHandSlot == active, scale);
        }

        private static void DrawHandBox(ImDrawListPtr dl, Slot slot, string title, Vector2 pos, Vector2 size, bool isActive, float scale)
        {
            var min = pos;
            var max = pos + size;
            DrawUtil.Panel(dl, min, max, Theme.PanelBg, isActive ? Theme.Accent : Theme.PanelBorder);
            if (isActive)
                dl.AddRectFilled(new Vector2(min.x, min.y), new Vector2(min.x + 4f, max.y), Theme.Accent, 2f);

            DrawUtil.Text(dl, min + new Vector2(10f, 6f), Theme.TextDim, title);

            DynamicThing occ = null;
            try { occ = slot?.Get(); } catch { }
            if (occ == null)
            {
                DrawUtil.Text(dl, min + new Vector2(10f, 26f), Theme.TextDisabled, "empty");
                return;
            }

            float icon = size.y - 22f;
            DrawUtil.Icon(dl, occ.GetThumbnail(), new Vector2(min.x + 14f + icon * 0.5f, min.y + 18f + icon * 0.5f), icon);
            DrawUtil.Text(dl, min + new Vector2(22f + icon, 24f), Theme.TextPrimary, occ.DisplayName);

            string state = ToolbeltRadialFeature.DescribeState(occ);
            if (!string.IsNullOrEmpty(state))
                DrawUtil.Text(dl, min + new Vector2(22f + icon, 42f), Theme.TextDim, state);

            // Charge bar for battery tools
            try
            {
                if (occ is PowerTool pt && pt.Battery != null)
                {
                    float ratio = pt.Battery.CurrentPowerPercentage / 100f;
                    DrawUtil.Bar(dl,
                        new Vector2(min.x + 22f + icon, max.y - 12f),
                        new Vector2(max.x - 10f, max.y - 6f),
                        ratio, ratio <= 0.15f ? Theme.Critical : Theme.Accent);
                }
            }
            catch { }
        }

        // ---------- top status strip ----------

        private void DrawStatusStrip(ImDrawListPtr dl, Human human, float scale)
        {
            float w = Mathf.Min(860f * scale, Screen.width - 240f);
            float h = 30f * scale;
            var min = new Vector2((Screen.width - w) * 0.5f, 12f);
            var max = min + new Vector2(w, h);
            DrawUtil.Panel(dl, min, max, Theme.PanelBg, Theme.PanelBorder, 12f);

            var cells = new System.Collections.Generic.List<(string label, string value, uint color)>();

            try
            {
                var atmos = human.WorldAtmosphere;
                if (atmos != null && atmos.IsValid())
                {
                    float kPa = atmos.PressureGassesAndLiquids.ToFloat();
                    float degC = atmos.Temperature.ToFloat() - 273.15f;
                    cells.Add(("PRES", kPa.ToString("0") + " kPa", kPa < 20f || kPa > 260f ? Theme.Warn : Theme.TextPrimary));
                    cells.Add(("TEMP", degC.ToString("0.0") + " C", degC < 0f || degC > 50f ? Theme.Warn : Theme.TextPrimary));
                }
                else
                {
                    cells.Add(("PRES", "--", Theme.TextDim));
                    cells.Add(("TEMP", "--", Theme.TextDim));
                }
            }
            catch { cells.Add(("ATMOS", "--", Theme.TextDim)); }

            try
            {
                ISuit suit = human.Suit;
                if (suit != null && suit.AsThing != null)
                {
                    var battery = suit.Battery;
                    float charge = battery != null ? battery.CurrentPowerPercentage / 100f : 0f;
                    cells.Add(("PWR", battery != null ? battery.CurrentPowerPercentage + "%" : "none", Theme.StateColor(charge)));

                    var air = suit.AirTank;
                    cells.Add(("AIR", air != null ? air.Pressure.ToFloat().ToString("0") + " kPa" : "none",
                        air != null ? Theme.TextPrimary : Theme.Critical));

                    if (suit.HasWasteTankSlot)
                    {
                        var waste = suit.WasteTank;
                        float wasteRatio = 0f;
                        if (waste != null && suit.WasteMaxPressure.ToFloat() > 1f)
                            wasteRatio = waste.Pressure.ToFloat() / suit.WasteMaxPressure.ToFloat();
                        cells.Add(("WASTE", waste != null ? (wasteRatio * 100f).ToString("0") + "%" : "none",
                            wasteRatio > 0.75f ? Theme.Warn : Theme.TextPrimary));
                    }
                    if (suit.HasFilters)
                        cells.Add(("FILTER", suit.EmptyFilter ? "EMPTY" : suit.LowFilter ? "LOW" : "OK",
                            suit.EmptyFilter ? Theme.Critical : suit.LowFilter ? Theme.Warn : Theme.Good));
                }
                else
                {
                    cells.Add(("SUIT", "none", Theme.Warn));
                }
            }
            catch { }

            try
            {
                bool internals = human.HasInternals && human.InternalsOn;
                cells.Add(("O2 SUP", internals ? "ON" : "OFF", internals ? Theme.Good : Theme.TextDim));
            }
            catch { }

            if (cells.Count == 0) return;
            float cellW = w / cells.Count;
            for (int i = 0; i < cells.Count; i++)
            {
                var (label, value, color) = cells[i];
                var center = new Vector2(min.x + cellW * i + cellW * 0.5f, min.y + h * 0.5f);
                string text = label + "  " + value;
                DrawUtil.TextShadowCentered(dl, center, color, text);
                if (i > 0)
                    DrawUtil.Line(dl, new Vector2(min.x + cellW * i, min.y + 6f),
                        new Vector2(min.x + cellW * i, max.y - 6f), Theme.RingSep);
            }
        }

        // ---------- vitals ----------

        private void DrawVitals(ImDrawListPtr dl, Human human, float scale)
        {
            float w = 190f * scale, h = 96f * scale;
            var min = new Vector2(Screen.width - w - 16f, Screen.height - h - 18f * scale);
            var max = min + new Vector2(w, h);
            DrawUtil.Panel(dl, min, max, Theme.PanelBg, Theme.PanelBorder);
            DrawUtil.Text(dl, min + new Vector2(10f, 5f), Theme.TextDim, "VITALS");

            void Row(int i, string label, float ratio, uint overrideColor = 0)
            {
                float y = min.y + 24f + i * 17f * scale;
                DrawUtil.Text(dl, new Vector2(min.x + 10f, y), Theme.TextDim, label);
                uint color = overrideColor != 0 ? overrideColor : Theme.StateColor(ratio);
                DrawUtil.Bar(dl, new Vector2(min.x + 64f, y + 3f), new Vector2(max.x - 42f, y + 11f), ratio, color);
                DrawUtil.Text(dl, new Vector2(max.x - 36f, y), Theme.TextPrimary, Mathf.RoundToInt(Mathf.Clamp01(ratio) * 100f) + "%");
            }

            try { Row(0, "HP", 1f - human.DamageState.TotalRatio); } catch { }
            try { Row(1, "O2", Mathf.Clamp01(human.OxygenQuality)); } catch { }
            try { Row(2, "H2O", Mathf.Clamp01(human.Hydration / 5f)); } catch { }
            try { Row(3, "FOOD", Mathf.Clamp01(human.NutritionRatio)); } catch { }
        }

        // ---------- clock ----------

        private void DrawClock(ImDrawListPtr dl, float scale)
        {
            string text;
            try
            {
                float t = OrbitalSimulation.TimeOfDay;
                int minutes = Mathf.FloorToInt(t * 24f * 60f) % (24 * 60);
                text = $"SOL {WorldManager.DaysPast + 1}  {minutes / 60:00}:{minutes % 60:00}";
            }
            catch
            {
                text = null;
            }
            if (string.IsNullOrEmpty(text)) return;
            DrawUtil.Text(dl, new Vector2(18f, 14f), Theme.TextPrimary, text);
        }

        // ---------- contextual reticle panel ----------

        private void DrawContextPanel(ImDrawListPtr dl, float scale)
        {
            Thing target = null;
            try { target = CursorManager.CursorThing; } catch { }
            if (target == null) return;
            // Skip items in our own inventory — this panel is for world devices.
            try { if (target is DynamicThing d && d.RootParentHuman == Guards.LocalHuman) return; } catch { }

            string name = target.DisplayName;
            if (string.IsNullOrEmpty(name)) return;

            string state = null;
            try
            {
                if (target.InteractOnOff != null)
                    state = target.OnOff ? "ONLINE" : "OFFLINE";
            }
            catch { }

            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f + 60f * scale);
            var nameSize = DrawUtil.TextSize(name);
            float w = Mathf.Max(nameSize.x + 28f, 150f * scale);
            float h = state != null ? 44f : 28f;
            var min = new Vector2(center.x - w * 0.5f, center.y);
            var max = min + new Vector2(w, h);
            DrawUtil.Panel(dl, min, max, Theme.PanelBg, Theme.PanelBorder);
            DrawUtil.TextShadowCentered(dl, new Vector2(center.x, min.y + 14f), Theme.TextPrimary, name);
            if (state != null)
                DrawUtil.TextShadowCentered(dl, new Vector2(center.x, min.y + 31f),
                    state == "ONLINE" ? Theme.Good : Theme.TextDim, state);
        }

        // ---------- visor arcs (flat approximation) ----------

        private static void DrawVisorArcs(ImDrawListPtr dl)
        {
            float wq = Screen.width * 0.25f;
            var leftCenter = new Vector2(-Screen.width * 0.10f, Screen.height * 0.5f);
            var rightCenter = new Vector2(Screen.width * 1.10f, Screen.height * 0.5f);
            float radius = Screen.width * 0.32f;
            uint color = Theme.C(0.28f, 0.75f, 0.80f, 0.18f);
            DrawUtil.ArcLine(dl, leftCenter, radius, -0.9f, 0.9f, color, 2.5f);
            DrawUtil.ArcLine(dl, rightCenter, radius, Mathf.PI - 0.9f, Mathf.PI + 0.9f, color, 2.5f);
        }

        // ---------- vanilla panel visibility (hide, never destroy) ----------

        private void SyncVanillaVisibility()
        {
            bool wantHide = UIAConfig.HideVanillaHands.Value || UIAConfig.HideVanillaClothing.Value || UIAConfig.HideVanillaStatus.Value;
            if (wantHide == _vanillaHidden) return;
            ApplyVanillaVisibility(!UIAConfig.HideVanillaHands.Value, !UIAConfig.HideVanillaClothing.Value, !UIAConfig.HideVanillaStatus.Value);
            _vanillaHidden = wantHide;
        }

        public void RestoreVanillaIfNeeded()
        {
            if (!_vanillaHidden) return;
            ApplyVanillaVisibility(true, true, true);
            _vanillaHidden = false;
        }

        private static void ApplyVanillaVisibility(bool hands, bool clothing, bool status)
        {
            try
            {
                var manager = InventoryManager.Instance;
                if (manager == null) return;
                manager.StartCoroutine(manager.SetUIPanelVisibility(hands, clothing, status, 0f));
            }
            catch (Exception e)
            {
                UIALog.Warn("SetUIPanelVisibility failed: " + e.Message);
            }
        }
    }
}
