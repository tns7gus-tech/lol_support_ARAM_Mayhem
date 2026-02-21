[CmdletBinding()]
param(
    [string]$Locale = "ko",
    [string]$Mode = "aram-mayhem",
    [string]$KnowledgeBasePath = "data/knowledge_base.json",
    [string]$OutputPath = "data/knowledge_base.json",
    [string[]]$ChampionSlugs = @(),
    [int]$DelayMs = 250,
    [int]$TimeoutSec = 30,
    [int]$Limit = 0,
    [switch]$UpdateDistCopy,
    [switch]$UpdateDebugCopy,
    [switch]$UpdateTestsDebugCopy
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Set-ObjectProperty {
    param(
        [Parameter(Mandatory = $true)] [object]$Object,
        [Parameter(Mandatory = $true)] [string]$Name,
        [Parameter(Mandatory = $false)] [object]$Value
    )

    $prop = $Object.PSObject.Properties[$Name]
    if ($null -ne $prop) {
        $prop.Value = $Value
    }
    else {
        $Object | Add-Member -MemberType NoteProperty -Name $Name -Value $Value
    }
}

function Ensure-ObjectProperty {
    param(
        [Parameter(Mandatory = $true)] [object]$Object,
        [Parameter(Mandatory = $true)] [string]$Name,
        [Parameter(Mandatory = $true)] [scriptblock]$Factory
    )

    $prop = $Object.PSObject.Properties[$Name]
    if ($null -eq $prop) {
        $value = & $Factory
        $Object | Add-Member -MemberType NoteProperty -Name $Name -Value $value
        return $value
    }

    if ($null -eq $prop.Value) {
        $value = & $Factory
        $prop.Value = $value
        return $value
    }

    return $prop.Value
}

function Normalize-Name {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) { return "" }

    $normalized = $Name.Trim().ToLowerInvariant()
    $normalized = $normalized -replace "\s+", ""
    $normalized = $normalized -replace "[^\p{L}\p{Nd}]", ""
    return $normalized
}

function Convert-ClassToTier {
    param([string]$ClassName)

    if ($ClassName -match "text-red") { return "S" }
    if ($ClassName -match "text-orange") { return "A" }
    if ($ClassName -match "text-yellow") { return "B" }
    if ($ClassName -match "text-gray") { return "C" }
    return "UNKNOWN"
}

function Get-TierRank {
    param([string]$Tier)

    switch ($Tier) {
        "S" { return 4 }
        "A" { return 3 }
        "B" { return 2 }
        "C" { return 1 }
        default { return 0 }
    }
}

function Download-Utf8Text {
    param(
        [Parameter(Mandatory = $true)] [string]$Url,
        [int]$TimeoutSec = 30
    )

    $wc = New-Object System.Net.WebClient
    $wc.Encoding = [System.Text.Encoding]::UTF8
    try {
        return $wc.DownloadString($Url)
    }
    finally {
        $wc.Dispose()
    }
}

function Get-ChampionMetaFromDataDragon {
    param([int]$TimeoutSec = 30)

    $versions = Invoke-RestMethod -Uri "https://ddragon.leagueoflegends.com/api/versions.json" -TimeoutSec $TimeoutSec
    $version = $versions[0]

    $enUrl = "https://ddragon.leagueoflegends.com/cdn/$version/data/en_US/champion.json"
    $koUrl = "https://ddragon.leagueoflegends.com/cdn/$version/data/ko_KR/champion.json"

    $enData = Invoke-RestMethod -Uri $enUrl -TimeoutSec $TimeoutSec
    $koData = Invoke-RestMethod -Uri $koUrl -TimeoutSec $TimeoutSec

    $aliasMap = @{}
    $keyMap = @{}

    foreach ($prop in $enData.data.PSObject.Properties) {
        $en = $prop.Value
        $koProp = $koData.data.PSObject.Properties[$prop.Name]
        $koName = if ($null -ne $koProp -and -not [string]::IsNullOrWhiteSpace($koProp.Value.name)) {
            $koProp.Value.name
        }
        else {
            $en.name
        }

        $entry = [PSCustomObject]@{
            alias = $en.id
            aliasLower = $en.id.ToLowerInvariant()
            key = $en.key.ToString()
            nameKo = $koName
            roles = @($en.tags)
        }

        $aliasMap[$entry.aliasLower] = $entry
        $keyMap[$entry.key] = $entry
    }

    return [PSCustomObject]@{
        version = $version
        aliasMap = $aliasMap
        keyMap = $keyMap
    }
}

function Get-ItemNameMapFromDataDragon {
    param(
        [Parameter(Mandatory = $true)] [string]$Version,
        [int]$TimeoutSec = 30
    )

    $url = "https://ddragon.leagueoflegends.com/cdn/$Version/data/ko_KR/item.json"
    $itemData = Invoke-RestMethod -Uri $url -TimeoutSec $TimeoutSec

    $map = @{}
    foreach ($prop in $itemData.data.PSObject.Properties) {
        $map[$prop.Name] = $prop.Value.name
    }
    return $map
}

function Get-OpggChampionSlugAliasMap {
    param(
        [string]$Locale = "ko",
        [string]$Mode = "aram-mayhem",
        [int]$TimeoutSec = 30
    )

    $url = "https://op.gg/$Locale/lol/modes/${Mode}?region=global"
    $html = (Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec $TimeoutSec).Content

    $loc = [regex]::Escape($Locale)
    $mode = [regex]::Escape($Mode)
    $pattern = "/$loc/lol/modes/$mode/(?<slug>[a-z0-9\-]+)/build[^\r\n]{0,700}?/champion/(?<alias>[A-Za-z0-9]+)\.png"
    $matches = [regex]::Matches($html, $pattern)

    $map = @{}
    foreach ($m in $matches) {
        $slug = $m.Groups["slug"].Value
        $alias = $m.Groups["alias"].Value
        if ([string]::IsNullOrWhiteSpace($slug) -or [string]::IsNullOrWhiteSpace($alias)) { continue }
        if (-not $map.ContainsKey($slug)) {
            $map[$slug] = $alias
        }
    }

    return $map
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

function Add-AugmentNameMapEntry {
    param(
        [hashtable]$ExactMap,
        [hashtable]$NormalizedMap,
        [string]$Name,
        [string]$Id
    )

    if ([string]::IsNullOrWhiteSpace($Name) -or [string]::IsNullOrWhiteSpace($Id)) { return }

    $trimmed = $Name.Trim()
    if (-not $ExactMap.ContainsKey($trimmed)) {
        $ExactMap[$trimmed] = $Id
    }

    $normalized = Normalize-Name -Name $trimmed
    if (-not [string]::IsNullOrWhiteSpace($normalized) -and -not $NormalizedMap.ContainsKey($normalized)) {
        $NormalizedMap[$normalized] = $Id
    }
}

function Resolve-AugmentId {
    param(
        [hashtable]$ExactMap,
        [hashtable]$NormalizedMap,
        [string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Name)) { return $null }
    if ($ExactMap.ContainsKey($Name)) { return $ExactMap[$Name] }

    $normalized = Normalize-Name -Name $Name
    if (-not [string]::IsNullOrWhiteSpace($normalized) -and $NormalizedMap.ContainsKey($normalized)) {
        return $NormalizedMap[$normalized]
    }

    return $null
}

function Ensure-ChampionEntry {
    param(
        [object]$KbChampions,
        [string]$ChampionKey,
        [object]$ChampionMeta
    )

    $prop = $KbChampions.PSObject.Properties[$ChampionKey]
    if ($null -eq $prop -or $null -eq $prop.Value) {
        $newChampion = [PSCustomObject]@{
            name = $ChampionMeta.nameKo
            roles = @($ChampionMeta.roles)
            augmentPreferences = @()
            itemBuild = [PSCustomObject]@{
                core = @()
                situational = @()
            }
        }
        Set-ObjectProperty -Object $KbChampions -Name $ChampionKey -Value $newChampion
        return $newChampion
    }

    $champion = $prop.Value
    if ([string]::IsNullOrWhiteSpace($champion.name)) {
        Set-ObjectProperty -Object $champion -Name "name" -Value $ChampionMeta.nameKo
    }

    $roles = Ensure-ObjectProperty -Object $champion -Name "roles" -Factory { @() }
    if (@($roles).Count -eq 0) {
        Set-ObjectProperty -Object $champion -Name "roles" -Value @($ChampionMeta.roles)
    }

    [void](Ensure-ObjectProperty -Object $champion -Name "augmentPreferences" -Factory { @() })
    $itemBuild = Ensure-ObjectProperty -Object $champion -Name "itemBuild" -Factory {
        [PSCustomObject]@{
            core = @()
            situational = @()
        }
    }
    [void](Ensure-ObjectProperty -Object $itemBuild -Name "core" -Factory { @() })
    [void](Ensure-ObjectProperty -Object $itemBuild -Name "situational" -Factory { @() })

    return $champion
}

function Upsert-AugmentCatalogEntry {
    param(
        [object]$KbAugments,
        [string]$AugmentId,
        [string]$Name,
        [string]$Tier
    )

    $prop = $KbAugments.PSObject.Properties[$AugmentId]
    if ($null -eq $prop -or $null -eq $prop.Value) {
        Set-ObjectProperty -Object $KbAugments -Name $AugmentId -Value ([PSCustomObject]@{
            name = $Name
            tier = $Tier
            tags = @()
            notes = "source: op.gg aram-mayhem + communitydragon ko_kr"
        })
        return
    }

    $entry = $prop.Value
    if (-not [string]::IsNullOrWhiteSpace($Name)) {
        Set-ObjectProperty -Object $entry -Name "name" -Value $Name
    }

    $oldTier = if ($entry.PSObject.Properties["tier"]) { [string]$entry.tier } else { "UNKNOWN" }
    if ((Get-TierRank -Tier $Tier) -gt (Get-TierRank -Tier $oldTier)) {
        Set-ObjectProperty -Object $entry -Name "tier" -Value $Tier
    }

    [void](Ensure-ObjectProperty -Object $entry -Name "tags" -Factory { @() })
    [void](Ensure-ObjectProperty -Object $entry -Name "notes" -Factory { "source: op.gg aram-mayhem + communitydragon ko_kr" })
}

function Upsert-ItemCatalogEntry {
    param(
        [object]$KbItems,
        [string]$ItemId,
        [string]$Name
    )

    $prop = $KbItems.PSObject.Properties[$ItemId]
    if ($null -eq $prop -or $null -eq $prop.Value) {
        Set-ObjectProperty -Object $KbItems -Name $ItemId -Value ([PSCustomObject]@{
            name = $Name
            tags = @()
        })
        return
    }

    $entry = $prop.Value
    if ([string]::IsNullOrWhiteSpace($entry.name) -or $entry.name -like "Item *") {
        Set-ObjectProperty -Object $entry -Name "name" -Value $Name
    }
    [void](Ensure-ObjectProperty -Object $entry -Name "tags" -Factory { @() })
}

function Build-ItemBuildFromRows {
    param([object]$ItemRows)

    $coreRows = @($ItemRows.core)
    $bootsRows = @($ItemRows.boots)
    $starterRows = @($ItemRows.starter)

    $bestCoreRow = $null
    if ($coreRows.Count -gt 0) {
        $bestCoreRow = $coreRows |
            Sort-Object @{ Expression = { @($_.items).Count }; Descending = $true }, @{ Expression = { $_.key }; Descending = $false } |
            Select-Object -First 1
    }

    $coreIds = @()
    if ($null -ne $bestCoreRow) {
        $coreIds = @($bestCoreRow.items | ForEach-Object { [int]$_.itemId } | Select-Object -Unique)
    }

    $coreSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($id in $coreIds) {
        [void]$coreSet.Add($id.ToString())
    }

    $situationalMap = [ordered]@{}
    $allItemNameById = @{}

    foreach ($row in @($coreRows + $bootsRows + $starterRows)) {
        foreach ($item in @($row.items)) {
            $id = [int]$item.itemId
            if (-not $allItemNameById.ContainsKey($id.ToString()) -and -not [string]::IsNullOrWhiteSpace($item.name)) {
                $allItemNameById[$id.ToString()] = $item.name
            }
        }
    }

    foreach ($row in $coreRows) {
        if ($null -ne $bestCoreRow -and $row.key -eq $bestCoreRow.key) { continue }
        foreach ($item in @($row.items)) {
            $id = [int]$item.itemId
            $idKey = $id.ToString()
            if ($coreSet.Contains($idKey)) { continue }
            if (-not $situationalMap.Contains($idKey)) {
                $situationalMap[$idKey] = [PSCustomObject]@{
                    itemId = $id
                    whenTags = @()
                    reason = "OP.GG alternative core build"
                }
            }
        }
    }

    foreach ($row in $bootsRows) {
        foreach ($item in @($row.items)) {
            $id = [int]$item.itemId
            $idKey = $id.ToString()
            if ($coreSet.Contains($idKey)) { continue }
            if (-not $situationalMap.Contains($idKey)) {
                $situationalMap[$idKey] = [PSCustomObject]@{
                    itemId = $id
                    whenTags = @()
                    reason = "OP.GG boots option"
                }
            }
        }
    }

    foreach ($row in $starterRows) {
        foreach ($item in @($row.items)) {
            $id = [int]$item.itemId
            $idKey = $id.ToString()
            if ($coreSet.Contains($idKey)) { continue }
            if (-not $situationalMap.Contains($idKey)) {
                $situationalMap[$idKey] = [PSCustomObject]@{
                    itemId = $id
                    whenTags = @()
                    reason = "OP.GG starter item"
                }
            }
        }
    }

    return [PSCustomObject]@{
        itemBuild = [PSCustomObject]@{
            core = $coreIds
            situational = @($situationalMap.Values)
        }
        itemNames = $allItemNameById
    }
}

if (-not (Test-Path $KnowledgeBasePath)) {
    throw "knowledge base file not found: $KnowledgeBasePath"
}

$kb = Get-Content -Path $KnowledgeBasePath -Raw -Encoding UTF8 | ConvertFrom-Json
[void](Ensure-ObjectProperty -Object $kb -Name "meta" -Factory { [PSCustomObject]@{} })
[void](Ensure-ObjectProperty -Object $kb -Name "augments" -Factory { [PSCustomObject]@{} })
[void](Ensure-ObjectProperty -Object $kb -Name "items" -Factory { [PSCustomObject]@{} })
[void](Ensure-ObjectProperty -Object $kb -Name "champions" -Factory { [PSCustomObject]@{} })

Write-Host "Loading Data Dragon metadata..."
$dd = Get-ChampionMetaFromDataDragon -TimeoutSec $TimeoutSec
$itemNameMap = Get-ItemNameMapFromDataDragon -Version $dd.version -TimeoutSec $TimeoutSec

Write-Host "Loading OP.GG champion mode list..."
$slugAliasMap = Get-OpggChampionSlugAliasMap -Locale $Locale -Mode $Mode -TimeoutSec $TimeoutSec

if ($ChampionSlugs.Count -gt 0) {
    $requested = [System.Collections.Generic.List[string]]::new()
    foreach ($slug in $ChampionSlugs) {
        if ($slugAliasMap.ContainsKey($slug)) {
            $requested.Add($slug)
        }
        else {
            Write-Warning "Skipping unknown slug from mode list: $slug"
        }
    }
    $targetSlugs = $requested.ToArray()
}
else {
    $targetSlugs = @($slugAliasMap.Keys | Sort-Object)
}

if ($Limit -gt 0) {
    $targetSlugs = @($targetSlugs | Select-Object -First $Limit)
}

Write-Host "Target champions: $($targetSlugs.Count)"

$augmentExactMap = @{}
$augmentNormalizedMap = @{}

# Seed name maps from existing knowledge base first.
foreach ($prop in $kb.augments.PSObject.Properties) {
    $name = [string]$prop.Value.name
    if ([string]::IsNullOrWhiteSpace($name)) { continue }
    Add-AugmentNameMapEntry -ExactMap $augmentExactMap -NormalizedMap $augmentNormalizedMap -Name $name -Id $prop.Name
}

# Extend with CommunityDragon names.
Write-Host "Loading CommunityDragon augment dictionary..."
$cdUrl = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/ko_kr/v1/cherry-augments.json"
$cdJson = Download-Utf8Text -Url $cdUrl -TimeoutSec $TimeoutSec
$cdAugments = $cdJson | ConvertFrom-Json
foreach ($node in $cdAugments) {
    $id = if ($node.id) { [string]$node.id } elseif ($node.apiName) { [string]$node.apiName } else { "" }
    if ([string]::IsNullOrWhiteSpace($id)) { continue }
    $name = if ($node.nameTRA) { [string]$node.nameTRA } elseif ($node.name) { [string]$node.name } else { "" }
    Add-AugmentNameMapEntry -ExactMap $augmentExactMap -NormalizedMap $augmentNormalizedMap -Name $name -Id $id
}

$seenAugments = @{}
$seenItemNames = @{}
$failed = New-Object System.Collections.Generic.List[string]
$unresolvedAugments = New-Object System.Collections.Generic.List[string]
$updatedChampions = 0

for ($i = 0; $i -lt $targetSlugs.Count; $i++) {
    $slug = $targetSlugs[$i]
    $alias = $slugAliasMap[$slug]
    $aliasLower = $alias.ToLowerInvariant()

    if (-not $dd.aliasMap.ContainsKey($aliasLower)) {
        $failed.Add("$slug (unknown alias: $alias)")
        continue
    }

    $champMeta = $dd.aliasMap[$aliasLower]
    $champKey = $champMeta.key
    $url = "https://op.gg/$Locale/lol/modes/$Mode/$slug/build"
    Write-Host ("[{0}/{1}] {2} -> {3}" -f ($i + 1), $targetSlugs.Count, $slug, $champMeta.nameKo)

    try {
        $html = (Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec $TimeoutSec).Content

        $augmentRows = Get-AugmentsFromBuildPage -Html $html -Locale $Locale -Mode $Mode -Slug $slug
        $starterRows = Get-ItemRowsFromBuildPage -Html $html -Prefix "starter_items_"
        $bootsRows = Get-ItemRowsFromBuildPage -Html $html -Prefix "boots_"
        $coreRows = Get-ItemRowsFromBuildPage -Html $html -Prefix "core_items_"

        $itemRows = [PSCustomObject]@{
            starter = $starterRows
            boots = $bootsRows
            core = $coreRows
        }

        $champion = Ensure-ChampionEntry -KbChampions $kb.champions -ChampionKey $champKey -ChampionMeta $champMeta

        $sTierRows = @($augmentRows | Where-Object { $_.tier -eq "S" })
        $newPreferences = New-Object System.Collections.Generic.List[object]
        $seenPrefAugmentIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

        for ($rank = 0; $rank -lt $sTierRows.Count; $rank++) {
            $row = $sTierRows[$rank]
            $augmentName = [string]$row.name
            $augmentId = Resolve-AugmentId -ExactMap $augmentExactMap -NormalizedMap $augmentNormalizedMap -Name $augmentName
            if ([string]::IsNullOrWhiteSpace($augmentId)) {
                $unresolvedAugments.Add("$slug::$augmentName")
                continue
            }
            if (-not $seenPrefAugmentIds.Add($augmentId)) { continue }

            $baseBonus = [Math]::Max(12, 40 - ($rank * 3))
            $newPreferences.Add([PSCustomObject]@{
                augmentId = $augmentId
                baseBonus = $baseBonus
                reason = "OP.GG S-tier (ARAM Mayhem)"
            })

            $tierForCatalog = if ([string]::IsNullOrWhiteSpace($row.tier)) { "S" } else { [string]$row.tier }
            if (-not $seenAugments.ContainsKey($augmentId)) {
                $seenAugments[$augmentId] = [PSCustomObject]@{
                    name = $augmentName
                    tier = $tierForCatalog
                }
            }
            else {
                $existing = $seenAugments[$augmentId]
                if ((Get-TierRank -Tier $tierForCatalog) -gt (Get-TierRank -Tier $existing.tier)) {
                    $existing.tier = $tierForCatalog
                }
                if ([string]::IsNullOrWhiteSpace($existing.name) -or $existing.name -eq "???") {
                    $existing.name = $augmentName
                }
            }
        }

        if ($newPreferences.Count -gt 0) {
            Set-ObjectProperty -Object $champion -Name "augmentPreferences" -Value $newPreferences.ToArray()
        }

        $itemBuildResult = Build-ItemBuildFromRows -ItemRows $itemRows
        $newItemBuild = $itemBuildResult.itemBuild
        if (@($newItemBuild.core).Count -gt 0 -or @($newItemBuild.situational).Count -gt 0) {
            Set-ObjectProperty -Object $champion -Name "itemBuild" -Value $newItemBuild
        }

        foreach ($kv in $itemBuildResult.itemNames.GetEnumerator()) {
            if (-not $seenItemNames.ContainsKey($kv.Key) -and -not [string]::IsNullOrWhiteSpace($kv.Value)) {
                $seenItemNames[$kv.Key] = $kv.Value
            }
        }

        $updatedChampions++
    }
    catch {
        $failed.Add("$slug ($($_.Exception.Message))")
    }

    if ($DelayMs -gt 0) {
        Start-Sleep -Milliseconds $DelayMs
    }
}

Write-Host "Ensuring champion entries for all Data Dragon champions..."
foreach ($entry in $dd.keyMap.Values) {
    [void](Ensure-ChampionEntry -KbChampions $kb.champions -ChampionKey $entry.key -ChampionMeta $entry)
}

Write-Host "Updating augment catalog..."
foreach ($kv in $seenAugments.GetEnumerator()) {
    Upsert-AugmentCatalogEntry -KbAugments $kb.augments -AugmentId $kv.Key -Name $kv.Value.name -Tier $kv.Value.tier
}

Write-Host "Updating item catalog..."
foreach ($champProp in $kb.champions.PSObject.Properties) {
    $champ = $champProp.Value
    if ($null -eq $champ.itemBuild) { continue }

    foreach ($itemId in @($champ.itemBuild.core)) {
        $itemKey = [string]$itemId
        $itemName = if ($itemNameMap.ContainsKey($itemKey)) {
            $itemNameMap[$itemKey]
        }
        elseif ($seenItemNames.ContainsKey($itemKey)) {
            $seenItemNames[$itemKey]
        }
        else {
            "Item $itemKey"
        }
        Upsert-ItemCatalogEntry -KbItems $kb.items -ItemId $itemKey -Name $itemName
    }

    foreach ($sit in @($champ.itemBuild.situational)) {
        $itemKey = [string]$sit.itemId
        $itemName = if ($itemNameMap.ContainsKey($itemKey)) {
            $itemNameMap[$itemKey]
        }
        elseif ($seenItemNames.ContainsKey($itemKey)) {
            $seenItemNames[$itemKey]
        }
        else {
            "Item $itemKey"
        }
        Upsert-ItemCatalogEntry -KbItems $kb.items -ItemId $itemKey -Name $itemName
    }
}

$today = Get-Date -Format "yyyy-MM-dd"
Set-ObjectProperty -Object $kb.meta -Name "updatedAt" -Value $today
Set-ObjectProperty -Object $kb.meta -Name "totalChampions" -Value (($kb.champions.PSObject.Properties | Measure-Object).Count)
Set-ObjectProperty -Object $kb.meta -Name "augmentDataSource" -Value "OP.GG champion build + OP.GG mode list + CommunityDragon ko_kr (S-tier only)"
Set-ObjectProperty -Object $kb.meta -Name "itemDataSource" -Value "OP.GG champion build item rows + Data Dragon ko_KR names"
Set-ObjectProperty -Object $kb.meta -Name "sTierAugmentCount" -Value (($kb.augments.PSObject.Properties | Where-Object { $_.Value.tier -eq "S" } | Measure-Object).Count)
Set-ObjectProperty -Object $kb.meta -Name "championsWithChampionSpecificSTier" -Value (($kb.champions.PSObject.Properties | Where-Object { @($_.Value.augmentPreferences).Count -gt 0 } | Measure-Object).Count)
Set-ObjectProperty -Object $kb.meta -Name "championsWithChampionSpecificItems" -Value (($kb.champions.PSObject.Properties | Where-Object {
            ($null -ne $_.Value.itemBuild) -and (
                @($_.Value.itemBuild.core).Count -gt 0 -or
                @($_.Value.itemBuild.situational).Count -gt 0
            )
        } | Measure-Object).Count)

$outDir = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

$json = $kb | ConvertTo-Json -Depth 30
Set-Content -Path $OutputPath -Value $json -Encoding UTF8

if ($UpdateDistCopy) {
    $distPath = "dist/data/knowledge_base.json"
    $distDir = Split-Path -Parent $distPath
    if (-not [string]::IsNullOrWhiteSpace($distDir)) {
        New-Item -ItemType Directory -Path $distDir -Force | Out-Null
    }
    Copy-Item -Path $OutputPath -Destination $distPath -Force
}

if ($UpdateDebugCopy) {
    $debugPath = "src/LSA.App/bin/Debug/net8.0-windows/data/knowledge_base.json"
    $debugDir = Split-Path -Parent $debugPath
    if (-not [string]::IsNullOrWhiteSpace($debugDir)) {
        New-Item -ItemType Directory -Path $debugDir -Force | Out-Null
    }
    Copy-Item -Path $OutputPath -Destination $debugPath -Force
}

if ($UpdateTestsDebugCopy) {
    $testsPath = "src/LSA.Tests/bin/Debug/net8.0/data/knowledge_base.json"
    $testsDir = Split-Path -Parent $testsPath
    if (-not [string]::IsNullOrWhiteSpace($testsDir)) {
        New-Item -ItemType Directory -Path $testsDir -Force | Out-Null
    }
    Copy-Item -Path $OutputPath -Destination $testsPath -Force
}

Write-Host ""
Write-Host "Done."
Write-Host "Updated champions: $updatedChampions"
Write-Host "Failed champions: $($failed.Count)"
Write-Host "Unresolved augment names: $($unresolvedAugments.Count)"
Write-Host "Output: $OutputPath"

if ($failed.Count -gt 0) {
    Write-Host ""
    Write-Host "Failed list (first 20):"
    $failed | Select-Object -First 20 | ForEach-Object { Write-Host " - $_" }
}

if ($unresolvedAugments.Count -gt 0) {
    Write-Host ""
    Write-Host "Unresolved augment names (first 20):"
    $unresolvedAugments | Select-Object -Unique | Select-Object -First 20 | ForEach-Object { Write-Host " - $_" }
}
