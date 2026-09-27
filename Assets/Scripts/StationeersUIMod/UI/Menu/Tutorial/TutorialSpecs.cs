using UnityEngine;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    /// <summary>
    /// One CARD (or F10-tour CALLOUT) for <see cref="TutorialCoach"/> to present - pure data, built
    /// by <c>TutorialDirector</c> (Documentation/0.9.8.0/Tutorial-Build-Contract.md section 5).
    ///
    /// <para><b>Text is already resolved</b> (tokens -&gt; live key glyphs) by whoever builds the
    /// spec; the coach runs <c>TutorialTokens.Resolve</c> over it once more only as a harmless
    /// safety net (resolved text contains no <c>{</c>). ASCII only - it reaches TMP.</para>
    ///
    /// <para><b>Buttons.</b> <see cref="OnButton"/> receives the index of the pressed label. The
    /// Director normally answers with <c>TutorialCoach.OpenCard</c> (next card - content swaps in
    /// place, the pause is never dropped between cards) or <c>TutorialCoach.CloseCard</c>. If it does
    /// NEITHER, the coach closes the card itself after the callback returns, so a button can never
    /// go dead. Enter presses <see cref="PrimaryIndex"/>. X / Esc close the card FIRST and then call
    /// <see cref="OnLater"/> ("Later"). An empty <see cref="Buttons"/> array shows one "Close"
    /// button that behaves like X.</para>
    ///
    /// <para><b>Pause.</b> <see cref="Pause"/> asks the coach to hold the shared single-player
    /// freeze (<c>Core/GamePause</c>, reason "tutorial") while the card is up. The coach also honours
    /// <c>UIAConfig.TutorialAutoPause</c> and <c>GamePause.CanOwnPause()</c> - never a pause in
    /// multiplayer.</para>
    ///
    /// <para><b>RunningLine.</b> When no pause can be owned (<c>GamePause.CanOwnPause()</c> false:
    /// multiplayer client, host with players, a vanilla menu) the coach REPLACES the body's pause
    /// sentence ("The game is paused." / the old "The game is paused while you read.") with this
    /// line. Replacement only - a body without a pause sentence is shown unchanged. (In
    /// single-player with the pause simply not held - TutorialAutoPause off, or the player clicked
    /// the header pause button - the sentence becomes "Your game is still running." instead, so the
    /// card never claims a freeze that is not there.)</para>
    ///
    /// <para><b>Hole.</b> <see cref="HasHole"/> + <see cref="HoleScreenRect"/> swap the full-screen
    /// scrim for four dark panels around the rect (expanded by 14 px), so a card can show a real HUD
    /// element - pair it with <c>TutorialSpotlight.Show</c> for the pulsing outline. The rect is in
    /// SCREEN pixels, bottom-left origin, y up (<c>Input.mousePosition</c> space - exactly what
    /// <c>HudSystem.TryGetElementScreenRect</c> returns). The card slides vertically off the hole
    /// when they would overlap. Ignored by callouts.</para>
    /// </summary>
    internal sealed class TCardSpec
    {
        /// <summary>The Part C step id ("entry.welcome"). Drives the dev EDIT button
        /// (<c>TutorialEditorWindow.OpenAt</c>) and the default in-place edit keys
        /// ("&lt;StepId&gt;|heading", "&lt;StepId&gt;|body" - see <see cref="HeadingKey"/> /
        /// <see cref="BodyKey"/>). Null = not editable.</summary>
        internal string StepId;
        /// <summary>Card heading, resolved (shown upper-cased).</summary>
        internal string Heading;
        /// <summary>Card body, resolved; may contain \n (a real line break).</summary>
        internal string Body;
        /// <summary>The copy key <see cref="Heading"/> was read from - what the coach's dev in-place
        /// field (uiadev) reads and writes. Null = the default "&lt;StepId&gt;|heading" (a step that
        /// declares no heading falls back to its "|title"). "" = no single key (composed text): the
        /// line is shown read-only and edited in the lesson editor. Any other value must be a key the
        /// step declares (or the shipped copy knows), else the line is read-only - writing an
        /// undeclared key would only create an orphan override the exporter never emits.
        /// (Explicitly initialised: a builder that never names a key must stay warning-free.)</summary>
        internal string HeadingKey = null;
        /// <summary>The copy key <see cref="Body"/> was read from. Set it whenever the body is a
        /// VARIANT (the Welcome's "entry.welcome|body@mp" / "|body@running"), so an in-place dev edit
        /// lands on the line actually on screen. Null = the default "&lt;StepId&gt;|body"; "" =
        /// composed (e.g. a Watch card's Says + Then) - read-only. Same rules as
        /// <see cref="HeadingKey"/>.</summary>
        internal string BodyKey = null;
        /// <summary>A <c>TutorialDemoStage</c> scene id; null (or unknown) = no demo area at all
        /// (the card shrinks - the "static frame").</summary>
        internal string DemoId;
        /// <summary>Button labels, resolved, left to right.</summary>
        internal string[] Buttons;
        /// <summary>The button Enter presses and the one drawn in the primary style. Default 0.</summary>
        internal int PrimaryIndex;
        /// <summary>Hold the single-player pause while this card is up (see the type remarks).</summary>
        internal bool Pause;
        /// <summary>Show a SPOT hole in the scrim (card mode only).</summary>
        internal bool HasHole;
        /// <summary>The hole, screen px, bottom-left origin (see the type remarks).</summary>
        internal Rect HoleScreenRect;
        /// <summary>Replaces the body's pause sentence when no pause can be owned (see remarks).</summary>
        internal string RunningLine;
        /// <summary>Button pressed (index into <see cref="Buttons"/>).</summary>
        internal System.Action<int> OnButton;
        /// <summary>X / Esc (and the system closes: world unloaded, F10 closed under a callout).
        /// Called AFTER the card has closed.</summary>
        internal System.Action OnLater;
    }

    /// <summary>
    /// The F10 anchor ids understood by <c>UiaControlCenter.TryGetAnchorRect</c>, plus the new F10's
    /// real tab titles (0.9.8.0 SmartStow/F10 overhaul, verified against each tab's <c>Title</c>).
    /// Tab titles are what <c>UiaControlCenter.OpenOnTab</c> / <c>SelectTab</c> take - never an
    /// index (the tab list is appended to by other work; Suggestions/Bugs is always last).
    ///
    /// <para>Anchor grammar: <c>"tab:&lt;exact tab title&gt;"</c>, <c>"search"</c> (the title-bar
    /// settings search box), <c>"pause"</c> (the title-bar pause button), <c>"close"</c> (the
    /// title-bar X). Extras (Presentation's call, additive): <c>"master"</c> (both master
    /// switches - RADIAL MENUS / VISOR HUD), <c>"master:radial"</c>, <c>"master:hud"</c>,
    /// <c>"window"</c> (the whole F10 window) and <c>"content"</c> (the selected tab's page).</para>
    /// </summary>
    internal static class TutorialAnchors
    {
        /// <summary>The Guide tab's exact title in the new F10 (<c>Tabs/GuideTab.cs</c>).</summary>
        internal const string GuideTab = "Guide";

        // The other tabs, in their current left-to-right order (titles, never indices).
        internal const string ThemesTab = "UI Themes";
        internal const string RadialTab = "Radial";
        internal const string HudTab = "HUD";
        internal const string SmartStowTab = "SmartStow";
        internal const string ControlsTab = "Controls";
        internal const string FeedbackTab = "Suggestions/Bugs";

        // Anchor ids.
        internal const string TabPrefix = "tab:";
        internal const string Search = "search";
        internal const string Pause = "pause";
        internal const string Close = "close";
        internal const string Master = "master";
        internal const string MasterRadial = "master:radial";
        internal const string MasterHud = "master:hud";
        internal const string Window = "window";
        internal const string Content = "content";

        /// <summary>"tab:" + title (allocates - call it when building a step, not per frame).</summary>
        internal static string Tab(string title) { return TabPrefix + (title ?? ""); }
    }
}
