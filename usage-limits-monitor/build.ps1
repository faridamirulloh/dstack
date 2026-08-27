param([switch]$Test, [string]$ProbeCodex = '', [switch]$TestsOnly, [switch]$Smoke)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler is required.' }
$output = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$sources = @('Model.cs', 'CodexClient.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$references = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Web.Extensions.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')
if ($Test -or $TestsOnly -or $ProbeCodex) {
    $testExe = Join-Path $output 'MonitorChecks.exe'
    & $compiler /nologo /warnaserror+ /optimize+ /target:exe "/out:$testExe" $references $sources (Join-Path $PSScriptRoot 'SelfTest.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Checks did not compile.' }
    & $testExe $output $ProbeCodex
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed. See artifacts/checks.txt.' }
}
if (-not $TestsOnly) {
    $app = Join-Path $output 'UsageLimitsMonitor.exe'
    & $compiler /nologo /warnaserror+ /optimize+ /target:winexe "/out:$app" "/win32manifest:$PSScriptRoot\app.manifest" $references $sources (Join-Path $PSScriptRoot 'Desktop.cs')
    if ($LASTEXITCODE -ne 0) { throw 'The app did not compile.' }
    $packageFiles = @($app)
    foreach ($document in @('README.md', 'CONTEXT.md', 'DESIGN-REVIEW.md')) {
        $destination = Join-Path $output $document
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $document) -Destination $destination -Force
        $packageFiles += $destination
    }
    Compress-Archive -LiteralPath $packageFiles -DestinationPath (Join-Path $output 'UsageLimitsMonitor-portable.zip') -Force
    Write-Output "Portable app: $app"
    if ($Smoke) {
        $monitorCheck = Start-Process -WindowStyle Hidden -FilePath $app -ArgumentList @('--smoke-test', ('"' + $output + '"')) -PassThru
        if (-not $monitorCheck.WaitForExit(15000)) { $monitorCheck.Kill(); throw 'UI check timed out.' }
        if ($monitorCheck.ExitCode -ne 0) { throw 'UI check failed.' }
        Get-Content -LiteralPath (Join-Path $output 'ui-checks.txt')
    }
}
