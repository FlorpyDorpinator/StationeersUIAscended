using UnityEngine;
using StationeersUIMod.Core;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// The 2026-07-16 legacy-style REGRESSION. Pre-standardisation profiles carried a third,
    /// implicit style state ("legacy mixed", styleSource 0/absent): the followGlobal colour
    /// flag, "-1 = follow global" sentinels, and fx* keys that MULTIPLIED the globals
    /// (fxFrostAmt et al. — the source of every "this element won't react to the global
    /// slider" bug). This pass, run from <see cref="HudDocument.Sanitize"/> on every load,
    /// rewrites each legacy element into the coherent two-state contract:
    ///
    ///  • An element whose effects/glass/sizing carried NO real override maps to GLOBAL —
    ///    it renders identically and every F9 effect slider now drives it 1:1.
    ///  • Anything with a real override maps to CUSTOM via the same snapshot path the F9
    ///    inspector uses (<see cref="HudElementView.SetUnifiedStyleSourceWithoutView"/>,
    ///    which honours full legacy semantics for a stored 0) — pixel-identical, but now a
    ///    complete, self-contained design instead of a mixture of sentinels and multipliers.
    ///
    /// COLOURS migrate by ref content, not by state: a legacy element that followed the
    /// global palette gets its refs rewritten to the palette NAMES (so it keeps tracking the
    /// F9 palette live); an element with its own refs keeps them verbatim. Colour identity is
    /// therefore never destroyed by the migration OR by later "follow global" flips.
    ///
    /// Idempotent by construction: it only touches elements whose stored styleSource is
    /// absent/0, and it always writes 1 or 2. Fail-soft: a throwing element is left as-is
    /// (StyleSourceOf reads 0 as Global at runtime, so nothing crashes — the element simply
    /// follows the globals until the next successful pass).
    /// </summary>
    internal static class HudStyleMigration
    {
        /// <summary>Keys whose presence (as a real value, not the -1 sentinel) marks the
        /// element as carrying its own effect/glass design → must map to Custom.
        /// sheen/spec are checked separately: under legacy followGlobal=true they were
        /// DORMANT (the flag forced the globals over them), so they must not block a
        /// follower's clean mapping to Global.</summary>
        private static readonly string[] SentinelKeys =
        {
            "feather", "bfade", "softEdge", "glow", "glowIn", "glowWidth",
            "glowDiffuse", "glowExtraDiffuse", "glowHaze", "glowBreath", "glowUneven",
            "glowOrganicScale", "glowFlowAura", "ripple", "rippleFreq", "edgeFlow",
            "frostDepth", "edgeLight",
        };

        /// <summary>The extinct legacy multiplier/opt-out pairs, folded into the Custom
        /// snapshot by the migration and then removed everywhere.</summary>
        private static readonly string[] LegacyFxKeys =
        {
            "fxShine", "fxShineAmt", "fxIrid", "fxIridAmt", "fxChroma", "fxChromaAmt",
            "fxFrost", "fxFrostAmt", "fxDissolve",
        };

        /// <summary>Everything a clean GLOBAL element must not carry: dormant style values
        /// would silently reactivate on the next separation snapshot as if they were the
        /// author's design. (Participation keys like edgeFade, insets, width, fadeEnds and
        /// content keys are NOT style state and are never touched.)</summary>
        private static readonly string[] GlobalStripKeys =
        {
            "sheen", "spec", "feather", "bfade", "softEdge", "glow", "glowIn", "glowWidth",
            "glowDiffuse", "glowExtraDiffuse", "glowHaze", "glowBreath", "glowUneven",
            "glowOrganicScale", "glowFlowAura", "ripple", "rippleFreq", "rippleSmooth",
            "edgeFlow", "frostDepth", "edgeLight", "squircle", "gaussianHalo",
            "fxCollapse", "fxCollapseAmt", "fxGlitch", "fxGlitchAmt", "fxWarp", "fxWarpAmt",
            "fxPulse", "fxPulseAmt", "customStyleReady",
        };

        // ---- Schema 16 (2026-07-26): the SDF-family back-fill --------------------------------
        //
        // Phase 0b of the per-element style parity plan made a MISSING per-element key mean "the
        // global" for the SDF halo family, where it used to mean "a fixed neutral / off". That is
        // the right rule going forward, but on its own it would have handed a feature to already-
        // stored Custom elements that never asked for one: an element snapshotted before these
        // keys existed would suddenly start breathing because the GLOBAL master happens to be on.
        //
        // So the rule change is paired with this one-time back-fill: every element that is already
        // Custom (per style slot) and LACKS one of these keys gets the current global written in,
        // freezing what it renders today. After that its stored value wins and nothing drifts.
        //
        // Self-gating exactly like HudTransitionFx.MigrateOne: the gate IS the absence of the key,
        // so the second pass finds nothing to do. No marker key, no schema read, no profile bloat
        // for elements that already carry the keys (which is every element separated since 0a).

        private static readonly string[] SdfFloatKeys =
        {
            "glowExtraDiffuse", "glowHaze", "glowBreath",
            "glowUneven", "glowOrganicScale", "glowFlowAura",
        };

        private static readonly string[] SdfBoolKeys =
        {
            "customGlowBreathOn", "customGlowUnevenOn", "customGlowFlowOn",
        };

        /// <summary>The OLD resolver's fallback for each float key — the "fixed neutral" the
        /// pre-0b <c>NewSdfOwnOrGlobal</c> used when a Custom element stored nothing. Parallel to
        /// <see cref="SdfFloatKeys"/>.</summary>
        private static readonly float[] SdfFloatNeutrals = { 0f, 0f, 0.35f, 0.5f, 1f, 0.6f };

        /// <summary>WHAT THIS SLOT RENDERS TODAY, under the pre-0b convention — deliberately NOT
        /// "the current global".
        ///
        /// The plan's §2 wording says to write the global in. Implemented literally that turns
        /// features ON: verified against FlorpyDorp's own installed profiles, where Stationeers
        /// Blue / Pure HUD / Zirillian Red each carry three BARE forks (b_g2-hands, b_g2-eqleft,
        /// b_g2-eqright) that store none of these keys — b_g2-hands resolves
        /// customGlowBreathOn/UnevenOn/FlowOn to the BASE's explicit `false`, while every one of
        /// those themes has the matching global master ON. Writing the global would have switched
        /// halo breathing, unevenness and the flowing aura on in bare mode for those elements, and
        /// re-created the exact suited-leaks-into-bare defect the Wave C fork exists to prevent.
        ///
        /// So the back-fill freezes the value the slot ALREADY resolves — the stored base value for
        /// a fork that inherits it, the old neutral where nothing is stored at all. That is
        /// pixel-identical by construction, which is Phase 0's gating test, and it still fully
        /// serves the stated purpose of the mitigation: once written, the key exists, so the new
        /// "absent means the global" rule can never change what this element renders.
        ///
        /// It also removes a load-order hazard: profile load is parse -> Sanitize -> SetActive ->
        /// HudTheme.Apply, so ANY live-global read inside Sanitize sees the OUTGOING profile's
        /// look. Reading nothing but the element's own stored values sidesteps that entirely.</summary>
        private static float SdfResolvedFloat(HudElementDef el, HudStyleSlot slot, int keyIndex)
            => el.GetFFor(slot, SdfFloatKeys[keyIndex], SdfFloatNeutrals[keyIndex]);

        private static bool SdfResolvedBool(HudElementDef el, HudStyleSlot slot, string key)
            => el.GetBFor(slot, key, false);

        /// <summary>RAW presence of <paramref name="key"/> in one slot — never resolved, because a
        /// forked slot that merely INHERITS the base still needs its own copy written.</summary>
        private static bool StoresKey(HudElementDef el, HudStyleSlot slot, string key)
            => slot == HudStyleSlot.Base
                ? el.GetS(key, null) != null
                : el.HasSlotOverride(slot, key);

        /// <summary>Back-fill one element's Custom slots. Returns the number of keys written.
        /// Idempotent and fail-soft; a slot that is NOT Custom is skipped entirely, so a following
        /// element gains nothing (its dormant keys stay dormant — decision 5).</summary>
        internal static int BackfillSdfKeys(HudElementDef el)
        {
            if (el == null) return 0;
            int written = 0;
            // BASE LAST. SetFFor/SetBFor on the base runs HudElementDef's copy-on-write, which
            // hands every forked slot the base's CURRENT value first — so doing the forks first
            // means each fork records what IT resolves, not what the base is about to become.
            if (el.ForksSlot(HudStyleSlot.Bare)) written += BackfillSlot(el, HudStyleSlot.Bare);
            if (el.ForksSlot(HudStyleSlot.Robot)) written += BackfillSlot(el, HudStyleSlot.Robot);
            written += BackfillSlot(el, HudStyleSlot.Base);
            return written;
        }

        private static int BackfillSlot(HudElementDef el, HudStyleSlot slot)
        {
            int written = 0;
            for (int i = 0; i < SdfFloatKeys.Length; i++)
            {
                string k = SdfFloatKeys[i];
                if (!BackfillWanted(el, slot, k)) continue;
                el.SetFFor(slot, k, SdfResolvedFloat(el, slot, i));
                written++;
            }
            for (int i = 0; i < SdfBoolKeys.Length; i++)
            {
                string k = SdfBoolKeys[i];
                if (!BackfillWanted(el, slot, k)) continue;
                el.SetBFor(slot, k, SdfResolvedBool(el, slot, k));
                written++;
            }
            return written;
        }

        /// <summary>Does this slot still need the Phase 0b freeze for one key?
        ///
        /// The gate was "is this slot's <c>styleSource</c> Custom" — ONE element-wide flag. Under
        /// Phase 3 that answers "yes" for a MIXED element (any category unfollowed writes 2), so it
        /// would back-fill Glow keys into an element that FOLLOWS Glow: unread, but profile bloat
        /// and a stale value the next unfollow has to overwrite. Ask the key's OWN category instead
        /// — five of these are Glow rows and <c>glowFlowAura</c> / <c>customGlowFlowOn</c> are Edges
        /// rows. On a pre-Phase-3 profile <c>styleSrc</c> is absent, so <c>PackedOf</c> falls back to
        /// the legacy field and this is bit-identical to the old test, which is what keeps Phase 0b's
        /// guarantee intact for the profiles it was written for.</summary>
        private static bool BackfillWanted(HudElementDef el, HudStyleSlot slot, string key)
        {
            if (StoresKey(el, slot, key)) return false;   // already frozen — never touch it again
            var def = HudStyleFx.FindByParam(key);
            var cat = def != null ? def.Category : HudFxCategory.Glow;
            return HudStyleFx.SourceOf(el, cat, slot) == HudFxSource.Own;
        }

        // ---- Phase 3 (2026-07-27): the two-state field -> the per-category packed word ---------
        //
        // Plan §4.2's mapping table, implemented literally:
        //
        //   stored styleSource == 1 (Global)  ->  styleSrc = every category Global. Nothing else is
        //                                        written; the element's dormant custom keys stay on
        //                                        disk, unread (FlorpyDorp's decision 5).
        //   stored styleSource == 2 (Custom)  ->  styleSrc = every category Own. Values are kept
        //                                        VERBATIM and nothing is seeded, because since
        //                                        Phase 0b an absent key already resolves to the
        //                                        global — that one convention is precisely what
        //                                        makes a seed unnecessary here.
        //   stored styleSource == 0 (legacy)  ->  Migrate() above has already regressed it to 1 or
        //                                        2 (it runs first, unchanged), so it lands in one
        //                                        of the two rows above.
        //
        //   Transitions is decided SEPARATELY, from the stored tri-states rather than from
        //   styleSource, because the transitions family was never gated on the style source at all
        //   (the deliberate 2026-07-19 fix). Any stored On/Off => Own, so the element keeps
        //   rendering exactly what it rendered; all-Inherit => Global.
        //
        // NO LIVE GLOBAL IS READ ANYWHERE IN HERE. Profile load is parse -> Sanitize -> SetActive
        // -> HudTheme.Apply, so a global read inside Sanitize sees the OUTGOING profile's look
        // (the load-order hazard Phase 0 recorded). This mapping needs no globals at all: it is a
        // pure re-encoding of state the element already stores.
        //
        // Self-gating on the ABSENCE of "styleSrc" per slot — the same discipline as the SDF
        // back-fill above and as HudTransitionFx.MigrateOne — so the second pass writes nothing.
        //
        // IT DELIBERATELY DOES NOT TOUCH THE LEGACY "styleSource" FIELD. Leaving a follower's stored
        // 1 exactly as it is means a downgrade to 0.9.2.x still reads that profile as fully
        // following, which is the truth; rewriting it here would buy nothing and could only make
        // that reading worse. The field is only ever re-written by an actual source CHANGE, through
        // HudElementView.WriteSourceBits.

        /// <summary>Map one element's stored style source onto the packed per-category word, per
        /// slot. Returns how many slots were written (0 when everything was already mapped).</summary>
        internal static int MapStyleSource(HudElementDef el)
        {
            if (el == null) return 0;
            int written = 0;
            // BASE LAST, for the same reason BackfillSdfKeys visits forks first: SetIFor on the
            // base runs HudElementDef's copy-on-write, which would otherwise hand a fork the value
            // the base is about to become.
            if (el.ForksSlot(HudStyleSlot.Bare)) written += MapSourceSlot(el, HudStyleSlot.Bare);
            if (el.ForksSlot(HudStyleSlot.Robot)) written += MapSourceSlot(el, HudStyleSlot.Robot);
            written += MapSourceSlot(el, HudStyleSlot.Base);
            return written;
        }

        private static int MapSourceSlot(HudElementDef el, HudStyleSlot slot)
        {
            if (StoresKey(el, slot, HudStyleFx.SourceParamKey)) return 0;   // already mapped
            // A fork that does not store its OWN styleSource inherits the base's decision, and so
            // must inherit the base's styleSrc too — writing one here would freeze the fork's
            // follow state against later base edits.
            if (slot != HudStyleSlot.Base && !StoresKey(el, slot, HudStyleFx.LegacySourceParamKey))
                return 0;

            int src = el.GetIFor(slot, HudStyleFx.LegacySourceParamKey, HudElementView.StyleGlobal);
            var steady = src == HudElementView.StyleCustom ? HudFxSource.Own : HudFxSource.Global;

            int packed = HudStyleFx.AllGlobalPacked;
            packed = HudStyleFx.WithSource(packed, HudFxCategory.Surface, steady);
            packed = HudStyleFx.WithSource(packed, HudFxCategory.Glass, steady);
            packed = HudStyleFx.WithSource(packed, HudFxCategory.Edges, steady);
            packed = HudStyleFx.WithSource(packed, HudFxCategory.Glow, steady);
            packed = HudStyleFx.WithSource(packed, HudFxCategory.Transitions,
                HasStoredTransition(el, slot) ? HudFxSource.Own : HudFxSource.Global);

            el.SetIFor(slot, HudStyleFx.SourceParamKey, packed);
            return 1;
        }

        /// <summary>The Transitions source an element SHOULD carry, without changing its motion:
        /// its stored bit when the packed word exists, else the migration's own rule (any stored
        /// On/Off ⇒ Own). The def-only snapshot and the F9 bulk buttons both use this to PRESERVE
        /// motion while they rewrite the four steady-state families — see
        /// <c>HudElementView.SetUnifiedStyleSourceWithoutView</c>.
        ///
        /// The "else" branch is what stops the legacy fold from silently re-enabling a transition
        /// the author turned off: at that point <c>styleSrc</c> does not exist yet, so reading it
        /// would fall back to the legacy two-state field and answer Global for an element that
        /// <c>HudTransitionFx.MigrateElement</c> had just given an explicit Off.</summary>
        internal static HudFxSource TransitionSourceForBase(HudElementDef el)
        {
            if (el == null) return HudFxSource.Global;
            if (el.GetS(HudStyleFx.SourceParamKey, null) != null)
                return HudStyleFx.SourceIn(el.GetI(HudStyleFx.SourceParamKey, 0),
                    HudFxCategory.Transitions);
            return HasStoredTransition(el, HudStyleSlot.Base) ? HudFxSource.Own : HudFxSource.Global;
        }

        /// <summary>Does this slot carry an explicit On/Off for ANY of the seven transitions?
        /// Reads the RAW stored mode (<see cref="HudTransitionFx.RawModeOf"/>) — the gated
        /// <c>ModeOf</c> would answer "Inherit" for everything precisely because the word this
        /// function exists to compute has not been written yet.</summary>
        private static bool HasStoredTransition(HudElementDef el, HudStyleSlot slot)
        {
            var all = HudTransitionFx.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (HudTransitionFx.RawModeOf(el, all[i], slot) != HudFxMode.Inherit) return true;
            }
            return false;
        }

        /// <summary>Regress every legacy-styled element in <paramref name="doc"/> to the
        /// two-state contract. Returns how many elements were rewritten.</summary>
        internal static int Migrate(HudDocument doc)
        {
            if (doc == null || doc.Elements == null) return 0;
            int migrated = 0;
            for (int i = 0; i < doc.Elements.Count; i++)
            {
                var el = doc.Elements[i];
                if (el == null) continue;
                try
                {
                    if (MigrateElement(el)) migrated++;
                }
                catch (System.Exception e)
                {
                    UIALog.Warn("HudStyleMigration: element '" + el.Id + "' failed ("
                        + e.Message + ") — left for the next pass.");
                }
            }
            return migrated;
        }

        private static bool MigrateElement(HudElementDef el)
        {
            int raw = el.GetI("styleSource", HudElementView.StyleLegacy);
            if (raw == HudElementView.StyleGlobal || raw == HudElementView.StyleCustom)
            {
                // Already coherent — just clear extinct legacy residue so it can never
                // resurface (cheap no-ops when the keys are absent).
                bool touched = el.GetS("followGlobal", null) != null;
                el.Set("followGlobal", null);
                for (int i = 0; i < LegacyFxKeys.Length; i++)
                {
                    // Custom keeps its motion/pulse keys; only the extinct multiplier family goes.
                    touched |= el.GetS(LegacyFxKeys[i], null) != null;
                    el.Set(LegacyFxKeys[i], null);
                }
                return touched;
            }

            bool followedColours = el.GetB("followGlobal", false);

            if (IsEffectivelyGlobal(el, followedColours))
            {
                // Renders identically as a pure follower; strip the sediment so nothing
                // dormant can reactivate later.
                el.SetI("styleSource", HudElementView.StyleGlobal);
                for (int i = 0; i < GlobalStripKeys.Length; i++) el.Set(GlobalStripKeys[i], null);
                el.BorderWidth = -1f;
                el.RTL = -1f; el.RTR = -1f; el.RBR = -1f; el.RBL = -1f;
            }
            else
            {
                // Real overrides: freeze the CURRENT legacy render into a complete Custom
                // snapshot (the WithoutView path honours the stored 0's full legacy
                // semantics — followGlobal glass gate, -1 sentinels, fx* multipliers,
                // the spec==0 edge-light opt-out).
                HudElementView.SetUnifiedStyleSourceWithoutView(el, false, true);
            }

            // Colour continuity. Legacy followGlobal=true painted the PALETTE over the
            // element's (dormant) refs; refs now always resolve, so hand the element the
            // palette NAMES — it keeps showing and tracking exactly what it showed.
            // followGlobal=false refs were already live: keep them verbatim.
            if (followedColours)
            {
                el.Fill = "HudPanelFill";
                el.Border = "HudPanelBorder";
                el.TextColor = "HudTextValue";
                if (el.Type == HudElementType.Readout)
                {
                    // Empty bar refs already mean "the palette slot" (Good/Warn/Critical/
                    // PanelBorder/TextValue) — exactly what legacy-following rendered.
                    el.Set("barFill", null);
                    el.Set("barWarn", null);
                    el.Set("barCrit", null);
                    el.Set("barTrack", null);
                    el.Set("barTarget", null);
                }
            }

            el.Set("followGlobal", null);
            for (int i = 0; i < LegacyFxKeys.Length; i++) el.Set(LegacyFxKeys[i], null);
            return true;
        }

        /// <summary>True when the legacy element's effects/glass/sizing resolve to the pure
        /// globals — i.e. every style key is at its "-1 = follow global" sentinel (or the
        /// equivalent neutral default), every fx* multiplier is neutral, and motion/pulse sit
        /// at the defaults Global enforces. Colour refs never matter here: they resolve
        /// identically in the Global state.</summary>
        private static bool IsEffectivelyGlobal(HudElementDef el, bool followedColours)
        {
            if (el.BorderWidth >= 0f) return false;
            if (el.RTL >= 0f || el.RTR >= 0f || el.RBR >= 0f || el.RBL >= 0f) return false;

            // Under legacy followGlobal=true, baked sheen/spec were dormant (the flag forced
            // the global glass) — the element rendered as a follower regardless, so they do
            // not block Global. Without the flag they were live.
            if (!followedColours
                && (el.GetF("sheen", -1f) >= 0f || el.GetF("spec", -1f) >= 0f)) return false;

            for (int i = 0; i < SentinelKeys.Length; i++)
                if (el.GetF(SentinelKeys[i], -1f) >= 0f) return false;

            // squircle's sentinel is "< 2", not "< 0".
            if (el.GetF("squircle", -1f) >= 2f) return false;
            // rippleSmooth has no global; Global renders it 0.
            if (el.GetF("rippleSmooth", 0f) > 0.0001f) return false;

            // Legacy multipliers/opt-outs at anything but neutral change the render.
            if (!el.GetB("fxShine", true) || !el.GetB("fxIrid", true)
                || !el.GetB("fxChroma", true) || !el.GetB("fxFrost", true)
                || !el.GetB("fxDissolve", true)) return false;
            if (!NearOne(el.GetF("fxShineAmt", 1f)) || !NearOne(el.GetF("fxIridAmt", 1f))
                || !NearOne(el.GetF("fxChromaAmt", 1f)) || !NearOne(el.GetF("fxFrostAmt", 1f)))
                return false;

            // Global forces collapse/glitch/warp ON at 1x and pulse OFF.
            if (!el.GetB("fxCollapse", true) || !el.GetB("fxGlitch", true)
                || !el.GetB("fxWarp", true) || el.GetB("fxPulse", false)) return false;
            if (!NearOne(el.GetF("fxCollapseAmt", 1f)) || !NearOne(el.GetF("fxGlitchAmt", 1f))
                || !NearOne(el.GetF("fxWarpAmt", 1f))) return false;

            return true;
        }

        private static bool NearOne(float v) => Mathf.Abs(v - 1f) < 0.0001f;
    }
}
