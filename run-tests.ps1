# Runs EditMode tests headlessly. Close the Unity Editor on this project first.
# Usage: ./run-tests.ps1 [-Filter "OpeningBell.Tests.PositionTests"]
param([string]$Filter = "")

$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe"
$project = $PSScriptRoot
$results = Join-Path $project "TestResults\editmode.xml"
$log = Join-Path $project "Logs\tests.log"
New-Item -ItemType Directory -Force (Split-Path $results) | Out-Null
if (Test-Path $results) { Remove-Item $results }

$unityArgs = @("-batchmode", "-nographics", "-projectPath", $project, "-runTests", "-testPlatform", "EditMode",
               "-testResults", $results, "-logFile", $log)
if ($Filter) { $unityArgs += @("-testFilter", $Filter) }
$proc = Start-Process -FilePath $unity -ArgumentList $unityArgs -Wait -PassThru

if (-not (Test-Path $results)) {
    Write-Output "No results (exit $($proc.ExitCode)). Compile errors:"
    Select-String -Path $log -Pattern "error CS" | Select-Object -First 20 | ForEach-Object { $_.Line }
    exit 1
}

[xml]$xml = Get-Content $results
$run = $xml.'test-run'
Write-Output "total $($run.total)  passed $($run.passed)  failed $($run.failed)  skipped $($run.skipped)  ($([math]::Round([double]$run.duration, 1))s)"
foreach ($case in $xml.SelectNodes("//test-case[@result='Failed']")) {
    Write-Output "FAIL $($case.fullname)"
    Write-Output ("  " + ($case.failure.message.InnerText -replace "`r?`n", "`n  ").Trim())
}
exit $proc.ExitCode
