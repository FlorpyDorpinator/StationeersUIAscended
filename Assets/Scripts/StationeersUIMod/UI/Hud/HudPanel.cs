using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// A clickable region of the HUD in the F9 editor: what it is called, which palette
    /// entries paint it, and which config values shape it. The editor window renders the
    /// values generically (bool → checkbox, ranged float → slider, font name → combo).
    /// Rect is in CANVAS coords (centre origin, y up) so hit-testing shares the warp
    /// inverse with everything else.
    /// </summary>
    public sealed class HudEditTarget
    {
        public string Title;
        public string[] Palette = System.Array.Empty<string>();
        public ConfigEntryBase[] Values = System.Array.Empty<ConfigEntryBase>();
        public Rect CanvasRect;
    }

    /// <summary>A screen region of the HUD that accepts a dragged radial chip (0.6.2):
    /// the hand boxes and the six equipment boxes. Rect in CANVAS coords (centre origin,
    /// y up) — hit-test with the inverse-warped mouse, exactly like HudEditTarget.</summary>
    public sealed class HudDropZone
    {
        public Rect CanvasRect;
        public Assets.Scripts.Objects.Slot Slot;
        public string Label;
    }

    /// <summary>
    /// Base of every visor HUD panel: owns a subtree under the HUD canvas, a CanvasGroup
    /// the animator drives, and its editor description. Panels read ONLY the HudSnapshot.
    /// </summary>
    internal abstract class HudPanel
    {
        public RectTransform Root { get; private set; }
        public CanvasGroup Group { get; private set; }
        public HudAnimator.Fader Fader;
        /// <summary>Fail-soft bookkeeping: HudSystem stops logging after a few failures.</summary>
        internal int UpdateFailures;

        public abstract string Id { get; }
        /// <summary>Suit-tier panels die with the suit (power-death collapse).</summary>
        public abstract bool SuitTier { get; }

        protected bool Built { get; private set; }

        public void Build(Transform parent)
        {
            if (Built) return;
            var go = new GameObject("UIA_" + Id, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Root = (RectTransform)go.transform;
            Root.anchorMin = Root.anchorMax = new Vector2(0.5f, 0.5f);
            Root.sizeDelta = Vector2.zero;
            Group = go.AddComponent<CanvasGroup>();
            Group.interactable = false;
            Group.blocksRaycasts = false;
            BuildContent(Root);
            Built = true;
        }

        public void Destroy()
        {
            // Borrow widgets (moodlet strip, portrait, damage doll) reparent a LIVE vanilla
            // object under Root; it MUST be handed back here or Destroy(Root) takes the vanilla
            // object with it and its per-frame vanilla updater NREs forever. Self-healing so it
            // no longer depends on every caller restoring first (the ManagerUpdate spam).
            try { OnBeforeDestroy(); } catch { }
            if (Root != null) Object.Destroy(Root.gameObject);
            Root = null;
            Group = null;
            Fader = null;
            Built = false;
        }

        /// <summary>Hook fired right before Root is destroyed — borrow widgets restore their
        /// borrowed vanilla object here. Default no-op.</summary>
        protected virtual void OnBeforeDestroy() { }

        protected abstract void BuildContent(RectTransform root);

        /// <summary>Static geometry: positions/sizes from config. Called on build and
        /// whenever a layout-affecting config changes (then meshes re-warp).</summary>
        public abstract void Layout(float scale);

        /// <summary>Per-frame content update from the snapshot.</summary>
        public abstract void UpdatePanel(HudSnapshot s, float scale);

        /// <summary>Whether the panel wants to be visible at this tier (before config
        /// toggles and animations).</summary>
        public abstract bool VisibleAt(HudTier tier);

        /// <summary>Config toggle for this panel.</summary>
        public abstract ConfigEntry<bool> Toggle { get; }

        /// <summary>Editor click targets (canvas coords).</summary>
        public abstract void CollectEditTargets(List<HudEditTarget> into, float scale);

        /// <summary>Chip drop targets this panel offers (hand boxes, equipment boxes).
        /// Default: none. Called only while the panel is actually visible.</summary>
        public virtual void CollectDropZones(List<HudDropZone> into, HudSnapshot s, float scale) { }

        // ---------- shared helpers ----------

        protected static PanelGraphic MakePanel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<PanelGraphic>();
            g.raycastTarget = false;
            go.AddComponent<VisorWarp>();
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            return g;
        }

        protected static UnityEngine.UI.Image MakeIcon(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            go.AddComponent<VisorWarp>();
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            return img;
        }

        /// <summary>The thin accent bars (charge bars, separators) — a PanelGraphic with
        /// no border.</summary>
        protected static PanelGraphic MakeBar(Transform parent, string name)
        {
            var g = MakePanel(parent, name);
            g.BorderWidth = 0f;
            return g;
        }
    }
}
