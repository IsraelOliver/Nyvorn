<#
Compares two V9 probe capture folders (--v9-probe-capture), state by state: SHA-256 of the float buffers (energy,
natural, artificial, glow), field window, every diagnostic PNG byte for byte, final/albedo frames pixel by pixel outside
the player's box (the player faces the real mouse even in scripted captures) and the check lines.
Usage: .\Docs\LightingV9\Verify-V9Promotion.ps1 -Reference <folder> -Candidate <folder> [-Output comparison.json]
Exit 0 only if every state of the reference is present and identical. PNGs are never modified.
#>
param(
    [Parameter(Mandatory = $true)] [string] $Reference,
    [Parameter(Mandatory = $true)] [string] $Candidate,
    [string] $Output
)

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class V9PixelDiff
{
    // ARGB pixels that differ outside the boxes (x0, y0, x1, y1 per box, end exclusive); -1 if the sizes differ.
    public static long Count(string a, string b, int[] boxes)
    {
        using (var imageA = new Bitmap(a))
        using (var imageB = new Bitmap(b))
        {
            if (imageA.Width != imageB.Width || imageA.Height != imageB.Height) return -1;
            var rect = new Rectangle(0, 0, imageA.Width, imageA.Height);
            BitmapData dataA = imageA.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData dataB = imageB.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int length = dataA.Stride * imageA.Height;
            var pixelsA = new byte[length];
            var pixelsB = new byte[length];
            Marshal.Copy(dataA.Scan0, pixelsA, 0, length);
            Marshal.Copy(dataB.Scan0, pixelsB, 0, length);
            int stride = dataA.Stride;
            imageA.UnlockBits(dataA);
            imageB.UnlockBits(dataB);
            long differing = 0;
            for (int y = 0; y < rect.Height; y++)
                for (int x = 0; x < rect.Width; x++)
                {
                    bool skip = false;
                    for (int k = 0; k + 3 < boxes.Length && !skip; k += 4)
                        skip = x >= boxes[k] && x < boxes[k + 2] && y >= boxes[k + 1] && y < boxes[k + 3];
                    if (skip) continue;
                    int i = y * stride + x * 4;
                    if (pixelsA[i] != pixelsB[i] || pixelsA[i + 1] != pixelsB[i + 1] || pixelsA[i + 2] != pixelsB[i + 2] || pixelsA[i + 3] != pixelsB[i + 3])
                        differing++;
                }
            return differing;
        }
    }
}
'@

function Get-PlayerBox($m) {
    $x = [int][math]::Floor(($m.player.x - $m.viewTopLeft.x) * $m.zoom)
    $y = [int][math]::Floor(($m.player.y - $m.viewTopLeft.y) * $m.zoom)
    return @(($x - 48), ($y - 80), ($x + 49), ($y + 17))
}

$result = [ordered]@{ reference = (Resolve-Path $Reference).Path; candidate = (Resolve-Path $Candidate).Path; states = 0; missing = @(); regressions = @(); pixelsOutsidePlayer = [ordered]@{} }
foreach ($file in Get-ChildItem $Reference -Filter '*_metrics.json' | Sort-Object Name) {
    $name = $file.Name -replace '_metrics\.json$', ''
    $other = Join-Path $Candidate $file.Name
    if (-not (Test-Path $other)) { $result.missing += $name; continue }
    $result.states++
    $a = Get-Content $file.FullName -Raw | ConvertFrom-Json
    $b = Get-Content $other -Raw | ConvertFrom-Json
    foreach ($h in 'energy', 'natural', 'artificial') { if ($a.hashes.$h -ne $b.hashes.$h) { $result.regressions += "${name}: $h hash" } }
    if ($a.skyGlow.hash -ne $b.skyGlow.hash) { $result.regressions += "${name}: glow hash" }
    if ($a.naturalModel -ne $b.naturalModel) { $result.regressions += "${name}: natural model $($a.naturalModel) -> $($b.naturalModel)" }
    if ("$($a.field.origin.x),$($a.field.origin.y),$($a.field.Width),$($a.field.Height)" -ne "$($b.field.origin.x),$($b.field.origin.y),$($b.field.Width),$($b.field.Height)") { $result.regressions += "${name}: field window" }
    foreach ($png in Get-ChildItem $Reference -Filter "${name}_*.png") {
        $suffix = $png.Name.Substring($name.Length + 1) -replace '\.png$', ''
        $otherPng = Join-Path $Candidate $png.Name
        if (-not (Test-Path $otherPng)) { $result.regressions += "$($png.Name): missing"; continue }
        if ($suffix -in 'final', 'albedo') {
            $count = [V9PixelDiff]::Count($png.FullName, $otherPng, [int[]]((Get-PlayerBox $a) + (Get-PlayerBox $b)))
            $result.pixelsOutsidePlayer[$png.Name] = $count
            if ($count -ne 0) { $result.regressions += "$($png.Name): $count pixels outside the player" }
        }
        elseif ((Get-FileHash $png.FullName).Hash -ne (Get-FileHash $otherPng).Hash) { $result.regressions += "$($png.Name): bytes" }
    }
}
$checksA = [string[]](Get-Content (Join-Path $Reference 'checks.txt'))
$checksB = [string[]](Get-Content (Join-Path $Candidate 'checks.txt'))
$result.referenceChecks = $checksA[-1]
$result.candidateChecks = $checksB[-1]
$result.checkLinesIdentical = (($checksA -join "`n") -eq ($checksB -join "`n"))
if (-not $result.checkLinesIdentical) { $result.regressions += 'checks.txt lines differ' }
$result.passed = ($result.states -gt 0 -and $result.missing.Count -eq 0 -and $result.regressions.Count -eq 0)
$json = $result | ConvertTo-Json -Depth 5
if ($Output) { $json | Out-File -Encoding utf8 $Output }
$result.Remove('pixelsOutsidePlayer')
$result | ConvertTo-Json -Depth 5
if (-not $result.passed) { exit 1 }
