# Feedback pipeline — Phase 0 checklist (armed by FlorpyDorp)

Phase 0 proves that the relay on the Dell plus the Claude triage bot work end-to-end,
turning a test issue into a plan comment—before any in-game UI exists. Nothing
player-facing changes until FlorpyDorp manually flips it on in the mod config.

---

## 1. Local dry run (no accounts, no secrets needed)

- [ ] Build the relay:
  ```
  dotnet build FeedbackRelay/FeedbackRelay.csproj -c Release
  ```
  Must be clean (0 warnings, 0 errors).

- [ ] Set dry-run mode and run:
  ```
  $env:UIA_RELAY_DRY_RUN=1
  dotnet run --project FeedbackRelay
  ```
  Relay listens on `http://127.0.0.1:8080`.

- [ ] In another terminal/PowerShell window, run the smoke test:
  ```
  .\FeedbackRelay/smoke-test.ps1
  ```
  Confirms it POSTs a sample report and the built issue JSON comes back (dry run never
  contacts GitHub).

---

## 2. Quick Tunnel smoke test (still no domain needed)

- [ ] Install cloudflared on the Dell:
  ```
  winget install Cloudflare.cloudflared
  ```
  Or download from https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/.

- [ ] Start a Quick Tunnel (testing only; Cloudflare caps it at 200 concurrent requests;
  never ship this URL):
  ```
  cloudflared tunnel --url http://localhost:8080
  ```
  Prints an ephemeral `https://<random>.trycloudflare.com` URL.

- [ ] From another machine, POST a sample report to that tunnel URL (note `curl.exe` —
  bare `curl` in Windows PowerShell is an alias for Invoke-WebRequest and takes different
  arguments; use the JSON body from step 6 or the smoke-test script's sample):
  ```
  curl.exe -X POST https://<random>.trycloudflare.com/v1/report -H "Content-Type: application/json" -d "{\"kind\":\"bug\",\"title\":\"Tunnel test\",\"description\":\"Testing through the quick tunnel.\"}"
  ```
  Confirm the same dry-run response arrives through the tunnel.

---

## 3. Domain + named tunnel (the production front door)

- [x] Domain decided (2026-09-26): **`uiascended.ssui.dev`**, provided for the project. The tunnel
  and its public hostname live in HIS Cloudflare account; he handed over only the tunnel
  token. (Not `uiascended.stationeers-wiki.com` — that zone challenges every request, which
  the game can't pass.)

- [x] Tunnel created, public hostname → `http://127.0.0.1:8080` (no trailing
  slash, Path empty). DNS + certificate verified valid from outside, 2026-09-26.

- [ ] On the **Dell**, as administrator, run the one-shot installer (it creates the relay
  service, starts it in dry run, installs cloudflared and connects the tunnel — paste the
  token when it asks; input is hidden):
  ```
  powershell -ExecutionPolicy Bypass -File C:\Services\UIAFeedbackRelay\setup-dell.ps1
  ```
  It ends with `ALL DONE` or a red `FAIL` line saying what to fix.

- [ ] From any machine: `curl.exe -s https://uiascended.ssui.dev/v1/ping` returns
  `{"ok":true,"dryRun":true}` (not Cloudflare error 1033, which means no connector is up).

- [ ] Add a burst rule in Cloudflare (defense-in-depth only — the Free plan supports just
  10-second windows, so the real per-hour/IP limit - 6 by default - is enforced by the relay itself):
  - Go to https://dash.cloudflare.com, select the domain.
  - **Security** → **Rate limiting** → create a rule scoped to the `/v1/report` path,
    e.g. 5 requests per 10 seconds per IP → block.

---

## 4. Mint the GitHub token (the only real secret)

- [ ] On GitHub, go to **Settings** → **Developer settings** → **Personal access tokens**
  → **Fine-grained personal access tokens** → **Generate new token**.

- [ ] Token settings:
  - **Token name:** `UIA Feedback Relay`
  - **Expiration:** 90 days (rotate annually)
  - **Repository access:** ONLY the feedback repo (e.g.,
    `FlorpyDorpinator/StationeersUIAscended`)
  - **Repository permissions:** Issues = Read and write. All others = None.

- [ ] Copy the token. Save it to `C:\Dev\.secrets\` (house convention; outside the repo):
  ```
  Set-Content -Path "C:\Dev\.secrets\UIA Feedback Token.txt" -Value "<paste token>" -Encoding utf8
  ```

- [ ] On the **Dell**, set the variables PERSISTENTLY (`$env:` only lasts for the current
  window — the relay must see these however it is launched):
  ```
  setx UIA_FEEDBACK_GITHUB_TOKEN "<paste token>"
  setx UIA_FEEDBACK_REPO "FlorpyDorpinator/StationeersUIAscended"
  setx UIA_RELAY_DRY_RUN ""
  ```
  (setx takes effect for NEW processes only — restart the relay after this.)

- [ ] Restart the RELAY app itself (not cloudflared — that is the tunnel, a separate
  process; see `FeedbackRelay/README.md` for how the relay is hosted and restarted).

- [ ] Optional client-key filter (obscurity, not security — filters drive-by scanners):
  ```
  setx UIA_RELAY_CLIENT_KEY "uia-mod"
  ```
  If set, it must be exactly `uia-mod` — that is the header value the mod sends.

- [ ] **Never commit or paste the token anywhere.** It lives on the Dell only.

---

## 5. Prepare the repo (before the first real issue)

- [x] Labels created 2026-09-26 via `gh label create` (bug/enhancement were GitHub defaults):
  - `feedback`
  - `bug`
  - `enhancement`
  - `triage:pending`
  - `plan:ready`

  (GitHub rejects issue creation with a 422 if a label doesn't exist.)

- [ ] Install the **Claude GitHub App** on the repo: https://github.com/apps/claude →
  Install → *Only select repositories* → `StationeersUIAscended`. Required: the workflow
  authenticates the action through OIDC (`id-token: write`, no `github_token` input), and
  that exchange fails on a repo where the app isn't installed. the sister app's install is on
  a different account and does not cover this repo.

- [ ] Add the Actions secret `CLAUDE_CODE_OAUTH_TOKEN`:
  - Run locally:
    ```
    claude setup-token
    ```
  - Copy the token (not the full output—just the token value).
  - Go to the repo **Settings** → **Secrets and variables** → **Actions** → **New repository secret**.
  - Name: `CLAUDE_CODE_OAUTH_TOKEN`. Paste the token.

- [ ] Commit and push the workflow file (IMPORTANT: authored as FlorpyDorpinator — cron
  runs are attributed to the workflow file's last editor, and a wrong identity can break
  the bot's auth). House rule: in PowerShell, write the commit message to a file and use
  `-F` (inline `-m` gets mis-parsed):
  ```
  git add .github/workflows/feedback-triage.yml
  Set-Content -Path msg.txt -Value "Add feedback triage workflow" -Encoding utf8
  git commit -F msg.txt
  git push
  Remove-Item msg.txt
  ```
  The workflow is inert until this push.

---

## 6. End-to-end proof

- [ ] POST one real test report through the tunnel URL. Easiest: save this as
  `test-report.json` and send it with `curl.exe` (field names match the relay contract):
  ```json
  {
    "kind": "bug",
    "title": "Test: radial closes unexpectedly",
    "description": "When I swap hands quickly, the radial menu closes.",
    "contact": "TestPlayer",
    "context": {
      "modVersion": "0.9.7.4 Experimental",
      "deployRoute": "ScriptEngine",
      "gameBuild": "27701",
      "profileName": "Stationeers Blue",
      "profileModified": false,
      "playerContext": "SP - Suited",
      "display": "1920x1080",
      "os": "Windows 11"
    }
  }
  ```
  ```
  curl.exe -X POST https://uiascended.ssui.dev/v1/report -H "Content-Type: application/json" -d @test-report.json
  ```

- [ ] Confirm in the repo:
  - An issue appears with title `[Bug] Test: radial closes unexpectedly`.
  - It has labels: `feedback`, `bug`, `triage:pending`.
  - The `feedback-triage` Actions run starts (visible in repo **Actions** tab).

- [ ] Wait ~10 minutes for the bot to finish:
  - The issue should have a new comment starting with a plan (plain words, implementation
    summary, risks, effort).
  - The label should flip from `triage:pending` to `plan:ready`.

- [ ] Close the test issue (not critical, just cleanup).

---

## 7. Point the mod at it

- [x] Nothing to configure: `https://uiascended.ssui.dev` is the mod's built-in relay, and an
  empty `[Feedback] RelayUrl` uses it. Set `RelayUrl` only to override (e.g. a Quick Tunnel
  while testing). `uiafeedback` with no arguments shows which relay is in use.

- [ ] In the game console, test the feedback command:
  ```
  uiafeedback bug Test title | Test description
  ```

- [ ] Confirm:
  - The console prints `UIA-<n>` (the issue number).
  - A receipt file appears at `BepInEx/config/StationeersUIMod/Feedback/sent/`.

---

## If something fails

The mod's outbox keeps every report on disk and retries on the next game launch and on
`uiafeedback retry`, so a down relay or Dell reboot delays reports but never loses them.

- **Relay logs:** the relay logs to its console — check however it is hosted on the Dell
  (see `FeedbackRelay/README.md`).
- **Actions logs:** Check repo **Actions** tab; run logs are kept as artifacts for 30
  days.
- **Token rotated or revoked:** Replace `UIA_FEEDBACK_GITHUB_TOKEN` on the Dell and
  restart the relay.
- **Tunnel down:** `cloudflared` auto-reconnects; if it doesn't, check `Get-Service
  cloudflared` status and restart it.
