param([Parameter(Mandatory)][string]$Spec)

# Keeps the Jira board in step from a UTF-8 JSON file (this script stays ASCII: PowerShell 5.1 reads a BOM-less script as
# ANSI). Stops at the first refusal. The file: { "create": [ { "summary", "desc": [paragraphs, `code` in backticks],
# "epic": "KAN-8", "owner": true, "type": "epic" } ], "move": [ { "key", "to": "Test", "owner": true, "comment": [...] } ] }.
# "owner" assigns it to the owner; a move to the status it is in only assigns and comments.
#   powershell -File tools\jira\board.ps1 -Spec <file.json>

$ErrorActionPreference = 'Stop'
$jira = Join-Path $PSScriptRoot 'jira.ps1'
$work = [IO.Path]::GetTempPath()
$ops = [IO.File]::ReadAllText($Spec, [Text.Encoding]::UTF8) | ConvertFrom-Json

function Call([string]$Method, [string]$Path, $Body) {
    $file = $null
    if ($null -ne $Body) {
        $file = Join-Path $work ('body-' + [guid]::NewGuid().ToString('N') + '.json')
        [IO.File]::WriteAllText($file, (ConvertTo-Json -InputObject $Body -Depth 30 -Compress), (New-Object Text.UTF8Encoding $false))
    }
    $out = if ($file) { & $jira $Method $Path $file } else { & $jira $Method $Path }
    if ($LASTEXITCODE -ne 0) { throw "Jira refused $Method $Path" }
    if ($file) { Remove-Item -LiteralPath $file }
    $text = ($out | Out-String).Trim()
    if ($text) { $text | ConvertFrom-Json }
}

# The owner is the account jira.ps1 is signed in as, asked once a run, so no account ID lives in the repository.
$script:ownerId = $null
function Owner {
    if (-not $script:ownerId) { $script:ownerId = (Call 'GET' 'rest/api/3/myself').accountId }
    $script:ownerId
}

function Doc($Paragraphs) {
    $content = New-Object System.Collections.ArrayList
    foreach ($p in $Paragraphs) {
        $parts = $p -split '`'
        $nodes = New-Object System.Collections.ArrayList
        for ($i = 0; $i -lt $parts.Count; $i++) {
            if ($parts[$i] -eq '') { continue }
            $node = [ordered]@{ type = 'text'; text = $parts[$i] }
            if ($i % 2 -eq 1) { $node['marks'] = @(@{ type = 'code' }) }
            [void]$nodes.Add($node)
        }
        [void]$content.Add([ordered]@{ type = 'paragraph'; content = $nodes.ToArray() })
    }
    [ordered]@{ type = 'doc'; version = 1; content = $content.ToArray() }
}

foreach ($c in @($ops.create)) {
    if (-not $c) { continue }
    $fields = [ordered]@{ project = @{ key = 'KAN' }; issuetype = @{ id = $(if ($c.type -eq 'epic') { '10005' } else { '10007' }) }; summary = $c.summary; description = (Doc $c.desc) }
    if ($c.epic) { $fields['parent'] = @{ key = $c.epic } }
    if ($c.owner) { $fields['assignee'] = @{ accountId = (Owner) } }
    $made = Call 'POST' 'rest/api/3/issue' @{ fields = $fields }
    Write-Host "created  $($made.key)  $($c.summary)"
}

foreach ($m in @($ops.move)) {
    if (-not $m) { continue }
    $now = (Call 'GET' "rest/api/3/issue/$($m.key)?fields=status").fields.status.name
    if ($now -ne $m.to) {
        $moves = (Call 'GET' "rest/api/3/issue/$($m.key)/transitions").transitions
        $move = $moves | Where-Object { $_.to.name -eq $m.to } | Select-Object -First 1
        if (-not $move) { throw "$($m.key) can't move to $($m.to)" }
        Call 'POST' "rest/api/3/issue/$($m.key)/transitions" @{ transition = @{ id = $move.id } } | Out-Null
    }
    if ($m.owner) { Call 'PUT' "rest/api/3/issue/$($m.key)/assignee" @{ accountId = (Owner) } | Out-Null }
    if ($m.comment) { Call 'POST' "rest/api/3/issue/$($m.key)/comment" @{ body = (Doc $m.comment) } | Out-Null }
    Write-Host "moved    $($m.key)  -> $($m.to)$(if ($m.owner) { ', assigned to the owner' })$(if ($m.comment) { ', with a comment' })"
}
