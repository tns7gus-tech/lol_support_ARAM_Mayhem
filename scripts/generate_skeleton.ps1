# Use .NET WebClient for proper UTF-8 handling
$wc = New-Object System.Net.WebClient
$wc.Encoding = [System.Text.Encoding]::UTF8
$jsonStr = $wc.DownloadString("https://ddragon.leagueoflegends.com/cdn/14.24.1/data/ko_KR/champion.json")
$data = $jsonStr | ConvertFrom-Json

# Build champions array using StringBuilder for clean JSON
$sb = New-Object System.Text.StringBuilder

[void]$sb.AppendLine('{')
[void]$sb.AppendLine('  "meta": {')
[void]$sb.AppendLine('    "version": "0.2.0",')
$date = Get-Date -Format "yyyy-MM-dd"
[void]$sb.AppendLine("    `"updatedAt`": `"$date`",")

# Count champions first
$champList = $data.data.PSObject.Properties | Sort-Object { [int]$_.Value.key }
$count = ($champList | Measure-Object).Count

[void]$sb.AppendLine("    `"totalChampions`": $count")
[void]$sb.AppendLine('  },')

# augments section - comprehensive ARAM augment list
[void]$sb.AppendLine('  "augments": {')
[void]$sb.AppendLine('    "gold": [],')
[void]$sb.AppendLine('    "silver": [],')
[void]$sb.AppendLine('    "prismatic": []')
[void]$sb.AppendLine('  },')

# items section
[void]$sb.AppendLine('  "items": {},')

# champions section
[void]$sb.AppendLine('  "champions": {')

$i = 0
foreach ($champ in $champList) {
    $i++
    $d = $champ.Value
    $key = $d.key
    $name = $d.name
    $id = $d.id
    
    # Build roles array
    $rolesStr = ($d.tags | ForEach-Object { "`"$_`"" }) -join ", "
    
    [void]$sb.AppendLine("    `"$key`": {")
    [void]$sb.AppendLine("      `"id`": `"$id`",")
    [void]$sb.AppendLine("      `"name`": `"$name`",")
    [void]$sb.AppendLine("      `"roles`": [$rolesStr],")
    [void]$sb.AppendLine('      "augmentPreferences": [],')
    [void]$sb.AppendLine('      "itemBuild": {')
    [void]$sb.AppendLine('        "core": [],')
    [void]$sb.AppendLine('        "situational": []')
    [void]$sb.AppendLine('      }')
    
    if ($i -lt $count) {
        [void]$sb.AppendLine('    },')
    }
    else {
        [void]$sb.AppendLine('    }')
    }
}

[void]$sb.AppendLine('  },')

# rules section
[void]$sb.AppendLine('  "rules": {')
[void]$sb.AppendLine('    "augmentSelection": {')
[void]$sb.AppendLine('      "maxPerRound": 1,')
[void]$sb.AppendLine('      "preferChampionSpecific": true')
[void]$sb.AppendLine('    }')
[void]$sb.AppendLine('  }')
[void]$sb.AppendLine('}')

$outputPath = "d:/projects/lol_support_ARAM_Mayhem/data/knowledge_base.json"
[System.IO.File]::WriteAllText($outputPath, $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))

Write-Host "=== Done ==="
Write-Host "Champions: $count"
$fs = (Get-Item $outputPath).Length
Write-Host "File size: $([math]::Round($fs / 1KB, 1)) KB"
