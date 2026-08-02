$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "src\LensFlow.App\LensFlow.App.csproj"

if (-not (Test-Path (Join-Path $repositoryRoot "tools\ffmpeg\bin\ffmpeg.exe"))) {
    Write-Warning "FFmpeg is missing. Recording and editing work, but export requires scripts\setup-ffmpeg.ps1."
}

dotnet run --project $project -c Debug -p:Platform=x64
