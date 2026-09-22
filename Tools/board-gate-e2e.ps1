# Board availability gate end-to-end validation against a live UGS environment.
#
# WHY THIS EXISTS
#   The 9x9/11x11 launch cut lives in two independent places: BOARD_CONFIGS decides what the client
#   offers, and CloudCode~/TicTacToeModule/BoardAvailability.cs decides what the server accepts. Only
#   the second one survives a modified client, and none of it is observable from the game - a cut
#   board simply stops appearing, which looks identical whether the server enforces anything or not.
#   The first version of this cut was client-side only and the whole EditMode suite stayed green.
#
# WHAT IT COVERS
#   That CreateMatch rejects a board size the deployed BOARD_CONFIGS does not list for the requested
#   mode, on the local and Ranked paths alike - Ranked being the one that never checked the size at
#   all before, and would have created a ranked match on a board with no deployed leaderboard.
#
#   The bogus-mode probe is the one worth understanding. BoardAvailability falls back to a compiled
#   floor of {3, 6} when BOARD_CONFIGS cannot be read, so a gate that read nothing at all still
#   rejects 9x9 and 11x11 and still accepts 3x3 - it passes every obvious probe while enforcing
#   nothing that the config says. Asking for size 3 (which the floor allows) in a mode nothing lists
#   is the only cheap way to tell the two apart: only the per-mode check against a config that was
#   actually read can reject it. Without that probe this script would be self-congratulatory.
#
#   The positive control matters for the opposite reason: a gate that rejected everything would pass
#   all three rejection probes and take the game down.
#
# WHAT IT DOES NOT COVER
#   Local 1P/2P play, which never calls CreateMatch - it runs entirely client-side by Milestone 1
#   design, so a modified client can still play a cut board offline and be paid by the same
#   client-writable currency key ERR-KB-006 describes. Closing that means moving local match
#   resolution server-side, which is a milestone rather than a fix.
#
#   The client half of the cut either: that BOARD_CONFIGS and the offline fallback agree, and that
#   Board Select filters by them, are covered by GameConfigServiceTests and
#   BoardSelectEligibilityTests in EditMode.
#
#   Rewards, matchmaking and move legality - see quickmatch-e2e.ps1 and ranked-e2e.ps1.
#
# WHEN A BOARD COMES BACK
#   Update -CutSizes to whatever is still cut. If nothing is, the rejection probes have nothing left
#   to assert and only the bogus-mode probe still means anything - keep that one.
#
# SAFETY
#   Creates one real anonymous player and one real 3x3 match record in whatever environment it points
#   at. Keep it on `development`.
#
# Usage:  powershell -File Tools\board-gate-e2e.ps1

[CmdletBinding()]
param(
    [string]$ProjectId   = '6edc8de2-6599-4f88-bbe1-e9228d6b19e8',
    [string]$Environment = 'development',
    [string]$Module      = 'TicTacToeModule',

    # Sizes expected to be cut from BOARD_CONFIGS. Probed against a local mode and against Ranked.
    [int[]] $CutSizes    = @(9, 11),

    # A size the config is expected to offer, used for the bogus-mode and positive-control probes.
    [int]   $ShippedSize = 3
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\ugs-e2e-common.ps1"
Initialize-E2E -ProjectId $ProjectId -Environment $Environment -Module $Module

$player = New-AnonPlayer -Label 'gate-probe'
Write-Host ''

$failures = @()

function Assert-Rejected {
    param([string]$Name, [int]$BoardSize, [string]$Mode)

    $err = Invoke-CloudCodeExpectingFailure -Player $player -Function 'CreateMatch' `
        -Params @{ boardSize = $BoardSize; mode = $Mode }

    if ($null -eq $err) {
        $script:failures += "$Name : ACCEPTED (expected rejection)"
        Write-Host "  FAIL  $Name -> accepted"
        return
    }

    $oneLine = ($err -replace '\s+', ' ')

    # Matching the gate's own wording on purpose: a rejection for some unrelated reason (bad
    # argument, auth, a throw further down) would otherwise read as the gate working.
    if ($oneLine -match 'not available') {
        Write-Host "  ok    $Name -> rejected by the gate"
    }
    else {
        $script:failures += "$Name : rejected, but not by the gate -> $oneLine"
        Write-Host "  ?     $Name -> rejected for another reason: $oneLine"
    }
}

Write-Host 'Probes that must be REJECTED:'
foreach ($size in $CutSizes) {
    Assert-Rejected -Name "${size}x${size} single_player (cut from BOARD_CONFIGS)" -BoardSize $size -Mode 'single_player'
    Assert-Rejected -Name "${size}x${size} ranked        (no leaderboard exists)"  -BoardSize $size -Mode 'ranked'
}

Assert-Rejected -Name "${ShippedSize}x${ShippedSize} bogus_mode    (proves the config was read)" `
    -BoardSize $ShippedSize -Mode 'definitely_not_a_mode'

Write-Host ''
Write-Host 'Probe that must be ACCEPTED (a gate rejecting everything would be worse than none):'
try {
    $ok = Invoke-CloudCode -Player $player -Function 'CreateMatch' `
        -Params @{ boardSize = $ShippedSize; mode = 'single_player' }

    if ($ok -and $ok.matchId) {
        Write-Host "  ok    ${ShippedSize}x${ShippedSize} single_player -> created matchId=$($ok.matchId)"
    }
    else {
        $failures += "${ShippedSize}x${ShippedSize} single_player : succeeded but returned no matchId"
        Write-Host "  FAIL  ${ShippedSize}x${ShippedSize} single_player -> no matchId in the response"
    }
}
catch {
    $failures += "${ShippedSize}x${ShippedSize} single_player : REJECTED (expected success) -> $($_.Exception.Message)"
    Write-Host "  FAIL  ${ShippedSize}x${ShippedSize} single_player -> rejected: $($_.Exception.Message)"
}

Write-Host ''
if ($failures.Count -eq 0) {
    Write-Host 'ALL PROBES PASSED'
}
else {
    Write-Host "FAILURES ($($failures.Count)):"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}
