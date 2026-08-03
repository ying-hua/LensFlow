$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolsDirectory = Join-Path $repositoryRoot "tools\ffmpeg"
$binaryDirectory = Join-Path $toolsDirectory "bin"
$archivePath = Join-Path $env:TEMP "lensflow-ffmpeg-8.1-lgpl.zip"
$extractPath = Join-Path $env:TEMP "lensflow-ffmpeg-8.1-lgpl"
$downloadUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.1-latest-win64-lgpl-8.1.zip"

New-Item -ItemType Directory -Force -Path $binaryDirectory | Out-Null

Write-Host "Downloading the LGPL FFmpeg 8.1 Windows x64 build..."
Invoke-WebRequest $downloadUrl -OutFile $archivePath

if (Test-Path $extractPath) {
    Remove-Item -LiteralPath $extractPath -Recurse -Force
}

Expand-Archive -Path $archivePath -DestinationPath $extractPath -Force
$ffmpeg = Get-ChildItem -Path $extractPath -Recurse -File -Filter "ffmpeg.exe" |
    Select-Object -First 1
if (-not $ffmpeg) {
    throw "ffmpeg.exe was not found in the downloaded archive."
}

# This build is dynamically linked (avcodec-*.dll, avformat-*.dll, etc. sit
# alongside ffmpeg.exe in the same folder), so the whole folder must be copied
# together - copying only ffmpeg.exe leaves it unable to load at runtime
# (STATUS_DLL_NOT_FOUND).
Copy-Item (Join-Path $ffmpeg.DirectoryName "*") $binaryDirectory -Recurse -Force
Remove-Item -LiteralPath $archivePath -Force
Remove-Item -LiteralPath $extractPath -Recurse -Force

& (Join-Path $binaryDirectory "ffmpeg.exe") -hide_banner -version |
    Select-Object -First 1
Write-Host "FFmpeg is ready."
