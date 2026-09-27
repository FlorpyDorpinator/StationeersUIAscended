# In-game feedback button → GitHub issues → Claude triage bot

> **Update 2026-09-27:** built and shipped in 1.0.0. When this code repo went public, the issues
> moved to the PRIVATE repo `FlorpyDorpinator/StationeersUIAscended-Feedback` (players' reports stay
> private), and the triage workflow moved there with them. It checks this repo out read-only. Where
> the text below says "the repo" for issues, labels or the Actions secret, read the feedback repo.

**Status (original): PROPOSED — awaiting FlorpyDorp's approval. Nothing is implemented.**
Requested by FlorpyDorp 2026-09-25: an in-game bug/suggestion button, modeled on the one in
the sister app (a sister project), that files a GitHub issue a Claude bot can read, plan
against, and report back on. Version target is FlorpyDorp's call, per house rules — nothing
here implies a release number.

**Transport decision (FlorpyDorp, 2026-09-25):** the relay runs on his Dell server at home,
fronted by a **Cloudflare Tunnel** — chosen over a Cloudflare Worker (he wants the relay and
the token on his own hardware) and over direct port-forward/DDNS (which would make his home
IP resolvable by anyone with the mod). The tunnel is the one shape that keeps the token at
home AND hides the home IP. Alternatives considered are recorded in §2.1.

---

## The brief (read this first — everything after is detail)

**Is it possible from inside the game? Yes.** The game client can make ordinary HTTPS calls
(Unity's `UnityWebRequest`, no game API involved), the F10 Control Center already has the
window, tab system, and text-input widget the form needs, and the sister app has already proven the
back half of the pipeline end-to-end: issue lands on GitHub → a GitHub Actions workflow runs
Claude (`anthropics/claude-code-action`) → Claude reads the real code and posts an
implementation plan as a comment → labels flip so the morning review is one filter click.
UIA can adapt the sister app's workflow file nearly verbatim.

**The one thing the sister app has that UIA doesn't: a backend.** the sister app's form posts to its own
Django server, and *the server* holds the GitHub token. UIA is a mod distributed publicly to
strangers — a GitHub token shipped inside the DLL would be scraped and auto-revoked within
hours (GitHub actively scans for this). So UIA needs one small new piece: a **token relay**
that holds the token server-side, accepts the mod's feedback POST, sanitizes and rate-limits
it, and files the GitHub issue. The mod ships only the relay's URL, which is safe to be
public. **The relay runs on FlorpyDorp's Dell server, fronted by a Cloudflare Tunnel**: the
relay app listens on localhost only, `cloudflared` dials *outbound* to Cloudflare (no router
ports opened, home IP never resolvable by players), and Cloudflare's edge provides TLS, DDoS
absorption, and a free rate-limiting rule. Verified against Cloudflare's docs 2026-09-25:
Tunnel is free on all plans; the one prerequisite is a domain on Cloudflare (~$10/yr at-cost
if none exists); `cloudflared.exe service install <token>` runs it as a Windows service;
ephemeral Quick Tunnels (`trycloudflare.com`) exist for testing only.

**The shape:**

```
Player presses F10 → Feedback tab → Bug | Suggestion → title + description → Send
   └─ report saved to disk FIRST (outbox — feedback is never lost)
       └─ HTTPS POST to uiascended.ssui.dev (Cloudflare edge → tunnel → relay on the Dell)
            ├─ sanitize, cap sizes, rate-limit (Cloudflare edge rule + in-app backstop)
            └─ files a GitHub issue (labels: feedback, bug|enhancement, triage:pending)
                 └─ GitHub Actions fires on issue-open (+ nightly sweep)
                      └─ Claude reads the issue + the real mod source, posts ONE plan
                         comment, flips triage:pending → plan:ready
Morning: FlorpyDorp filters label:plan:ready and reads plans, not raw reports.
Later (Phase 3): the mod polls the relay for status → "Your report: Fixed in 0.9.8.0".
```

**What it costs:** $0/month, plus a domain (~$10/yr) if FlorpyDorp doesn't have one on
Cloudflare already. Cloudflare Tunnel is free on all plans; the Dell is already running;
GitHub Actions on the private repo has 2,000 free minutes/month (a plan run is ~5–10 min, so
~200+ reports/month before it costs anything); Claude runs on the subscription OAuth token
(`claude setup-token`), not metered API — this is exactly how the sister app runs it.

**Phases** (each independently shippable, detail in §7):
- **Phase 0 — prove the loop with zero game code.** Relay running on the Dell behind a
  Quick Tunnel, the Actions workflow committed; file a test issue with `curl`; watch Claude
  plan it. Then the named tunnel on the real domain. An evening of work.
- **Phase 1 — the in-game form.** New Feedback tab in F10, outbox on disk, POST to the relay.
  (2026-09-25: FlorpyDorp pulled the mod-side *backend* of Phase 1 forward — FeedbackService,
  outbox, and a console command, no UI. The tab remains Phase 1 proper.)
- **Phase 2 — richer context.** Opt-in attach of the active HUD profile XML and a filtered
  log excerpt (both are the two things we always end up asking testers for).
- **Phase 3 — close the loop in game.** "Your reports" list with live status
  (Received / Under review / Fixed in X / Closed).

**Decisions FlorpyDorp needs to make** before Phase 0 goes live (full list in §8): which
repo the issues land in (recommendation: the main private repo), and which domain fronts the
tunnel (an existing one on his Cloudflare account, or a new ~$10/yr registration).

---

## §0 What we're copying from the sister app, and what we're not

the sister app's pipeline (read `C:\Dev\<sister project>\the sister app\Documentation\FEEDBACK_PIPELINE_PLAN.md`
and `api/feedback/github.py` — both are heavily annotated) commits to three invariants worth
adopting wholesale:

1. **Feedback is never lost.** The report is durably stored *before* GitHub is contacted;
   a GitHub outage leaves a readable report, never a lost one. the sister app stores a DB row;
   UIA stores an outbox file (§1.5).
2. **User-typed text is data, not markup.** HTML comments are defanged before they reach the
   issue body, so nobody can forge the triage bot's dedupe marker; every cap is enforced
   server-side (for UIA: relay-side). Ported to the relay in §2.4.
3. **The bot cannot write code.** The Actions workflow grants `issues: write` and
   `contents: read`, nothing else. A prompt-injection attempt tops out at a silly comment.

What UIA does **not** copy:
- **User accounts / reporter identity.** the sister app knows who filed what (session auth).
  Stationeers players are anonymous; the report carries no identity unless the player types
  a Discord name into an optional "how to reach you" field (§1.3). This is a feature — no
  privacy surface — and a limitation — no per-user notification when their item ships.
- **Server-held attachments.** the sister app keeps screenshots in the app and lends them to the
  bot via a bearer-token endpoint. UIA has no app server; instead the two attachments that
  actually matter for HUD bugs (the profile XML, a log excerpt) are small text and travel
  *inline* in the issue body, opt-in and capped (§1.4). Screenshots are deliberately out of
  scope until Phase 3+ (§7).
- **The Django backend.** Replaced by the Dell-hosted relay, which is stateless — GitHub
  itself is the database (the sister app's FB row ↔ our issue).

---

## §1 The in-game capture (mod side)

### 1.1 Where the button lives

A new **`FeedbackTab`** implementing `IUiaTab`
([UiaControlCenter.cs:15](../../Assets/Scripts/StationeersUIMod/UI/Menu/UiaControlCenter.cs))
added to the `_tabs` list at
[UiaControlCenter.cs:312](../../Assets/Scripts/StationeersUIMod/UI/Menu/UiaControlCenter.cs)
— after `GuideTab`, so the tab order stays "settings first, help/meta last". The F10 window
is the mod's front door and already handles cursor unlock, scrim, popup layer, and theme.
Seven tabs must still fit the tab bar at the 1000 px reference width — check `BuildTabBar`
spacing during implementation; worst case the label is "Report" instead of "Feedback".

No HUD overlay button, deliberately: design philosophy #2 keeps the bottom UI two hand
boxes, and a feedback affordance is not gameplay. The Guide tab and the hint bar can
*mention* F10 → Feedback.

**Standing directive #8 conformance:** this is a Control Center surface, not a HUD
element/effect — nothing per-tier, nothing that travels with a theme, no `HudStyleSlot`
involvement. Documented here as deliberately shared/content. New config keys (§1.6) are
infrastructure, not theme, and live outside `HudTheme` entirely.

### 1.2 The form

Built from the existing kit (`UiaUi.*`), same as every other tab. All displayed strings
ASCII-only (the TMP tofu rule); any check mark or chevron is drawn with
`TriangleGraphic`/`PanelGraphic`, not glyphs.

1. **Kind picker** — two large side-by-side tiles, `Bug` ("Something is not working") and
   `Suggestion` ("An idea to make this better"). Accent fill on the selected tile.
2. **Title** — single-line `UiaUi.InputField`
   ([UiaUi.cs:129](../../Assets/Scripts/StationeersUIMod/UI/Menu/Kit/UiaUi.cs)), 120 chars,
   required. Placeholder swaps with kind: "Radial closes when I..." / "Let me pin the...".
3. **Description** — required, multi-line. `UiaUi.InputField` is single-line
   (`LineType.SingleLine`); add a `UiaUi.TextArea` variant in the kit —
   `LineType.MultiLineNewline`, fixed-height row (~120 px), top-aligned text. Same
   LayoutElement collapse guard the single-line field already documents (the D-009 lesson).
   Cap 4,000 chars. Hint for bugs: "What you did, what you expected, what happened instead."
4. **Contact (optional)** — one single-line field: "Discord name, if you want a reply
   (optional)". The only identity in the whole pipeline, and only if typed.
5. **Include my HUD profile** — checkbox, default ON for bugs, OFF for suggestions (§1.4).
6. **Include recent mod log lines** — checkbox, default OFF (§1.4).
7. **What gets sent** — a small always-visible mute-text block listing exactly the
   auto-context of §1.3. No surprises; this is the privacy disclosure.
8. **Footer** — `Cancel` + `Send` (disabled until kind+title+description present; label
   "Sending..." while busy). **On failure the typed text is not cleared** and the report is
   already in the outbox (§1.5) — the error line says "Saved locally - will retry."
9. **Success panel** — replaces the form body (not a transient toast): drawn check mark,
   "Thanks - that's now UIA-104. It goes straight to the developers." and a `Done` button.
   The reference number comes back from the relay (it's the GitHub issue number).

Typing in TMP input fields inside the F10 window already coexists with game keybinds today
(StorageTab and the tutorial's `TutorialTextField` ship input fields); reuse whatever focus
handling they rely on — no new input plumbing.

### 1.3 Auto-captured context (sent with every report, disclosed in the form)

| Field | Source |
|---|---|
| Mod version | `StationeersUIMod.ModVersion` ([StationeersUIMod.cs:21](../../Assets/Scripts/StationeersUIMod/StationeersUIMod.cs)) |
| Deploy route | plugins vs ScriptEngine (the cfg filename already encodes this) |
| Game build | the game's own version string — **verify exact API against the decompile at implementation time** (house rule; candidates: the value the main menu renders, `Application.version`) |
| Active HUD profile | name + a `modified-from-shipped` flag (hash vs `.shipped-manifest`, the SyncShipped machinery) |
| Player context | SP / MP-client / MP-host; current tier (Bare/Suited/Robot) at open time |
| Display | resolution + UI scale |
| OS | `SystemInfo.operatingSystem` |

No Steam ID, no save name, no IP (the relay sees the IP for rate limiting but never writes
it into the issue).

### 1.4 Opt-in payloads (the two things we always ask testers for anyway)

- **Active profile XML** — read from disk (`config/StationeersUIMod/HudProfiles/<active>.xml`),
  capped 100 KB, shipped inside a `<details><summary>Profile XML</summary>` code fence in the
  issue body. Rationale: the standing lesson that HUD/tier bugs can't be reproduced from the
  shipped default — this puts the player's *actual* profile in front of the bot and us on
  every bug report, without a round trip through Discord.
- **Log excerpt** — NOT the whole BepInEx log (it contains save names, mod lists, and
  worse). Filter: the last ~100 lines matching the mod's own log source (`UIALog` prefix)
  plus any exception blocks that mention `StationeersUIMod` frames. Capped 30 KB, same
  `<details>` treatment. Default OFF.

Both are plain text, both inline — no file uploads, no storage service, and the
issue-body cap (§2.3) truncates the profile before the description, never the reverse.

### 1.5 The outbox (invariant 1: feedback is never lost)

New `Core/FeedbackService.cs` (pure service — **not** in `ItemActions.cs`, which stays
mutations-only; this feature never mutates game state and is trivially MP-safe):

1. On Send: serialize the report to
   `config/StationeersUIMod/Feedback/outbox/<utc-timestamp>.json`, **then** POST.
2. On 201 from the relay: move the file to `Feedback/sent/<timestamp>-UIA-<n>.json`
   (this doubles as the local receipt Phase 3 reads).
3. On failure: file stays in outbox; retried on next game launch and next F10-Feedback open
   (max 5 attempts recorded in the file, mirroring the sister app's attempts gate — a dead relay
   is never hammered forever; a "Retry now" button clears the counter).
4. HTTP via `UnityWebRequest` in a coroutine on a dedicated `DontDestroyOnLoad` host object.
   `UnityWebRequest` uses Unity's own TLS stack, sidestepping net48/Mono
   `ServicePointManager` TLS-1.2 configuration entirely. Timeout 8 s (a player is waiting).
   No game API involved — nothing to verify against the decompile for the transport.
5. **Hot-reload safety:** the host object and every static (`_inFlight`, config handles) are
   torn down in the mod's `Shutdown` path; a double-F6 leaves nothing stale. In-flight
   request at teardown → aborted; the outbox file makes that lossless.
6. **`uiareset` must delete `Feedback/`** — add it to the folder list in
   `Core/FinderCommands.cs` (the config-tree rule: a fresh-install test wipes everything).

### 1.6 New config keys

- `Feedback.Enabled` (default true) — kill switch; also lets a streamer hide the tab.
- `Feedback.RelayUrl` (default EMPTY = the built-in relay `https://uiascended.ssui.dev`,
  a constant in `FeedbackService`) — set only to override: a dead/rotated relay is fixable
  by config before a mod update ships, and testing can point at a Quick Tunnel or
  `localhost`. Empty-means-built-in (rather than baking the URL into the cfg default) is
  deliberate: BepInEx never overwrites a saved value, so a baked URL would strand every
  install on the old address after a move.

Both are new adds, no renames — no `ConfigMigration` step needed (directive #8 clause 3
satisfied trivially; noted here so the review doesn't have to ask).

---

## §2 The token relay (on the Dell, behind a Cloudflare Tunnel)

### 2.1 Why a relay, and why this shape (alternatives rejected)

- **PAT inside the mod** — rejected. Public DLL + string-scannable secret = scraped and
  auto-revoked by GitHub's secret scanning, likely within hours. Obfuscation only delays it.
- **GitHub App / device flow** — rejected. Requires every *player* to have and link a GitHub
  account. Bad friction for a game audience; most reporters would bounce.
- **Discord webhook instead of GitHub** — genuinely cheaper (webhook URL posts straight to a
  channel, feeds the existing `tools/fetch-discord.ps1` + `/triage` pipeline). Rejected as
  the *primary* because the whole point is machine-readable issues a bot can plan against
  and label-flip, and the mod can't read Discord back for Phase 3 status.
- **Cloudflare Worker** — viable and near-zero-maintenance, rejected by FlorpyDorp's call:
  he wants the relay and the token on hardware he owns. Remains the documented fallback if
  home hosting ever hurts (the mod's `Feedback.RelayUrl` override makes moving painless).
- **Direct port-forward + DDNS on the Dell** — rejected: the DDNS name resolves to his home
  IP, readable by anyone with the mod, with no edge absorbing junk traffic.
- **Dell + Cloudflare Tunnel** — **chosen** (FlorpyDorp, 2026-09-25). Token stays home, home
  IP stays hidden, $0/month, and Phase 3's status endpoint works through the same tunnel.

### 2.2 Topology (verified against developers.cloudflare.com/tunnel, 2026-09-25)

- The relay is a small **C#/.NET minimal-API service** (house language) listening on
  `http://127.0.0.1:8080` **only** — never bound to a LAN or WAN interface.
- `cloudflared` runs beside it as a Windows service (`cloudflared.exe service install
  <TUNNEL_TOKEN>`), dialing *outbound* to Cloudflare (needs outbound port 7844); zero
  inbound router ports. Players resolve `uiascended.ssui.dev` to Cloudflare's edge, never
  to the house. Tunnel keeps four redundant connections to two Cloudflare datacenters and
  auto-reconnects across reboots.
- TLS terminates at Cloudflare's edge cert — no Let's Encrypt, no pinning, no renewals.
  (Consequence, accepted: Cloudflare can see request plaintext in transit — bug reports and
  HUD profile XML, nothing sensitive.)
- A **Cloudflare edge rate-limiting rule** filters bursts as defense-in-depth — but the
  Free plan only supports 10-second windows (verified 2026-09-25, corrected from an
  earlier draft that assumed an hourly edge rule), so the authoritative per-hour/IP policy
  lives **in the relay itself** (§2.4); the edge rule just kills floods cheaply.
- Prerequisite: a domain in Cloudflare DNS. For Phase 0 smoke testing, an ephemeral Quick
  Tunnel (`cloudflared tunnel --url http://localhost:8080`, testing-only per Cloudflare)
  works with no account or domain at all.
- **Dell downtime = a few minutes of edge 530s**, which the mod's outbox absorbs by design
  (queue on disk, retry later). Delayed, never lost.

### 2.3 Endpoints

- `POST /v1/report` — body: `{kind, title, description, contact?, context{...},
  profileXml?, logExcerpt?}`. Validates, sanitizes, files the issue via
  `POST /repos/<owner>/<repo>/issues` (fine-grained PAT, Issues RW on that one repo,
  nothing else). Returns `{number}` → the in-game "UIA-104".
- `GET /v1/status/<number>` (Phase 3) — reads the issue's state + labels via the same PAT,
  returns `{state, labels}` only. No bodies, no comments — nothing a scraper can mine.
- A `DRY_RUN` mode (env-gated) returns the fully built issue JSON instead of calling GitHub
  — the same "preview before faith" trick as the sister app's `build_issue()`, and what the
  Phase 0 smoke test exercises before a PAT exists.

### 2.4 Sanitization and abuse posture (ported from the sister app's `github.py`)

- `_neutralize`: defang `<!--`/`-->` in every user-typed field so nobody can forge the
  `uia-feedback` or triage-plan markers (invariant 2, verbatim from the sister app).
- Caps enforced relay-side (client caps are courtesy): title 120, description 4,000,
  contact 60, profile 100 KB, log 30 KB, total body 60,000 chars (GitHub's cap is 65,536)
  — truncation order: log, then profile, then never the description.
- Rate limit: **6 reports/hour/IP enforced in the relay** (raised from 3 by FlorpyDorp,
  2026-09-26; `UIA_RELAY_REPORTS_PER_HOUR` / `setup-dell.ps1 -ReportsPerHour N` changes it
  without a rebuild; keyed on `CF-Connecting-IP`),
  plus a same-title+description dedupe window, plus a Cloudflare edge burst rule (10-second
  window — the most the Free plan allows) as defense-in-depth. 429 → the mod's outbox
  retry handles it politely.
- A static `X-UIA-Client` header ships in the mod — **obscurity, not security**, and
  documented as such; it filters drive-by scanners, not a determined abuser.
- Worst case accepted: someone scripts spam issues into a **private** repo. Blast radius is
  Actions minutes and annoyance; response is rotate the tunnel hostname (the mod's URL is
  config-overridable, §1.6) and tighten limits. No player data is at risk because none is
  held.
- The PAT lives in an environment variable on the Dell + a copy in `C:\Dev\.secrets\` (the
  existing convention: `the sister app Feedback Token.txt` already lives there). Never in the
  repo, never in the mod, never in the issue.

### 2.5 Issue format (what the bot and FlorpyDorp read)

```
Title:  [Bug] Radial closes when swapping hands

<!-- uia-feedback:v1:id=<report-guid> -->
**Bug reported from in game** - 25 Sep 2026, 9:14 PM UTC

### What they said
> (defanged, quoted description)

### Details
| | |
|---|---|
| Mod version | 0.9.7.4 Experimental (ScriptEngine) |
| Game build | 27701 |
| HUD profile | "Stationeers Blue" (modified from shipped) |
| Context | MP client - Suited tier |
| Display | 2560x1440 @ 1.0 |
| Contact | JacksonEnjoyer42 (Discord) - or "(none given)" |

<details><summary>Profile XML (player opted in)</summary> ... </details>
<details><summary>Mod log excerpt (player opted in)</summary> ... </details>

<sub>Filed automatically from in-game feedback. The reporter is anonymous
unless a contact is listed.</sub>

Labels: feedback, bug|enhancement, triage:pending
```

The marker's `id` is a relay-generated GUID (the body is built *before* GitHub assigns an
issue number, and the relay is stateless — GitHub itself is the database). The player-facing
"UIA-104" reference is the issue number from the create response. The marker exists so the
bot's dedupe has a machine-readable "filed by the pipeline" join key, same role as
the sister app's `sister-app-feedback:v1:id=`; Phase 3's status lookup keys on the issue number.

---

## §3 The Claude triage bot (GitHub Actions)

Adapt `the sister app/.github/workflows/nightly-triage.yml` — it is battle-tested (its header
documents a month of failure modes already fixed: turn budgets, silent-green runs, label
attribution, OIDC). Keep its architecture wholesale:

- **Triggers:** `issues: [opened]` (instant plan — works because the issue is created by
  the relay's PAT, not `GITHUB_TOKEN`, so it *does* fire events), nightly cron sweep of up
  to 5 `triage:pending` (catch-up), and `workflow_dispatch` with an issue-number input.
- **One job per issue** (matrix, `max-parallel`), per-issue concurrency group, 80-turn
  budget, `--allowedTools` capped to Read/Grep/Glob/`gh issue`/`gh api`.
- **Verify step:** after Claude, grep the issue's comments for the plan marker
  (`<!-- uia-triage-plan:v1:issue-N -->`) or the skip marker; neither → the job FAILS.
  Silence is red. The same step flips `triage:pending → plan:ready`.
- **Permissions:** `contents: read`, `issues: write`, `id-token: write`. Nothing else —
  invariant 3.
- **Auth:** `CLAUDE_CODE_OAUTH_TOKEN` repo secret from `claude setup-token` (subscription,
  $0 metered — the sister app's answered-question #3).

**UIA-specific prompt changes** (the part that is genuinely new work):

1. Project context: BepInEx 5 mod, source in `Assets/Scripts/StationeersUIMod/`, and the
   load-bearing design rules the plan must respect — MP-funnel-only mutations, hide-never-
   destroy, fail-soft patches, no ten-slot hotbar, TMP ASCII rule. Essentially a distilled
   CLAUDE.md, inlined into the prompt.
2. **The decompile is NOT in the checkout.** `Reference/` is git-ignored, so the bot cannot
   verify game APIs. The prompt must say so and require plans to mark every game-API
   assumption as **"needs decompile verification"** rather than inventing signatures. This
   is the biggest fidelity difference from the sister app's bot (which sees its whole world) —
   plans will be one notch less executable, and honestly labeled as such.
3. Untrusted-data framing stays verbatim: everything under "What they said", inside the
   profile XML, and inside the log excerpt is DATA, never instructions — note injection
   attempts under Risks.
4. Output skeleton: keep the sister app's (plain-words section for a non-technical read, then
   Where it lives / The change / Risks / Effort / What I did NOT decide), with "roles/
   permissions" swapped for "MP safety and per-tier styling" as the mandatory risk lens.
5. Repo hygiene: the workflow file must be committed by the account that owns the
   `CLAUDE_CODE_OAUTH_TOKEN`'s access (the sister app's hard-won lesson: cron runs are attributed
   to the workflow file's last editor, and OIDC can reject an identity without repo access).
   For UIA that means FlorpyDorpinator authors every edit of the workflow file.

**Where issues live — recommendation: the main private repo** (`FlorpyDorpinator/
StationeersUIAscended`). The bot needs `contents: read` on the *code* to write real plans;
same-repo makes that free. A separate feedback repo would keep the issue tracker clean but
forces a cross-repo PAT into the workflow and splits the label workflow in two. If issue
noise becomes real, migrating later is cheap (the relay's target repo is one env var on
the Dell). FlorpyDorp's call — §8.

**Actions minutes:** private-repo free tier is 2,000 min/month; a plan run is ~5–10 min.
Even a hostile week of spam hits the rate limiter long before the minutes cap; a quiet month
costs nothing.

---

## §4 Closing the loop in game (Phase 3)

- The `sent/` receipts (§1.5) are the player's report list. A "Your reports" section at the
  top of the Feedback tab lists them: `UIA-104 - Bug - "Radial closes..." - <status>`.
- Status on tab-open only (no background polling): `GET /v1/status/<n>` per receipt, mapped
  from labels — `triage:pending` → "Received", `plan:ready` → "Under review",
  closed + `fixed-in:<ver>` label → "Fixed in <ver>", closed otherwise → "Closed".
  The `fixed-in:<ver>` label becomes part of the release ritual (one label per shipped fix).
- Degrades to "--" when the relay is unreachable — never a guess (the same read-only
  degradation rule the HUD follows for server-only values).

---

## §5 Security & privacy invariants (the contract, in one place)

1. Feedback is never lost — outbox before network, always (§1.5).
2. User-typed text is data, not markup — defang before the issue body (§2.3).
3. No secrets in the mod — the DLL carries a URL and a cosmetic header, nothing else (§2).
4. No identity by default — contact is one optional field the player types (§1.3).
5. The bot cannot write code — Actions permissions caps (§3).
6. Player text/profile/log reaching the LLM is explicitly framed as untrusted (§3.3).
7. Everything sent is shown in the form before Send — the disclosure block (§1.2.7).

---

## §6 What this feature does NOT do (scope fences)

- No screenshots (image hosting + Camo-privacy questions; revisit after Phase 3).
- No in-game issue browser/comment thread (Discord remains the conversation venue).
- No auto-crash-reporting — reports are always player-initiated, never silent telemetry.
- No game-state mutation anywhere; `ItemActions.cs` untouched.
- No vanilla HUD/UI objects touched — this is entirely inside the mod's own F10 canvas.

## §7 Phases and effort

| Phase | What ships | Effort |
|---|---|---|
| 0 | Relay built + running on the Dell (DRY_RUN → Quick Tunnel → named tunnel on the real domain), PAT minted, workflow committed, one curl-filed issue planned by the bot end-to-end | 1 evening |
| 1 | FeedbackTab + TextArea kit widget + FeedbackService/outbox + uiareset entry; live relay URL in config (mod-side backend + console command pulled forward 2026-09-25 on FlorpyDorp's word) | 2–3 sessions |
| 2 | Profile-XML and log-excerpt opt-ins, incl. relay cap/truncation order | 1 session |
| 3 | Status endpoint + "Your reports" list + `fixed-in:` label ritual | 1–2 sessions |

Phase 0 before any game UI, deliberately: it proves the *novel* half (relay + bot) with
zero UI investment, and its tunnel is what Phase 1 develops against. Phase 1 gets the
standard adversarial review before shipping (mutation paths: none; but review the outbox
file handling, teardown, and the relay's sanitization against §5).

## §8 Open questions for FlorpyDorp (nothing proceeds past Phase 0 without these)

1. **Which repo do issues land in?** Recommendation: main private repo (§3). OK?
2. ~~Which domain fronts the tunnel~~ — **DECIDED 2026-09-26: `uiascended.ssui.dev`**,
   provided for the project; the tunnel lives in a separate Cloudflare account and only the
   tunnel token. `uiascended.stationeers-wiki.com` was rejected: that zone challenges every
   request, which the game cannot pass. (Hosting hardware: your Dell.)
3. ~~Rate limits~~ — **DECIDED 2026-09-26: 6/hour/IP** (configurable on the Dell).
4. **The contact field** — keep it (one optional Discord-name line), or fully anonymous?
5. **Discord-webhook fallback** — want it wired as a relay-down fallback path, or is the
   outbox retry enough? (Recommendation: outbox is enough; fewer secrets.)
6. **Version target** — yours to name, as always.

## New game-API dependencies

None for the pipeline itself (`UnityWebRequest`, `SystemInfo`, TMP input are Unity, not
game APIs). The one to-verify item is the **game build string** for §1.3 context — flagged
for decompile verification at Phase 1 implementation time per house rule.
