using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// Owns every Tier B/C effect material: ONE shared material per effect family (batching
    /// preserved — per-element strength rides uv0.x, see IHudFxGraphic), plus the mode-C
    /// bookkeeping. Modeled on HudSystem's _ztestMat/_overlayFontMats discipline: create
    /// once, destroy all in Shutdown, never leak across an F6 hot reload.
    ///
    /// SCOPING CONTRACT (adversarial audit 2026-07-13): effect materials are assigned ONLY
    /// to graphics that (a) implement IHudFxGraphic — never borrowed vanilla graphics,
    /// Image icons, RawImages — and (b) are not under a UGUI Mask (a material without the
    /// stencil op kills the portrait's circular clip; the mode-C lesson, inverted).
    ///
    /// MODE C: HudSystem's ApplyWorldMaterials/RestoreWorldMaterials call the HandleWorld*
    /// hooks below FIRST. A graphic carrying one of OUR materials gets a per-family
    /// ZTest-Always clone (world-space canvas needs it, same reason _ztestMat exists)
    /// instead of being clobbered to _ztestMat / null. Batching is NOT preserved for
    /// effect materials in mode C — documented, profiler-measured. Graphics without an
    /// effect material fall through to the stock swap (Tier A panels still need _ztestMat).
    /// </summary>
    internal static class HudFxMaterials
    {
        // family key -> the shared screen-space material (created when Tier B/C wires up)
        private static readonly Dictionary<string, Material> _shared = new Dictionary<string, Material>();
        // shared material -> its ZTest-Always clone for mode C
        private static readonly Dictionary<Material, Material> _worldClones = new Dictionary<Material, Material>();
        // graphics currently carrying one of our effect materials (registry — never walk subtrees)
        private static readonly Dictionary<Graphic, Material> _assigned = new Dictionary<Graphic, Material>();

        /// <summary>True once the shader bundle loaded and materials exist (Tier B available).</summary>
        public static bool Available => _shared.Count > 0;

        /// <summary>Register/create a shared effect material for a family (called by the
        /// bundle loader once per shader). Idempotent; replaces a dead material after reload.</summary>
        public static Material Register(string family, Shader shader)
        {
            if (shader == null) return null;
            Material m;
            if (_shared.TryGetValue(family, out m) && m != null) return m;
            m = new Material(shader) { hideFlags = HideFlags.DontSave };
            _shared[family] = m;
            return m;
        }

        public static Material Get(string family)
        {
            Material m;
            return _shared.TryGetValue(family, out m) ? m : null;
        }

        /// <summary>Assign an effect material to a UIA-owned graphic. Enforces the scoping
        /// contract; returns false (and assigns nothing) when the target is out of scope.
        /// Idempotent — re-assigning the same family is a dictionary lookup, no material churn
        /// (safe to call from a per-frame styling path like ApplyGlass).</summary>
        public static bool Assign(Graphic g, string family)
        {
            if (g == null || !(g is IHudFxGraphic)) return false;
            var m = Get(family);
            if (m == null) return false;
            Material cur;
            if (_assigned.TryGetValue(g, out cur) && ReferenceEquals(cur, m)) return true; // already ours
            if (g.GetComponentInParent<Mask>() != null) return false; // stencil safety
            g.material = m;
            _assigned[g] = m;
            return true;
        }

        /// <summary>Put a graphic back on the default UI material and forget it. Cheap no-op
        /// when the graphic carries no effect material (per-frame safe).</summary>
        public static void Unassign(Graphic g)
        {
            if (g == null) return;
            if (_assigned.Remove(g) && g) g.material = null;
        }

        /// <summary>Mode-C swap hook — called by ApplyWorldMaterials BEFORE its stock swap.
        /// Returns true when this graphic is ours and has been given its world-safe clone
        /// (caller must skip it). False = not ours, stock behaviour applies.</summary>
        public static bool HandleWorldSwap(Graphic g)
        {
            Material m;
            if (g == null || !_assigned.TryGetValue(g, out m) || m == null) return false;
            Material clone;
            if (!_worldClones.TryGetValue(m, out clone) || clone == null)
            {
                clone = new Material(m) { renderQueue = 4000, hideFlags = HideFlags.DontSave };
                clone.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                _worldClones[m] = clone;
            }
            g.material = clone;
            return true;
        }

        /// <summary>Mode-C restore hook — called by RestoreWorldMaterials BEFORE its stock
        /// null-out. Returns true when this graphic is ours and got its SHARED material back
        /// (a blind material=null would strip the effect; audit 2026-07-13).</summary>
        public static bool HandleWorldRestore(Graphic g)
        {
            Material m;
            if (g == null || !_assigned.TryGetValue(g, out m)) return false;
            if (g) g.material = m;
            return true;
        }

        /// <summary>Hot-reload / teardown: forget assignments (graphics are being destroyed
        /// anyway), destroy every material we created, clear all statics.</summary>
        public static void Shutdown()
        {
            foreach (var kv in _assigned)
                if (kv.Key) kv.Key.material = null;
            _assigned.Clear();
            foreach (var kv in _worldClones)
                if (kv.Value != null) Object.DestroyImmediate(kv.Value);
            _worldClones.Clear();
            foreach (var kv in _shared)
                if (kv.Value != null) Object.DestroyImmediate(kv.Value);
            _shared.Clear();
        }
    }
}
