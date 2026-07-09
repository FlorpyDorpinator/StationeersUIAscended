using ImGuiNET;
using UnityEngine;

namespace StationeersUIAscended.Overlay
{
    /// <summary>A brief on-screen message drawn center-screen, so feedback doesn't require the F3 console.</summary>
    public static class Toast
    {
        private static string _msg;
        private static float _until;
        private static uint _color = Theme.TextPrimary;

        public static void Show(string message, uint color, float seconds = 3f)
        {
            _msg = message;
            _color = color;
            _until = Time.unscaledTime + seconds;
        }

        public static void Draw()
        {
            if (string.IsNullOrEmpty(_msg) || Time.unscaledTime > _until) return;
            var dl = ImGui.GetForegroundDrawList();
            var size = ImGui.CalcTextSize(_msg);
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.32f);
            var min = center - size * 0.5f - new Vector2(14f, 8f);
            var max = center + size * 0.5f + new Vector2(14f, 8f);
            DrawUtil.Panel(dl, min, max, Theme.PanelBgSolid, Theme.PanelBorder, 6f);
            DrawUtil.TextShadowCentered(dl, center, _color, _msg);
        }
    }
}
