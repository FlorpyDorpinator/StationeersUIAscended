using System;
using System.Reflection;
using Assets.Scripts;                 // GameManager
using Assets.Scripts.GridSystem;      // GameState (GameState.cs:3 — NOT Assets.Scripts)
using Assets.Scripts.Inventory;       // InventoryManager
using Assets.Scripts.Objects;         // Entity
using Assets.Scripts.UI;              // StatusUpdates
using UnityEngine;

namespace StationeersUIMod.Core
{
    /// <summary>Alarm severity ranking. Ordered so a plain &gt; comparison works.</summary>
    public enum WarnSev { None = 0, Caution = 1, Critical = 2 }

    /// <summary>
    /// READ-ONLY view of the life-support alarms the game already shows the player. Calls nothing
    /// but vanilla's public Is*Caution() / Is*Critical() predicates on <c>StatusUpdates</c>, from an
    /// explicit whitelist. Mutates no game state, ever.
    ///
    /// Why a whitelist rather than a reflection sweep over every Is(.*)Caution/Is(.*)Critical:
    ///  - IsSanitationCaution/Critical (StatusUpdates.cs:589/600) read Entity.SanitationRatio, which
    ///    carries no NetworkUpdateFlags and is recomputed locally from an organ. On a multiplayer
    ///    client that is a GUESS, so it is excluded outright (CLAUDE.md multiplayer rule).
    ///  - IsJetpackPropellent* / IsLeavingMissionArea dereference _human unguarded.
    ///  - IsCoolant* reaches CoolantTank.InternalAtmosphere with no null check.
    ///  - A loose sweep would also catch IsLightOn / IsInternalsOn (StatusUpdates.cs:811/831), which
    ///    WRITE PlayerStateWindow images. A read-only feature has to own its list.
    /// Reflection still does the BINDING, so one predicate renamed by a game update kills a single
    /// channel rather than the whole feature.
    ///
    /// Caution and Critical are NESTED, not exclusive (IsOxygenCaution is 'quality &lt; WarningOxygen',
    /// IsOxygenCritical is 'quality &lt; CriticalOxygen'), so severity resolves as: Critical if any
    /// critical predicate is true, else Caution if any caution predicate is true, else None.
    /// </summary>
    public static class WarningSensor
    {
        private const int Cap = 24;
        private const float PollSeconds = 0.25f;    // 4 Hz — an alarm does not need frame rate
        private const float SettleSeconds = 2.5f;   // hold-off after a Parent identity change
        private const byte StrikeLimit = 3;

        private static readonly Func<bool>[] _caution = new Func<bool>[Cap];
        private static readonly Func<bool>[] _critical = new Func<bool>[Cap];
        private static readonly byte[] _strikes = new byte[Cap];
        private static int _count;
        private static object _boundTo;
        private static object _parentSeen;

        private static float _nextPoll;
        private static float _holdUntil;
        private static int _failures;
        private static WarnSev _sev;
        private static uint _mask;
        private static uint _seq;

        public static WarnSev Severity { get { return _sev; } }

        /// <summary>Bit per bound channel that is currently alarming — lets a consumer tell "a NEW
        /// condition joined" from "the same condition persists".</summary>
        public static uint Mask { get { return _mask; } }

        /// <summary>Monotonic sample counter. Bumped only when a poll actually lands, so a consumer
        /// can act on SAMPLES instead of frames (a frame-driven debounce would be ~33 ms, not the
        /// intended 0.5 s).</summary>
        public static uint Seq { get { return _seq; } }

        /// <summary>Mandatory hot-reload reset: the cached delegates pin a live StatusUpdates
        /// instance and a method handle into an assembly that is about to be replaced. Deliberately
        /// does NOT clear _failures — HudSystem.Shutdown() is re-entrant from inside Update on a
        /// DocumentMode toggle, and a user toggle must not refill a per-session poison budget. A
        /// genuine F6 reload replaces the assembly and re-zeroes the static anyway.</summary>
        public static void Shutdown()
        {
            ClearBinding();
            _sev = WarnSev.None; _mask = 0u; _nextPoll = 0f; _holdUntil = 0f; _parentSeen = null;
        }

        /// <summary>Stand-down (world unload / menu / master-enable off). Drops the sampled state AND
        /// the delegates — each one holds its StatusUpdates target, which would otherwise stay
        /// strongly referenced by a mod static across the unload.</summary>
        public static void Clear()
        {
            ClearBinding();
            _sev = WarnSev.None; _mask = 0u;
            _nextPoll = 0f;               // 0f, not now+interval: the next world polls immediately
            _holdUntil = 0f; _parentSeen = null;
        }

        private static void ClearBinding()
        {
            for (int i = 0; i < Cap; i++) { _caution[i] = null; _critical[i] = null; _strikes[i] = 0; }
            _count = 0; _boundTo = null;
        }

        /// <summary>Poll if due. <paramref name="human"/> is HudSnapshot.Human and acts as an IDENTITY
        /// gate against the body vanilla is actually reporting on. Bumps <see cref="Seq"/> only when a
        /// sample lands.</summary>
        public static void Tick(UnityEngine.Object human)
        {
            if (_failures >= 3) return;
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollSeconds;

            try
            {
                StatusUpdates su = StatusUpdates.Instance;
                Entity parent = StatusUpdates.Parent;

                // Respawn / body-swap / world-load settle window. HandleIconUpdates — the only
                // assigner of the private _human/_suit cache — is SKIPPED while _waitStart >= 1f
                // (StatusUpdates.cs:233); DisableStatus sets _waitStart = 1f (:214) and the
                // Parent==null branch sets 2f (:250). So there is a window where Parent is already
                // the NEW body (our identity gate passes) while _human/_suit still point at the old
                // one, and RefreshAtmosphereValues' zero-initialised _pressure/_temperature read as
                // CRITICAL. _waitStart is private and deliberately not reflected — this explicit
                // hold-off is the fail-soft equivalent.
                if (!ReferenceEquals(parent, _parentSeen))
                {
                    _parentSeen = parent;
                    _holdUntil = Time.unscaledTime + SettleSeconds;
                }

                if (su == null || human == null || parent == null
                    || GameManager.GameState != GameState.Running
                    || InventoryManager.ParentHuman == null
                    || !ReferenceEquals(parent.AsHuman, human)
                    || Time.unscaledTime < _holdUntil)
                { _sev = WarnSev.None; _mask = 0u; _seq++; return; }

                if (!ReferenceEquals(_boundTo, su)) Bind(su);

                WarnSev worst = WarnSev.None;
                uint mask = 0u;
                for (int i = 0; i < _count; i++)
                {
                    if (_strikes[i] >= StrikeLimit) continue;
                    try
                    {
                        bool crit = _critical[i] != null && _critical[i]();
                        bool caut = !crit && _caution[i] != null && _caution[i]();
                        if (crit) { worst = WarnSev.Critical; mask |= (1u << i); }
                        else if (caut) { if (worst == WarnSev.None) worst = WarnSev.Caution; mask |= (1u << i); }
                        _strikes[i] = 0;   // a clean sample forgives a transient
                    }
                    catch
                    {
                        // _suit is INTERFACE-typed (StatusUpdates.cs:496 'ISuit suit = this._suit;'),
                        // so vanilla's != null is a plain reference compare and a suit destroyed
                        // between HandleIconUpdates runs throws MissingReferenceException. That is
                        // transient — strike the channel, do not execute it on the first offence.
                        // Strikes must be CONSECUTIVE (a clean sample zeroes the counter above), so
                        // reaching the limit means three back-to-back throws, not three all session.
                        if (_strikes[i] < StrikeLimit)
                        {
                            _strikes[i]++;
                            // Announce the retirement: without this a safety channel goes quiet with
                            // no way to tell it ever existed.
                            if (_strikes[i] >= StrikeLimit)
                                UIALog.Warn("WarningSensor: channel " + i + " retired after "
                                    + StrikeLimit + " consecutive faults; it stops contributing to the alert pulse.");
                        }
                    }
                }
                _sev = worst; _mask = mask; _seq++;
                // A poll that completed without throwing forgives the whole-sensor budget, so it
                // counts CONSECUTIVE failures — matching the per-channel strike semantics above.
                // Otherwise three unrelated hiccups across a long session would retire the feature.
                _failures = 0;
            }
            catch (Exception e)
            {
                if (++_failures <= 3)
                    UIALog.Warn("WarningSensor poll failed: " + e.Message
                        + (_failures >= 3 ? " (alert pulse disabled for this session)" : ""));
                _sev = WarnSev.None; _mask = 0u; _seq++;
            }
        }

        private static void Bind(StatusUpdates su)
        {
            ClearBinding();
            _boundTo = su;

            // The "you are in trouble in the next few minutes" set. Slow/social/cosmetic channels
            // (Mood, Hygiene, Waste, Sanitation) stay out: an alarm that is lit most of the time is
            // noise, and Sanitation is server-only state besides.
            Add(su, "Oxygen");      Add(su, "Power");        Add(su, "AirTank");
            Add(su, "Filter");      Add(su, "Nutrition");    Add(su, "Hydration");
            Add(su, "Toxin");       Add(su, "PressureLow");  Add(su, "PressureHigh");
            Add(su, "Hot");         Add(su, "Cold");         Add(su, "Leaking");
            Add(su, "Health");
        }

        private static void Add(StatusUpdates su, string channel)
        {
            if (_count >= Cap) return;
            Func<bool> c = Make(su, "Is" + channel + "Caution");
            Func<bool> k = Make(su, "Is" + channel + "Critical");
            if (c == null && k == null) return;   // channel gone after a game update: skip it
            _caution[_count] = c; _critical[_count] = k; _count++;
        }

        private static Func<bool> Make(StatusUpdates su, string method)
        {
            try
            {
                MethodInfo mi = typeof(StatusUpdates).GetMethod(
                    method, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (mi == null || mi.ReturnType != typeof(bool)) return null;
                return (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), su, mi);
            }
            catch { return null; }
        }
    }
}
