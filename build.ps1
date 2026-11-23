# Builds dist\Art14Helper.exe for 64-bit Windows: one native exe compiled with NativeAOT.
# NativeAOT links with the Visual Studio C++ tools ("Desktop development with C++"). Without
# them it falls back to a trimmed self-contained single file, which runs the same but is
# larger and starts slower. -RequireAot fails instead (CI); -SelfTest runs the built exe's
# self-test afterwards.
param([switch]$RequireAot, [switch]$SelfTest)
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'Art14Helper.csproj'
$dist = Join-Path $PSScriptRoot 'dist'
$exe = Join-Path $dist 'Art14Helper.exe'

function Publish([string[]]$extra) {
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
    & dotnet publish $project --configuration Release --runtime win-x64 --output $dist `
        -p:DebugType=None -p:DebugSymbols=false @extra | Out-Host
    return $LASTEXITCODE -eq 0
}

$kind = 'NativeAOT'
if (-not (Publish @())) {
    if ($RequireAot) { throw 'NativeAOT build failed.' }
    Write-Warning 'NativeAOT build failed (are the C++ build tools installed?). Building a trimmed single file instead.'
    $kind = 'trimmed single file'
    $ok = Publish @('-p:PublishAot=false', '-p:SelfContained=true', '-p:PublishTrimmed=true', '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true')
    if (-not $ok) { throw 'Publishing failed.' }
}

# Only the exe is shipped.
Get-ChildItem $dist -File | Where-Object Name -ne 'Art14Helper.exe' | Remove-Item -Force
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
"Built $exe ($kind, $size MB)"

if ($SelfTest) {
    $run = Start-Process $exe -ArgumentList '--self-test' -Wait -PassThru
    if ($run.ExitCode -ne 0) {
        Get-Content (Join-Path $env:LOCALAPPDATA 'Art14Helper\Art14Helper.log') -Tail 20 -ErrorAction SilentlyContinue
        throw "Self-test failed (exit code $($run.ExitCode))."
    }
    'Self-test passed'
}
