param([switch]$NoBuild)

$root = Split-Path $PSScriptRoot
$project = Join-Path $root 'src\Supermonitor\Supermonitor.csproj'
if (-not $NoBuild) {
    dotnet build $project -c Debug -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$exe = Get-ChildItem (Join-Path $root 'src\Supermonitor\bin\x64\Debug') -Recurse -Filter Supermonitor.exe | Select-Object -First 1
if (-not $exe) { throw 'Supermonitor.exe not found; build first.' }
Start-Process $exe.FullName -Verb RunAs
