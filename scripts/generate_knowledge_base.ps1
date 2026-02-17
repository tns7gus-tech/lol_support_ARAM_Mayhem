# Restructure knowledge_base.json per master plan v1.0
# Uses Data Dragon for champion list + role-based tag mapping

$wc = New-Object System.Net.WebClient
$wc.Encoding = [System.Text.Encoding]::UTF8
$jsonStr = $wc.DownloadString("https://ddragon.leagueoflegends.com/cdn/14.24.1/data/ko_KR/champion.json")
$ddData = $jsonStr | ConvertFrom-Json

# Role -> preferredTags mapping (weights)
$rolePreferred = @{
    "Mage" = @{ "burst" = 15; "poke" = 15; "mana_regen" = 10; "ability_power" = 15; "magic_pen" = 10 }
    "Fighter" = @{ "dps" = 15; "survivability" = 15; "tank_killer" = 10; "omnivamp" = 10 }
    "Tank" = @{ "survivability" = 20; "tank_stats" = 15; "cc" = 10; "shield" = 10 }
    "Marksman" = @{ "dps" = 20; "attack_speed" = 15; "crit" = 15; "lifesteal" = 10 }
    "Assassin" = @{ "burst" = 20; "mobility" = 15; "lethality" = 15; "execute" = 10 }
    "Support" = @{ "healing" = 15; "shielding" = 15; "utility" = 10; "mana_regen" = 10 }
}

# Role -> avoidTags mapping (penalties)
$roleAvoid = @{
    "Mage" = @{ "attack_speed" = -20; "crit" = -20; "lifesteal" = -15 }
    "Fighter" = @{ "support_only" = -20; "mana_regen" = -10 }
    "Tank" = @{ "crit" = -20; "lethality" = -15; "ability_power" = -10 }
    "Marksman" = @{ "tank_stats" = -15; "support_only" = -20 }
    "Assassin" = @{ "tank_stats" = -20; "support_only" = -20; "healing" = -10 }
    "Support" = @{ "crit" = -20; "lethality" = -15; "attack_speed" = -10 }
}

# Role -> default rerollThreshold
$roleThreshold = @{
    "Mage" = 70; "Fighter" = 70; "Tank" = 65; "Marksman" = 75; "Assassin" = 80; "Support" = 65
}

# Build champions data
$champList = $ddData.data.PSObject.Properties | Sort-Object { [int]$_.Value.key }
$sb = New-Object System.Text.StringBuilder

# Start JSON
[void]$sb.AppendLine('{')
[void]$sb.AppendLine('  "meta": {')
[void]$sb.AppendLine('    "version": "1.0.0",')
$date = Get-Date -Format "yyyy-MM-dd"
$count = ($champList | Measure-Object).Count
[void]$sb.AppendLine("    `"updatedAt`": `"$date`",")
[void]$sb.AppendLine("    `"totalChampions`": $count,")
[void]$sb.AppendLine('    "dataSource": "Riot Data Dragon (champion list) + role-based tag mapping",')
[void]$sb.AppendLine('    "scoringFormula": "score = tierBase + champSynergyBonus + tagMatchBonus + counterBonus + antiSynergyPenalty"')
[void]$sb.AppendLine('  },')

# Tags taxonomy
[void]$sb.AppendLine('  "tags": {')
$tags = [ordered]@{
    "dps" = "지속 딜"
    "burst" = "폭딜"
    "poke" = "포킹"
    "tank_killer" = "탱커 대응"
    "anti_heal" = "치유 감소"
    "survivability" = "생존력"
    "tank_stats" = "탱커 스탯"
    "cc" = "군중 제어"
    "shield" = "보호막"
    "healing" = "치유"
    "shielding" = "아군 보호막"
    "utility" = "유틸리티"
    "mobility" = "이동력"
    "execute" = "처형"
    "attack_speed" = "공격 속도"
    "crit" = "치명타"
    "lethality" = "치명률"
    "lifesteal" = "생명력 흡수"
    "omnivamp" = "모든 피해 흡혈"
    "ability_power" = "주문력"
    "magic_pen" = "마법 관통력"
    "mana_regen" = "마나 회복"
    "on_hit" = "적중 시 효과"
    "cooldown" = "쿨다운 감소"
    "support_only" = "서포터 전용"
    "aoe" = "광역 피해"
    "true_damage" = "고정 피해"
    "tenacity" = "강인함"
}
$tagI = 0
$tagCount = $tags.Count
foreach ($t in $tags.GetEnumerator()) {
    $tagI++
    $comma = if ($tagI -lt $tagCount) { "," } else { "" }
    [void]$sb.AppendLine("    `"$($t.Key)`": { `"label`": `"$($t.Value)`" }$comma")
}
[void]$sb.AppendLine('  },')

# Champions
[void]$sb.AppendLine('  "champions": {')

$i = 0
foreach ($champ in $champList) {
    $i++
    $d = $champ.Value
    $key = $d.key
    $name = $d.name
    $id = $d.id
    $roles = @($d.tags)

    # Merge preferredTags from all roles
    $preferred = @{}
    $avoid = @{}
    $threshold = 70  # default

    foreach ($role in $roles) {
        if ($rolePreferred.ContainsKey($role)) {
            foreach ($kv in $rolePreferred[$role].GetEnumerator()) {
                if (-not $preferred.ContainsKey($kv.Key) -or $preferred[$kv.Key] -lt $kv.Value) {
                    $preferred[$kv.Key] = $kv.Value
                }
            }
        }
        if ($roleAvoid.ContainsKey($role)) {
            foreach ($kv in $roleAvoid[$role].GetEnumerator()) {
                if (-not $avoid.ContainsKey($kv.Key) -or $avoid[$kv.Key] -gt $kv.Value) {
                    $avoid[$kv.Key] = $kv.Value
                }
                # Remove conflicting: if a tag is in both preferred and avoid, keep preferred
                if ($preferred.ContainsKey($kv.Key)) {
                    $avoid.Remove($kv.Key)
                }
            }
        }
        if ($roleThreshold.ContainsKey($role)) {
            $t = $roleThreshold[$role]
            if ($t -gt $threshold) { $threshold = $t }
        }
    }

    # Build preferredTags JSON
    $rolesStr = ($roles | ForEach-Object { "`"$_`"" }) -join ", "
    $prefParts = @()
    foreach ($kv in ($preferred.GetEnumerator() | Sort-Object Value -Descending)) {
        $prefParts += "`"$($kv.Key)`": $($kv.Value)"
    }
    $prefStr = $prefParts -join ", "

    $avoidParts = @()
    foreach ($kv in ($avoid.GetEnumerator() | Sort-Object Value)) {
        $avoidParts += "`"$($kv.Key)`": $($kv.Value)"
    }
    $avoidStr = $avoidParts -join ", "

    [void]$sb.AppendLine("    `"$key`": {")
    [void]$sb.AppendLine("      `"id`": `"$id`",")
    [void]$sb.AppendLine("      `"name`": `"$name`",")
    [void]$sb.AppendLine("      `"roles`": [$rolesStr],")
    [void]$sb.AppendLine("      `"preferredTags`": { $prefStr },")
    [void]$sb.AppendLine("      `"avoidTags`": { $avoidStr },")
    [void]$sb.AppendLine("      `"rerollThreshold`": $threshold")

    if ($i -lt $count) {
        [void]$sb.AppendLine('    },')
    } else {
        [void]$sb.AppendLine('    }')
    }
}
[void]$sb.AppendLine('  },')

# Rules
[void]$sb.AppendLine('  "rules": {')
[void]$sb.AppendLine('    "tierBase": { "S": 100, "A": 80, "B": 60, "C": 40 },')
[void]$sb.AppendLine('    "enemyTagWeights": {')
[void]$sb.AppendLine('      "tank": { "tank_killer": 25 },')
[void]$sb.AppendLine('      "heal": { "anti_heal": 20 },')
[void]$sb.AppendLine('      "burst": { "survivability": 15 },')
[void]$sb.AppendLine('      "poke": { "survivability": 10, "healing": 10 },')
[void]$sb.AppendLine('      "assassin": { "survivability": 20, "shield": 10 }')
[void]$sb.AppendLine('    },')
[void]$sb.AppendLine('    "rerollDefaults": {')
[void]$sb.AppendLine('      "defaultThreshold": 70,')
[void]$sb.AppendLine('      "conservativeIfSynergyExists": true,')
[void]$sb.AppendLine('      "aggressiveIfAllAntiSynergy": true')
[void]$sb.AppendLine('    }')
[void]$sb.AppendLine('  }')
[void]$sb.AppendLine('}')

$outputPath = "d:/projects/lol_support_ARAM_Mayhem/data/knowledge_base.json"
[System.IO.File]::WriteAllText($outputPath, $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))

Write-Host "=== knowledge_base.json ==="
Write-Host "Champions: $count"
Write-Host "Tags: $tagCount"
$fs = (Get-Item $outputPath).Length
Write-Host "Size: $([math]::Round($fs / 1KB, 1)) KB"
