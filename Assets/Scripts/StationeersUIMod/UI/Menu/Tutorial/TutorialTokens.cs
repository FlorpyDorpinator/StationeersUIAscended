using System;
using System.Text;
using StationeersUIMod.Core;
using UnityEngine;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    /// <summary>
    /// Live key-glyph tokens for every piece of tutorial copy - lifted out of
    /// <c>TutorialCoach.Resolve/GlyphFor</c> (same logic) so the coach, the strip, the demo stage and
    /// the editor all resolve text the same way and a rebind can never make a lesson lie.
    ///
    /// <para>Token grammar (the "How to read Part C" table in Tutorial-Plan-and-Script.md):</para>
    /// <list type="bullet">
    ///   <item><c>{UIA_Grid}</c> - one of the mod's own binds, read from <see cref="UiaKeybinds"/>
    ///     (UIA_ToolRadial, UIA_ToolbeltRadial, UIA_BagRadial, UIA_Grid, UIA_Menu, UIA_HudDesigner,
    ///     UIA_HandSwap, UIA_Page, UIA_FineAdjust).</item>
    ///   <item><c>{V:SmartStow}</c> - a VANILLA button read live through
    ///     <c>KeyManager.GetKey(name)</c> (GLOBAL namespace; names verified in V27798 KeyManager.cs
    ///     AddKey calls :492-534 - SwapHands, SmartStow, InventorySelect, MouseControl, Drop,
    ///     MoveAllOfType, HelmetSlot..ToolBeltSlot).</item>
    /// </list>
    /// <para><see cref="Resolve"/> wraps each glyph in brackets (<c>{V:MouseControl}</c> -&gt; <c>[Alt]</c>);
    /// <see cref="Glyph"/> returns the bare glyph (<c>Alt</c>) for keycaps that draw their own box.
    /// An unknown or unbound token degrades to its own inner name (minus our <c>UIA_</c> prefix) and
    /// never throws. Stateless - nothing to reset on hot reload.</para>
    /// </summary>
    internal static class TutorialTokens
    {
        /// <summary>Replace every <c>{token}</c> with the player's CURRENT key glyph, in brackets.
        /// Allocates (a StringBuilder) - call it when text changes, never per frame. Null-safe.</summary>
        internal static string Resolve(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw ?? "";
            if (raw.IndexOf('{') < 0) return raw;
            var sb = new StringBuilder(raw.Length + 24);
            int pos = 0;
            while (pos < raw.Length)
            {
                int open = raw.IndexOf('{', pos);
                if (open < 0) { sb.Append(raw, pos, raw.Length - pos); break; }
                int close = raw.IndexOf('}', open + 1);
                if (close < 0) { sb.Append(raw, pos, raw.Length - pos); break; }
                sb.Append(raw, pos, open - pos);
                sb.Append('[').Append(Glyph(raw.Substring(open + 1, close - open - 1))).Append(']');
                pos = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>The bare live glyph for one token ("Alt", "MMB", "3"). Accepts the token with or
        /// without its braces (<c>{V:MouseControl}</c> or <c>V:MouseControl</c>) - the demo stage passes
        /// the braced form from a <c>keys:&lt;pattern&gt;:&lt;token&gt;</c> id. Never null, never throws.</summary>
        internal static string Glyph(string token)
        {
            if (string.IsNullOrEmpty(token)) return "?";
            if (token.Length >= 2 && token[0] == '{' && token[token.Length - 1] == '}')
                token = token.Substring(1, token.Length - 2);
            if (token.Length == 0) return "?";
            string inner = token;
            string glyph = null;
            try
            {
                if (token.StartsWith("V:", StringComparison.Ordinal))
                {
                    // A VANILLA button, live from the game's own registry (GLOBAL namespace).
                    inner = token.Substring(2);
                    KeyCode k = KeyManager.GetKey(inner);
                    if (k != KeyCode.None) glyph = UiaKeybinds.Glyph(k);
                }
                else
                {
                    UiaKeybinds.EnsureBuilt();
                    if (UiaKeybinds.Find(token) != null)
                    {
                        KeyCode k = UiaKeybinds.Key(token);
                        if (k != KeyCode.None) glyph = UiaKeybinds.Glyph(k);
                    }
                }
            }
            catch { glyph = null; }
            if (string.IsNullOrEmpty(glyph) || glyph == "-") glyph = Friendly(inner);
            return glyph;
        }

        /// <summary>Is <paramref name="token"/> (braces optional) a token this resolver knows by name?
        /// Used by <see cref="TutorialLint"/>. A V: name counts when the game's key registry knows it
        /// (bound or not); a UIA_ id when <see cref="UiaKeybinds"/> registers it.</summary>
        internal static bool IsKnown(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (token.Length >= 2 && token[0] == '{' && token[token.Length - 1] == '}')
                token = token.Substring(1, token.Length - 2);
            try
            {
                if (token.StartsWith("V:", StringComparison.Ordinal))
                {
                    string name = token.Substring(2);
                    if (name.Length == 0) return false;
                    if (KeyManager.GetKey(name) != KeyCode.None) return true;
                    // An unbound vanilla key reads None; accept the names the tutorial ships with.
                    for (int i = 0; i < KnownVanilla.Length; i++)
                        if (string.Equals(KnownVanilla[i], name, StringComparison.Ordinal)) return true;
                    return false;
                }
                UiaKeybinds.EnsureBuilt();
                return UiaKeybinds.Find(token) != null;
            }
            catch { return false; }
        }

        // Vanilla KeyManager names the shipped copy uses (V27798 KeyManager.cs :492-534).
        private static readonly string[] KnownVanilla =
        {
            "SwapHands", "SmartStow", "InventorySelect", "MouseControl", "Drop", "MoveAllOfType",
            "HelmetSlot", "GlassesSlot", "SuitSlot", "BackSlot", "UniformSlot", "ToolBeltSlot",
        };

        /// <summary>Fallback label for an unbound/unknown token: the bare id, minus our own "UIA_"
        /// prefix so it at least reads like a control name.</summary>
        private static string Friendly(string inner)
        {
            if (string.IsNullOrEmpty(inner)) return "?";
            return inner.StartsWith("UIA_", StringComparison.Ordinal) ? inner.Substring(4) : inner;
        }
    }
}
