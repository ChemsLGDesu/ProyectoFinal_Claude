namespace TTTXO.CloudCode
{
    // ---- GetGameConfig contract -------------------------------------------------------------
    // Kept 1:1 with Assets/Scripts/Game/Services/GameConfigService.cs's client-side DTOs (same
    // property names/casing) so the client's CallModuleEndpointAsync<GetGameConfigResponse>
    // deserializes this response without any name mapping.

    /// <summary>Mirrors one entry of the future Remote Config <c>BOARD_CONFIGS</c> key (design-doc.md section 1).</summary>
    public class BoardConfigDto
    {
        public int Size { get; set; }
        public int WinLength { get; set; }
        public string[] Modes { get; set; } = System.Array.Empty<string>();
    }

    /// <summary>Mirrors one (difficulty, boardSize) row of the future Remote Config <c>AI_DIFFICULTY_PARAMS</c> key (design-doc.md section 3).</summary>
    public class AiDifficultyParamsDto
    {
        public string Difficulty { get; set; } = string.Empty;
        public int BoardSize { get; set; }
        public double WinProbability { get; set; }
        public double BlockProbability { get; set; }
        public double BlunderChance { get; set; }
        public int SearchDepth { get; set; }
    }

    /// <summary>
    /// Mirrors the future Remote Config <c>MATCHMAKING_CONFIG</c> key (Milestone 4,
    /// Docs/03-Arquitectura-UGS-TicTacToe.md "Resolucion detallada de riesgos" #3/#4). Read via
    /// <see cref="MatchmakingConfigReader"/> - never throws, falls back to these defaults if the key
    /// is missing/unreachable so matchmaking/abandonment resolution degrades gracefully instead of
    /// breaking play.
    /// </summary>
    public class MatchmakingConfigDto
    {
        /// <summary>Risk #4: minutes of rival inactivity before <c>GetMatchState</c> resolves the match as an abandonment win for the caller.</summary>
        public int AbandonTimeoutMinutes { get; set; } = 3;

        /// <summary>Risk #2/UX: seconds the Matchmaking screen waits before showing the "Taking too long?" fallback card.</summary>
        public int WaitCeilingSeconds { get; set; } = 45;

        /// <summary>Risk #3: ticket TTL configured server-side in the Matchmaker pool itself (see quickmatch-pool.mmq) - mirrored here only so the client can display/reason about it if needed.</summary>
        public int TicketTtlSeconds { get; set; } = 180;
    }

    /// <summary>Response of <see cref="GetGameConfigFunctions.GetGameConfig"/>.</summary>
    public class GetGameConfigResponse
    {
        public int Version { get; set; }
        public List<BoardConfigDto> BoardConfigs { get; set; } = new();
        public List<AiDifficultyParamsDto> AiDifficultyParams { get; set; } = new();
        public MatchmakingConfigDto MatchmakingConfig { get; set; } = new();

        /// <summary>Milestone 5 - design-doc.md section 6.1: "Todos los numeros de esta seccion viven en una nueva key de Remote Config RANKED_CONFIG... expuesta al cliente via GetGameConfig".</summary>
        public RankedConfigDto RankedConfig { get; set; } = new();
    }

    // ---- Match state persistence (Cloud Save Custom Data, key = matchId) -------------------
    // Docs/03-Arquitectura-UGS-TicTacToe.md#Cloud Save: "Estado de partida (Custom Data, clave =
    // matchId): MatchState completo (tablero, turno, tamano, modo, simbolos a alinear)".
    //
    // Stored as an append-only move list rather than a raw board snapshot: TTTXO.Core's
    // MatchController only mutates its board through PlayMove(row, col), which is exactly what we
    // want reused unmodified (see Docs/01-Directrices-Proyecto.md#Seguridad / anti-cheat - Cloud
    // Code must decide who won using the same code as the client, never re-derive it). Replaying
    // the recorded moves through a fresh MatchController on every call reconstructs the identical
    // Board/Status/CurrentPlayer/TurnCount deterministically (see MatchStateStore.Replay).

    public class MoveRecord
    {
        public int Row { get; set; }
        public int Col { get; set; }
    }

    /// <summary>One matched player of an online match (Milestone 4). Empty for 1P/local matches.</summary>
    public class MatchPlayerRecord
    {
        public string PlayerId { get; set; } = string.Empty;

        /// <summary>"X" or "O" - see MatchFunctions.CreateMatch for the deterministic assignment rule.</summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>Touched by every PlayMove/GetMatchState call this player makes - the abandonment check (risk #4) compares this against "now".</summary>
        public long LastActivityAtUnixSeconds { get; set; }
    }

    public class MatchStateRecord
    {
        public string MatchId { get; set; } = string.Empty;
        public int BoardSize { get; set; }
        public int WinLength { get; set; }

        /// <summary>Metadata only (see GameAnalytics.ModeCode on the client) - board rules only depend on BoardSize/WinLength.</summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>Always "X" per design-doc.md section 1 ("X siempre mueve primero"), kept explicit for clarity/future-proofing.</summary>
        public string FirstPlayer { get; set; } = "X";

        public List<MoveRecord> Moves { get; set; } = new();

        public long CreatedAtUnixSeconds { get; set; }

        /// <summary>Touched by every PlayMove/GetMatchState call, regardless of mode - "something happened" marker, kept for 1P/local compatibility/diagnostics.</summary>
        public long LastActivityAtUnixSeconds { get; set; }

        /// <summary>
        /// Milestone 4 online matches only (empty for 1P/local, per the Milestone 2 contract this
        /// record started with): exactly 2 entries once <see cref="MatchFunctions.CreateMatch"/>
        /// resolves an online_quickmatch ticket. See MatchFunctions.CreateMatch for the
        /// lexicographic-order X/O assignment rule (Docs/03-Arquitectura-UGS-TicTacToe.md#Matchmaker).
        /// </summary>
        public List<MatchPlayerRecord> Players { get; set; } = new();

        /// <summary>
        /// The Matchmaker <c>MatchIdAssignment.MatchId</c> both matched clients received (shared by
        /// both, unlike each player's own individual Matchmaker ticket id - <c>maxPlayersPerTicket
        /// = 1</c> in quickmatch-pool, see Assets/Matchmaker/QuickmatchQueue.mmq) - the idempotency/
        /// join key for <see cref="MatchFunctions.CreateMatch"/> (risk #1). Null for 1P/local.
        /// </summary>
        public string HandoffId { get; set; }

        /// <summary>
        /// Set once <see cref="MatchFunctions.GetMatchState"/> resolves the match as an abandonment
        /// win (risk #4) - the playerId of the still-active player. Layered on top of the
        /// move-replay result in <see cref="MatchStateStore.ToDto"/> rather than injected as a fake
        /// move, so <see cref="MatchStateStore.Replay"/> stays a pure function of the recorded moves.
        /// </summary>
        public string AbandonedWinnerPlayerId { get; set; }

        public long AbandonedAtUnixSeconds { get; set; }

        /// <summary>
        /// Set once <see cref="MatchFunctions.SweepAbandonedMatches"/> (proactive sweep, see
        /// MatchIndexStore) resolves the match because BOTH players - not just the rival of an
        /// active caller, see <see cref="AbandonedWinnerPlayerId"/> - had no activity past
        /// <c>MATCHMAKING_CONFIG.AbandonTimeoutMinutes</c>. Design decision (README "Barrido
        /// proactivo"): with nobody still active, there is no natural "winner" the way the reactive
        /// path has one, so this resolves as a draw/no-winner rather than picking either side -
        /// mirrored in <see cref="MatchStateStore.ToDto"/> as <c>Status = "draw"</c>,
        /// <c>EndedByAbandonment = true</c>. Mutually exclusive with
        /// <see cref="AbandonedWinnerPlayerId"/> being set (only one of the two abandonment paths can
        /// ever resolve a given match, whichever gets there first - both are idempotent no-ops
        /// against an already-resolved record, see MatchFunctions).
        /// </summary>
        public bool BothPlayersAbandoned { get; set; }

        /// <summary>
        /// Milestone 4 online matches only: soft currency actually granted to each player once the
        /// match reached a terminal status, keyed by playerId (design-doc.md section 4, "Modo
        /// online Quickmatch"). Computed exactly once - inside the <see cref="MatchFunctions.PlayMove"/>
        /// call that ends the match (see <see cref="MatchFunctions.AwardOnlineRewardsAsync"/>), or,
        /// for an abandonment resolution, inside <see cref="MatchFunctions.GetMatchState"/>'s
        /// abandonment check (which always awards 0 to the abandonment winner) - never recomputed
        /// or re-awarded by a later poll. This dictionary is the persisted record of "what was
        /// actually paid", read back verbatim by <see cref="MatchStateStore.ToDto"/> for every
        /// subsequent <c>GetMatchState</c> call from either player.
        /// </summary>
        public Dictionary<string, int> AwardedSoftCurrencyByPlayerId { get; set; } = new();

        // ---- Milestone 5 - Ranked additions (design-doc.md section 6) ------------------------
        // Only ever populated when Mode == "ranked" (see RankedMatchSupport.cs). A Ranked match is
        // otherwise a completely ordinary MatchStateRecord replayed through the exact same
        // MatchStateStore.Replay/PlayMove path as Quickmatch/1P/local - Ranked never forks
        // TTTXO.Core or the move-application pipeline, only what happens AFTER a move/abandonment
        // resolves the match (see MatchFunctions.SettleRankedUnitIfNeededAsync).

        /// <summary>Each player's MMR for this board size, snapshotted once when this match's 2-player roster completed - see design-doc.md section 6.8 Q1's "read a stable snapshot, not a live re-read" reasoning (a lazy decay tick landing mid-series must not change the Elo inputs of a series already in progress).</summary>
        public Dictionary<string, int> RankedMmrBeforeByPlayerId { get; set; } = new();

        /// <summary>Non-null only for a 3x3 Ranked game (design-doc.md section 6.1 "serie de 2 partidas") - the id of the <see cref="RankedSeriesRecord"/> this match is game 1 or 2 of. Null for a 6x6 Ranked match (single-game unit) and for every non-Ranked mode.</summary>
        public string RankedSeriesId { get; set; }

        /// <summary>1 or 2 within <see cref="RankedSeriesId"/>'s series; 0 when not part of a series (6x6 Ranked, or any non-Ranked mode).</summary>
        public int RankedSeriesGameIndex { get; set; }

        /// <summary>
        /// Ranked-only turn timer (design-doc.md section 6.1 "Extiende sección 2": 20s/3x3, 30s/6x6),
        /// resolved reactively like the existing abandon timeout (see
        /// MatchFunctions.ResolveAbandonmentIfNeededAsync) - never a new cron. Set once both players
        /// join and re-set to "now" after every successful move; a caller who observes
        /// <c>now - TurnStartedAtUnixSeconds &gt; RankedConfigDto.TurnTimeoutSecondsFor(BoardSize)</c>
        /// resolves the match as an abandonment loss for whoever's turn it currently is, exactly like
        /// the existing 3-minute idle check. 0 (never set/checked) for non-Ranked modes.
        /// </summary>
        public long TurnStartedAtUnixSeconds { get; set; }

        /// <summary>
        /// True once this match's outcome has already been folded into its Ranked unit (the series
        /// for 3x3, or the match itself for 6x6) - MMR/currency/leaderboard settlement guard so a
        /// retried/duplicate resolution (e.g. GetMatchState called again after the reactive
        /// abandonment path already settled this match) can never double-settle. Mirrors the same
        /// "a retried PlayMove on an already-finished match fails validation before reaching award
        /// logic" guarantee Quickmatch already relies on (see MatchFunctions.PlayMove remarks) -
        /// this flag exists because Ranked's settlement can also be triggered from GetMatchState/
        /// SweepAbandonedMatches, which do not have that same natural guard.
        /// </summary>
        public bool RankedSettled { get; set; }

        /// <summary>Settled Ranked MMR result per playerId, mirrored here (from the series record, for a 3x3 game) so <see cref="MatchStateStore.ToDto"/>-based retrieval works identically for 3x3/6x6 - see <see cref="RankedMmrResultDto"/> / <see cref="RankedMatchSupport.EnrichDtoWithSeriesInfoAsync"/>.</summary>
        public Dictionary<string, RankedMmrResultDto> RankedMmrResultByPlayerId { get; set; } = new();
    }

    // ---- Ranked (Milestone 5, design-doc.md section 6) --------------------------------------

    /// <summary>
    /// Mirrors the future Remote Config <c>RANKED_CONFIG</c> key (design-doc.md section 6.1: "Todos
    /// los numeros de esta seccion viven en una nueva key de Remote Config RANKED_CONFIG"). Read via
    /// <see cref="RankedConfigReader"/> - like <see cref="MatchmakingConfigDto"/>, never throws;
    /// falls back to these defaults (the exact values ratified in design-doc.md section 6) so a
    /// missing/unreachable key degrades gracefully instead of breaking a Ranked match resolution.
    /// </summary>
    public class RankedConfigDto
    {
        public int InitialMmr { get; set; } = 1000;
        public int MmrFloor { get; set; } = 500;

        /// <summary>design-doc.md section 6.1 K-factor table, in ascending placements-played order: below <see cref="PlacementMatches"/> -> 40, up to 29 -> 32, >=30 (and MMR &lt; KHighMmrThreshold) -> 24, >=30 (and MMR &gt;= KHighMmrThreshold) -> KHighMmr.</summary>
        public int KPlacement { get; set; } = 40;
        public int KIntermediate { get; set; } = 32;
        public int KEstablished { get; set; } = 24;
        public int KHighMmr { get; set; } = 16;
        public int KHighMmrThreshold { get; set; } = 1600;

        /// <summary>design-doc.md section 6.5 - 5 since seasons became monthly (was 10 with 8-week seasons): it gates the K=40 window, leaderboard visibility and season-reward eligibility, and a casual player has to clear it with a month of play, not two.</summary>
        public int PlacementMatches { get; set; } = 5;

        /// <summary>First-move compensation `F` (design-doc.md section 6.1) - always 0 for 3x3 (the series already neutralizes it, enforced in code, this value is only read for board size 6).</summary>
        public int FirstMoveEloBoard6 { get; set; } = 50;

        /// <summary>design-doc.md section 6.1 "Amortiguación por rival repetido" thresholds - ascending win-ordinal-today, e.g. [1,1,0.5,0.5,0,0,...] read as "damping for the Nth paid/scored win today equals array[N-1], array's last value repeats for any N beyond its length".</summary>
        public double[] RepeatRivalDamping { get; set; } = { 1.0, 1.0, 0.5, 0.5, 0.0 };

        /// <summary>design-doc.md section 6.1 "Extiende sección 2": Ranked turn timer, seconds, by board size.</summary>
        public int TurnTimeoutSecondsBoard3 { get; set; } = 20;
        public int TurnTimeoutSecondsBoard6 { get; set; } = 30;

        /// <summary>design-doc.md section 6.3 Ranked reward table (victory/draw - defeat and abandonment-win are always 0, not configurable).</summary>
        public int RewardWinBoard3 { get; set; } = 10;
        public int RewardDrawBoard3 { get; set; } = 4;
        public int RewardWinBoard6 { get; set; } = 15;
        public int RewardDrawBoard6 { get; set; } = 5;

        /// <summary>design-doc.md section 6.6 leaderboard decay - MMR floor above which decay applies, grace days before it starts, and the daily rate past the grace period. Decay never crosses <see cref="MmrFloor"/>.</summary>
        public int DecayProtectedFloor { get; set; } = 1200;
        public int DecayGraceDays { get; set; } = 7;
        public int DecayPerDay { get; set; } = 25;

        /// <summary>design-doc.md section 6.5 - unix seconds of the Temporada 1 boundary (2026-08-28T00:00:00Z) and season length in whole CALENDAR months. <see cref="RankedSeasonCalculator"/> derives every later season's id/boundaries from these two values, so every boundary lands on the anchor's day-of-month (the 28th) - MUST stay in sync with the Leaderboards ResetConfig.Start/Schedule deployed for ranked_3x3/ranked_6x6 (see Assets/Leaderboards/README, "Sincronización obligatoria"; asserted by SeasonConfigAlignmentTests).</summary>
        public long SeasonStartUnixSeconds { get; set; }
        public int SeasonDurationMonths { get; set; } = 1;

        /// <summary>design-doc.md section 6.5 tier table - ascending MMR cutoffs, index 0 is Bronce's upper bound is "next cutoff", last entry (Diamante) has no upper bound. Kept as parallel arrays (not a list of records) to stay a trivial flat Remote Config JSON shape like the rest of this DTO.</summary>
        public string[] TierNames { get; set; } = { "bronze", "silver", "gold", "platinum", "diamond" };
        public int[] TierMinMmr { get; set; } = { 0, 900, 1100, 1300, 1500 };
        public int[] TierSoftCurrency { get; set; } = { 100, 200, 400, 700, 1200 };
        public int Top100BonusSoftCurrency { get; set; } = 500;

        public int TurnTimeoutSecondsFor(int boardSize) => boardSize == 3 ? TurnTimeoutSecondsBoard3 : TurnTimeoutSecondsBoard6;

        public int KFactorFor(int placementsPlayed, int currentMmr)
        {
            if (placementsPlayed < PlacementMatches)
            {
                return KPlacement;
            }

            if (placementsPlayed < 30)
            {
                return KIntermediate;
            }

            return currentMmr >= KHighMmrThreshold ? KHighMmr : KEstablished;
        }

        /// <summary><paramref name="winOrdinalTodayOneBased"/> is this win's 1st/2nd/3rd/... occurrence against the same rival today (see design-doc.md section 6.3 table) - values beyond the configured array repeat the last (most damped) entry rather than falling back to 1.0, so a misconfigured short array fails safe (more anti-boosting, never less).</summary>
        public double RepeatRivalDampingFor(int winOrdinalTodayOneBased)
        {
            if (RepeatRivalDamping == null || RepeatRivalDamping.Length == 0)
            {
                return 1.0;
            }

            int index = Math.Min(winOrdinalTodayOneBased, RepeatRivalDamping.Length) - 1;
            return RepeatRivalDamping[Math.Max(0, index)];
        }
    }

    /// <summary>Cloud Save Player Data payload for the <c>rankedProfile</c> key - deliberately its OWN key, separate from the client-writable <c>profile</c> key (Assets/Scripts/Game/Services/PlayerDataService.cs's <c>ProfileSaveData</c>). See this task's final report "Corrección de arquitectura no pedida explícitamente": the client's <c>PlayerDataService.SaveAfterMatchAsync</c> does a full overwrite of the <c>profile</c> item on every 1P/local match end with a payload shape that has no <c>ranked</c> field - had Ranked MMR been added to that same key as design-doc.md section 6.1 literally suggests, the next 1P/local match played by that same client would silently wipe it. <c>rankedProfile</c> is never read or written by the client directly - only via Cloud Code (GetRankedProfile, to be added when the Perfil screen needs it).</summary>
    public class RankedProfileSaveData
    {
        /// <summary>Keyed by board size as a string ("3"/"6"), same JSON-object-key-safety convention as the client's own <c>ProfileSaveData.AdaptiveLevelsByBoardSize</c>.</summary>
        public Dictionary<string, RankedBoardProfileRecord> ByBoardSize { get; set; } = new();
    }

    /// <summary>One board size's worth of design-doc.md section 6.1's persistence contract (<c>ranked: { "3": {...}, "6": {...} }</c>) - see <see cref="RankedProfileSaveData"/> for why this lives under its own Cloud Save key instead of literally inside <c>profile</c>.</summary>
    public class RankedBoardProfileRecord
    {
        public int Mmr { get; set; }
        public int PlacementsPlayed { get; set; }
        public long LastRankedMatchAtUnixSeconds { get; set; }

        /// <summary>design-doc.md section 6.6 lazy decay - the unix-seconds boundary through which decay has already been applied/persisted. Advanced by <see cref="RankedProfileStore"/> whenever a Ranked settlement or a Leaderboard/Perfil screen visit collapses pending decay (design-doc.md section 6.8 Q8 - NOT on every ordinary <c>profile</c>/session-start read, to bound write amplification).</summary>
        public long DecayAppliedThroughUnixSeconds { get; set; }

        /// <summary>design-doc.md section 6.5 lazy per-player season rollover - the season id this record's <see cref="Mmr"/>/<see cref="PlacementsPlayed"/> currently belong to. <see cref="RankedProfileStore"/> compares this against <see cref="RankedSeasonCalculator"/>'s current season id on touch and applies the soft reset + grants the outgoing season's tier reward exactly once when they diverge (design-doc.md section 6.8 Q9).</summary>
        public int SeasonId { get; set; }
    }

    /// <summary>
    /// Cloud Save Custom Data payload (key = seriesId) for a 3x3 Ranked series (design-doc.md
    /// section 6.1 "Decisión estructural 2" - "la unidad puntuada en 3x3 es una serie de 2
    /// partidas"). Wraps up to 2 completely ordinary <see cref="MatchStateRecord"/>s (each one
    /// replayed/validated by the exact same <see cref="MatchStateStore"/> path as every other
    /// match in this module - see design-doc.md section 6.8 Q1) rather than embedding the games
    /// inline, so <see cref="MatchStateStore.Replay"/>/<c>PlayMove</c> never need to know a series
    /// exists at all. See CloudCode~/TicTacToeModule/README.md "Serie de 2 partidas (Ranked 3x3)"
    /// for the full model/rationale.
    /// </summary>
    public class RankedSeriesRecord
    {
        public string SeriesId { get; set; } = string.Empty;
        public int BoardSize { get; set; } = 3;

        /// <summary>Matchmaker's shared <c>MatchIdAssignment.MatchId</c> for THIS series (analogous to <see cref="MatchStateRecord.HandoffId"/>) - the idempotency/join key for series creation, same role the handoff pointer already plays for a plain online match.</summary>
        public string HandoffId { get; set; } = string.Empty;

        /// <summary>1 entry while waiting for the second matched player, 2 once complete - same "waiting" shape as <see cref="MatchStateRecord.Players"/>, kept as raw ids here (symbols are decided per-game, not per-series - see <see cref="MatchIds"/>).</summary>
        public List<string> PlayerIds { get; set; } = new();

        /// <summary>MMR snapshot for both players, taken once when the roster completes - see <see cref="MatchStateRecord.RankedMmrBeforeByPlayerId"/> remarks for why this is a snapshot, not a live re-read.</summary>
        public Dictionary<string, int> MmrBeforeByPlayerId { get; set; } = new();

        /// <summary>Up to 2 matchIds, in play order: index 0 is game 1 (first caller pair's roster-completion order decides X, per design-doc.md section 1), index 1 is game 2 (symbols swapped - see RankedMatchSupport.CreateSeriesGameAsync).</summary>
        public List<string> MatchIds { get; set; } = new();

        /// <summary>1 while game 1 is in progress/just finished-but-game-2-not-created-yet, 2 once game 2 exists.</summary>
        public int CurrentGameIndex { get; set; } = 1;

        /// <summary>True once the series has a final result (2 games completed, OR decided early by an abandonment - design-doc.md section 6.4 "se pierde la serie entera") and MMR/reward/leaderboard settlement has run. Guards double-settlement the same way <see cref="MatchStateRecord.RankedSettled"/> does for a 6x6 match.</summary>
        public bool Complete { get; set; }

        public long CreatedAtUnixSeconds { get; set; }
        public long LastActivityAtUnixSeconds { get; set; }
    }

    /// <summary>Client-facing Ranked MMR result (design-doc.md section 6.7 <c>ranked_mmr_changed</c>'s shape, and wireframe 7's "card de MMR en Resultado") - populated on <see cref="MatchStateDto"/> only on the exact call whose move/resolution just settled a Ranked unit (series or 6x6 match); null on every other call, including later polls of the same already-settled match.</summary>
    public class RankedMmrResultDto
    {
        public int BoardSize { get; set; }
        public int MmrBefore { get; set; }
        public int MmrAfter { get; set; }
        public int Delta { get; set; }
        public int OpponentMmr { get; set; }
        public int KFactor { get; set; }

        /// <summary>"win" | "draw" | "loss" | "no_contest" (both abandoned - design-doc.md section 6.4) - matches <c>ranked_mmr_changed.result</c>.</summary>
        public string Result { get; set; } = string.Empty;

        public int Season { get; set; }
    }

    // ---- Ranked profile / leaderboard reads (Milestone 5, GetRankedProfile/GetRankedLeaderboard) --
    // design-doc.md section 6, wireframe 8 (Perfil) and wireframe 10 (Leaderboard). Both are pure
    // reads - see RankedQueryFunctions.cs - built on the exact same primitives PlayMove/GetMatchState
    // already use to settle a Ranked unit (RankedProfileStore.TouchAsync, RankedTierCalculator,
    // RankedSeasonCalculator, RankedLeaderboardStore), never a separate/duplicated calculation.

    /// <summary>One board size's worth of the CALLING player's own authoritative Ranked status (design-doc.md section 6.1's persistence contract, evaluated live) - see <see cref="RankedProfileResponse"/>.</summary>
    public class RankedBoardProfileDto
    {
        public int BoardSize { get; set; }
        public int Mmr { get; set; }
        public int PlacementsPlayed { get; set; }
        public int PlacementMatchesRequired { get; set; }

        /// <summary><c>PlacementsPlayed &gt;= PlacementMatchesRequired</c> - wireframe 8's "Colocación: 3/5" vs. a real MMR/tier display (design-doc.md section 6.6: un jugador aparece en el leaderboard solo tras completar sus partidas de colocacion). The required count travels in the response rather than being assumed client-side, so tuning <see cref="RankedConfigDto.PlacementMatches"/> needs no client build.</summary>
        public bool IsPlaced { get; set; }

        /// <summary>design-doc.md section 6.5 tier name ("bronze".."diamond") for the CURRENT live Mmr - reuses <see cref="RankedTierCalculator"/>, the same table the season-close reward uses, just evaluated against the live value instead of a season-final snapshot. Meaningful even before <see cref="IsPlaced"/> is true (a placement player still has SOME tier by MMR), the client decides whether to show it.</summary>
        public string Tier { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response of <c>GetRankedProfile</c> (wireframe 8, Perfil's MMR/tier/placement stat) - the
    /// CALLING player's own status for every Ranked-enabled board size, never another player's (see
    /// <see cref="RankedQueryFunctions.GetRankedProfile"/> - every read is scoped to
    /// <c>context.PlayerId</c>). Season context is board-agnostic (Ranked has one global season
    /// clock, design-doc.md section 6.5), so it is returned once at the top level rather than
    /// per-board.
    /// </summary>
    public class RankedProfileResponse
    {
        public int Season { get; set; }
        public long SeasonEndUnixSeconds { get; set; }
        public List<RankedBoardProfileDto> Boards { get; set; } = new();
    }

    /// <summary>One row of a Ranked leaderboard (top N entry, or the caller's own row) - design-doc.md section 6.6 "el puntaje publicado: el MMR crudo, entero". See <see cref="RankedLeaderboardResponse"/>.</summary>
    public class RankedLeaderboardEntryDto
    {
        public string PlayerId { get; set; } = string.Empty;

        /// <summary>Authentication player name with the <c>#1234</c> suffix (design-doc.md section 6.6: "El nombre mostrado sale del player name de Authentication... ya resuelto por SetPlayerName") - may be empty if the player never had a name set, the client decides the placeholder.</summary>
        public string PlayerName { get; set; } = string.Empty;

        public int Rank { get; set; }
        public int Mmr { get; set; }
    }

    /// <summary>
    /// Response of <c>GetRankedLeaderboard</c> (wireframe 10: "Top N en cards + fila propia
    /// destacada con posicion y MMR"). <see cref="HasOwnEntry"/> is false when the caller has not
    /// finished placement on this board yet (design-doc.md section 6.6 elegibilidad) - the top list is
    /// still returned in that case, only the caller's own row is omitted, never fabricated.
    ///
    /// That holds because <c>RankedMatchSupport</c> does not submit a score until placement is
    /// complete, so an unplaced player simply has no entry to find. Before 2026-08-08 this paragraph
    /// described an intent rather than a behaviour - nothing gated the write, and a player at 1/5
    /// appeared both here and in <see cref="Top"/>.
    /// </summary>
    public class RankedLeaderboardResponse
    {
        public int BoardSize { get; set; }
        public int Season { get; set; }
        public List<RankedLeaderboardEntryDto> Top { get; set; } = new();
        public bool HasOwnEntry { get; set; }
        public RankedLeaderboardEntryDto OwnEntry { get; set; }
    }

    // ---- Online reward ledger (Milestone 4 - Cloud Save Player Data) ------------------------
    // design-doc.md section 4, "Modo online Quickmatch": all reward math and anti-farming caps run
    // server-side, never on the client (Docs/01-Directrices-Proyecto.md#Seguridad / anti-cheat).
    // See OnlineRewardCalculator.cs (pure math) and OnlineRewardStore.cs (Cloud Save I/O).

    /// <summary>
    /// Cloud Save Player Data payload for the <c>currency</c> key - kept 1:1 (same property
    /// names/casing) with the client's own <c>CurrencySaveData</c>
    /// (Assets/Scripts/Game/Services/PlayerDataService.cs) so a balance written here by
    /// <see cref="OnlineRewardStore"/> is read back correctly by the client's
    /// <c>PlayerDataService.LoadIntoSessionAsync</c> on its next session-start Cloud Save pull.
    /// This is the ONLY key an online match ever touches for currency - the client never writes it
    /// for an online match (design-doc.md section 4: "el cliente nunca calcula ni escribe la
    /// moneda online").
    /// </summary>
    public class CurrencySaveData
    {
        public int Balance { get; set; }
        public long UpdatedAtUnixSeconds { get; set; }
    }

    /// <summary>
    /// Cloud Save Player Data payload for the <c>onlineCurrencyLedger</c> key (server-only, the
    /// client never reads or writes it): tracks this player's online-Quickmatch soft-currency
    /// earnings for the current UTC server day, used to enforce design-doc.md section 4's
    /// anti-farming caps - <see cref="OnlineRewardCalculator.OnlineDailyCap"/> (200/day,
    /// online-only) and <see cref="OnlineRewardCalculator.GlobalDailyCap"/> (300/day, shared
    /// ceiling).
    ///
    /// LIMITATION (documented, not solved here): <see cref="GlobalEarnedToday"/> currently only
    /// ever accumulates online-Quickmatch earnings, because the 1P/local offline economy still
    /// lives entirely on-device (design-doc.md section 4: "el saldo se persiste localmente en M1
    /// ... los pagos, a futuro, los otorgara Cloud Code al resolver PlayMove final" - GameManager's
    /// own 300/day cap on the client is a *separate*, device-local counter reset at local midnight,
    /// that this server-side ledger has no visibility into and cannot merge with). True
    /// unification of the 300/day pool across online+offline requires migrating offline reward
    /// granting to Cloud Code too - out of scope for this task (see this task's final report).
    /// Until then, this counter is a strict subset of online earnings, so in practice it is always
    /// bounded tighter by <see cref="OnlineRewardCalculator.OnlineDailyCap"/> (200 &lt; 300) and
    /// never the actual binding constraint - it exists so a future server-side offline-reward
    /// migration (or Ranked, Milestone 5) can add its own earnings into the same counter without a
    /// schema change.
    /// </summary>
    public class OnlineCurrencyLedgerRecord
    {
        /// <summary>UTC calendar day ("yyyy-MM-dd") the two counters below apply to - reset to 0 whenever a read/write observes a new day (server clock, never the device's, per design-doc.md section 4: "medianoche UTC de servidor").</summary>
        public string DayUtc { get; set; } = string.Empty;

        public int OnlineEarnedToday { get; set; }

        public int GlobalEarnedToday { get; set; }
    }

    /// <summary>One matched player as seen by the client - which symbol they were assigned. Empty array for 1P/local matches.</summary>
    public class MatchPlayerDto
    {
        public string PlayerId { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
    }

    /// <summary>Client-facing view of a match, returned by CreateMatch/PlayMove/GetMatchState.</summary>
    public class MatchStateDto
    {
        public string MatchId { get; set; } = string.Empty;
        public int BoardSize { get; set; }
        public int WinLength { get; set; }
        public string Mode { get; set; } = string.Empty;

        /// <summary>Row-major flat board, one char per cell: "X", "O" or "" (empty).</summary>
        public string[] Board { get; set; } = System.Array.Empty<string>();

        public string CurrentPlayer { get; set; } = string.Empty;

        /// <summary>"in_progress" | "x_won" | "o_won" | "draw" (same codes as GameAnalytics.MatchStatusCode on the client) - "x_won"/"o_won" also covers an abandonment resolution, see <see cref="EndedByAbandonment"/>.</summary>
        public string Status { get; set; } = string.Empty;

        public int TurnCount { get; set; }

        /// <summary>Winning cells as (row, col) pairs, empty unless Status is "x_won"/"o_won" by an actual line (never set on an abandonment resolution).</summary>
        public int[][] WinningLine { get; set; } = System.Array.Empty<int[]>();

        /// <summary>Milestone 4: empty for 1P/local; 1 entry while an online match is waiting for the second matched player to also call CreateMatch (see <see cref="IsWaitingForOpponent"/>); 2 entries (with symbol assignment) once complete.</summary>
        public MatchPlayerDto[] Players { get; set; } = System.Array.Empty<MatchPlayerDto>();

        /// <summary>True when <see cref="Status"/> was resolved by the risk #4 reactive abandonment check rather than an actual winning line.</summary>
        public bool EndedByAbandonment { get; set; }

        /// <summary>True for an online match that only the caller has joined so far - <c>PlayMove</c> rejects moves until the second player's own CreateMatch call completes the roster (see MatchFunctions.CreateMatch).</summary>
        public bool IsWaitingForOpponent { get; set; }

        /// <summary>
        /// Milestone 4: soft currency actually granted to the CALLER for this match, computed and
        /// credited entirely server-side (design-doc.md section 4, "Modo online Quickmatch" - the
        /// client never calculates or writes online currency, see Docs/01-Directrices-Proyecto.md
        /// #Seguridad / anti-cheat). Always 0 for 1P/local matches (no online reward path) and for
        /// an online match still <c>in_progress</c>. Once the match is terminal, this is the exact
        /// amount already credited to the caller's Cloud Save <c>currency</c> - the client only
        /// displays it (see GameScreenController.HandleOnlineMatchEnd).
        /// </summary>
        public int AwardedSoftCurrency { get; set; }

        // ---- Milestone 5 - Ranked additions (design-doc.md section 6, mirrors MatchStateRecord's own Ranked fields above) ----

        /// <summary>Non-null only for a 3x3 Ranked game - see <see cref="MatchStateRecord.RankedSeriesId"/>.</summary>
        public string RankedSeriesId { get; set; }

        /// <summary>1 or 2, 0 when not part of a series - see <see cref="MatchStateRecord.RankedSeriesGameIndex"/>.</summary>
        public int RankedSeriesGameIndex { get; set; }

        /// <summary>Populated the moment game 2 of a 3x3 Ranked series is auto-created (right when game 1 ends) so the client can navigate straight to it without a new Matchmaker search - design-doc.md section 6.1 "en la partida 1 empieza uno; en la partida 2, el otro", both games share the same 2 players.</summary>
        public string RankedNextMatchId { get; set; }

        /// <summary>True once this match's owning Ranked unit (the series for 3x3, or the match itself for 6x6) has a final result - mirrors <see cref="Status"/> being terminal but is series-aware: game 1 of a 3x3 series ending is NOT series-complete (see <see cref="RankedNextMatchId"/>).</summary>
        public bool RankedSeriesComplete { get; set; }

        /// <summary>Non-null only on the exact call that just settled Ranked MMR for the caller (wireframe 7's "card de MMR en Resultado") - see <see cref="RankedMmrResultDto"/>.</summary>
        public RankedMmrResultDto RankedMmrResult { get; set; }
    }

    /// <summary>Response of GetAiMove - the caller applies it via a separate PlayMove call (see MatchFunctions.GetAiMove).</summary>
    public class AiMoveDto
    {
        public int Row { get; set; }
        public int Col { get; set; }
    }

    // ---- Active match index (proactive abandonment sweep, Cloud Save Custom Data) -----------
    // See MatchIndexStore.cs: one document per UTC calendar day (`active-matches-{yyyy-MM-dd}`),
    // listing the matchIds of online matches with a completed 2-player roster that have not yet
    // been observed terminal - README "Barrido proactivo de partidas abandonadas".

    /// <summary>Cloud Save Custom Data payload for one day partition of the active-match index (see <see cref="MatchIndexStore"/>).</summary>
    public class ActiveMatchIndexRecord
    {
        public List<string> MatchIds { get; set; } = new();
    }

    /// <summary>Response of <see cref="MatchFunctions.SweepAbandonedMatches"/> - purely informational (server logs/monitoring), the client never calls this function.</summary>
    public class SweepResultDto
    {
        /// <summary>How many indexed matchIds were actually loaded and evaluated this run (bounded by the <c>maxMatches</c> parameter/default cap - see MatchFunctions.SweepAbandonedMatches).</summary>
        public int MatchesProcessed { get; set; }

        /// <summary>How many of those were genuinely resolved as a both-abandoned draw this run.</summary>
        public int MatchesResolvedAbandoned { get; set; }

        /// <summary>How many of those were pruned from the index without resolving anything (already terminal via the reactive path, a real move, or a missing/corrupt record) - see MatchFunctions.SweepAbandonedMatches.</summary>
        public int MatchesPruned { get; set; }
    }
}
