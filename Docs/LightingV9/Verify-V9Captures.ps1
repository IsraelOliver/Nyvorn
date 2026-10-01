param([string]$CaptureDirectory = "$PSScriptRoot/../../screenshots/v9-lab/final")
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $CaptureDirectory).Path
$report = [System.Collections.Generic.List[string]]::new()
function Check([string]$Name, [bool]$Condition) {
    $report.Add("$(if ($Condition) { 'PASS' } else { 'FAIL' }) $Name")
}
function Metrics([string]$Name) {
    Get-Content -LiteralPath (Join-Path $root ($Name + '_metrics.json')) -Raw | ConvertFrom-Json
}
function Same([string]$A, [string]$B, [string]$Suffix) {
    (Get-FileHash -LiteralPath (Join-Path $root ($A + '_' + $Suffix + '.png'))).Hash -eq
    (Get-FileHash -LiteralPath (Join-Path $root ($B + '_' + $Suffix + '.png'))).Hash
}
function Energy($Metrics, [string]$Label) {
    $sample = $Metrics.fixedSamples | Where-Object label -eq $Label
    [double]$sample.linearIrradiance.r + [double]$sample.linearIrradiance.g + [double]$sample.linearIrradiance.b
}
$base = Metrics 'base'; $two = Metrics 'two'; $five = Metrics 'five'
Check 'source order: entire five-source frame identical' (Same 'five' 'five-reversed' 'final')
Check 'blocker restored: entire base frame restored exactly' (Same 'base' 'blocker-restored' 'final')
Check 'far torch does not change original player pixels' (Same 'base' 'far' 'entity-lit')
foreach ($name in @('pan', 'zoom2', 'zoom3', 'player-moved')) {
    Check "$name keeps world irradiance identical" (Same 'base' $name 'irradiance-diagnostic')
}
Check 'all-source shadow is exactly zero with one and five torches' ((Energy $base 'shadow') -eq 0 -and (Energy $five 'shadow') -eq 0)
Check 'removed blocker admits light at fixed shadow point' ((Energy (Metrics 'blocker-removed') 'shadow') -gt 0)
Check 'moving torch reveals its previous shadow' ((Energy (Metrics 'source-moved') 'shadow') -gt 0)
Check 'second remote torch illuminates first torch shadow' ((Energy (Metrics 'far') 'shadow') -gt 0)
Check 'wall gets more energy with two and five lights' ((Energy $base 'wall') -lt (Energy $two 'wall') -and (Energy $two 'wall') -lt (Energy $five 'wall'))
Check 'player brightness increases with two and five lights' ($base.entity.lit.meanLinearLuminance -lt $two.entity.lit.meanLinearLuminance -and $two.entity.lit.meanLinearLuminance -lt $five.entity.lit.meanLinearLuminance)
Check 'wall texture has measurable contrast with five lights' ($five.patches.wallMaterial.final.stddevLinearLuminance -gt .005)
Check 'closing entrance changes nearby player illumination without channel clipping' ((Metrics 'near-open').entity.lit.meanLinearLuminance -gt (Metrics 'near-closed').entity.lit.meanLinearLuminance -and (Metrics 'near-open').entity.lit.pixelsAnyChannel254Plus -eq 0)
foreach ($name in @('base','two','five','pan','zoom2','zoom3','player-moved','near-open','near-closed')) {
    $m = Metrics $name
    Check "$name GPU player agrees with CPU reference within 0.03 normalized RGB" ($m.entity.shaderAgreement.opaquePixelCount -gt 0 -and $m.entity.shaderAgreement.maxNormalizedRgbError -lt .03)
}
# Examine actual runtime pixels. The six-pixel terminal surface is allowed on the exterior side of the roof.
Add-Type -AssemblyName System.Drawing
foreach ($name in @('all-off', 'sky-closed')) {
    $bitmap = [System.Drawing.Bitmap]::new((Join-Path $root ($name + '_final.png')))
    try {
        $nonblack = 0
        for ($y = 140; $y -lt $bitmap.Height; $y++) {
            for ($x = 0; $x -lt $bitmap.Width; $x++) {
                $p = $bitmap.GetPixel($x,$y)
                if (($p.R -ne 0) -or ($p.G -ne 0) -or ($p.B -ne 0)) { $nonblack++ }
            }
        }
        Check "${name}: interior below world y=56 is entirely black ($nonblack nonblack pixels)" ($nonblack -eq 0)
    } finally { $bitmap.Dispose() }
}
$report.Add('Visual quality, contrast loss under extreme light and performance remain separate evaluations.')
$report | Set-Content -LiteralPath (Join-Path $root 'capture-checks.txt')
$report
if (@($report | Where-Object { $_.StartsWith('FAIL') }).Count -gt 0) { throw 'V9 runtime capture checks failed.' }
