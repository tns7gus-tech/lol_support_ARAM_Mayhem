[CmdletBinding()]
param(
    [string]$Locale = "ko",
    [string]$Mode = "aram-mayhem",
    [string[]]$ChampionSlugs = @(),
    [string]$OutputPath = "data/opgg_aram_mayhem_builds.raw.json",
    [int]$DelayMs = 250,
    [int]$TimeoutSec = 30,
    [int]$Limit = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-ChampionSlugsFromDataDragon {
    $versionsUrl = "https://ddragon.leagueoflegends.com/api/versions.json"
    $versions = Invoke-RestMethod -Uri $versionsUrl -TimeoutSec 20
    $version = $versions[0]
    $championsUrl = "https://ddragon.leagueoflegends.com/cdn/$version/data/en_US/champion.json"
    $champions = Invoke-RestMethod -Uri $championsUrl -TimeoutSec 20
    return @(
        $champions.data.PSObject.Properties.Value |
        ForEach-Object { $_.id.ToLowerInvariant() } |
        Sort-Object -Unique
    )
}

function Convert-ClassToTier {
    param([string]$ClassName)

    if ($ClassName -match "text-red") { return "S" }
    if ($ClassName -match "text-orange") { return "A" }
    if ($ClassName -match "text-yellow") { return "B" }
    if ($ClassName -match "text-gray") { return "C" }
    return "UNKNOWN"
}

function Get-AugmentsFromBuildPage {
    param(
        [string]$Html,
        [string]$Locale,
        [string]$Mode,
        [string]$Slug
    )

    $sectionPattern = '(?s)<a href="/{0}/lol/modes/{1}/{2}/augments">.*?</a>(?<section>.*?)</section>' -f `
        [regex]::Escape($Locale),
        [regex]::Escape($Mode),
        [regex]::Escape($Slug)

    $sectionMatch = [regex]::Match($Html, $sectionPattern)
    if (-not $sectionMatch.Success) {
        return @()
    }

    $itemPattern = '(?s)<li[^>]*>.*?<img alt="(?<name>[^"]+)".*?<svg[^>]*class="(?<cls>[^"]*)"'
    $matches = [regex]::Matches($sectionMatch.Groups["section"].Value, $itemPattern)

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($m in $matches) {
        $name = [System.Net.WebUtility]::HtmlDecode($m.Groups["name"].Value.Trim())
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        if (-not $seen.Add($name)) { continue }

        $className = $m.Groups["cls"].Value
        $rows.Add([PSCustomObject]@{
            name = $name
            tier = (Convert-ClassToTier -ClassName $className)
            className = $className
        })
    }

    return $rows.ToArray()
}

function Get-ItemRowsFromBuildPage {
    param(
        [string]$Html,
        [string]$Prefix
    )

    $prefixEscaped = [regex]::Escape($Prefix)
    $rowPattern = '(?s)\\\"(?<row>{0}\d+)\\\"(?<body>.*?)(?=\\\"(?:starter_items_|boots_|core_items_)\d+\\\"|</script>)' -f $prefixEscaped
    $rowMatches = [regex]::Matches($Html, $rowPattern)

    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($rowMatch in $rowMatches) {
        $itemMatches = [regex]::Matches(
            $rowMatch.Groups["body"].Value,
            'metaId\\\":(?<id>\d+).*?alt\\\":\\\"(?<name>[^\\"]+)\\\"'
        )

        $items = New-Object System.Collections.Generic.List[object]
        $seenIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

        foreach ($item in $itemMatches) {
            $id = $item.Groups["id"].Value
            if (-not $seenIds.Add($id)) { continue }

            $name = [System.Net.WebUtility]::HtmlDecode($item.Groups["name"].Value.Trim())
            $items.Add([PSCustomObject]@{
                itemId = [int]$id
                name = $name
            })
        }

        $rows.Add([PSCustomObject]@{
            key = $rowMatch.Groups["row"].Value
            items = $items.ToArray()
        })
    }

    return $rows.ToArray()
}

if ($ChampionSlugs.Count -eq 0) {
    Write-Host "Loading champion slugs from Data Dragon..."
    $ChampionSlugs = Get-ChampionSlugsFromDataDragon
}

if ($Limit -gt 0) {
    $ChampionSlugs = @($ChampionSlugs | Select-Object -First $Limit)
}

Write-Host "Target champions: $($ChampionSlugs.Count)"

$results = New-Object System.Collections.Generic.List[object]
$ok = 0
$fail = 0

for ($i = 0; $i -lt $ChampionSlugs.Count; $i++) {
    $slug = $ChampionSlugs[$i]
    $url = "https://op.gg/$Locale/lol/modes/$Mode/$slug/build"
    Write-Host ("[{0}/{1}] {2}" -f ($i + 1), $ChampionSlugs.Count, $slug)

    try {
        $html = (Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec $TimeoutSec).Content

        $augments = Get-AugmentsFromBuildPage -Html $html -Locale $Locale -Mode $Mode -Slug $slug
        $starterRows = Get-ItemRowsFromBuildPage -Html $html -Prefix "starter_items_"
        $bootsRows = Get-ItemRowsFromBuildPage -Html $html -Prefix "boots_"
        $coreRows = Get-ItemRowsFromBuildPage -Html $html -Prefix "core_items_"

        $results.Add([PSCustomObject]@{
            championSlug = $slug
            url = $url
            fetchedAt = [DateTimeOffset]::UtcNow.ToString("o")
            status = "ok"
            augmentRows = $augments
            itemRows = [PSCustomObject]@{
                starter = $starterRows
                boots = $bootsRows
                core = $coreRows
            }
        })

        $ok++
    }
    catch {
        $results.Add([PSCustomObject]@{
            championSlug = $slug
            url = $url
            fetchedAt = [DateTimeOffset]::UtcNow.ToString("o")
            status = "error"
            error = $_.Exception.Message
            augmentRows = @()
            itemRows = [PSCustomObject]@{
                starter = @()
                boots = @()
                core = @()
            }
        })

        $fail++
    }

    if ($DelayMs -gt 0) {
        Start-Sleep -Milliseconds $DelayMs
    }
}

$payload = [PSCustomObject]@{
    meta = [PSCustomObject]@{
        source = "op.gg"
        locale = $Locale
        mode = $Mode
        generatedAt = [DateTimeOffset]::UtcNow.ToString("o")
        totalRequested = $ChampionSlugs.Count
        success = $ok
        failed = $fail
    }
    champions = $results.ToArray()
}

$dir = Split-Path -Path $OutputPath -Parent
if (-not [string]::IsNullOrWhiteSpace($dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

$json = $payload | ConvertTo-Json -Depth 20
Set-Content -Path $OutputPath -Value $json -Encoding UTF8

Write-Host ""
Write-Host "Done."
Write-Host "Output: $OutputPath"
Write-Host "Success: $ok / Failed: $fail"
