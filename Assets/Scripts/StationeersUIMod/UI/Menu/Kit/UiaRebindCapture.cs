using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using StationeersUIMod.Core;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>A tiny "press a key" capture state machine for rebinding a <see cref="UiaBind"/> in
    /// the Control Center. Pumped from UiaControlCenter.Update while open. Writes the new key into
    /// the bind's config entry (every consumer reads that), then also pushes it to the game's native
    /// binding via <see cref="UiaKeybinds.PushToGame"/> so the two stay in step. LMB/RMB are reserved
    /// (radial select/back) and never captured.</summary>
    public static class UiaRebindCapture
    {
        private static UiaBind _bind;
        private static Action _done;
        private static GameObject _prompt;
        private static KeyCode[] _all;

        public static bool Active => _bind != null;

        public static void Begin(UiaBind bind, Action done)
        {
            if (bind == null) return;
            _bind = bind;
            _done = done;
            ShowPrompt(bind.Label);
        }

        public static void Cancel()
        {
            _bind = null;
            _done = null;
            HidePrompt();
        }

        public static void Tick()
        {
            if (_bind == null) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }

            if (_all == null) _all = (KeyCode[])Enum.GetValues(typeof(KeyCode));
            for (int i = 0; i < _all.Length; i++)
            {
                var k = _all[i];
                if (k == KeyCode.None || k == KeyCode.Escape) continue;
                if (k == KeyCode.Mouse0 || k == KeyCode.Mouse1) continue; // reserved for select/back
                if (Input.GetKeyDown(k)) { Assign(k); return; }
            }
        }

        private static void Assign(KeyCode k)
        {
            var bind = _bind;
            var done = _done;
            _bind = null; _done = null;
            HidePrompt();
            try
            {
                if (bind.Set != null) bind.Set(k);
                UiaKeybinds.PushToGame(bind, k);
            }
            catch (Exception e) { UIALog.Warn("Rebind failed: " + e.Message); }
            if (done != null) done();
        }

        private static void ShowPrompt(string label)
        {
            HidePrompt();
            var layer = UiaControls.PopupLayer;
            if (layer == null) return;
            _prompt = UiaUi.Go("rebind-prompt", layer);
            var img = _prompt.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.55f);
            UiaUi.Fill((RectTransform)_prompt.transform);
            _prompt.transform.SetAsLastSibling();

            var boxGo = UiaUi.Go("box", _prompt.transform);
            var box = (RectTransform)boxGo.transform;
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(460f, 130f);
            var bimg = boxGo.AddComponent<Image>(); bimg.color = UiaTheme.Window;
            UiaUi.OutlineOf(bimg, UiaTheme.Accent, 1.5f);
            var t = UiaUi.Text(boxGo.transform, "Press a key for\n" + label + "\n\n(Esc to cancel)",
                UiaTheme.LabelSize, UiaTheme.Text, TextAlignmentOptions.Center, true);
            UiaUi.Fill((RectTransform)t.transform, 10f);
            // Click the dim backdrop to cancel.
            _prompt.AddComponent<UiaControls.UiaButton>().Init(img, img.color, img.color, img.color).OnClick = Cancel;
        }

        private static void HidePrompt()
        {
            if (_prompt != null) UnityEngine.Object.Destroy(_prompt);
            _prompt = null;
        }

        public static void Reset()
        {
            _bind = null; _done = null; HidePrompt();
        }
    }
}
