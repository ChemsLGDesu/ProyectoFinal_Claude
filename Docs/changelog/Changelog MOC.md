# Changelog — Map of Content

Central index of all project releases and significant updates, ordered chronologically (newest first).

## Release Timeline

### v0.5.0 — Ranked (Milestone 5): Elo Rating, Seasons, Leaderboards

**Date:** 2026-07-23  
**Status:** Unreleased (internal build)

[[v0.5.0-Ranked-Milestone-5]] — Complete competitive ranked mode with Elo rating, seasonal play (8-week cycles with soft reset), per-board MMR separation (mmr3x3/mmr6x6 only; tiers: bronze [0–899], silver [900–1099], gold [1100–1299], platinum [1300–1499], diamond [1500+]), series-based fairness for 3x3 (solved game, 2-game series with alternated first-move), anti-boosting via repeated-rival dilution (24h window: ×1.0/1.0/0.5/0.5/0.0), leaderboards with inactivity decay (−25/day above 1200, grace 7d), tier-based seasonal rewards. Server: 10 Ranked* support files + 2 new Cloud Code endpoints (GetRankedProfile, GetRankedLeaderboard). Reuses CreateMatch/PlayMove/GetMatchState (no new game functions). Client: matchmaking Ranked, series auto-chaining (3x3), Result/Profile/Leaderboard UI. Analytics: 5 new events + 3 extensions. Fixed: match_finished mode hardcoding bug.

**Key Deliverables:**
- RankedQueue.mmq (skill-based matching, relaxation curve: ±100/±200/±350/±600/open)
- ranked_3x3.lb, ranked_6x6.lb (Latest score, seasonal reset)
- RANKED_CONFIG in Remote Config (JSON: tiers, K factors [40/32/24/16], season dates, decay rates, seasonal rewards)
- 10 CloudCodeFunctions (Ranked* files + RankedQueryFunctions); series model via RankedSeriesRecord
- MMR storage: separate Cloud Save item (`rankedProfile`), not in `profile`
- Placement: 10 games minimum; soft reset post-season (1000 + (prev − 1000) × 0.5, floor 500)
- Anti-boosting: 24h UTC window per (playerId, rivalId, boardSize), gains diluted ×1.00/1.00/0.50/0.50/0.00
- Abandonment: derrota completa (abandoner), full win (stayer), no-contest if both (ΔMMR = 0)
- Leaderboard decay: −25 MMR/day above 1200 (grace 7d, floor 1200); decay applied lazily
- Tier rewards: bronze 100 → diamond 1200 SC + frame; top100 +500 SC + banner (exeunt de topes diarios)

**Related Architecture:**
- [[design-doc]] — Section 6 (Ranked), Section 4 v3 (economy with Ranked)
- [[03-Arquitectura-UGS-TicTacToe]] — Leaderboard integration, Cloud Code patterns
- [[06-Wireframes-UI]] — Wireframes 7 (Result MMR delta), 8 (Profile Ranked tab), 10 (Leaderboard)
- [[02-GDD-TicTacToe]] — Economy comparison (Ranked 10/15 vs Quickmatch 12/25)
- [[04-Store-Catalog-TicTacToe]] — Cosmetics (seasonal frames not vendible; SKU corrected)
- [[v0.4.2-Pre-Ranked-Technical-Debt]] — Parent version (ticket verification, abandonment sweep)

**User Actions (Critical):**
- Install com.unity.services.leaderboards 2.3.4
- Deploy ranked_3x3.lb and ranked_6x6.lb via `Window → Deployment`
- Verify UpdateType = Latest score in Dashboard
- Register 5 new Analytics events in Event Manager
- **Update RANKED_CONFIG.seasonStartUnixSeconds to real UTC timestamp** (placeholder: 1785542400 = 2026-08-01T00:00:00Z)
- Sync ResetConfig.Start in both .lb with RANKED_CONFIG.seasonStartUnixSeconds
- Deploy TicTacToeModule.ccmr to Cloud Code

---

### v0.4.2 — Pre-Ranked Technical Debt: Hard Ticket Verification + Proactive Abandonment Sweep

**Date:** 2026-07-23  
**Status:** Unreleased (internal build)

[[v0.4.2-Pre-Ranked-Technical-Debt]] — Matchmaker ticket verification elevated from best-effort to strict blocking (root cause: parameter swap bug, now fixed). Proactive abandoned match resolution via `SweepAbandonedMatches()` with UTC-partitioned index; both players abandon → draw + 0 reward. MatchIndexStore.cs (new), trigger config-as-code (UGS CLI deployment required). Closes M5 blocker items.

**Key Deliverables:**
- `MatchIndexStore.cs` — UTC-partitioned active match registry (Cloud Save Custom Data)
- `SweepAbandonedMatches()` — CloudCode function + cron triggers (5 min, 200 match/exec cap)
- MatchStateRecord.BothPlayersAbandoned — New optional field, backward compatible
- Ticket verification: `context.ServiceToken` + `ticketId` (correct parameters; no new roles)
- CreateMatch now strict: rejects if ticket unverifiable
- Assets/Triggers/*.tr and *.sched (deploy via `ugs deploy`)

**Related Architecture:**
- [[03-Arquitectura-UGS-TicTacToe]] — Ticket verification, Cloud Save patterns
- [[design-doc]] — Fairness specification (abandonment = no reward, no MMR bias)
- [[v0.4.1-Online-Reward-Anti-Farming]] — Parent version (reward infrastructure)

---

### v0.4.1 — Online Reward (Server-Side) + Anti-Farming

**Date:** 2026-07-23  
**Status:** Unreleased (internal build)

[[v0.4.1-Online-Reward-Anti-Farming]] — Server-side reward calculation with corrected balance (1.25× victory multiplier per design-doc v2), per-rival caps, daily anti-farming limits, and abandonment exclusion. Removed client-side reward logic and provisional multiplier.

**Key Deliverables:**
- `CloudCode~/TicTacToeModule/OnlineRewardCalculator.cs` — Derives RewardCalculator, applies online multiplier
- `CloudCode~/TicTacToeModule/OnlineRewardStore.cs` — Cloud Save ledger (daily, per-rival), enforces caps
- Reward balance: victory 1.25× (3×3=12, 6×6=25, 9×9=37, 11×11=50), draw 1.0×, loss/abandonment=0
- Anti-farming: 200/day online cap, 300/day global cap, 3 paid wins per rival per 24h
- MatchStateDto.AwardedSoftCurrency (new optional field, backward compatible)
- GameScreenController: removed provisional reward block; client displays server-sourced value only
- Analytics: soft_currency_earned now emits source="online_quickmatch"

**Known Limitation Documented:**
- Global 300/day cap audits online rewards only; offline rewards remain client-stored (future consolidation M4.2+)

**Related Architecture:**
- [[design-doc]] — Balance specification (v2, game-designer ratified)
- [[03-Arquitectura-UGS-TicTacToe]] — Cloud Code and Cloud Save patterns
- [[v0.4.0-Online-Quickmatch-Wire]] — Parent version (Quickmatch + Wire foundation)

---

### v0.4.0 — Online Quickmatch + Wire (Milestone 4)

**Date:** 2026-07-23  
**Status:** Unreleased (internal build)

[[v0.4.0-Online-Quickmatch-Wire]] — Complete multiplayer matchmaking via Matchmaker service and real-time synchronization via Wire SDK. Players search for opponents by board size, create online matches with serverless hosting, and play in real-time with push notifications. Includes defensive grid fallback and navigation stack fixes.

**Key Deliverables:**
- `com.unity.services.multiplayer` 2.2.3 with Wire SDK 1.4.4
- Matchmaker config-as-code (QuickmatchQueue.mmq, MatchmakerEnvironmentConfig.mme)
- Cloud Code CreateMatch (idempotent handoff) and PlayMove (online validation + Wire push)
- MatchmakingService (ticket lifecycle) and OnlineMatchService (Wire subscription + polling fallback)
- MatchmakingScreen (spinner, cancel, 45s fallback to AI), GameScreen with Wire status chip
- Analytics: matchmaking_wait_time, matchmaking_cancelled
- Remote Config: BOARD_CONFIGS.online_quickmatch, MATCHMAKING_CONFIG (3 min abandon timeout, 45s wait ceiling, 180s ticket TTL)
- Online rewards base 1.0× (provisional)

**Related Architecture:**
- [[03-Arquitectura-UGS-TicTacToe]] — Wire and Matchmaker integration
- [[06-Wireframes-UI]] — Matchmaking UI (wireframe 5, with fallback deviation)
- [[design-doc]] — Milestone 4 specification

---

### v0.3.0 — Localization (10 Languages)

**Date:** 2026-07-23  
**Status:** Unreleased (internal build)

[[v0.3.0-Localization-10-Languages]] — Complete localization for 10 languages (en, es, fr, de, pt, it, id, vi, tr, pl) with active language selector, 85 localized UI strings, Unity Localization 1.5.12

**Key Deliverables:**
- 10 supported locales with native language names
- `UiStrings` StringTable (85 keys, all translated)
- Idempotent editor setup tool
- Language persistence and hot-reload

---

### v0.2.0 — UI Wireframes + UGS Foundations

**Date:** 2026-07-23  
**Status:** Unreleased (internal build)

[[v0.2.0-UI-Wireframes-UGS-Foundations]] — UI redesign per approved wireframes, new screens (Profile, Match History, Settings, Store), and foundational UGS integration (Authentication, Analytics, Remote Config, Cloud Save, Cloud Code) with 100% offline fallback

**Key Deliverables:**
- [[06-Wireframes-UI]] — Screen mockups
- [[07-Estetica-UI]] — Dark theme + neon accent palette
- [[03-Arquitectura-UGS-TicTacToe]] — UGS integration patterns
- 7 UGS packages integrated (core 1.18.0 — deployment 1.7.2)
- Match history storage (last 50 games in PlayerPrefs)
- Confirmation overlays and toast notifications
- Splash screen with UGS initialization + error handling

---

### v0.1.0 — Milestone 1: Local Play Foundation

**Date:** 2026-07-23  
**Status:** Unreleased (internal build)

[[v0.1.0-Milestone-1-Local-Play-Foundation]] — Complete local play with AI, 4 board sizes, 4 difficulty levels, local 2P, soft currency rewards, 75 passing tests

**Key Deliverables:**
- [[design-doc]] — M1 specification
- [[02-GDD-TicTacToe]] — Game design overview
- [[03-Arquitectura-UGS-TicTacToe]] — Architecture reference

---

## Quick Links

- Players: [[../../changelog-jugadores.md|changelog-jugadores.md]]
- Developers: [[../../CHANGELOG-dev.md|CHANGELOG-dev.md]]
- Design: [[../02-GDD-TicTacToe]]

---

*Last updated: 2026-07-23 (v0.5.0)*
