# Quickmatch (online) end-to-end validation against a live UGS environment.
#
# WHY THIS EXISTS
#   Everything Quickmatch actually guarantees lives in Cloud Code: who gets X, whether a move is
#   legal, and how many coins each side earned. None of it is observable from the client, and
#   reproducing it by hand needs two devices. This drives the real service - two anonymous players,
#   real quickmatch-queue tickets, and the actual Cloud Code functions - and asserts the exact
#   numbers design-doc.md section 4 ("Modo online Quickmatch") promises.
#
# WHAT IT COVERS
#   Matchmaking handoff, the two-phase CreateMatch join and its idempotency, deterministic X/O
#   assignment, server-side move rejection (out of turn, occupied cell, non-participant), the
#   reward table, per-caller reward projection, replay-safety of the award, and the repeated-rival
#   anti-farming cap. See the run plan below for the exact sequence.
#
# WHAT IT DOES NOT COVER
#   The client. Wire push, the matchmaking screen and its "taking too long" fallback are untouched
#   here and still need a manual pass with two builds. The daily caps (200 online / 300 global) and
#   the 3-minute abandonment path are not exercised either - the first would need ~17 wins, the
#   second a 3-minute wall-clock wait. Both are reachable with -Wins / a future flag if wanted.
#
# SAFETY
#   Creates real anonymous players and matches in whatever environment it points at, and writes to
#   one throwaway player's own Cloud Save during the write-access probe. Keep it on `development`.
#
# Usage:  powershell -File Tools\quickmatch-e2e.ps1

[CmdletBinding()]
param(
    [string]$ProjectId   = '6edc8de2-6599-4f88-bbe1-e9228d6b19e8',
    [string]$Environment = 'development',
    [string]$Module      = 'TicTacToeModule',
    [int]   $BoardSize   = 3
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\ugs-e2e-common.ps1"
Initialize-E2E -ProjectId $ProjectId -Environment $Environment -Module $Module

if ($BoardSize -ne 3) { throw "the scripted lines below are 3x3-only; -BoardSize $BoardSize has no plan" }

# design-doc.md section 4: online victory = floor(base_win x 1.25), draw = base_draw x 1.0,
# defeat = 0. The base table is the 1P-vs-Medium one (x1.0), so for 3x3: win 10, draw 5.
$ExpectedWin  = [Math]::Floor(10 * 1.25)   # 12
$ExpectedDraw = 5
$ExpectedLoss = 0

# design-doc.md section 4: "Tope por rival repetido = 3 victorias pagadas por rival cada 24 h".
$MaxPaidWinsPerRival = 3

# Scripted lines so every outcome is deterministic and the expected payout is knowable.
$WinAsX = @{ 'X' = @(@(0, 0), @(0, 1), @(0, 2)); 'O' = @(@(1, 0), @(1, 1)) }
$WinAsO = @{ 'X' = @(@(0, 0), @(0, 1), @(2, 2)); 'O' = @(@(1, 0), @(1, 1), @(1, 2)) }
# X O X / X O O / O X X - full board, no line for either side.
$DrawLine = @{ 'X' = @(@(0, 0), @(0, 2), @(1, 0), @(2, 1), @(2, 2)); 'O' = @(@(0, 1), @(1, 1), @(2, 0), @(1, 2)) }

$errors = @()
function Assert-That {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { $script:errors += $Message }
}

# --- one matched pair, from ticket to joined match ---------------------------

function New-QuickmatchPair {
    param([pscustomobject]$P1, [pscustomobject]$P2, [int]$Round)

    # board_size is the only rule source in Assets/Matchmaker/QuickmatchQueue.mmq (SameBoardSize).
    $P1.TicketId = New-MatchmakerTicket -Player $P1 -QueueName 'quickmatch-queue' -CustomData @{ board_size = $BoardSize }
    $P2.TicketId = New-MatchmakerTicket -Player $P2 -QueueName 'quickmatch-queue' -CustomData @{ board_size = $BoardSize }

    $h1 = Wait-Assignment -Player $P1
    $h2 = Wait-Assignment -Player $P2
    Assert-That ($h1 -eq $h2) "round ${Round}: both tickets must resolve to the same handoffId ($h1 vs $h2)"

    # First caller creates a 1-player "waiting" match; the second completes the roster. Both must
    # land on the same matchId - that idempotency on handoffId is risk #1 in the architecture doc.
    $first  = Invoke-CloudCode -Player $P1 -Function 'CreateMatch' -Params @{
        boardSize = $BoardSize; mode = 'online_quickmatch'; ticketId = $P1.TicketId; handoffId = $h1
    }
    $second = Invoke-CloudCode -Player $P2 -Function 'CreateMatch' -Params @{
        boardSize = $BoardSize; mode = 'online_quickmatch'; ticketId = $P2.TicketId; handoffId = $h2
    }

    if ($Round -eq 1) {
        Assert-That ([bool]$first.isWaitingForOpponent) "first CreateMatch must report isWaitingForOpponent (got $($first.isWaitingForOpponent))"
        Assert-That ($first.players.Count -eq 1) "first CreateMatch must return a 1-player roster (got $($first.players.Count))"
        Assert-That (-not $second.isWaitingForOpponent) "second CreateMatch must not report isWaitingForOpponent"
        Assert-That ($second.players.Count -eq 2) "second CreateMatch must complete the roster (got $($second.players.Count))"
        Assert-That ($first.matchId -eq $second.matchId) "both callers must join the same matchId ($($first.matchId) vs $($second.matchId))"

        # Idempotent retry after the roster is already complete (network blip on the client).
        $retry = Invoke-CloudCode -Player $P1 -Function 'CreateMatch' -Params @{
            boardSize = $BoardSize; mode = 'online_quickmatch'; ticketId = $P1.TicketId; handoffId = $h1
        }
        Assert-That ($retry.matchId -eq $second.matchId) "a retried CreateMatch must return the same match, not a new one"
        Assert-That ($retry.players.Count -eq 2) "a retried CreateMatch must not disturb the completed roster"
    }

    return $second.matchId
}

# --- run ---------------------------------------------------------------------

Write-Host "=== Quickmatch e2e | env=$Environment board=${BoardSize}x$BoardSize ==="
Write-Host ''

$p1 = New-AnonPlayer -Label 'P1'
$p2 = New-AnonPlayer -Label 'P2'
Write-Host ''

$config = Invoke-CloudCode -Player $p1 -Function 'GetGameConfig'
$board3 = $config.boardConfigs | Where-Object { $_.size -eq $BoardSize }
Write-Host "protocol version=$($config.version)  abandonTimeout=$($config.matchmakingConfig.abandonTimeoutMinutes)min  ticketTtl=$($config.matchmakingConfig.ticketTtlSeconds)s"
Assert-That ($board3.modes -contains 'online_quickmatch') "GetGameConfig must advertise online_quickmatch for ${BoardSize}x$BoardSize (got: $($board3.modes -join ', '))"
Write-Host ''

# Round 1 win, round 2 draw, rounds 3-5 wins. P1 wins four times against the same rival, so the
# fourth (round 5) must pay 0 - the draw does not consume a rival-win ordinal.
$plan = @(
    [pscustomobject]@{ Round = 1; Outcome = 'win';  ExpectP1 = $ExpectedWin;  ExpectP2 = $ExpectedLoss; Note = 'paid win 1/3' }
    [pscustomobject]@{ Round = 2; Outcome = 'draw'; ExpectP1 = $ExpectedDraw; ExpectP2 = $ExpectedDraw; Note = 'draw pays both, consumes no rival ordinal' }
    [pscustomobject]@{ Round = 3; Outcome = 'win';  ExpectP1 = $ExpectedWin;  ExpectP2 = $ExpectedLoss; Note = 'paid win 2/3' }
    [pscustomobject]@{ Round = 4; Outcome = 'win';  ExpectP1 = $ExpectedWin;  ExpectP2 = $ExpectedLoss; Note = 'paid win 3/3' }
    [pscustomobject]@{ Round = 5; Outcome = 'win';  ExpectP1 = 0;             ExpectP2 = $ExpectedLoss; Note = "win 4 vs same rival - capped at $MaxPaidWinsPerRival" }
)

$expectedP1Total = 0
$expectedP2Total = 0
$firstSymbols = $null

foreach ($step in $plan) {
    Write-Host "--- round $($step.Round) ($($step.Outcome)) : $($step.Note) ---"

    $matchId = New-QuickmatchPair -P1 $p1 -P2 $p2 -Round $step.Round
    $state = Invoke-CloudCode -Player $p1 -Function 'GetMatchState' -Params @{ matchId = $matchId }

    $bySymbol = @{}
    foreach ($entry in $state.players) {
        if ($entry.playerId -eq $p1.PlayerId) { $bySymbol[$entry.symbol] = $p1 } else { $bySymbol[$entry.symbol] = $p2 }
    }
    $p1Symbol = ($state.players | Where-Object { $_.playerId -eq $p1.PlayerId }).symbol
    $p2Symbol = ($state.players | Where-Object { $_.playerId -eq $p2.PlayerId }).symbol

    # X/O is not a coin flip: the two playerIds are sorted ordinally and the first becomes X.
    # CompareOrdinal, not Sort-Object - the server uses StringComparer.Ordinal and PowerShell's
    # default string comparison is culture-aware, which orders mixed case differently.
    $expectedXHolder = if ([string]::CompareOrdinal($p1.PlayerId, $p2.PlayerId) -lt 0) { 'P1' } else { 'P2' }
    $actualXHolder   = if ($p1Symbol -eq 'X') { 'P1' } else { 'P2' }
    Assert-That ($expectedXHolder -eq $actualXHolder) "round $($step.Round): X must go to the lexicographically first playerId ($expectedXHolder), went to $actualXHolder"

    if ($null -eq $firstSymbols) { $firstSymbols = "P1=$p1Symbol P2=$p2Symbol" }
    Assert-That ($firstSymbols -eq "P1=$p1Symbol P2=$p2Symbol") "round $($step.Round): symbols changed between matches ($firstSymbols -> P1=$p1Symbol P2=$p2Symbol); Quickmatch assignment is deterministic per pair"

    if ($step.Round -eq 1) {
        # Server-side move validation - the whole anti-cheat premise of an authoritative backend.
        $onTurn  = $bySymbol['X']
        $offTurn = $bySymbol['O']

        $rejected = Invoke-CloudCodeExpectingFailure -Player $offTurn -Function 'PlayMove' -Params @{ matchId = $matchId; row = 2; col = 2 }
        Assert-That ($null -ne $rejected) "an out-of-turn move must be rejected by the server"

        Invoke-CloudCode -Player $onTurn -Function 'PlayMove' -Params @{ matchId = $matchId; row = 2; col = 2 } | Out-Null
        $rejected = Invoke-CloudCodeExpectingFailure -Player $offTurn -Function 'PlayMove' -Params @{ matchId = $matchId; row = 2; col = 2 }
        Assert-That ($null -ne $rejected) "a move onto an occupied cell must be rejected by the server"

        $intruder = New-AnonPlayer -Label 'INTRUDER'
        $rejected = Invoke-CloudCodeExpectingFailure -Player $intruder -Function 'PlayMove' -Params @{ matchId = $matchId; row = 0; col = 0 }
        Assert-That ($null -ne $rejected) "a non-participant must not be able to move in someone else's match"

        # That probe consumed (2,2), so this round is played on a fresh pair instead of a
        # half-scripted board - the scripted lines all assume an empty 3x3.
        Write-Host "    move-validation probes done on a scratch match; re-matching for the scored round"
        $matchId = New-QuickmatchPair -P1 $p1 -P2 $p2 -Round $step.Round
        $state = Invoke-CloudCode -Player $p1 -Function 'GetMatchState' -Params @{ matchId = $matchId }
        $bySymbol = @{}
        foreach ($entry in $state.players) {
            if ($entry.playerId -eq $p1.PlayerId) { $bySymbol[$entry.symbol] = $p1 } else { $bySymbol[$entry.symbol] = $p2 }
        }
    }

    $line = if ($step.Outcome -eq 'draw') { $DrawLine } elseif ($p1Symbol -eq 'X') { $WinAsX } else { $WinAsO }
    $final = Invoke-ScriptedGame -MatchId $matchId -PlayersBySymbol $bySymbol -MovesBySymbol $line

    $expectedStatus = if ($step.Outcome -eq 'draw') { 'draw' } elseif ($p1Symbol -eq 'X') { 'x_won' } else { 'o_won' }
    Assert-That ($final.status -eq $expectedStatus) "round $($step.Round): expected status $expectedStatus, got $($final.status)"

    # Each player reads its OWN award - the DTO is projected per caller, never a shared dictionary.
    $p1View = Invoke-CloudCode -Player $p1 -Function 'GetMatchState' -Params @{ matchId = $matchId }
    $p2View = Invoke-CloudCode -Player $p2 -Function 'GetMatchState' -Params @{ matchId = $matchId }

    Write-Host ("    status={0}  P1({1}) earned {2}  P2({3}) earned {4}" -f `
        $final.status, $p1Symbol, $p1View.awardedSoftCurrency, $p2Symbol, $p2View.awardedSoftCurrency)

    Assert-That ($p1View.awardedSoftCurrency -eq $step.ExpectP1) "round $($step.Round): P1 earned $($p1View.awardedSoftCurrency), expected $($step.ExpectP1)"
    Assert-That ($p2View.awardedSoftCurrency -eq $step.ExpectP2) "round $($step.Round): P2 earned $($p2View.awardedSoftCurrency), expected $($step.ExpectP2)"

    # A later poll must report the same number, never re-award it.
    $rePoll = Invoke-CloudCode -Player $p1 -Function 'GetMatchState' -Params @{ matchId = $matchId }
    Assert-That ($rePoll.awardedSoftCurrency -eq $step.ExpectP1) "round $($step.Round): re-polling GetMatchState changed P1's award ($($step.ExpectP1) -> $($rePoll.awardedSoftCurrency))"

    $expectedP1Total += $step.ExpectP1
    $expectedP2Total += $step.ExpectP2
}

# --- did the coins actually land where the client reads them? ----------------

Write-Host ''
Write-Host "--- wallet (Cloud Save `currency`, read with each player's own token) ---"

$w1 = Get-PlayerDataItem -Player $p1 -Key 'currency'
$w2 = Get-PlayerDataItem -Player $p2 -Key 'currency'
$bal1 = $w1.value.balance
$bal2 = $w2.value.balance

Write-Host "P1 balance=$bal1 (expected $expectedP1Total)   P2 balance=$bal2 (expected $expectedP2Total)"
Assert-That ($bal1 -eq $expectedP1Total) "P1 wallet is $bal1, expected $expectedP1Total"
Assert-That ($bal2 -eq $expectedP2Total) "P2 wallet is $bal2, expected $expectedP2Total"

# Observation, not an assertion: the module credits `currency` through the Player Data API's
# default access class, which is the same key/class the client writes itself in
# Assets/Scripts/Game/Services/PlayerDataService.cs (SaveAfterMatchAsync). If that is really the
# default class, a player can set their own balance with their own token and every server-side
# reward rule above is decorative. This probe answers it rather than leaving it to inference.
Write-Host ''
Write-Host '--- probe: can a player overwrite their own wallet? ---'
$probe = New-AnonPlayer -Label 'PROBE'
$writeError = Set-PlayerDataItem -Player $probe -Key 'currency' -Value @{ balance = 999999; updatedAtUnixSeconds = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() }

if ($null -eq $writeError) {
    $after = Get-PlayerDataItem -Player $probe -Key 'currency'
    Write-Host "  ACCEPTED - player wrote balance=$($after.value.balance) directly with their own token"
} else {
    Write-Host "  REJECTED - $writeError"
}

# --- verdict -----------------------------------------------------------------

if ($errors.Count) {
    Write-Host ''
    $errors | ForEach-Object { Write-Host "FAIL  $_" }
    throw "$($errors.Count) assertion(s) failed"
}

Write-Host ''
Write-Host "PASS  matchmaking, join, move validation, reward table and rival cap all behaved"
