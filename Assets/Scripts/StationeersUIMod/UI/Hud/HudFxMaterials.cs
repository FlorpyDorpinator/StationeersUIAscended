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
        // CurvedWorldCanvas applies ZTest-Always materials once when the render mode changes.
        // Styling can still change a graphic's family later (including the first lazy SDF load),
        // so Assign must know which variant is currently required instead of blindly restoring
        // the shared screen-space material after that one-time sweep.
        private static Canvas _worldModeCanvas;
        private static Material _worldModeFallback;

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

        /// <summary>Set a float on a shared family and, when mode C has already created one,
        /// its ZTest-Always clone. Property IDs are precomputed by callers so per-frame effect
        /// clocks remain allocation-free.</summary>
        public static void SetFloat(string family, int propertyId, float value)
        {
            Material shared;
            if (!_shared.TryGetValue(family, out shared) || shared == null) return;
            shared.SetFloat(propertyId, value);
            Material clone;
            if (_worldClones.TryGetValue(shared, out clone) && clone != null)
                clone.SetFloat(propertyId, value);
        }

        /// <summary>Colour counterpart to <see cref="SetFloat"/>; keeps an existing mode-C
        /// clone synchronized with its shared family.</summary>
        public static void SetColor(string family, int propertyId, Color value)
        {
            Material shared;
            if (!_shared.TryGetValue(family, out shared) || shared == null) return;
            shared.SetColor(propertyId, value);
            Material clone;
            if (_worldClones.TryGetValue(shared, out clone) && clone != null)
                clone.SetColor(propertyId, value);
        }

        /// <summary>Vector counterpart to <see cref="SetFloat"/>; keeps an existing mode-C
        /// clone synchronized with its shared family.</summary>
        public static void SetVector(string family, int propertyId, Vector4 value)
        {
            Material shared;
            if (!_shared.TryGetValue(family, out shared) || shared == null) return;
            shared.SetVector(propertyId, value);
            Material clone;
            if (_worldClones.TryGetValue(shared, out clone) && clone != null)
                clone.SetVector(propertyId, value);
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
            bool alreadyAssigned = _assigned.TryGetValue(g, out cur) && ReferenceEquals(cur, m);
            if (!alreadyAssigned && g.GetComponentInParent<Mask>() != null) return false; // stencil safety
            // HudFxMaterials also owns F10's radial glass on a separate overlay Canvas. Scope
            // the world variant to HudSystem's actual mode-C canvas so radial assignments stay
            // on the normal shared material while the HUD itself is curved in world space.
            bool needsWorldVariant = IsOnWorldModeCanvas(g);
            Material target = needsWorldVariant ? GetWorldClone(m) : m;
            if (target == null) return false; // fail soft: caller keeps/chooses the mesh fallback
            if (alreadyAssigned)
            {
                // Usually a no-op. The correction matters when mode C was entered before the
                // family was lazily assigned, or when a prior external write replaced our variant.
                if (!ReferenceEquals(g.material, target)) g.material = target;
                return true;
            }
            g.material = target;
            _assigned[g] = m;
            return true;
        }

        /// <summary>Tell the material owner which render-path variant new assignments require.
        /// HudSystem still performs the one-time subtree sweep for already-present graphics;
        /// this state closes the gap for families assigned or changed after that sweep.</summary>
        public static void SetWorldModeCanvas(Canvas canvas, Material fallback)
        {
            _worldModeCanvas = canvas;
            _worldModeFallback = canvas != null ? fallback : null;
        }

        /// <summary>Put a graphic back on the default UI material and forget it. Cheap no-op
        /// when the graphic carries no effect material (per-frame safe).</summary>
        public static void Unassign(Graphic g)
        {
            if (g == null) return;
            if (_assigned.Remove(g) && g)
                g.material = IsOnWorldModeCanvas(g) ? _worldModeFallback : null;
        }

        private static bool IsOnWorldModeCanvas(Graphic g)
            => g != null && _worldModeCanvas != null && g.canvas == _worldModeCanvas;

        /// <summary>Mode-C swap hook — called by ApplyWorldMaterials BEFORE its stock swap.
        /// Returns true when this graphic is ours and has been given its world-safe clone
        /// (caller must skip it). False = not ours, stock behaviour applies.</summary>
        public static bool HandleWorldSwap(Graphic g)
        {
            Material m;
            if (g == null || !_assigned.TryGetValue(g, out m) || m == null) return false;
            Material clone = GetWorldClone(m);
            // Keep the matching shader ABI even if clone allocation fails. Returning false here
            // would make HudSystem install UI/Default on an SDF parameter mesh (a solid grid).
            g.material = clone != null ? clone : m;
            return true;
        }

        /// <summary>Return the single ZTest-Always clone for a shared family. Creation is
        /// fail-soft because Assign runs in the normal HUD content path, outside the guarded
        /// curvature sweep; a graphics-device teardown must degrade to the caller's fallback.</summary>
        private static Material GetWorldClone(Material shared)
        {
            if (shared == null) return null;
            Material clone;
            if (_worldClones.TryGetValue(shared, out clone) && clone != null) return clone;
            try
            {
                clone = new Material(shared) { renderQueue = 4000, hideFlags = HideFlags.DontSave };
                clone.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                _worldClones[shared] = clone;
                return clone;
            }
            catch
            {
                // A device teardown can fail after allocation but before the clone reaches the
                // ownership dictionary. Destroy that partial object here so F6 cannot leak it.
                if (clone != null)
                {
                    try { Object.DestroyImmediate(clone); } catch { }
                }
                return null;
            }
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
            _worldModeCanvas = null;
            _worldModeFallback = null;
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
