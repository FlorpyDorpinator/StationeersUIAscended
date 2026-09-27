using UnityEngine;

namespace StationeersUIMod.UI.Menu.Tutorial
{
    /// <summary>Everything the lesson director can hear about. Raised by the REAL UI call sites
    /// AFTER their action returned (Documentation/0.9.8.0/Tutorial-Build-Contract.md s3-s4), never
    /// inside the item-action mutation funnel. Append new members before <see cref="Count"/> only.</summary>
    internal enum TSignal : byte
    {
        None = 0,
        HandSwapped, WheelOpened, WheelClosed, WedgeHovered, WedgeCommitted, ChildWheelOpened,
        BackedOut, ChipParked, ChipDroppedOnTarget, ChipsDroppedOnClose, ChipsCancelled,
        WorldReachGrab, WorldSlotCueShown, HubMoved, KeepOpenUsed, ValueScrolled, GreyClicked,
        HotkeyBound, BagBound, BoundBagOpened, InWheelDigitJump, GearHeld, BeltPickerOpened,
        BeltSwapped, BeltPickerBack, SearchOpened, SearchTook, SmartStowed, GridOpened, GridClosed,
        GridTabToggled, GridCellTaken, GridItemWheelOpened, GridDragMoved, GridShiftDragMoved,
        GridStackPopupOpened, GridSplitDone, DeviceWindowOpened, PinCreated, PinClosed,
        InGridOpened, ScrollHighlightMoved, KeyboardTake, VitalsTooltipShown, MenuOpened,
        MenuTabSelected, DesignerOpening,
        Count
    }

    /// <summary>Which wheel a <see cref="TSignal.WheelOpened"/> is about (low byte of its arg).</summary>
    internal enum TWheelKind : byte { Other = 0, Belt = 1, HeldTool = 2, Bag = 3, Gear = 4, BoundBag = 5, BeltPicker = 6, Search = 7, Hub = 8 }

    /// <summary>What a hovered / committed wedge does (low byte of the arg) - the same classifier as
    /// the wheel's own curved action word (<c>RadialEntry.ClickVerb</c>), so a lesson and the wheel
    /// can never disagree.</summary>
    internal enum TWedgeKind : byte { Other = 0, Take = 1, Open = 2, Stow = 3, Swap = 4, Setting = 5, Value = 6, Hub = 7, Close = 8 }

    /// <summary>How a wheel closed (<see cref="TSignal.WheelClosed"/>'s arg).</summary>
    internal enum TCloseRoute : byte { Other = 0, Rmb = 1, Esc = 2, OpenerKey = 3, Mmb = 4, CloseBand = 5, AfterAction = 6, Switch = 7, Guard = 8 }

    /// <summary>
    /// The tutorial's EARS: an allocation-free, never-throwing signal bus. A raise site writes one
    /// line (<c>TutorialSignals.Raise(TSignal.GearHeld)</c>); the director reads frame stamps and
    /// running counts instead of subscribing, so nothing here holds a delegate and a hot reload has
    /// nothing to unhook.
    ///
    /// <para>Per signal it keeps: the <c>Time.frameCount</c> of the last raise (-1 = never), the last
    /// arg, the last text (a stored reference, never copied), and the raise count since load. Many
    /// raises in one frame collapse to the last arg; the count still sees all of them.</para>
    ///
    /// <para>Arg packing: <c>kind | (flags &lt;&lt; 8)</c> - see <see cref="Pack"/> and the arg table in
    /// the build contract (s3). Everything resets in <see cref="Shutdown"/> (hot-reload rule).</para>
    /// </summary>
    internal static class TutorialSignals
    {
        private const int N = (int)TSignal.Count;

        private static readonly int[] _frame = NewFrames();
        private static readonly int[] _arg = new int[N];
        private static readonly string[] _text = new string[N];
        private static readonly int[] _count = new int[N];

        private static int[] NewFrames()
        {
            var a = new int[N];
            for (int i = 0; i < N; i++) a[i] = -1;
            return a;
        }

        /// <summary>Record a raise. Never throws and never allocates; an out-of-range signal is ignored.
        /// Call it AFTER the real action returned successfully, from the UI call site.</summary>
        internal static void Raise(TSignal s, int arg = 0, string text = null)
        {
            int i = (int)s;
            if (i <= 0 || i >= N) return;
            try
            {
                _frame[i] = Time.frameCount;
                _arg[i] = arg;
                _text[i] = text;
                unchecked { _count[i]++; }
            }
            catch { }
        }

        /// <summary><c>Time.frameCount</c> of the last raise, -1 = never (since load).</summary>
        internal static int Frame(TSignal s)
        {
            int i = (int)s;
            return i > 0 && i < N ? _frame[i] : -1;
        }

        /// <summary>The last raise's arg (0 when never raised).</summary>
        internal static int Arg(TSignal s)
        {
            int i = (int)s;
            return i > 0 && i < N ? _arg[i] : 0;
        }

        /// <summary>The last raise's text (the stored reference - no copy), or null.</summary>
        internal static string Text(TSignal s)
        {
            int i = (int)s;
            return i > 0 && i < N ? _text[i] : null;
        }

        /// <summary>Raises since load (resets in <see cref="Shutdown"/>).</summary>
        internal static int Count(TSignal s)
        {
            int i = (int)s;
            return i > 0 && i < N ? _count[i] : 0;
        }

        /// <summary>Raised at or after <paramref name="frame"/>?</summary>
        internal static bool Since(TSignal s, int frame)
        {
            int f = Frame(s);
            return f >= 0 && f >= frame;
        }

        /// <summary>Pack a kind (low byte) with flags (bits 8 and up) - the contract's arg layout.</summary>
        internal static int Pack(int kind, int flags) => kind | (flags << 8);

        /// <summary>The kind part of a packed arg.</summary>
        internal static int KindOf(int arg) => arg & 0xFF;

        /// <summary>The flags part of a packed arg.</summary>
        internal static int FlagsOf(int arg) => (arg >> 8) & 0xFFFFFF;

        /// <summary>Hot-reload / teardown: forget every raise.</summary>
        internal static void Shutdown()
        {
            for (int i = 0; i < N; i++)
            {
                _frame[i] = -1;
                _arg[i] = 0;
                _text[i] = null;
                _count[i] = 0;
            }
        }
    }
}
