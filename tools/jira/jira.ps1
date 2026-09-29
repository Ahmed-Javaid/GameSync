param(
    [Parameter(Mandatory, Position = 0)][ValidateSet('signin', 'GET', 'POST', 'PUT', 'DELETE')][string]$Method,
    [Parameter(Position = 1)][string]$Path,
    [Parameter(Position = 2)][string]$BodyFile,
    [string]$Site,
    [string]$Email,
    [string]$TokenFile
)

# Calls Jira's REST API as the owner, so Claude can keep GameSync's work in Jira. signin checks the site, the email of
# the Atlassian account and an API token together, then keeps them in notes\jira.json (git-ignored) with the token
# encrypted for this Windows user (DPAPI); nothing here prints the token. A token with scopes goes through
# api.atlassian.com and one without through the site itself; signin finds which. Every other call prints Jira's
# answer as JSON, or its status and message on stderr with exit code 1.
#   powershell -File tools\jira\jira.ps1 signin -Site yourname.atlassian.net -Email you@example.com -TokenFile notes\Jira_API_KEY.txt
#   powershell -File tools\jira\jira.ps1 signin -Site yourname.atlassian.net -Email you@example.com   (asks for the token, hidden)
#   powershell -File tools\jira\jira.ps1 GET rest/api/3/myself
#   powershell -File tools\jira\jira.ps1 POST rest/api/3/issue body.json   (the body is a UTF-8 JSON file)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Net.Http
$client = [Net.Http.HttpClient]::new()
$configPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\notes\jira.json'))

function Send([string]$Verb, [string]$Url, [string]$Auth, [string]$Json) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Verb), $Url)
    $request.Headers.Accept.ParseAdd('application/json')
    if ($Auth) { $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Basic', $Auth) }
    if ($Json) { $request.Content = [Net.Http.StringContent]::new($Json, [Text.Encoding]::UTF8, 'application/json') }
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
}

function Basic([string]$Who, [string]$Token) { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("${Who}:$Token")) }

function Fail([string]$Message) { [Console]::Error.WriteLine($Message); exit 1 }

if ($Method -eq 'signin') {
    if (-not $Site -or -not $Email) { Fail 'signin needs -Site (like yourname.atlassian.net) and -Email (the one you sign in to Atlassian with).' }
    $Site = $Site.Trim() -replace '^https?://', '' -replace '/.*$', ''
    $Email = $Email.Trim()
    if ($TokenFile) { $token = (Get-Content -LiteralPath $TokenFile -Raw).Trim() }
    else { $token = [Net.NetworkCredential]::new('', (Read-Host 'API token (it will not show)' -AsSecureString)).Password.Trim() }
    $auth = Basic $Email $token

    try { $tenant = Send 'GET' "https://$Site/_edge/tenant_info" }
    catch { Fail "Couldn't reach $Site. Check the address, like yourname.atlassian.net." }
    $cloudId = $null
    if ($tenant.Status -eq 200) { try { $cloudId = ($tenant.Body | ConvertFrom-Json).cloudId } catch { } }
    if (-not $cloudId) { Fail "$Site doesn't answer as an Atlassian site (HTTP $($tenant.Status)). Check the address." }

    # The project list needs only read:jira-work, so it shows which way in takes this token.
    $base = $null
    $refusals = @()
    foreach ($try in "https://api.atlassian.com/ex/jira/$cloudId", "https://$Site") {
        $projects = Send 'GET' "$try/rest/api/3/project/search?maxResults=100" $auth
        if ($projects.Status -eq 200) { $base = $try; break }
        $refusals += "HTTP $($projects.Status) from ${try}: $($projects.Body)"
    }
    if (-not $base) {
        if ($refusals -match 'scope does not match') {
            Fail ("The token has scopes, but not the ones needed. Make a new one for Jira with read:jira-work, write:jira-work and read:jira-user.`n" + ($refusals -join "`n"))
        }
        Fail ("Jira didn't take this email and token together. Check that the email is the Atlassian account the token was made in, and that the whole token was copied.`n" + ($refusals -join "`n"))
    }

    $me = Send 'GET' "$base/rest/api/3/myself" $auth
    $who = if ($me.Status -eq 200) { ($me.Body | ConvertFrom-Json).displayName } else { "$Email (its name needs read:jira-user, which assigning issues needs too)" }
    $saved = [ordered]@{ site = $Site; email = $Email; cloudId = $cloudId; base = $base; token = (ConvertTo-SecureString $token -AsPlainText -Force | ConvertFrom-SecureString) }
    New-Item -ItemType Directory -Force (Split-Path $configPath) | Out-Null
    [pscustomobject]$saved | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding UTF8
    $kind = if ($base -like 'https://api.atlassian.com/*') { 'a token with scopes' } else { 'a token without scopes' }
    "Signed in to $Site as $who, with $kind. Saved in $configPath."
    $list = @(($projects.Body | ConvertFrom-Json).values)
    if ($list.Count) { 'Projects: ' + (($list | ForEach-Object { "$($_.key) ($($_.name))" }) -join ', ') } else { 'No projects yet.' }
    exit 0
}

if (-not (Test-Path -LiteralPath $configPath)) { Fail 'Not signed in to Jira yet: run this with signin first.' }
if (-not $Path) { Fail 'Give the API path, like rest/api/3/myself.' }
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$auth = Basic $config.email ([Net.NetworkCredential]::new('', ($config.token | ConvertTo-SecureString)).Password)
$json = $null
if ($BodyFile) { $json = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $BodyFile).Path, [Text.Encoding]::UTF8) }
$result = Send $Method.ToUpperInvariant() "$($config.base)/$($Path.TrimStart('/'))" $auth $json
if ($result.Status -ge 200 -and $result.Status -lt 300) { $result.Body; exit 0 }
if ($result.Status -eq 401) { Fail "HTTP 401: Jira no longer takes the saved token; it may have run out. Make a new one and sign in again.`n$($result.Body)" }
Fail "HTTP $($result.Status): $($result.Body)"
