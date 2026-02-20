$jsonPath = "d:\projects\lol_support_ARAM_Mayhem\data\knowledge_base.json"
try {
    $content = Get-Content $jsonPath -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
}
catch {
    Write-Error "Failed to read or parse JSON: $_"
    exit 1
}

Write-Host "Content Type: $($content.GetType().Name)"
if (-not $content) {
    Write-Error "Content is null after parsing"
    exit 1
}

if ($content.champions) {
    Write-Host "Champions found. Type: $($content.champions.GetType().Name)"
    $props = $content.champions.PSObject.Properties
    Write-Host "Champions Properties Count: $($props.Count)"
}
else {
    Write-Host "Champions property NOT found on content object."
    exit 1
}

# Define Templates
$templates = @{
    "Mage"     = @{
        "augments" = @(
            @{ "augmentId" = "aug_arcane_surge"; "baseBonus" = 20; "reason" = "Role: Mage (Burst)" },
            @{ "augmentId" = "aug_spellweaver"; "baseBonus" = 15; "reason" = "Role: Mage (DPS)" },
            @{ "augmentId" = "aug_manaflow_overdrive"; "baseBonus" = 10; "reason" = "Role: Mage (Mana)" }
        );
        "items"    = @{
            "core"        = @(6655, 3020, 3157);
            "situational" = @(
                @{ "itemId" = 3089; "whenTags" = @("burst", "scaling"); "reason" = "Heavy AP" },
                @{ "itemId" = 3135; "whenTags" = @("tank"); "reason" = "Magic Pen" }
            )
        }
    };
    "Marksman" = @{
        "augments" = @(
            @{ "augmentId" = "aug_lethal_tempo"; "baseBonus" = 22; "reason" = "Role: Marksman (AS)" },
            @{ "augmentId" = "aug_sharpshooter_focus"; "baseBonus" = 18; "reason" = "Role: Marksman (Crit)" },
            @{ "augmentId" = "aug_kiting_master"; "baseBonus" = 12; "reason" = "Role: Marksman (Mobility)" }
        );
        "items"    = @{
            "core"        = @(6672, 3006, 3094);
            "situational" = @(
                @{ "itemId" = 3031; "whenTags" = @("crit", "burst"); "reason" = "Infinity Edge" },
                @{ "itemId" = 3036; "whenTags" = @("tank"); "reason" = "Armor Pen" }
            )
        }
    };
    "Fighter"  = @{
        "augments" = @(
            @{ "augmentId" = "aug_bruiser_drive"; "baseBonus" = 20; "reason" = "Role: Fighter (Sustain)" },
            @{ "augmentId" = "aug_omnivamp_frenzy"; "baseBonus" = 15; "reason" = "Role: Fighter (Vamp)" },
            @{ "augmentId" = "aug_anti_tank_core"; "baseBonus" = 12; "reason" = "Role: Fighter (Anti-Tank)" }
        );
        "items"    = @{
            "core"        = @(6630, 3071, 3053);
            "situational" = @(
                @{ "itemId" = 3742; "whenTags" = @("ad", "defense"); "reason" = "Death's Dance" },
                @{ "itemId" = 3075; "whenTags" = @("heal"); "reason" = "Anti-Heal" }
            )
        }
    };
    "Tank"     = @{
        "augments" = @(
            @{ "augmentId" = "aug_colossus_heart"; "baseBonus" = 22; "reason" = "Role: Tank (Health)" },
            @{ "augmentId" = "aug_stone_skin"; "baseBonus" = 18; "reason" = "Role: Tank (Resist)" },
            @{ "augmentId" = "aug_frontline_oath"; "baseBonus" = 14; "reason" = "Role: Tank (Peel)" }
        );
        "items"    = @{
            "core"        = @(3068, 3111, 3075);
            "situational" = @(
                @{ "itemId" = 3143; "whenTags" = @("ad", "crit"); "reason" = "Randuin's" },
                @{ "itemId" = 4401; "whenTags" = @("ap"); "reason" = "Force of Nature" }
            )
        }
    };
    "Assassin" = @{
        "augments" = @(
            @{ "augmentId" = "aug_shadow_hunt"; "baseBonus" = 22; "reason" = "Role: Assassin (Burst)" },
            @{ "augmentId" = "aug_executioner_edge"; "baseBonus" = 18; "reason" = "Role: Assassin (Execute)" },
            @{ "augmentId" = "aug_bruiser_drive"; "baseBonus" = 10; "reason" = "Role: Assassin (Survival)" }
        );
        "items"    = @{
            "core"        = @(6692, 3158, 3142); # Youmuu, Lucidity, Ghostblade/Edge
            "situational" = @(
                @{ "itemId" = 3814; "whenTags" = @("cc"); "reason" = "Edge of Night" },
                @{ "itemId" = 6693; "whenTags" = @("tank"); "reason" = "Prowler's" } # Assuming Prowlers ID or similar
            )
        }
    };
    "AssassinAP" = @{
        "augments" = @(
            @{ "augmentId" = "aug_shadow_hunt"; "baseBonus" = 20; "reason" = "Role: AP Assassin (Burst)" },
            @{ "augmentId" = "aug_arcane_surge"; "baseBonus" = 18; "reason" = "Role: AP Assassin (Spell Burst)" },
            @{ "augmentId" = "aug_spellweaver"; "baseBonus" = 12; "reason" = "Role: AP Assassin (DPS)" }
        );
        "items"    = @{
            "core"        = @(6655, 3020, 3157);
            "situational" = @(
                @{ "itemId" = 3089; "whenTags" = @("burst", "scaling"); "reason" = "Heavy AP" },
                @{ "itemId" = 3135; "whenTags" = @("tank"); "reason" = "Magic Pen" }
            )
        }
    };
    "Support"  = @{
        "augments" = @(
            @{ "augmentId" = "aug_harmonic_echo"; "baseBonus" = 20; "reason" = "Role: Support (Heal)" },
            @{ "augmentId" = "aug_moonlit_grace"; "baseBonus" = 15; "reason" = "Role: Support (Shield)" },
            @{ "augmentId" = "aug_cc_chain"; "baseBonus" = 12; "reason" = "Role: Support (CC)" }
        );
        "items"    = @{
            "core"        = @(6617, 3158, 2065); # Moonstone, Lucidity, Shurelya
            "situational" = @(
                @{ "itemId" = 3504; "whenTags" = @("as"); "reason" = "Ardent Censer" },
                @{ "itemId" = 3190; "whenTags" = @("ap", "defense"); "reason" = "Locket" } # Locket
            )
        }
    };
}

# Champion-level overrides for templates.
# These champions are AP-oriented in ARAM and should not use the AD assassin/fighter template.
$championTemplateOverrides = @{
    "Leblanc" = "AssassinAP";
    "Evelynn" = "AssassinAP";
    "Katarina" = "AssassinAP";
    "Elise" = "AssassinAP";
    "Nidalee" = "AssassinAP";
    "Akali" = "AssassinAP";
    "Ekko" = "AssassinAP";
    "Fizz" = "AssassinAP";
    "Diana" = "AssassinAP";
}

$count = 0
$debugCount = 0
foreach ($prop in $content.champions.PSObject.Properties) {
    $champ = $prop.Value
    if ($debugCount -lt 5) {
        Write-Host "Debug: $($champ.name) ($($champ.id)) PrefCount: $($champ.augmentPreferences.Count) Type: $($champ.augmentPreferences.GetType().Name)"
        $debugCount++
    }
    $forcedTemplate = $null
    if ($championTemplateOverrides.ContainsKey($champ.id)) {
        $forcedTemplate = $championTemplateOverrides[$champ.id]
    }

    # Keep the original behavior (fill only missing data),
    # but allow explicit champion overrides to fix known bad templates.
    if ($champ.augmentPreferences.Count -eq 0 -or $forcedTemplate) {
        $templateKey = if ($forcedTemplate) { $forcedTemplate } else { $champ.roles[0] }
        if (-not $templates.ContainsKey($templateKey)) {
            Write-Host "Warning: No template for role/template $templateKey (Champ: $($champ.name))" -ForegroundColor Yellow
            continue
        }
        
        $template = $templates[$templateKey]
        
        # Apply Augments
        # Convert to PS custom object array to ensure JSON serialization works
        $champ.augmentPreferences = @()
        foreach ($aug in $template.augments) {
            $champ.augmentPreferences += $aug
        }
        
        # Apply Items
        $champ.itemBuild.core = $template.items.core
        $champ.itemBuild.situational = $template.items.situational
        
        $count++
        if ($forcedTemplate) {
            Write-Host "Updated $($champ.name) ($($champ.id)) with forced $templateKey template" -ForegroundColor Green
        }
        else {
            Write-Host "Updated $($champ.name) ($($champ.id)) with $templateKey template" -ForegroundColor Green
        }
    }
}

$jsonOutput = $content | ConvertTo-Json -Depth 10
Set-Content -Path $jsonPath -Value $jsonOutput -Encoding UTF8
Write-Host "Successfully updated $count champions." -ForegroundColor Cyan
