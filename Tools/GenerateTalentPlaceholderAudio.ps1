param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$sampleRate = 48000
$channels = 2
$bitsPerSample = 16
$durationSeconds = 0.70
$sampleCount = [int]($sampleRate * $durationSeconds)
$clickSeconds = 0.080
$fadeSeconds = 0.030
$targetPeak = [Math]::Pow(10.0, -1.0 / 20.0)
$samples = [double[]]::new($sampleCount)
$phase = 0.0
[uint32]$noiseState = 0x6D2B79F5
$previousNoise = 0.0
$peak = 0.0

for ($index = 0; $index -lt $sampleCount; $index++) {
    $time = $index / [double]$sampleRate
    $progress = $time / $durationSeconds
    $frequency = 620.0 + ((980.0 - 620.0) * $progress)
    $phase += 2.0 * [Math]::PI * $frequency / $sampleRate
    $chimeEnvelope = (1.0 - [Math]::Exp(-$time * 55.0)) * [Math]::Exp(-$time * 5.2)
    $chime = [Math]::Sin($phase) * $chimeEnvelope

    $click = 0.0
    if ($time -lt $clickSeconds) {
        $noiseState = $noiseState -bxor (($noiseState -shl 13) -band 0xffffffff)
        $noiseState = $noiseState -bxor ($noiseState -shr 17)
        $noiseState = $noiseState -bxor (($noiseState -shl 5) -band 0xffffffff)
        $rawNoise = (($noiseState / [double][uint32]::MaxValue) * 2.0) - 1.0
        $filteredNoise = $rawNoise - (0.72 * $previousNoise)
        $previousNoise = $rawNoise
        $clickEnvelope = [Math]::Exp(-$time * 62.0) * (1.0 - ($time / $clickSeconds))
        $click = $filteredNoise * $clickEnvelope * 0.52
    }

    $sample = ($chime * 0.76) + $click
    $fadeStart = $durationSeconds - $fadeSeconds
    if ($time -gt $fadeStart) {
        $sample *= [Math]::Max(0.0, ($durationSeconds - $time) / $fadeSeconds)
    }
    $samples[$index] = $sample
    $absolute = [Math]::Abs($sample)
    if ($absolute -gt $peak) { $peak = $absolute }
}

$normalization = if ($peak -gt 0.0) { $targetPeak / $peak } else { 1.0 }
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$parentDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutput)
if (-not [string]::IsNullOrEmpty($parentDirectory)) {
    [System.IO.Directory]::CreateDirectory($parentDirectory) | Out-Null
}

$fileStream = [System.IO.File]::Open($resolvedOutput, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = [System.IO.BinaryWriter]::new($fileStream, [System.Text.Encoding]::ASCII, $false)
try {
    $blockAlign = [int16]($channels * ($bitsPerSample / 8))
    $byteRate = $sampleRate * $blockAlign
    $dataSize = $sampleCount * $blockAlign
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
    $writer.Write([int](36 + $dataSize))
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('WAVE'))
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('fmt '))
    $writer.Write([int]16)
    $writer.Write([int16]1)
    $writer.Write([int16]$channels)
    $writer.Write([int]$sampleRate)
    $writer.Write([int]$byteRate)
    $writer.Write($blockAlign)
    $writer.Write([int16]$bitsPerSample)
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('data'))
    $writer.Write([int]$dataSize)

    foreach ($sample in $samples) {
        $value = [Math]::Max(-1.0, [Math]::Min(1.0, $sample * $normalization))
        $pcm = [int16][Math]::Truncate($value * [int16]::MaxValue)
        $writer.Write($pcm)
        $writer.Write($pcm)
    }
}
finally {
    $writer.Dispose()
}
