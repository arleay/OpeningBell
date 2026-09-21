# Runs tests headlessly. Close the Unity Editor on this project first.
# Usage: ./run-tests.ps1 [-Platform EditMode|PlayMode] [-Filter "OpeningBell.Tests.PositionTests"]
# PlayMode runs with a graphics device (UI renders; screenshots land in TestResults/).
param([string]$Filter = "", [ValidateSet("EditMode", "PlayMode")][string]$Platform = "EditMode")

$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe"
$project = $PSScriptRoot
$results = Join-Path $project "TestResults\$($Platform.ToLower()).xml"
$log = Join-Path $project "Logs\tests-$($Platform.ToLower()).log"
New-Item -ItemType Directory -Force (Split-Path $results) | Out-Null
if (Test-Path $results) { Remove-Item $results }

$unityArgs = @("-batchmode", "-projectPath", $project, "-runTests", "-testPlatform", $Platform,
               "-testResults", $results, "-logFile", $log)
if ($Platform -eq "EditMode") { $unityArgs += "-nographics" }
if ($Filter) { $unityArgs += @("-testFilter", $Filter) }
$proc = Start-Process -FilePath $unity -ArgumentList $unityArgs -PassThru
$proc.WaitForExit()  # -Wait would also wait on Unity's lingering child processes

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

