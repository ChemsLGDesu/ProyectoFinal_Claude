# Changelog (Development)

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

> **Gap notice:** this file stops at 0.5.0 (2026-07-23) while dozens of commits have landed since.
> The 0.6.0 section below only covers work logged from 2026-08-08 onwards — it is not a backfill.

### [Unreleased]

#### Security

- **ERR-KB-006 closed in code: server-granted currency and every anti-abuse counter left the
  player-writable access class.** Cloud Save Player Data written with `SetItemAsync` is access class
  `Default`, which the player can write against the REST API with their own token, without the SDK,
  and without the module hearing about it. Online rewards - computed server-side with daily caps and
  anti-collusion checks - were landing there, which made all of it decorative: one POST skipped it.

  | Key | Was | Now | Why that class |
  |---|---|---|---|
  | `authoritativeCurrency` *(new)* | — | **Protected** | player reads their balance, never writes it |
  | `currency` | Default | **Default, unchanged** | still the client-owned local-first offline mirror |
  | `onlineCurrencyLedger` | Default | **Private** | the player has no business reading its own cap counters |
  | `rivalWin_*` | Default | **Private** | anti-farming |
  | `rankedProfile` | Default | **Protected** | shown on profile and result; only Cloud Code writes it |
  | `rankedWin_*` | Default | **Private** | anti-boosting - reading it alone tells you when to stop |

  - **The record's table of three keys was incomplete.** Grepping the module for the I/O *pattern*
    rather than trusting the list turned up two more, both per-rival per-day and both writable:
    `rivalWin_*` and `rankedWin_*`. The second is the worse one - resetting it farms undamped Elo off
    the same rival indefinitely. The defect was never in the keys, it was in the pattern, so the
    search had to be by pattern.
  - **Local 1P/2P currency stays writable, deliberately.** The server cannot verify a match it never
    saw, so locking that mirror buys nothing. What changed is that server-computed rewards no longer
    share the key.
  - Every store now carries a greppable `ACCESS CLASS` comment stating its class and why - the record's
    own prevention item, and the absence of which is how all five keys ended up `Default` without
    anyone choosing it.
  - Client: `GameManager` splits the wallet into `SoftCurrencyBalance` (offline, mirrored) and
    `AuthoritativeCurrencyBalance` (server-granted, read-only), shown added together as
    `TotalCurrencyBalance`. `ApplyOnlineRewardCredit` credits the authoritative side and, as before,
    does not stamp the local-state clock - an existing test already guarded that invariant and caught
    a first version of this change that broke it.
  - Three new tests pin the split from both directions: an online reward must never reach the
    client-writable wallet, and a local reward must never reach the authoritative one. Suite 237 → 240.

#### Changed

- **`GameProtocol.Version` 1 → 2.** The RPC shapes did not change, but which key an RPC credits is
  part of the contract from a client's point of view: a v1 client reads only `currency`, so against a
  v2 server it plays perfectly and silently stops seeing its online winnings. The `b4` beta APK
  already in a tester's hands is that v1 client.

#### Known Limitations

- **Not verified against the backend.** The module compiles and the suite is green, but the only
  thing that proves the write is now *rejected* is redeploying and re-running the probe at the end of
  `Tools\quickmatch-e2e.ps1` - the same probe that found the hole.
- **No migration.** Changing access class means the old `Default` values are not read from
  `Protected`/`Private`: existing `rankedProfile` records are lost and MMR returns to its initial
  value, and the cap counters restart once. Accepted for `development` test data, but the Ranked
  leaderboard already holds entries and those do not clear themselves - expect scores with no profile
  behind them until the next season reset.

### [0.6.0] — 2026-08-11 — first Android beta

The first build of this project that leaves the developer's desktop. Everything logged since
2026-08-08 ships in it.

#### Added

- **`BetaBuild`** — one-command Android beta APK (`TTTXO → Build → Android Beta APK`, plus a
  batch-mode entry point). It exists because every setting a tester build depends on was wrong or
  unset the first time an Android build was attempted, and none of them fail the build: the
  application identifier was still the template's `com.DefaultCompany.2D-URP`, the company name was
  `DefaultCompany`, and `bundleVersion` was `1.0` with the project on its fifth milestone. The result
  would have been an APK that installs under the wrong identity and reports the wrong version in
  every bug report.
  - **Bumps `versionCode` on every build.** Android refuses to install an APK whose code is lower
    than the installed one, and two sideloaded builds sharing a code resolve to whichever was
    installed first, silently.
  - **Forces `BuildOptions.Development`.** Not a convenience: `LocalAnalyticsBuildGuard` fails any
    release build that still defines `TTTXO_LOCAL_ANALYTICS`, and a beta wants that define. The two
    rules only compose in one direction.
  - **Reads the release keystore from four environment variables**, never from
    `ProjectSettings.asset` (which is versioned) or from a script in the repo. Falls back to Unity's
    debug keystore with a warning that says what the fallback costs — a later switch to a real key
    forces every tester to uninstall, taking their local wallet and match history with it.
  - Forces APK over AAB: an app bundle is not installable without Play or bundletool, and this build
    is handed out by link.

- **`Docs/10-Beta-Testers.md`** — how a beta is cut and handed out, what is in it, and the
  copy-and-paste message for testers. Leads with what is *not* verified, because the honest headline
  is that this APK has never run on a physical phone — the project still has zero builds on real
  hardware, and the six devices in `Docs/09-Encuadre-Dispositivos.md` are all Device Simulator.

#### Changed

- **Android app identity set for the first time**: `com.hellscythe25.tttxo`, company `Hellscythe25`,
  `bundleVersion` `0.6.0`. Applied by script rather than by hand — Unity holds Project Settings in
  memory and only writes `ProjectSettings.asset` when the project is saved, so an edit made to the
  file while the Editor is open is lost.

#### Fixed

- **The beta APK now carries `armeabi-v7a` alongside `arm64-v8a`.** The first build (`b2`) shipped
  ARM64-only against a `minSdkVersion` of 25, and those contradict each other: API 25 is Android 7.1
  (2016), an era with plenty of 32-bit-only phones. On one of those, an ARM64-only APK does not fail
  to launch — it fails to *install*, with `INSTALL_FAILED_NO_MATCHING_ABIS`, which reaches the user
  as nothing more than "App not installed". It also explains why `b2` ran under MuMu 12 and not under
  BlueStacks: MuMu translates ARM, several BlueStacks builds do not. 104.8 MB → 145.8 MB, not double,
  because IL2CPP compiles twice but the 32-bit libraries compress better.
  - **Not yet confirmed on hardware.** This is the hypothesis that survived the ones that could be
    refuted, not a verified cause. Refuted by measurement, so nobody retries them: the APK is signed
    (v2, `CN=Android Debug`), all seven `.so` are already 16 KB aligned (`LOAD align: 0x4000` via
    `llvm-readelf`), it is not a split APK, and it parses intact.
  - **`zipalign -c -P 16` is not the test for 16 KB.** It returned `OK - compressed` for every
    library, which reads as a pass and is not one — it exempts compressed entries. Only the ELF
    program headers decide it.
  - Target SDK was deliberately left alone in the same build, so the result stays attributable to one
    change.

- **Android target SDK pinned to 35**, from `Automatic`. `Automatic` resolves to the highest SDK
  platform installed on the machine doing the build — it silently produced 36 here and would produce
  something else elsewhere, changing the app's behaviour with no code change and nothing in the diff
  to show for it. A target level is a behavioural contract with the OS, so it belongs in source;
  `BetaBuild.ApplyAndroidIdentity` now imposes it on every build.
  - `b4` is the first APK carrying it: `targetSdkVersion: 35`, `compileSdkVersion: 35`, verified with
    `aapt2`. `b3` was superseded without ever reaching hardware.
  - **The manifest cannot settle the portrait question, contrary to what this entry first claimed.**
    `b3` (target 36) and `b4` (target 35) declare exactly the same thing — `screenOrientation=1` and
    `resizeableActivity=true`. The difference lives in how Android interprets `targetSdkVersion` at
    runtime, not in the file, so a static check cannot discriminate. Only rotating a 600dp-and-up
    screen on Android 16 can.
  - `b4` moves two variables against `b2`, but attribution survives: **`targetSdkVersion` does not
    gate installation.** Android blocks on target only when it is too *low* (below 23, since Android
    14), never for dropping 36 to 35.

#### Known Limitations

- **The APK is 145.8 MB** (`b3`; `b2` was 104.8 MB before the second ABI). Part of that is the
  development build. The rest is that
  `com.unity.ai.inference` (Sentis) ships its runtime shaders into the player even though no game
  script references it — it arrives as a dependency of `com.unity.ai.assistant`, the Editor tool the
  Unity MCP runs on. The split between the two causes was not measured.
- **`Android App Info` is not configured in Localization Settings.** The app name is not localized in
  the launcher and the game does not appear in Android 13+ per-app language settings. Verified not to
  affect the in-game language picker: the startup chain is `PlayerPrefLocaleSelector` →
  `SystemLocaleSelector` → default, none of which depend on that metadata.
- **Triggers still not deployed**, so `SweepAbandonedMatches` does not run and an abandoned online
  match stays open instead of resolving itself. Testers playing online will hit this.

#### Added

- **Local analytics sink for beta, behind `TTTXO_LOCAL_ANALYTICS`.** UGS bills per custom event and a
  beta does not need to pay for them. With the define set, `GameAnalytics.SafeRecord` appends each
  event to a JSON Lines file under `Application.persistentDataPath/analytics/` and does not call
  `AnalyticsService` at all.
  - A **scripting define, not a runtime flag**: "unplugged for production" has to mean the
    file-writing code is absent from the build, not that a boolean is false.
  - **`LocalAnalyticsBuildGuard` fails any release build that still has the define.** Otherwise the
    failure is invisible — the build succeeds, the game plays, and the missing telemetry only shows
    up when someone goes looking for it. Development builds are allowed to carry it.
  - The **serialization stays compiled and tested unconditionally** (`AnalyticsEvent.ToJsonLine`,
    10 tests); only the I/O is behind the define (6 more, gated). Malformed JSON is another silent
    failure: the beta keeps playing and the file keeps growing until someone tries to parse a month
    of it. Covered: escaping, control characters, non-ASCII from the 10 shipped languages, and
    invariant-culture floats — under es-AR a naive `ToString()` writes `1,5` and splits a parameter
    in two.
  - The typed event methods are untouched. The lambda receives an `AnalyticsEvent` instead of a
    `CustomEvent`, so all ~15 call sites still read `evt.Add("board_size", size)`.
  - `GameManager.DeleteAllPlayerData` deletes the directory: recorded events are player data, and
    leaving them would make the Settings screen's promise false.
  - Side effect worth having: the local path skips the "UGS session is not Ready" bail-out, so events
    are captured offline and in the Editor, where every one of them is currently dropped.
  - Suite is now 229 (75 `TTTXO.Core.Tests` + 154 `TTTXO.Game.Tests`).

#### Changed

- **9x9 and 11x11 are not offered at launch, deferred to post-launch.** Not a cancellation: their
  rules, rewards and AI params stay specified and implemented, and `TTTXO.Core.BoardConfig` still
  knows all four sizes. The cut lives in `BOARD_CONFIGS`, in the new
  `GameConfigService.ShippedBoardSizes` and in Board Select's own filter, so bringing them back is
  two config entries and no code.
  - **Board Select now filters local play by the config too.** It returned `BoardConfig.All`
    outright for 1P and 2P local, so the first version of this cut removed the boards everywhere
    except where they were actually played, and the suite stayed green throughout: the sizes were
    gone from the config and from the offline fallback, both tested, while the screen ignored both.
    Caught by looking at the rendered card list after deploying, not by a test.
  - `BoardSelectEligibilityTests` (6 cases) is the seam that was missing - the filter is now a
    static that takes the flow flags and the config, so every branch is reachable without a
    UIDocument, including the empty-config fallbacks.
  - **The reason is touch targets and it is arithmetic, not layout.** Measured on a Redmi 6 Pro: an
    11x11 cell is 26.67dp against Android's 48dp minimum. On a 360dp-wide phone eleven cells give
    32.7dp *even at 100% of the width*, so no amount of framing fixes it; the playable maximum at
    that width is 7 cells. By the same arithmetic 9x9 misses it too and 6x6 clears it barely.
  - `LocalFallback_OmitsTheBoardsDeferredToPostLaunch` fails if one half of the cut is undone without
    the other, alongside the existing mirror test that compares the offline fallback against the
    deployed `GameConfig.rc`.
  - **Requires a Remote Config deploy to take effect.** `GameConfig.rc` is config-as-code; until it
    is pushed from `Window → Deployment` the backend still serves four boards, and no test in this
    repo can see that — they compare the fallback against the *file*.
  - Suite is now 237.

- **The bottom nav button widens from 124 to 136** so the longest localised label keeps a real
  margin. Measured across all 10 locales with `MeasureTextSize`: German "Einstellungen" came to 94.8
  of 96.6 units — it fitted by 1.8, which is not a margin, and `.secondary-nav-label` carries no
  `text-overflow`, so the failure mode is a visible spill rather than an ellipsis. At 136 the room is
  108.6 and German drops to 87%. Three buttons plus margins use 444 of 672, and the row still ends at
  1120.2 of a 1152 panel.
  - Nothing else in any locale overflows; the runner-up is Turkish "Uyarlanabilir" at 86% of a
    difficulty chip. Full table in `Docs/09-Encuadre-Dispositivos.md` (L-08).
  - The character-count estimate that preceded this predicted German would overflow and was wrong -
    narrow lowercase letters weigh far less than the average. Recorded as such, because the technique
    is useful for choosing what to measure and useless for concluding.

- **The game is locked to portrait** (`defaultScreenOrientation: 0`). It was on `AutoRotation` with
  all four orientations enabled, so any player could rotate into a layout that had never been
  designed or measured — the Galaxy J7 in landscape would be a 720x405 panel against the 1152–1558
  everything is framed for. Closed by fixing the orientation rather than by supporting landscape,
  which would have meant a second pass over all 12 screens. Found by reading PlayerSettings while
  measuring the Android tablet, not by anything failing.
  - Also checked and dismissed in the same pass: the tablet declares `navigationBarHeight: 96` and
    nothing in `Screen`/`safeArea` subtracts it, but the app runs `startInFullscreen` in
    `FullScreenWindow`, so that band is not permanently occupied.

#### Security

- **`CreateMatch` now enforces board availability server-side** (`BoardAvailability`). It validated
  only against `BoardConfig.ForSize` - the table of board *rules*, which lists every size the game
  has ever specified - so the launch cut was client-side only and a modified client could still
  create an online 9x9 and be paid ordinary rewards.
  - **The Ranked path was worse and this is the real find:** it never checked the size at all.
    `mode="ranked"` with an 11x11 board went straight down the 6x6-shaped branch, creating a ranked
    match on a board with no deployed leaderboard (`ranked_3x3.lb`/`ranked_6x6.lb` are the only two).
    The gate now covers all three modes uniformly, before any of them branch.
  - **Fails to a launch floor, not open.** `MatchmakingConfigReader` and `RankedConfigReader`
    deliberately fail open to their defaults so a Remote Config hiccup cannot stop a match from
    resolving; that posture is wrong for a security control, where it would mean the gate vanishes
    exactly when the read is flaky. An unreadable BOARD_CONFIGS falls back to `{3, 6}` instead:
    closed against everything the cut removed, never blocking a board a legitimate client can ask
    for. Cost is a second place to edit when a board returns, the same trade
    `RankedQueryFunctions.RankedBoardSizes` already makes.
  - An empty parsed list is treated as unreadable rather than as "nothing is playable" - far more
    likely a broken deploy, and taking it literally would take the whole game down.
  - **Scope, stated plainly:** this closes the server path only. Local 1P/2P never calls
    `CreateMatch` - it runs entirely client-side by Milestone 1 design - so a modified client can
    still play a cut board locally, and its rewards ride the same client-writable `currency` key
    ERR-KB-006 already describes. Closing that is a different job.
  - **Requires a Cloud Code deploy** to take effect.
  - **`Tools\board-gate-e2e.ps1`** verifies it against a live environment, since none of this is
    observable from the client and there are no Cloud Code tests. Six probes, of which one carries
    the whole argument: because the gate falls back to `{3, 6}` when the config is unreadable, a
    gate that read *nothing* still rejects 9x9/11x11 and still accepts 3x3 — it passes every obvious
    probe while enforcing nothing the config says. Asking for size 3 in a mode nothing lists is what
    separates the two, since only a per-mode check against a config that was actually read can
    reject it. The positive control covers the opposite failure, a gate that rejects everything.
  - Verified against `development` after deploying: all six probes pass, including both Ranked
    rejections.

- **The UI sizes and positions itself against the screen instead of the pixel grid.** `MainPanelSettings`
  moved from `ConstantPhysicalSize` to `ScaleWithScreenSize` at 720x1400 with `match=0`, so the panel is
  always 720 units wide. Before, every USS px was a physical pixel: the Play button held 20% of the
  width on a 720-wide panel and 11.3% on an iPhone 13 Pro Max. Menus and the board now centre instead
  of anchoring to the top, and the Play button, Home title, header avatar and bottom nav all grew.
  - **`SafeAreaController` keeps every screen clear of the notch and home indicator.** Nothing read
    `Screen.safeArea` before, which is what cut the player name in half on a 13 Pro Max. The inset is
    padding per screen, not on the root, so backgrounds keep bleeding under the notch.
  - Verified by measurement on five simulated devices, iOS and Android, including the two mirror
    cases: iPad Pro (0 above, 40 below) and Redmi 6 Pro (89 above, 0 below). Nothing overflows on the
    tightest panel (720x960).
  - Full findings, and the traps that made two of these measurements lie, in
    `Docs/09-Encuadre-Dispositivos.md`.

- **Ranked seasons are now calendar months, closing on the 28th at 00:00 UTC** (previously 8 weeks /
  56 days). The owner had already set that cadence on the Dashboard for `ranked_3x3`/`ranked_6x6`;
  the repo would have overwritten it on the next `Window → Deployment`, because config-as-code wins
  over the Dashboard.
  - `RankedSeasonCalculator` now does calendar arithmetic (`DateTimeOffset.AddMonths`) instead of
    integer-dividing a fixed day count. No day count can express "the 28th of every month" — months
    are 28–31 days, so any fixed duration drifts off the leaderboard's reset within one season.
    Boundaries are always measured from the anchor, never by stacking month-adds, so `AddMonths`
    clamping (Jan 31 → Feb 28) cannot accumulate into permanent drift.
  - `RANKED_CONFIG.seasonDurationDays: 56` → `seasonDurationMonths: 1`, mirrored in the client's
    `RankedConfigDto`. `seasonStartUnixSeconds` stops being a placeholder: `1787875200`
    (2026-08-28T00:00:00Z), the real season-1 anchor.
  - Both `.lb` files: `ResetConfig.Start` 2026-08-28T00:00:00Z with `Schedule: "0 0 28 * *"`. Cron is
    valid here — Unity's own leaderboard-asset example uses a 5-field expression — and `@every 1344h`
    could not have expressed this.
  - **One design consequence left open, flagged in `design-doc.md` 6.5**: the season reward ceiling
    (3400 soft) now pays ~12×/year instead of ~6×, doubling annual season-reward income.

- **`placementMatches: 10` → `5`.** The other consequence of monthly seasons, and the reason the
  original doc rejected 4-week seasons: a casual player (≈13 Ranked matches a month) barely finished
  placement before close and had no season left to climb. Two knock-on effects, documented where they
  live rather than buried here:
  - The K=40 window halves, so post-soft-reset convergence leans more on the K=32 band (`design-doc.md`
    6.1). The number sits in `RANKED_CONFIG` precisely so `ranked_placement_completed` can retune it.
  - The season-reward eligibility gate is what closes throwaway-account bronze farming, and it now
    costs half: 10 real online matches for the 200 soft of bronze on both boards instead of 20, across
    12 windows a year instead of 6. Still under one day of the 300/day cap and it still requires
    playing real opponents, so it is accepted — the lever if it shows up in telemetry is raising the
    minimum for the bronze tier alone, not for all of placement.
  - No client build is needed to retune it: `PlacementsPlayed`/`PlacementMatchesRequired` both travel
    in the `GetRankedProfile` response and the "Colocación 3/5" label formats from them.

- **The two leaderboards deliberately carry no `TieringConfig`.** The Dashboard briefly had tier bands
  at 100/250/450/700 on a scale where the published score *is* raw MMR, `initialMmr` is 1000 and
  `mmrFloor` is 500 — so every player was diamond from their first match and bronze/silver/gold were
  unreachable, while `RankedTierCalculator` called that same player silver off `tierMinMmr`
  (`[0, 900, 1100, 1300, 1500]`). One tier table now, Cloud Code's; the client draws the badge from it.

#### Added

- **`SeasonConfigAlignmentTests`** — the season boundary lives in two independently deployed config
  surfaces (Remote Config's `RANKED_CONFIG` and the Leaderboards `ResetConfig`) that UGS never
  reconciles. `Assets/Leaderboards/README.md` called keeping them aligned mandatory, but nothing
  enforced it, and drift is silent: the board clears on one date while the MMR soft reset and the tier
  reward fire on another. The tests compare anchor, day-of-month, hour and cadence across all three
  files, reject an anchor past the 28th (where `AddMonths` clamping and cron day-of-month stop
  agreeing), and fail if a `TieringConfig` reappears. `TTTXO.Game.Tests` is now 138.

- **`TTTXO.Game.Tests` (EditMode) — 135 tests, first coverage the Unity layer has ever had.** The
  project docs listed this as the most critical debt to carry into real-money IAP. Total suite was
  210 at that point (75 in `TTTXO.Core.Tests` + 135 here) — the "76 Core" that had been circulating in
  the docs was off by one, measured against the Test Runner on 2026-08-08.
  - `GameManagerTests` — the 300/day offline soft-currency cap; that `ApplyOnlineRewardCredit`
    consumes neither that cap nor the local "who's newer" timestamp (both documented invariants with
    a security rationale); first-player alternation and its sequence key; match history order and
    50-entry cap; the delete-my-data privacy path.
  - `ScreenHistoryTests` — back-stack scenarios: Profile ↔ Store cannot loop, a sub-screen returns
    along the route actually taken, ten match-flow cycles do not accumulate, Splash stays unreachable
    by a back button.
  - `GameConfigServiceTests` — the offline fallback that keeps the game playable with no session, and
    the response shapes a live run never produces (null, incomplete, missing optional sections,
    mismatched protocol version).
  - `CosmeticCatalogTests` / `CosmeticSelectionTests` — cosmetics are a preference, not an
    entitlement; an unknown id always resolves to something paintable.
  - `GameAnalyticsCodeTests` — every enum value maps to snake_case, walking whole enums so a new
    member added without updating the mapper cannot silently emit PascalCase and be dropped by the
    ingestion pipeline.
  - `ContractMirrorTests` — `GameProtocol.Version` and the Ranked mode code checked against the Cloud
    Code module's own source, enforcing the "bump both sides together" convention for the first time.
  - `RankedProfileCacheTests` / `RankedQueryServiceTests` — offline MMR fallback per board, and the
    leaderboard argument map.
  - `PlayerPrefsSandbox` — EditMode tests share the developer's real PlayerPrefs, so it captures and
    restores every key the Game layer writes. A new key must be listed there or it is unprotected.

- `ScreenHistory` — the navigation back stack, extracted from `ScreenRouter` so it can be tested
  without a live `UIDocument`. Behaviour unchanged; the four rules and their ordering are identical.

- `com.unity.device-simulator.devices` 1.0.1 — real device profiles for the Simulator view (the view
  itself already ships with the Editor). Intended for safe-area checks on the game screen, where the
  slack below the board is the space wireframe 6 reserves for the reactions row.

#### Verified

- **The EditMode tests ran inside the Editor for the first time in the project's life.** They had
  only ever run in a standalone project, so nothing guaranteed they passed in the real environment.
  They do — and there are **76, not the 75** the docs had been carrying.
- **MCP routing (ERR-KB-003) verified at the config level.** After a full restart of the Claude Code
  app, one `relay_win.exe --mcp` is alive instead of three and carries `--project-path`, and the
  Editor answers with its own `dataPath`. Explicitly *not* proven: only one Editor was open, and with
  one Editor the routing is deterministic by construction, so the contention case — which is the bug —
  was never exercised.
- **The Ranked season timestamps agree across all three deploy targets**: `seasonStartUnixSeconds`
  (1785542400) matches `ResetConfig.Start` in both `.lb` files (2026-08-01T00:00:00Z), and
  `seasonDurationDays: 56` matches `@every 1344h`. `UpdateType` is `keepLatest` on both boards. The
  remaining problem is not consistency but the value: it is still the placeholder, now in the past.
- **The offline config fallback mirrors the deployed `BOARD_CONFIGS`** in
  `Assets/RemoteConfig/GameConfig.rc` — a claim the fallback builder's comment made and nothing
  checked. Drift there would offer an offline player a different set of modes than an online one.

- Board anchoring (`433af2a`) confirmed in play mode at 720×1400 (Android): board 640×640, cells
  205×205, top edge 40px below the turn bar, 568px of slack underneath — every figure matching the
  arithmetic the commit was written with. The padding subtraction the same commit added was only
  exercised at 1280×400, where height is the limiting axis: with it the board fits at 208×208 flush
  with the container, without it it would have measured 232 and overflowed the screen by 8px.

#### Changed

- `GameConfigService.InitializeAsync` split into fetching and applying; `ApplyServerResponse` and
  `ApplyLocalFallback` are now `internal` so the response shapes can be exercised without a live
  session. No mutable static, no delegate, no fake network client.
- `RankedQueryService.BuildLeaderboardArgs` extracted — it holds the wrapper's only decision, that
  `limit` is omitted rather than sent as zero or null so the server applies its own default.
- `Assets/Scripts/Game/AssemblyInfo.cs` grants `InternalsVisibleTo("TTTXO.Game.Tests")`, keeping those
  seams out of the public surface where they would invite the wrong call sites.
- `ProjectSettings.asset` picked up `scriptingDefineSymbols: Android`, mirroring the Standalone entry.
  Side effect of switching the active build target to Android; no setting was edited by hand.

#### Fixed (documentation)

- **ERR-KB-004 claimed, as verified, that `Application.runInBackground` does not persist to
  `ProjectSettings.asset`. It does.** The two are the same variable — `Application.runInBackground`
  *is* `PlayerSettings.runInBackground` — so setting it at runtime edits ProjectSettings directly and
  only the write to disk is deferred. The original check read the file immediately and was therefore
  premature, not wrong. Commit `637007b` repeats the false claim in its own body.
- **The `ugs` skill recovery note in `CLAUDE.md` was wrong in the two ways that only surface while
  following it**: the link is `.claude/skills/ugs` (not `.claude/skills/`), and it is a Windows
  junction (not a symlink, which needs elevation). Recreating it as written would have left
  `SKILL.md` where Claude Code does not look, reproducing the exact symptom being fixed.

### [0.5.0] — 2026-07-23

#### Added

**Ranked Mode (Milestone 5)**

**Cloud Code Ranked Infrastructure**

- 10 new Ranked support files in `CloudCode~/TicTacToeModule/`:
  - `RankedConfigReader.cs` — Loads RANKED_CONFIG from Remote Config (tiers, K factors, season dates)
  - `RankedEloCalculator.cs` — Standard Elo computation with variable K-factor based on placement phase and MMR
  - `RankedSeasonCalculator.cs` — Season tracking and soft-reset logic (post-season MMR = 1000 + (prev_MMR - 1000) × 0.5, floor 500)
  - `RankedTierCalculator.cs` — Tier derivation from MMR (tiers: bronze [0–899], silver [900–1099], gold [1100–1299], platinum [1300–1499], diamond [1500+])
  - `RankedRewardCalculator.cs` — Per-mode reward table (3×3 series: 10 W / 4 D / 0 L; 6×6 match: 15 W / 5 D / 0 L)
  - `RankedProfileStore.cs` — Cloud Save item (`rankedProfile`) persistence for per-board MMR, tier, placement count, pending seasonal rewards
  - `RankedRewardStore.cs` — Anti-farming ledger (shared with Quickmatch: 200 online/day, 300 global/day, 3 paid wins per rival/24h)
  - `RankedLeaderboardStore.cs` — Leaderboard SDK wrapper (read top N, query self entry, decay management −25 MMR/day above 1200 with 7-day grace)
  - `RankedSeriesStore.cs` — 3×3 series tracking (RankedSeriesRecord wraps up to 2 MatchIds; each match is ordinary MatchStateRecord)
  - `RankedQueryFunctions.cs` — Two new Cloud Code endpoints:
    - `GetRankedProfile()` — Returns authoritative MMR by board (with lazy decay + rollover), tier, placement state, season context, applies pending reward if season closed
    - `GetRankedLeaderboard()` — Returns top N (leaderboard rank by MMR, tied by latest submission timestamp), self entry highlighted

- 3×3 Series Model (Fairness Solution)
  - k-in-a-line on 3×3 is a solved game; single-match Elo is insufficient fairness measure
  - Solution: score the **series** of 2 games (player A as X in game 1, player B as X in game 2) as a single Elo transaction
  - Each game follows normal CreateMatch/PlayMove pipeline (reuses existing validation, Wire, abandonment logic)
  - Series completion: after game 2, single MMR update applied to both players (not per-game)
  - Series state stored in RankedSeriesRecord; client detects end-of-game-1 and automatically re-invokes CreateMatch for game 2 with empty MatchId

- MMR Separation by Board
  - Separate MMR pools: `rankedProfile.mmr3x3`, `rankedProfile.mmr6x6` (future: mmr9x9, mmr11x11)
  - Initial MMR: 1000; floor: 500
  - K-factor (variable, per placement phase and MMR band):
    - Placement games 0–9: K=40
    - Games 10–29: K=32
    - Games ≥30, MMR < 1600: K=24
    - Games ≥30, MMR ≥ 1600: K=16

- Anti-Boosting Safeguard (MMR Dilution by Repeated Rival)
  - Tracks rival victories per board in 24-hour UTC window (not 30 days)
  - MMR gain multiplier by encounter count (only on victories; losses/draws always full K):
    - 1st–2nd victory vs same rival: ×1.00
    - 3rd–4th victory: ×0.50
    - 5th+ victory: ×0.00
  - Losses and draws always apply full K (no dilution)
  - Rationale: detects serial farming without penalizing casual rematches

- Abandonment Handling (Ranked-Specific)
  - Abandoned match: full K loss for abandoner, full K win for stayer
  - Both players abandon: treat as no-contest (ΔMMR = 0, ΔREWARD = 0); marked in MatchStateRecord.BothPlayersAbandoned
  - Timer expiration (> `turnTimeoutSeconds`) counts as abandonment

- Seasonal Structure
  - Duration: 8 weeks (56 days); automated rollover on RANKED_CONFIG.seasonStartUnixSeconds + N×56 days
  - Soft reset: post-season MMR = 1000 + (preseason_MMR − 1000) × 0.5, floor 500; tier recalculated; placement counter reset to 0
  - Tier-based seasonal rewards (on season close, applied lazily on GetRankedProfile):
    - Bronze (0–899): 100 SC
    - Silver (900–1099): 200 SC
    - Gold (1100–1299): 400 SC
    - Platinum (1300–1499): 700 SC
    - Diamond (1500+): 1200 SC + animated frame cosmetic
    - Top 100 global (additional): +500 SC + exclusive banner cosmetic
  - Rewards exempt from daily caps (separate ledger)

- Leaderboard (Per-Board, Per-Season)
  - Displays raw MMR, sorted descending by MMR (ties broken by earliest submission timestamp)
  - Visible only after placement complete (10 games minimum per board per season)
  - Decay only applies to MMR > 1200:
    - Grace: 7 consecutive days without a completed Ranked match
    - Rate: −25 MMR per day beyond grace period
    - Decay floor: 1200 (does not decay below 1200; also bounded by hard floor 500)
  - Leaderboard reset (new season): archival of prior leaderboard in Dashboard; new entries start fresh
  - Decay applied lazily on profile read/write; emitted as `ranked_mmr_changed` with `reason = inactivity_decay`

**Matchmaker Ranked Queue**

- `Assets/Matchmaker/RankedQueue.mmq` — New queue definition
  - Skill-based matching enabled via Rule "Difference" on MMR attribute
  - Relaxation curve (literal rule replacement per wait duration):
    - 0–9s: ±100 MMR
    - 10–19s: ±200 MMR
    - 20–29s: ±350 MMR
    - 30–44s: ±600 MMR
    - 45s+: no restriction (only SameBoardSize required)
  - Ticket TTL: 180s; wait ceiling: 45s (decision point shown at 45s: keep waiting, switch to Quickmatch, or play vs AI)
  - Backfill by board_size attribute (3x3 or 6x6 only in v0.5.0; 9x9/11x11 reserved)

**Remote Config & Config-as-Code**

- New key: `RANKED_CONFIG` (JSON format)
  - Actual structure (per Assets/RemoteConfig/GameConfig.rc):
    - `initialMmr`: 1000
    - `mmrFloor`: 500
    - `kPlacement`: 40
    - `kIntermediate`: 32
    - `kEstablished`: 24
    - `kHighMmr`: 16
    - `kHighMmrThreshold`: 1600
    - `placementMatches`: 10
    - `firstMoveEloBoard6`: 50
    - `repeatRivalDamping`: [1.0, 1.0, 0.5, 0.5, 0.0]
    - `turnTimeoutSecondsBoard3`: 20
    - `turnTimeoutSecondsBoard6`: 30
    - `rewardWinBoard3`: 10
    - `rewardDrawBoard3`: 4
    - `rewardWinBoard6`: 15
    - `rewardDrawBoard6`: 5
    - `decayProtectedFloor`: 1200
    - `decayGraceDays`: 7
    - `decayPerDay`: 25
    - `seasonStartUnixSeconds`: 1785542400 (2026-08-01T00:00:00Z UTC; placeholder — must be updated to production launch time before deployment)
    - `seasonDurationDays`: 56
    - `tierNames`: ["bronze", "silver", "gold", "platinum", "diamond"]
    - `tierMinMmr`: [0, 900, 1100, 1300, 1500]
    - `tierSoftCurrency`: [100, 200, 400, 700, 1200]
    - `top100BonusSoftCurrency`: 500

- BOARD_CONFIGS updated: "ranked" mode added for 3x3 and 6x6 (9x9/11x11 reserved)

**Leaderboard Config-as-Code**

- `Assets/Leaderboards/ranked_3x3.lb` — Leaderboard definition
  - Version type: Latest Score (new season resets, preserves archive)
  - Reset scheduled per season boundary (synced with RANKED_CONFIG.seasonStartUnixSeconds)

- `Assets/Leaderboards/ranked_6x6.lb` — Leaderboard definition (same structure)

- **Critical User Action:** Verify UpdateType = Latest score (not Best score) in both .lb files after deployment

**Client-Side Ranked Integration**

- `RankedQueryService` (`Assets/Scripts/Game/Services/RankedQueryService.cs`)
  - Invokes `GetRankedProfile()` and `GetRankedLeaderboard()` endpoints
  - Caches profile locally (fallback for offline display, clearly marked as stale)

- **Ranked Matchmaking Flow**
  - Mode selection: ModeSelect now lists "Ranked" (no longer "Soon")
  - Board selection: BoardSelect filtered to 3x3 and 6x6 only
  - Queue: Uses `RankedQueue` (skill-based matching by MMR)
  - Series handling (3x3):
    - Game 1 complete → MatchId empty in series record
    - Client checks result; if series continues, automatically invokes CreateMatch again for game 2
    - Game 2 result → series marked complete, single Elo update applied server-side
  - Result screen: Displays MMR change card (±N, colored) but does NOT show opponent MMR (see Known Limitations)

- **Result Screen Updates**
  - New card: MMR change (±N, colored red/green) per ResultScreenController.cs
  - Reward display: separate from Quickmatch (values lower by design: Ranked 10/15 vs Quickmatch 12/25 to preserve Quickmatch engagement)

- **Profile Screen Ranked Tab**
  - Current board: MMR, tier badge, rank (if placement complete; otherwise "Placement: X/10")
  - Board switcher (3x3 ↔ 6x6)
  - Season countdown (closes in X days)
  - Pending seasonal reward badge (if tier closed and reward pending)

- **New Leaderboard Screen (Wireframe 10)**
  - Top N ranked players (board selector: 3x3 / 6x6)
  - Self entry highlighted (rank, MMR, last submission time, decay indicator if applicable)
  - Scroll through top 100+

- **New Localization Keys**
  - 20+ keys (ranked/mode labels, tier names, season info, MMR labels, etc.) registered in UiStrings StringTable for all 10 languages

**Analytics Events**

- 5 new events (must be registered in Event Manager before emission):
  - `ranked_mmr_changed` — Attributes: board_size, mmr_before, mmr_after, delta (signed), k_factor, result (win/draw/loss/no_contest), reason (match/season_reset/inactivity_decay)
  - `ranked_placement_completed` — Attributes: board_size, final_mmr, wins, losses, draws
  - `ranked_match_abandoned` — Attributes: board_size, role (abandoner/stayer/both), turn_index, game_index (1 or 2)
  - `ranked_queue_fallback_shown` — Attributes: board_size, waited_seconds, choice (keep_waiting/switch_quickmatch/play_ai)
  - `ranked_season_reward_granted` — Attributes: season, board_size, tier, soft_currency, top100 (bool)

- 3 existing event extensions:
  - `match_finished` (online matches): new attribute mode="ranked_3x3" or "ranked_6x6" (fixed from hardcoded "online_quickmatch")
  - `matchmaking_wait_time`: new attributes mode, mmr_gap (0 if opponent MMR unknown — see Known Limitations)

#### Changed

**Match State Management**

- `CreateMatch()` / `PlayMove()` / `GetMatchState()` — Shared pipeline reused for ranked mode
  - mode="ranked_3x3" or mode="ranked_6x6" branches handled transparently
  - No new match functions; mode parameter routes to Ranked-specific reward/MMR logic
  - Timer enforcement (server-side turn timeout):
    - 20 seconds per turn (3x3 Ranked)
    - 30 seconds per turn (6x6 Ranked)
    - Exceeded → auto-concede (treated as abandonment for MMR)

**Architecture Correction**

- **MMR Storage Location:** Design-doc initially stated MMR lives in `profile` item; actually implemented in separate `rankedProfile` item
  - Rationale: client syncs `profile` after 1P/local matches; would overwrite MMR if stored there
  - `rankedProfile` isolation: only updated by Cloud Code Ranked functions, never by client
  - Backward compatible: profile item unchanged; new item is additive only

#### Fixed

- **Analytics mode bug (pre-Ranked):** match_started and match_finished were hardcoding mode="online_quickmatch" for all online matches
  - Now correctly emits actual mode (e.g., "ranked_3x3", "ranked_6x6", "online_quickmatch")
  - Affects only event telemetry; no client/server behavior change

#### Known Limitations

- **Placement completion event:** ranked_placement_completed not yet emitted (server does not expose 10th-game marker to client; deferred M5.1+)
- **Seasonal reward delivery:** ranked_season_reward_granted not emitted client-side (reward applied lazily server-side on profile refresh; deferred M5.1+)
- **Opponent MMR visibility:** mmr_gap in matchmaking_wait_time always reports 0 (client does not receive opponent MMR in ticket metadata; deferred M5.1+). Result screen does NOT display opponent MMR.
- **Abandonment attribution:** ranked_match_abandoned only emits for role stayer/both; abandoner role not emitted (acceptable for player privacy)
- **Series indicator UI:** No client-side UI indicator "Game 1 of 2" implemented; series state managed server-side only
- **Client test coverage:** TTTXO.Game assembly has no unit tests (only TTTXO.Core.Tests); Ranked UI integration tested manually
- **Trigger deployment:** Assets/Triggers/* (SweepAbandonedMatches) requires `ugs deploy` via CLI; not visible in `Window → Deployment` (v1.7.2 limitation)

#### Design Decisions

- **Series over single-game (3x3):** Elo rating for 3x3 requires 2-game series with swapped first-player roles to enforce fairness. Complexity absorbed in RankedSeriesRecord; each game remains a normal MatchStateRecord for anti-cheat validation.
- **Anti-boosting via repeated-rival dilution:** Distinguishes farming (same rival 5x) from casual rematches (2nd match gets full K). Simpler than per-rival rating floors. Tracks over 24h UTC window per (playerId, rivalId, boardSize).
- **Leaderboard decay with grace period:** Decay (−25/day above 1200) protects against top-player hoarding without permanently removing inactive players. Grace of 7 days accommodates expected absences; decay rate and floor are tunable via Remote Config.
- **Seasonal soft reset (50% regression):** Smooth transition between seasons; players near 1000 stay flat, high-rating players regress 50% toward base, floor 500 prevents collapse. Convergence after soft reset is rapid (10–15 games) due to K=40 placement phase.

#### User Action Required (Critical)

1. Install package `com.unity.services.leaderboards` 2.3.4 (required for .lb discovery in Deployment window)
2. Deploy `.lb` files: `Assets/Leaderboards/ranked_3x3.lb` and `ranked_6x6.lb`
   - Use `Window → Deployment` after package installed
3. Enable Leaderboards in Dashboard (if not already active)
4. Verify in Dashboard: ranked_3x3 and ranked_6x6 have UpdateType = Latest score (not Best score)
5. Register 5 new Analytics events in Event Manager:
   - ranked_mmr_changed, ranked_placement_completed, ranked_match_abandoned, ranked_queue_fallback_shown, ranked_season_reward_granted
6. **Critical:** Update RANKED_CONFIG.seasonStartUnixSeconds to real UTC timestamp of first season start (placeholder 2026-08-01T00:00:00Z = 1785542400; update before production deployment)
7. Synchronize ResetConfig.Start in both ranked_3x3.lb and ranked_6x6.lb with RANKED_CONFIG.seasonStartUnixSeconds value
8. Deploy `TicTacToeModule.ccmr` to Cloud Code

#### Documentation Updated

- `Docs/design-doc.md` — Section 6 (Ranked) new; Section 4 (Economy) extended to v3 with Ranked details
- `Docs/06-Wireframes-UI.md` — Wireframes 7 (Result with MMR delta), 8 (Profile Ranked tab), 10 (Leaderboard) populated
- `Docs/04-Store-Catalog-TicTacToe.md` — SKU `frame_profile_gold_season1` renamed to `frame_profile_gold_ornate` (seasonal reward frames are not vendible)
- Localization keys: 20+ new keys in UiStrings (all 10 languages)

#### Testing & QA

- ✅ Module compiles (0 errors), 8 CloudCodeFunctions total
- ✅ All 75 TTTXO.Core tests remain passing (no Core modifications)
- Series 3x3: game 1 completes → client re-invokes CreateMatch → game 2 completes → MMR update applied once
- Abandonment scenarios: solo abandon (full K loss/win), both abandon (no-contest, ΔMMR=0), timer expire (abandonment)
- Anti-boosting: repeated rival 5x → gain 1.0 / 1.0 / 0.5 / 0.5 / 0.0 multipliers within 24h window
- Placement: 10 games on new board → ranked_placement_completed eligible (deferred emission)
- Seasonal reward: tier closed, pending reward → GetRankedProfile applies lazily on next profile access
- Leaderboard decay: above 1200 MMR, no activity 7+ days → −25/day applied on next query (capped at 1200 floor)
- Matchmaking relaxation: wait 0–9s (±100), 10–19s (±200), 20–29s (±350), 30–44s (±600), 45s+ (open)
- Soft reset: 2000 → 1500, 1600 → 1300, 1000 → 1000, 600 → 800 per formula

---

### [0.4.2] — 2026-07-23

#### Added

**Proactive Abandoned Match Resolution**

- `MatchIndexStore.cs` (new) — Online active match index in Cloud Save Custom Data
  - Partitioned by UTC date (`active-matches-{yyyy-MM-dd}`) to prevent unbounded growth
  - Enrollment: match added when 2-player roster completes
  - Removal: on all termination paths (PlayMove terminal, reactive abandonment, proactive sweep)

- `SweepAbandonedMatches()` (new Cloud Code function)
  - Scans up to 3 daily partitions; re-validates each match against live state (does not trust index alone)
  - Idempotent and tolerant of parallel resolution (e.g., by reactive fallback or manual play)
  - Hard caps: 200 matches per execution, 500 lookback, 3-day window
  - Trigger config-as-code: `Assets/Triggers/SweepAbandonedMatches.tr` and `SweepAbandonedMatchesSchedule.sched` (cron: every 5 minutes)

- `MatchStateRecord.BothPlayersAbandoned` (new optional field)
  - Set `true` when proactive sweep detects both players inactive beyond `MATCHMAKING_CONFIG.abandonTimeoutMinutes`
  - Match resolves as **draw (no winner)** — neither player survives to claim victory
  - Reward: 0 for both (consistent with all abandonment scenarios)
  - Preserves MMR fairness: no player loses/gains rating for mutual abandonment

**Trigger Deployment Note**

- Package `com.unity.services.deployment` 1.7.2 does not expose Triggers/Scheduler in `Window → Deployment` UI
- Deploy via UGS CLI: `ugs deploy` (documented in module README)
- Triggers are invocable manually via Cloud Code console in the interim

#### Changed

**Matchmaker Ticket Verification (Bug Fix → Blocker)**

- Root cause identified: parameter order bug, not SDK limitation or permission issue
  - Real signature (reflected from `Com.Unity.Services.CloudCode.Apis` 0.0.26):
    `GetTicketStatusAsync(IExecutionContext, string accessToken, string id, string impersonatedUserId = null, CancellationToken = default)`
  - Previous implementation passed `context.ProjectId` (not a token) to `accessToken` position
  - And `context.EnvironmentId` (not ticket ID) to `id` position
  - Result: never looked up actual ticket; always 401 Unauthorized

- `VerifyTicketResolvesToHandoffAsync` (renamed from `TryVerifyTicketResolvesToHandoffAsync`)
  - Now uses correct parameters: `context.ServiceToken` and `ticketId`
  - Throws on any verification failure (blocks CreateMatch if ticket does not resolve to claimed handoffId)
  - No service account or new Dashboard roles required (Matchmaker under "UGS Client APIs", supported by Service Token)

- `CreateMatch()` — Now rejects match creation if ticket cannot be verified
  - Previously: logged warning, proceeded anyway (best-effort degradation)
  - Now: strict verification aligns with Ranked architecture (planned M5)

**Match State Storage**

- `MatchStateStore.cs` — Updated to sync `BothPlayersAbandoned` flag (read/write)
- `Dtos.cs` — `MatchStateDto` extended with new field (backward compatible, optional)

#### Fixed

- **Matchmaker ticket 401 bug:** Corrected parameter positions in `GetTicketStatusAsync()` call. Ticket verification now succeeds on first attempt (subject to environment validation).
- **Unbounded match index growth:** Match index now partitioned by UTC date; old partitions can be pruned without affecting active matches.

#### Known Limitations

- **Ticket verification not validated in live environment:** Implemented per assembly inspection; ready for development environment testing before production promotion.
- **Triggers require UGS CLI deployment:** `Window → Deployment` UI does not support Triggers/Scheduler in 1.7.2. Workaround documented in module README.
- **Proactive sweep query strategy:** Evaluated Cloud Save Query/Index API but deferred (requires index provisioning verification outside deployment flow). Current linear-scan implementation is safe and idempotent; can be optimized post-Ranked (M5.1+).

#### Design Decisions

- **Both players abandoned → draw, not victory:** When both players abandon, the match resolves as an unpaid draw (reward=0 for both). Rationale: neither player "survives" to claim victory; the alternative (random winner) would bias MMR and reward abandonment. Consistent with existing rule: "Victory by opponent abandonment always = 0 reward."
- **Index partitioning by UTC date:** Avoids unbounded Cloud Save Custom Data growth; any daily partition older than sweep lookback (3 days) can be independently pruned without affecting current matches.

#### Architecture Alignment

- Closes two pre-Ranked (Milestone 5) technical debt items:
  - ✅ Hard ticket verification (no longer best-effort)
  - ✅ Proactive abandoned match resolution (complements reactive fallback)

#### User Action Required

- Re-deploy `TicTacToeModule.ccmr` to Cloud Code (module modified)
- Validate ticket verification in development environment before production (now strict)
- Deploy `Assets/Triggers/*.tr` and `*.sched` via UGS CLI (optional until ready for automatic execution)

#### Testing & QA

- ✅ Module compiles (0 errors)
- ✅ All 75 TTTXO.Core tests remain passing (no Core modifications)
- Sweep idempotence: same sweep run twice produces identical results
- Both-abandon edge case: both players timeout → match resolves as draw (reward=0, no MMR change)
- Partition lifecycle: matches in partition N move to partition N+1 as UTC date advances; old partitions safe for deletion

---

### [0.4.1] — 2026-07-23

#### Added

**Server-Side Reward Infrastructure**

- `CloudCode~/TicTacToeModule/OnlineRewardCalculator.cs` — Server-side reward calculation
  - Derives from `TTTXO.Core.RewardCalculator` (no table duplication)
  - Applies online multiplier (1.25× victory, 1.0× draw)
  - Enforces abandonment exclusion (0 reward for any win via opponent forfeit)

- `CloudCode~/TicTacToeModule/OnlineRewardStore.cs` — Cloud Save ledger management
  - Tracks daily earned amount (UTC server date)
  - Tracks per-rival victory count (24h window)
  - Enforces caps before issuance:
    - Sub-cap: 200 online soft currency per UTC day
    - Global cap: 300 soft currency per UTC day (online + offline combined, offline audit-only)
    - Per-rival cap: Max 3 paid wins per rival in 24h (4th+ = 0)
    - Abandonment: Victory by opponent abandonment always = 0

#### Changed

**Balance Correction (Design-Doc v2)**

- Online victory rewards now 1.25× base (up from provisional 1.0×)
  - 3×3: 12 (previously 10)
  - 6×6: 25 (previously 20)
  - 9×9: 37 (previously 30)
  - 11×11: 50 (previously 40)
- Draw now: base × 1.0 (no multiplier)
- Loss: 0 (removed provisional 1P floor: 2/4/6/8)

**Cloud Code Functions**

- `MatchFunctions.PlayMove()` — Now calls `OnlineRewardCalculator.CalculateReward()` at terminal move
  - Broadcasts `AwardedSoftCurrency` via Wire to opponent
  - Stores award in `MatchStateStore` (audit trail)

- `MatchFunctions.GetMatchState()` — Abandonment resolution
  - Detects inactivity via `MATCHMAKING_CONFIG.abandonTimeoutMinutes` (Remote Config)
  - Emits 0 reward for both victor and loser on abandonment
  - Resolves match state atomically

**Data Transfer Objects**

- `MatchStateDto.AwardedSoftCurrency` — New optional field (nullable)
  - Transmitted 1:1 between server and client
  - Additive (per-turn award if applicable)
  - No protocol major-version bump (optional field, backward compatible)

**Client-Side**

- `GameScreenController` — Removed provisional reward block (comment: "pending game-designer ratification")
  - Removed client-side `RewardCalculator.SoftCurrencyFor(Medium)` call
  - Now displays `AwardedSoftCurrency` from `MatchStateDto` only (server-sourced)

**Analytics**

- `soft_currency_earned` event — Now includes `source="online_quickmatch"` attribute for online matches
  - Event already existed; no new event registration required
  - Ledger audit available in Cloud Save

#### Known Limitations

**Offline Rewards Not Audited (Future Consolidation)**

- Global 300/day cap currently audits **online rewards only** (server-side)
- Offline rewards (local 1P vs AI, local 2P) remain client-stored, not subject to server audit
- **Reason:** Offline rewards never leave the device; unifying requires migrating offline issuance to Cloud Code
- **Future Scope (M4.2+):** Consolidate offline + online under server audit for unified player cap

#### Testing & QA

- ✅ All 75 TTTXO.Core tests remain passing (no changes to Core)
- Victory rewards: 3×3 → 12, 6×6 → 25, 9×9 → 37, 11×11 → 50
- Draw rewards: base × 1.0
- Loss rewards: 0
- Abandonment rewards: 0 (both victor and loser)
- Daily online sub-cap: 200 enforced at issuance
- Global cap: 300 enforced at issuance
- Per-rival cap: 3 in 24h (4th+ = 0)
- Ledger audit: Cloud Save contains daily + rival records
- Analytics: soft_currency_earned emits with source="online_quickmatch"

#### User Action Required

- Re-deploy `TicTacToeModule.ccmr` to Cloud Code (already performed)
- No new Remote Config keys (caps are constants)

---

### [0.4.0] — 2026-07-23

#### Added

**Multiplayer Services (Milestone 4 - Online Quickmatch)**

- `com.unity.services.multiplayer` 2.2.3 (transitively bundles Wire SDK 1.4.4)
  - Replaces deprecated `com.unity.services.matchmaker` (unified package in Unity 6)

**Matchmaker Configuration (Config-as-Code)**

- `Assets/Matchmaker/QuickmatchQueue.mmq` — Queue definition
- `Assets/Matchmaker/MatchmakerEnvironmentConfig.mme` — Environment config
  - Pool: quickmatch (no ranking, skill-based matching disabled)
  - Backfill: match by `board_size` attribute; hosting as MatchId (serverless, no dedicated server)
  - Ticket TTL: 180 seconds

**Cloud Code Enhancements**

- `CloudCode~/TicTacToeModule/CreateMatch()` — Idempotent match creation with two-step handoff
  - Receives Matchmaker ticket; cross-validates ticket (best-effort, no blocking 401)
  - Resolves player roster via ticket metadata (Matchmaker SDK limitation: roster not exposed; workaround via ticket fields)
  - Allocates MatchId; initializes empty board
  - Player assignment: X/O determined by lexicographic sort of playerId (deterministic across client retries)
  - Method `TryVerifyTicketResolvesToHandoffAsync` — Logs warning on 401, does not throw (service-account limitations documented)

- `PlayMove()` — Online identity validation
  - Validates requesting playerId matches turn owner (use `InvalidReason: "Not this player's turn"` to reject)
  - Publishes move event via Wire (`IPushClient`) to opponent (real-time notification)

- `GetMatchState()` — Reactive abandonment detection
  - Checks inactivity duration against Remote Config key `MATCHMAKING_CONFIG.abandonTimeoutMinutes` (default: 3 min)
  - Marks match as abandoned if player absent beyond threshold; opponent can forfeit-win

**Client-Side Matchmaking & Networking**

- `MatchmakingService` (`Assets/Scripts/Game/Services/MatchmakingService.cs`)
  - Ticket lifecycle: `CreateTicket()` → `PollTicketStatus()` (async) → `CancelTicket()` (on app pause via `OnApplicationPause`)
  - Graceful polling loop with configurable backoff (Remote Config `MATCHMAKING_CONFIG.ticketTtlSeconds`)

- `OnlineMatchService` (`Assets/Scripts/Game/Services/OnlineMatchService.cs`)
  - Wire subscription (`IPushClient`, async init)
  - States: `Connecting` → `Subscribed` (active) / `Offline` (network lost)
  - Re-fetches `GetMatchState()` on receipt of Wire push (real-time sync)
  - Fallback polling every 10 seconds (network resilience)

- **Matchmaking Screen** (`MatchmakingScreenController`)
  - Spinner + elapsed time display
  - Cancel button (calls `CancelTicket()` on cleanup)
  - Fallback link: "Play vs AI" if wait exceeds `MATCHMAKING_CONFIG.waitCeilingSeconds` (default: 45s)
  - Fallback was **not** in wireframe 5 (documented deviation)

- **Game Screen Online**
  - New Wire status chip (displays connection state)
  - Polling fallback every 10s if Wire push fails
  - Move validation on server prevents client-side prediction errors

- **Mode Select Submenu**
  - Quickmatch (enabled)
  - Ranked (UI placeholder, "Soon")

**Analytics Events (Event Manager)**

- `matchmaking_wait_time` — Player matched after N seconds
  - Attributes: `+seconds` (duration), `board_size`, `matched` (bool)
  - Tracks both successful matches and cancellations

- `matchmaking_cancelled` — Player cancelled matchmaking
  - Attributes: `+reason` (timeout, user_click, app_pause, etc.)

**Remote Config Expansion**

- `BOARD_CONFIGS` — Now includes `"online_quickmatch"` entry per board size (3×3, 6×6, 9×9, 11×11)
  - Defensive: if no online config is specified, falls back to all sizes available

- New key: `MATCHMAKING_CONFIG`
  ```yaml
  abandonTimeoutMinutes: 3
  waitCeilingSeconds: 45
  ticketTtlSeconds: 180
  ```

**Rewards (Online)**

- Base multiplier: **1.0×** (provisional, pending game-designer ratification)
  - Same as local vs AI
  - May adjust based on engagement metrics

#### Changed

- `ScreenRouter` navigation model — Replaced single "previous screen" with back stack
  - `Show()` — Pushes screen onto stack
  - `GoBack()` — Pops stack and navigates
  - `Home` — Clears stack (prevents bounce-back loops)

- Cloud Code registration — Replaced obsolete `GameApiClient.Create()` with `config.AddGameApiClient()`

#### Fixed

- **Navigation loop (Profile ↔ Store):** `ScreenRouter` tracked only a single previous screen,
  causing Profile → Store → back → back to bounce forever. Replaced with proper back stack.
  (`Assets/Scripts/Game/UI/ScreenRouter.cs`)

- **Cloud Code build failure:** Missing `Microsoft.Extensions.Logging.Abstractions` NuGet reference
  (`ILogger<T>` compilation error). (`CloudCode~/TicTacToeModule/`)

- **Cloud Code deployment failure:** Missing folder publish profile (`Properties/PublishProfiles/FolderProfile.pubxml`).
  Without it, `Window → Deployment` fails with "Could not find a Publish Profile".

- **Empty board grid in online matches:** `BoardSelect` now defensively falls back to all available sizes
  if Remote Config does not specify a size for online mode. Also fixed font-rendering issue in `GameConfig.rc`.

- **CreateMatch 401 Unauthorized:** Matchmaker ticket verification (admin endpoint) now degrades to warning
  instead of blocking. Full enforcement requires service account with Matchmaker Admin role (documented).
  Method renamed `TryVerifyTicketResolvesToHandoffAsync` to clarify best-effort behavior.

#### Known Limitations

- **Ranked mode:** Placeholder only; "Soon" label in UI, no matchmaking logic yet (M5+)
- **Dedicated servers:** Not implemented; all matches use serverless hosting via MatchId
- **Turnbased fairness:** Turn order assigned lexicographically (no random swap); first player (X) advantages TBD
- **Reconnection:** If player drops mid-match, no auto-reconnect UI; manual re-entry via ticket recovery (M4.1+)
- **Cross-region:** No latency-aware region selection; Wire defaults to closest regional endpoint

---

### [0.3.0] — 2026-07-23

#### Added

**Localization System**
- `com.unity.localization` 1.5.12
- `Assets/Localization/` — Locale assets and StringTable infrastructure
  - 10 supported languages: English, Spanish, French, German, Portuguese, Italian, Indonesian, Vietnamese, Turkish, Polish
  - `UiStrings` StringTable with 85 localized keys (screen titles, difficulty/size labels, actions, notifications)
- Idempotent editor tool: `TTTXO/Setup/Create Localization Assets` (safe to run repeatedly)
- `UiText` component updated: resolves strings via `LocalizationSettings` with hard fallback to English (never breaks UI)
- Language selector in Settings screen (native language names, no-op prior versions but UI ready)
- Persistent language preference (`TTTXO_Language` PlayerPrefs)
- Hot-reload: language changes apply without restart

#### Changed

- `UiText` resolution strategy: now uses Unity Localization API (backward compatible, fallback to hardcoded English)
- Settings screen: language selector now wired (prior versions had placeholder)

#### Known Gaps

- Font coverage for Turkish, Vietnamese, Polish special characters flagged as **QA TODO** (glyphs present, rendering verification pending)
- Asian languages (CJK) not included (M4+ if demand exists)
- Right-to-left (RTL) languages not supported (M4+ if needed)

---

### [0.2.0] — 2026-07-23

#### Added

**UI Redesign (Milestone 1.5)**
- All 6 core screens rebuilt per approved wireframes ([[06-Wireframes-UI]])
- New aesthetic guide (`Docs/07-Estetica-UI.md`): dark palette with neon accents (X cyan #35E6C6, O violet #7C5CFF)
- New player-facing screens:
  - `ProfileScreen` — Player name display ("Name#1234" format, server-allocated)
  - `MatchHistoryScreen` — Last 50 local matches with timestamps, opponent type, result (stored in PlayerPrefs as `TTTXO_MatchHistory`)
  - `SettingsScreen` — Sound/Music volume sliders (persisted, no audio engine yet), language selector (UI ready), delete data flows
  - `StoreScreen` — Stub "Coming soon" placeholder (cosmetics/battle pass planned M3+)
- Shared UI components:
  - Confirmation overlay (reusable modal for destructive actions)
  - Toast notification system (in-game feedback)
  - "Delete my data" flow: local-only, clears PlayerPrefs
  - "Delete account & data" flow: UI only, server-side logic pending Dashboard activation
- Online status indicator in Home screen with "Soon" tag (no connectivity yet)

**UGS Foundations (Milestone 2)**
- **Authentication Module**
  - Anonymous sign-in at app launch
  - Player nickname allocation: "Name#1234" format (4-digit random suffix)
  - Home screen displays current player name
  - Graceful fallback to local-only if network unavailable

- **Analytics Wrapper**
  - Defensive wrapper around GameAnalytics (nullable, graceful skip if unavailable)
  - 4 tracked events: `match_started`, `match_finished`, `board_size_selected`, `soft_currency_earned`
  - Server-side processing; no telemetry payload exposed to client

- **Remote Config (Config-as-Code)**
  - `Assets/RemoteConfig/GameConfig.rc` (YAML-based)
  - Mirrors design-doc: `BOARD_CONFIGS` (board size → k-value, reward base), `AI_DIFFICULTY_PARAMS` (Easy/Medium/Hard/Adaptive)
  - Zero runtime loading; configs baked at build time
  - Fallback to hardcoded values if config file missing

- **Cloud Save (Best-Effort)**
  - Periodic sync: soft currency wallet, match history (50 entries), adaptive AI levels per board size
  - Client-authoritative; local state takes precedence on conflict
  - Silent skip if network unavailable (no error UI)

- **Cloud Code Module**
  - `CloudCode~/TicTacToeModule/` — Server-side C# functions
  - `GetGameConfig()` — Returns board/AI configs
  - `CreateMatch()` — Match ID allocation, state initialization
  - `PlayMove(matchId, cellIndex)` — Server validation + AI move
  - `GetMatchState(matchId)` — Board state retrieval
  - `GetAiMove(matchId, boardState, difficulty)` — AI strategy execution
  - **Zero code duplication:** all functions reuse TTTXO.Core (BoardState, AiPlayer, RulesEngine)
  - No online matchmaking or PvP yet (foundations only)

- **Splash Screen UX**
  - Initialization sequence: UGS service check, auth status log
  - Generic error state with "Retry" button
  - Timeout failover to local-only play (60s max)

- **Game Protocol Versioning**
  - `GameProtocol.Version` handshake (warning if mismatch, non-fatal)

- **UGS Package Versions**
  - `com.unity.services.core` 1.18.0
  - `com.unity.services.authentication` 3.7.3
  - `com.unity.services.cloudcode` 2.10.4
  - `com.unity.services.cloudsave` 3.4.1
  - `com.unity.services.analytics` 6.3.0
  - `com.unity.services.remote-config` 4.2.5
  - `com.unity.deployment` 1.7.2

#### Changed

- `GameManager` — Integrated UGS authentication; displays player name on Home screen
- Splash screen — Now includes UGS initialization + error handling
- `UiText` — Prepared for localization integration (M3)

#### Design Documentation

- `Docs/06-Wireframes-UI.md` — Screen wireframes (from Claude Design)
- `Docs/07-Estetica-UI.md` — Dark theme + neon accent specification

#### Known Limitations

- No online matchmaking or PvP (M3+)
- No audio engine (M2 deferred; volume sliders persist but no playback)
- Store is stub only
- Server-side currency cap enforcement pending (M3+)
- Cloud Code functions not exposed to client (M3+)
- Font coverage for tr/vi/pl — QA TODO (deferred from M2)

#### User Action Required

To activate UGS services:
1. Enable services in [Unity Dashboard](https://dashboard.unity.com/) (Authentication, Analytics, Remote Config, Cloud Save, Cloud Code)
2. Register 4 custom events in Event Manager
3. Deploy Cloud Code via `Window → Deployment`
4. Test offline mode thoroughly (no network, mid-sync termination)

---

### [0.1.0] — 2026-07-23

#### Added

**Core Assembly (TTTXO.Core)**
- `Assets/Scripts/Core/` — Pure C# assembly without UnityEngine dependency, reusable in Cloud Code
  - Enums: `CellOwner`, `MatchStatus`, `GameMode`, `AiDifficulty`
  - `BoardConfig` — Configuration for board sizes (3×3 k=3, 6×6 k=4, 9×9 k=5, 11×11 k=5)
  - `BoardState` — Game state representation and validation
  - `MatchController` — Move validation, turn alternation, win/draw detection
  - `RulesEngine` — k-in-a-line detection with overline validation
  - `AiParams` — Difficulty-based AI parameters (cascaded pWin/pBlock/blunder rates)
    - Easy: 0.70 pWin / 0.50 pBlock
    - Medium: 1.0 pWin / 0.9 pBlock / 0.25 blunder
    - Hard: Unbeatable on 3×3, 0.05 blunder on 6×6+, depth 4 minimax
    - Adaptive: Levels 0–100 with 10-game rolling window, target win rate 0.40–0.60
  - `BoardHeuristics` — Position evaluation with Chebyshev window weighting (defensive ×1.2 multiplier)
  - `AiPlayer` — Cascading strategy (win → block → positional) with alpha-beta minimax (full on 3×3; Chebyshev ≤2, max 12 candidates, depth-limited on 6×6+)
  - `AdaptiveAiController` — Win-rate tracking and difficulty auto-scaling per board size
  - `RewardCalculator` — Soft currency rewards (base 10/20/30/40 per board size on victory, multipliers ×0.5/×1/×1.5 based on difficulty, local 2P flat ×2, daily cap 20, global cap 300/day)

**Test Suite (EditMode)**
- `Assets/Scripts/Tests/Core/` — 75 EditMode tests across 6 files (all passing)
  - `RulesEngineTests`, `MatchControllerTests`, `BoardConfigTests`, `AiPlayerTests`, `AdaptiveAiControllerTests`, `RewardCalculatorTests`
  - 50+ Hard-difficulty 3×3 vs random games without defeat
  - Standalone validation without scene dependencies

**Unity UI Layer (UI Toolkit)**
- `Assets/UI/Screens/Root.uxml` — Screen hierarchy (6 screens: Splash, Home, ModeSelect, BoardSelect, Game, Result)
- `Assets/UI/Styles/main.uss` — Dark theme with neon accent palette
- `Assets/Scripts/Game/UI/`
  - `ScreenRouter` — Screen navigation controller
  - Screen controllers: `SplashScreenController`, `HomeScreenController`, `ModeSelectScreenController`, `BoardSelectScreenController`, `GameScreenController`, `ResultScreenController`
  - `IScreenController` — Interface for screen lifecycle
  - `ScreenId` — Screen enumeration
  - `UiText` — Centralized UI string localization (TODO: integrate Unity Localization)

**Game Systems**
- `Assets/Scripts/Game/Bootstrap/`
  - `AppBootstrap` — Application initialization and scene setup
  - `GameManager` — Match orchestration, first-player alternation, adaptive AI per board size, soft wallet with daily persistence (PlayerPrefs)
- `Assets/Scripts/Game/Editor/MainSceneSetup.cs` — Editor utility script for idempotent Main.unity scene creation (PanelSettings + theme + scene structure)
- Dynamic board generation (C# code-driven)
- AI execution on background thread with 400ms delay per move

**Project Configuration**
- `.claude/agents/` — Agent definitions for distributed development
- `CLAUDE.md` — Project guidelines and agent responsibilities
- Assembly definition files (asmdef) for Core, Tests, and Game namespaces

#### Changed

- Unity version: 6000.3.20f1 (baseline for this milestone)
- PlayerPrefs scheme for wallet persistence (soft currency state)

#### Fixed

- (No fixes in M1; release notes only)

#### Removed

- (N/A for M1)

---

## Notes

- Test suite runs in EditMode (Window → General → Test Runner); batch mode blocked by editor lock in validation environment
- AI response time capped at 400ms to maintain UX responsiveness
- No turn timer in M1 (planned for future milestone)
- Overline is allowed; draw only on full board
- Soft currency daily limits enforced server-side in future builds
