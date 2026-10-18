# Independently compare the red marks in the four source images with the
# axial coordinate arrays stored in AttackAreaGeometry.cs.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$project = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$source = Get-Content -LiteralPath (Join-Path $project 'Assets\Game\Scripts\Cards\AttackAreaGeometry.cs') -Raw -Encoding UTF8
$arrayMatches = [regex]::Matches($source, 'new\[\] \{(?<cells>.*?)\}', [Text.RegularExpressions.RegexOptions]::Singleline)
if ($arrayMatches.Count -ne 4) { throw "Expected four fixed patterns, found $($arrayMatches.Count)" }

$conceptMap = Get-Content -LiteralPath (Join-Path $project 'Assets\Game\Prefabs\Board\ConceptMap.prefab') -Raw -Encoding UTF8
$gridMatches = [regex]::Matches($conceptMap, '(?m)^  - q: (-?\d+)\r?\n    r: (-?\d+)')
$coords = foreach ($match in $gridMatches) {
    [pscustomobject]@{ Q = [int]$match.Groups[1].Value; R = [int]$match.Groups[2].Value }
}

for ($index = 0; $index -lt 4; $index++) {
    $expected = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($match in [regex]::Matches($arrayMatches[$index].Groups['cells'].Value, 'C\((-?\d+),(-?\d+)\)')) {
        [void]$expected.Add("$($match.Groups[1].Value),$($match.Groups[2].Value)")
    }

    $imagePath = Join-Path $project "docs\skill-range-references\bombardment-$($index + 1).png"
    $bitmap = [System.Drawing.Bitmap]::new($imagePath)
    try {
        $observed = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($coord in $coords) {
            # Pixel centers are fitted once from the concept-map grid in the source images.
            $centerX = [int](99 + 70 * ($coord.Q + 5))
            $centerY = [int](274 + 60 * ($coord.R + $coord.Q / 2))
            $redSamples = 0
            for ($x = $centerX - 25; $x -le $centerX + 25; $x += 2) {
                for ($y = $centerY - 22; $y -le $centerY + 22; $y += 2) {
                    if ($x -lt 0 -or $x -ge $bitmap.Width -or $y -lt 0 -or $y -ge $bitmap.Height) { continue }
                    $pixel = $bitmap.GetPixel($x, $y)
                    if ($pixel.R -gt 170 -and $pixel.R -gt $pixel.G + 35 -and $pixel.R -gt $pixel.B + 35) {
                        $redSamples++
                    }
                }
            }
            if ($redSamples -gt 2) { [void]$observed.Add("$($coord.Q),$($coord.R)") }
        }
    }
    finally { $bitmap.Dispose() }

    $missing = @($observed | Where-Object { -not $expected.Contains($_) })
    $extra = @($expected | Where-Object { -not $observed.Contains($_) })
    if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
        throw "Pattern $($index + 1) differs from source image. Missing: $($missing -join ' '); Extra: $($extra -join ' ')"
    }
    Write-Output "Pattern $($index + 1): $($observed.Count) marked cells match."
}
