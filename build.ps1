$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    dotnet restore Playroma.JRPG.sln
    dotnet build Playroma.JRPG.sln --configuration Release --no-restore
}
finally {
    Pop-Location
}
