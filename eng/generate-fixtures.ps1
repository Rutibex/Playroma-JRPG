$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Push-Location $Root
try {
    dotnet run --project tools/Playroma.JRPG.FixtureBuilder -c Release
}
finally {
    Pop-Location
}
