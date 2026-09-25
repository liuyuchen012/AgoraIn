# AgoraIn v4 质量门禁：build → test → selftest（全部通过才算阶段完成）
# 用法：powershell -ExecutionPolicy Bypass -File scripts/gate.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "== [1/3] dotnet build ==" -ForegroundColor Cyan
dotnet build AgoraIn.sln -c Debug
if ($LASTEXITCODE -ne 0) { Write-Host "门禁失败：build" -ForegroundColor Red; exit 1 }

Write-Host "== [2/3] dotnet test ==" -ForegroundColor Cyan
dotnet test AgoraIn.sln --no-build -v minimal
if ($LASTEXITCODE -ne 0) { Write-Host "门禁失败：test" -ForegroundColor Red; exit 1 }

Write-Host "== [3/3] selftest ==" -ForegroundColor Cyan
$exe = Join-Path $root "src/AgoraIn.App/bin/Debug/net10.0/AgoraIn.exe"
$report = Join-Path $root "out/selftest-report.txt"
& $exe --selftest --out $report
if ($LASTEXITCODE -ne 0) { Write-Host "门禁失败：selftest" -ForegroundColor Red; exit 1 }

Write-Host "质量门禁通过：build + test + selftest 全绿" -ForegroundColor Green
Get-Content $report
exit 0
