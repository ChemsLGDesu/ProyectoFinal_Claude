# Ranked end-to-end validation against a live UGS environment.
#
# WHY THIS EXISTS
#   The Ranked rules live entirely in Cloud Code, so a UI test cannot assert that MMR landed on an
#   exact number, and reproducing a match by hand needs two devices. This drives the real service:
#   two anonymous players, real ranked-queue tickets, and the actual Cloud Code functions.
#
#   Its first run found two stacked production blockers that reflection and documentation had both
#   missed - ticket verification came back Forbidden, and once that was fixed the SDK could not
#   deserialize its own response. See CloudCode~/TicTacToeModule/README.md "Verificación de ticket
#   de Matchmaker".
#
# WHAT IT DOES NOT COVER
#   The client. Wire push, the series UI, the matchmaking screen and its fallback are untouched here
#   and still need a manual pass with two builds. Decay and season rollover are time-dependent and
#   are not exercised either.
#
# SAFETY
#   Creates real anonymous players, matches and leaderboard entries in whatever environment it points
#   at. Keep it on `development`.
#
# Usage:  powershell -File Tools\ranked-e2e.ps1 [-Series 5]
#
#   Sign-in, the Cloud Code envelope, ticket creation and the scripted-game driver are shared with
#   Tools\quickmatch-e2e.ps1 and live in Tools\ugs-e2e-common.ps1.

[CmdletBinding()]
param(
    [string]$ProjectId   = '6edc8de2-6599-4f88-bbe1-e9228d6b19e8',
    [string]$Environment = 'development',
    [string]$Module      = 'TicTacToeModule',
    [int]   $BoardSize   = 3,
    [int]   $Series      = 5
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\ugs-e2e-common.ps1"
Initialize-E2E -ProjectId $ProjectId -Environment $Environment -Module $Module

# Scripted lines so every outcome is deterministic and the expected MMR delta is knowable.
# Winner-is-X: X takes the top row. Winner-is-O: O takes the middle row while X is sent to a corner.
$WinAsX = @{ 'X' = @(@(0, 0), @(0, 1), @(0, 2)); 'O' = @(@(1, 0), @(1, 1)) }
$WinAsO = @{ 'X' = @(@(0, 0), @(0, 1), @(2, 2)); 'O' = @(@(1, 0), @(1, 1), @(1, 2)) }

function Get-BoardProfile {
    param([pscustomobject]$Player)

    $prof = Invoke-CloudCode -Player $Player -Function 'GetRankedProfile'
    return $prof.boards | Where-Object { $_.boardSize -eq $BoardSize }
}

function New-RankedTicket {
    param([pscustomobject]$Player, [int]$Mmr)

    # board_size and mmr are the two rule sources in Assets/Matchmaker/RankedQueue.mmq.
    return New-MatchmakerTicket -Player $Player -QueueName 'ranked-queue' -CustomData @{ board_size = $BoardSize; mmr = $Mmr }
}

function Invoke-Series {
    param([pscustomobject]$Winner, [pscustomobject]$Loser, [int]$Index)

    $wBefore = Get-BoardProfile -Player $Winner
    $lBefore = Get-BoardProfile -Player $Loser

    $Winner.TicketId = New-RankedTicket -Player $Winner -Mmr $wBefore.mmr
    $Loser.TicketId  = New-RankedTicket -Player $Loser  -Mmr $lBefore.mmr

    $hw = Wait-Assignment -Player $Winner
    $hl = Wait-Assignment -Player $Loser
    if ($hw -ne $hl) { throw "series ${Index}: handoff mismatch ($hw vs $hl)" }

    $mw = Invoke-CloudCode -Player $Winner -Function 'CreateMatch' -Params @{
        boardSize = $BoardSize; mode = 'ranked'; ticketId = $Winner.TicketId; handoffId = $hw
    }
    $ml = Invoke-CloudCode -Player $Loser -Function 'CreateMatch' -Params @{
        boardSize = $BoardSize; mode = 'ranked'; ticketId = $Loser.TicketId; handoffId = $hl
    }

    $match = if ($ml.matchId) { $ml } else { $mw }
    if (-not $match.matchId) { throw "series ${Index}: neither CreateMatch returned a joined match" }

    $result = $null
    $matchId = $match.matchId
    $game = 1

    while ($matchId) {
        $state = Invoke-CloudCode -Player $Winner -Function 'GetMatchState' -Params @{ matchId = $matchId }

        $bySymbol = @{}
        foreach ($entry in $state.players) {
            $bySymbol[$entry.symbol] = if ($entry.playerId -eq $Winner.PlayerId) { $Winner } else { $Loser }
        }

        # Roles swap between games (design-doc 6.1), so re-read the symbol every game instead of
        # assuming - that swap is the whole point of the 3x3 series.
        $winnerSymbol = ($state.players | Where-Object { $_.playerId -eq $Winner.PlayerId }).symbol
        $plan = if ($winnerSymbol -eq 'X') { $WinAsX } else { $WinAsO }

        $final = Invoke-ScriptedGame -MatchId $matchId -PlayersBySymbol $bySymbol -MovesBySymbol $plan
        Write-Host ("    game {0}: {1} as {2} -> {3}" -f $game, $Winner.Label, $winnerSymbol, $final.status)

        if ($final.rankedMmrResult) { $result = $final.rankedMmrResult }
        $matchId = $final.rankedNextMatchId
        $game++

        if ($game -gt 4) { throw "series ${Index}: game chain did not terminate" }
    }

    $wAfter = Get-BoardProfile -Player $Winner
    $lAfter = Get-BoardProfile -Player $Loser

    return [pscustomobject]@{
        Index       = $Index
        WinnerFrom  = $wBefore.mmr
        WinnerTo    = $wAfter.mmr
        LoserFrom   = $lBefore.mmr
        LoserTo     = $lAfter.mmr
        WinnerGain  = $wAfter.mmr - $wBefore.mmr
        LoserDrop   = $lBefore.mmr - $lAfter.mmr
        Placements  = $wAfter.placementsPlayed
        MmrResult   = $result
    }
}

# --- run --------------------------------------------------------------------

Write-Host "=== Ranked e2e | env=$Environment board=${BoardSize}x$BoardSize series=$Series ==="

$p1 = New-AnonPlayer -Label 'P1'
$p2 = New-AnonPlayer -Label 'P2'

$start = Get-BoardProfile -Player $p1
Write-Host "placement required: $($start.placementMatchesRequired)"
Write-Host ''

$rows = @()
$unplacedProbe = $null

for ($i = 1; $i -le $Series; $i++) {
    Write-Host "--- series $i ---"
    $r = Invoke-Series -Winner $p1 -Loser $p2 -Index $i
    $rows += $r
    Write-Host ("    P1 {0} -> {1} (+{2})   P2 {3} -> {4} (-{5})   placements={6}" -f `
        $r.WinnerFrom, $r.WinnerTo, $r.WinnerGain, $r.LoserFrom, $r.LoserTo, $r.LoserDrop, $r.Placements)

    if ($i -eq 1) {
        # design-doc 6.6 elegibilidad: "un jugador aparece en el leaderboard solo tras completar sus
        # partidas de colocacion... Evita que el #1 de la semana 1 sea alguien con 1 partida jugada."
        # After exactly one series P1 has 1/5 and must NOT be visible.
        $probe = Invoke-CloudCode -Player $p1 -Function 'GetRankedLeaderboard' -Params @{ boardSize = $BoardSize }
        $prof  = Get-BoardProfile -Player $p1
        $unplacedProbe = [pscustomobject]@{
            IsPlaced    = $prof.isPlaced
            Placements  = $prof.placementsPlayed
            InTop       = (@($probe.top | ForEach-Object { $_.playerId }) -contains $p1.PlayerId)
            HasOwnEntry = [bool]$probe.hasOwnEntry
        }
        Write-Host ("    eligibility probe: isPlaced={0} ({1}/{2})  inTop={3}  hasOwnEntry={4}" -f `
            $unplacedProbe.IsPlaced, $unplacedProbe.Placements, $prof.placementMatchesRequired,
            $unplacedProbe.InTop, $unplacedProbe.HasOwnEntry)
    }
}

$after1 = Get-BoardProfile -Player $p1
$after2 = Get-BoardProfile -Player $p2

Write-Host ''
Write-Host "P1  mmr=$($after1.mmr)  placements=$($after1.placementsPlayed)  isPlaced=$($after1.isPlaced)  tier=$($after1.tier)"
Write-Host "P2  mmr=$($after2.mmr)  placements=$($after2.placementsPlayed)  isPlaced=$($after2.isPlaced)  tier=$($after2.tier)"

# design-doc 6.3: repeated wins against the same rival inside 24h are damped 1.0/1.0/0.5/0.5/0.0,
# while losses always take full K. Five straight wins over one opponent is exactly that curve.
Write-Host ''
Write-Host "anti-boosting curve (winner gain per series): $(($rows | ForEach-Object { $_.WinnerGain }) -join ', ')"
Write-Host "loser drop per series:                        $(($rows | ForEach-Object { $_.LoserDrop }) -join ', ')"

$board = Invoke-CloudCode -Player $p1 -Function 'GetRankedLeaderboard' -Params @{ boardSize = $BoardSize }
Write-Host ''
Write-Host "--- leaderboard (top $($board.entries.Count)) ---"
$board | ConvertTo-Json -Depth 8 -Compress | Write-Host

# --- assertions --------------------------------------------------------------

$errors = @()
$shouldBePlaced = $Series -ge $after1.placementMatchesRequired

if ($after1.placementsPlayed -ne $Series) { $errors += "P1 placementsPlayed=$($after1.placementsPlayed), expected $Series" }
if ($after2.placementsPlayed -ne $Series) { $errors += "P2 placementsPlayed=$($after2.placementsPlayed), expected $Series" }
if ($after1.isPlaced -ne $shouldBePlaced) { $errors += "P1 isPlaced=$($after1.isPlaced) after $Series series, expected $shouldBePlaced" }
if ($after2.isPlaced -ne $shouldBePlaced) { $errors += "P2 isPlaced=$($after2.isPlaced) after $Series series, expected $shouldBePlaced" }
if ($after1.mmr -le $after2.mmr) { $errors += "winner ($($after1.mmr)) is not above loser ($($after2.mmr))" }

# repeatRivalDamping only reaches x0.0 on the fifth win, so these only mean something at -Series 5+.
if ($Series -ge 5) {
    if ($rows[4].WinnerGain -ne 0) {
        $errors += "5th win against the same rival gained $($rows[4].WinnerGain) MMR, expected 0 (damping x0.0)"
    }
    if ($rows[4].LoserDrop -le 0) {
        $errors += "loser did not take a full-K loss on series 5 (dropped $($rows[4].LoserDrop)); losses are never damped"
    }
}

$ids = @($board.top | ForEach-Object { $_.playerId })
if ($shouldBePlaced) {
    if ($ids -notcontains $p1.PlayerId) { $errors += "P1 is placed but absent from the leaderboard" }
    if ($ids -notcontains $p2.PlayerId) { $errors += "P2 is placed but absent from the leaderboard" }
    if (-not $board.hasOwnEntry) { $errors += "P1 is placed but hasOwnEntry is false" }
}

# design-doc 6.6 elegibilidad, enforced by the placement check in RankedMatchSupport - the only place
# a score is ever submitted. Until 2026-08-08 nothing gated it and a player at 1/5 was already ranked.
if ($unplacedProbe -and -not $unplacedProbe.IsPlaced) {
    if ($unplacedProbe.InTop) {
        $errors += "design-doc 6.6: an unplaced player ($($unplacedProbe.Placements)/5) appears in the leaderboard top"
    }
    if ($unplacedProbe.HasOwnEntry) {
        $errors += "design-doc 6.6: an unplaced player ($($unplacedProbe.Placements)/5) has hasOwnEntry = true"
    }
}

if ($errors.Count) {
    Write-Host ''
    $errors | ForEach-Object { Write-Host "FAIL  $_" }
    throw "$($errors.Count) assertion(s) failed"
}

Write-Host ''
if ($shouldBePlaced) {
    Write-Host "PASS  both players placed, ranked, and on the leaderboard"
} else {
    # A short probing run ($Series below placementMatchesRequired) asserts the opposite: still
    # unplaced, and therefore still absent from the leaderboard.
    Write-Host "PASS  $Series series settled; both players still unplaced and correctly off the leaderboard"
}
