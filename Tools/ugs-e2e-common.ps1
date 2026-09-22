# Shared plumbing for the live UGS end-to-end harnesses (ranked-e2e.ps1, quickmatch-e2e.ps1).
#
# Everything here is mode-agnostic: anonymous sign-in, the Cloud Code call envelope, Matchmaker
# ticket creation, assignment polling, and driving a scripted board to a terminal status. Anything
# that knows what a series, an MMR or a reward is belongs in the calling harness, not here.
#
# Usage from a harness:
#     . "$PSScriptRoot\ugs-e2e-common.ps1"
#     Initialize-E2E -ProjectId $ProjectId -Environment $Environment -Module $Module
#
# Dot-sourcing puts these functions in the caller's script scope, which is also where
# Initialize-E2E's $script: variables land - so the two always agree without any global state.

$AuthUrl    = 'https://player-auth.services.api.unity.com/v1/authentication/anonymous'
$TicketsUrl = 'https://matchmaker.services.api.unity.com/v2/tickets'
$CloudSaveUrl = 'https://cloud-save.services.api.unity.com/v1/data/projects'

function Initialize-E2E {
    param(
        [Parameter(Mandatory)][string]$ProjectId,
        [Parameter(Mandatory)][string]$Environment,
        [Parameter(Mandatory)][string]$Module
    )

    $script:E2EProjectId    = $ProjectId
    $script:E2EEnvironment  = $Environment
    $script:E2ECloudCodeUrl = "https://cloud-code.services.api.unity.com/v1/projects/$ProjectId/modules/$Module"
}

function New-AnonPlayer {
    param([string]$Label)

    $res = Invoke-RestMethod -Method Post -Uri $AuthUrl -Headers @{
        'ProjectId'        = $script:E2EProjectId
        'UnityEnvironment' = $script:E2EEnvironment
        'Content-Type'     = 'application/json'
    } -Body '{}'

    Write-Host "[$Label] playerId=$($res.userId)"
    return [pscustomobject]@{ Label = $Label; PlayerId = $res.userId; Token = $res.idToken; TicketId = $null }
}

# PowerShell 5.1 swallows the response body on a non-2xx, and a Cloud Code 422 says nothing without
# it - the whole stack trace of the failing module function is in there.
function Get-HttpErrorBody {
    param($ErrorRecord)

    if ($ErrorRecord.ErrorDetails -and $ErrorRecord.ErrorDetails.Message) {
        return $ErrorRecord.ErrorDetails.Message
    }

    if ($ErrorRecord.Exception.Response) {
        $stream = $ErrorRecord.Exception.Response.GetResponseStream()
        $stream.Position = 0
        return (New-Object System.IO.StreamReader($stream)).ReadToEnd()
    }

    return ''
}

function Invoke-CloudCode {
    param([pscustomobject]$Player, [string]$Function, [hashtable]$Params = @{})

    # Module functions take their arguments under "params"; the result comes back under "output".
    $body = @{ params = $Params } | ConvertTo-Json -Depth 10 -Compress

    try {
        $res = Invoke-RestMethod -Method Post -Uri "$($script:E2ECloudCodeUrl)/$Function" -Headers @{
            'Authorization'    = "Bearer $($Player.Token)"
            'UnityEnvironment' = $script:E2EEnvironment
            'Content-Type'     = 'application/json'
        } -Body $body
        return $res.output
    }
    catch {
        throw "CloudCode $Function failed for $($Player.Label): $($_.Exception.Message)`n  body: $(Get-HttpErrorBody $_)"
    }
}

# For the calls a harness WANTS to fail (rejected moves, spoofed handoffs). Returns the server's
# error body on rejection, $null if the call unexpectedly succeeded - so the caller asserts on it
# instead of the run dying on an expected failure.
function Invoke-CloudCodeExpectingFailure {
    param([pscustomobject]$Player, [string]$Function, [hashtable]$Params = @{})

    $body = @{ params = $Params } | ConvertTo-Json -Depth 10 -Compress

    try {
        Invoke-RestMethod -Method Post -Uri "$($script:E2ECloudCodeUrl)/$Function" -Headers @{
            'Authorization'    = "Bearer $($Player.Token)"
            'UnityEnvironment' = $script:E2EEnvironment
            'Content-Type'     = 'application/json'
        } -Body $body | Out-Null
        return $null
    }
    catch {
        return Get-HttpErrorBody $_
    }
}

function New-MatchmakerTicket {
    param([pscustomobject]$Player, [string]$QueueName, [hashtable]$CustomData)

    $body = @{
        queueName = $QueueName
        players   = @(@{ id = $Player.PlayerId; customData = $CustomData })
    } | ConvertTo-Json -Depth 10 -Compress

    $res = Invoke-RestMethod -Method Post -Uri $TicketsUrl -Headers @{
        'Authorization'    = "Bearer $($Player.Token)"
        'UnityEnvironment' = $script:E2EEnvironment
        'Content-Type'     = 'application/json'
    } -Body $body

    return $res.id
}

function Wait-Assignment {
    param([pscustomobject]$Player, [int]$TimeoutSeconds = 120)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $res = Invoke-RestMethod -Method Get -Uri "$TicketsUrl/status?id=$($Player.TicketId)" -Headers @{
            'Authorization'    = "Bearer $($Player.Token)"
            'UnityEnvironment' = $script:E2EEnvironment
        }

        # The wire shape is flat - assignmentType/status/matchId at the top level. The SDK's
        # TicketStatusResponse.Value/Type oneOf wrapper is a client-side construct, not the payload.
        if ($res.status -eq 'Found') { return $res.matchId }
        if ($res.status -in @('Timeout', 'Failed')) {
            throw "[$($Player.Label)] matchmaking $($res.status): $($res.message)"
        }

        Start-Sleep -Milliseconds 1500
    }

    throw "[$($Player.Label)] matchmaking timed out after ${TimeoutSeconds}s"
}

# Drives one match to a terminal status from a scripted line, playing whichever symbol the server
# says is on turn. Returns the final MatchStateDto as seen by the player who made the last move.
function Invoke-ScriptedGame {
    param([string]$MatchId, [hashtable]$PlayersBySymbol, [hashtable]$MovesBySymbol)

    $cursor = @{ 'X' = 0; 'O' = 0 }
    $state = Invoke-CloudCode -Player $PlayersBySymbol['X'] -Function 'GetMatchState' -Params @{ matchId = $MatchId }

    while ($state.status -eq 'in_progress') {
        $symbol = $state.currentPlayer
        $moves  = $MovesBySymbol[$symbol]

        if ($cursor[$symbol] -ge $moves.Count) { throw "ran out of scripted moves for $symbol" }

        $cell = $moves[$cursor[$symbol]]
        $cursor[$symbol]++

        $state = Invoke-CloudCode -Player $PlayersBySymbol[$symbol] -Function 'PlayMove' -Params @{
            matchId = $MatchId; row = $cell[0]; col = $cell[1]
        }
    }

    return $state
}

# Reads one Cloud Save Player Data key with the player's OWN token (not the module's ServiceToken) -
# i.e. exactly what the game client can see, which is the point when verifying that a server-side
# credit actually landed somewhere the client will read.
function Get-PlayerDataItem {
    param([pscustomobject]$Player, [string]$Key)

    $uri = "$CloudSaveUrl/$($script:E2EProjectId)/players/$($Player.PlayerId)/items?keys=$Key"

    try {
        $res = Invoke-RestMethod -Method Get -Uri $uri -Headers @{
            'Authorization'    = "Bearer $($Player.Token)"
            'UnityEnvironment' = $script:E2EEnvironment
        }
        return ($res.results | Where-Object { $_.key -eq $Key })
    }
    catch {
        throw "CloudSave read of '$Key' failed for $($Player.Label): $($_.Exception.Message)`n  body: $(Get-HttpErrorBody $_)"
    }
}

# Companion of Get-PlayerDataItem for the write side. Returns $null on success, the error body on
# rejection - harnesses use it to assert what a player can and cannot overwrite in their own data.
function Set-PlayerDataItem {
    param([pscustomobject]$Player, [string]$Key, $Value)

    $uri = "$CloudSaveUrl/$($script:E2EProjectId)/players/$($Player.PlayerId)/items"
    $body = @{ key = $Key; value = $Value } | ConvertTo-Json -Depth 10 -Compress

    try {
        Invoke-RestMethod -Method Post -Uri $uri -Headers @{
            'Authorization'    = "Bearer $($Player.Token)"
            'UnityEnvironment' = $script:E2EEnvironment
            'Content-Type'     = 'application/json'
        } -Body $body | Out-Null
        return $null
    }
    catch {
        return Get-HttpErrorBody $_
    }
}
