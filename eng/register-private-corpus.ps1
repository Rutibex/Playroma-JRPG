param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,

    [string]$Name = "Private MV Project"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Resolved = (Resolve-Path $ProjectPath).Path
$OutputDirectory = Join-Path $Root "testdata/private"
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$Manifest = [ordered]@{
    schemaVersion = 2
    name = $Name
    root = $Resolved
    environmentVariable = "PLAYROMA_PRIVATE_MV_PROJECT"
    registeredUtc = [DateTime]::UtcNow.ToString("O")
    copiedIntoRepository = $false
    contentHashesRecorded = $false
    expected = [ordered]@{
        maps = 138
        mapEvents = 3296
        mapEventCommands = 152687
        enabledPlugins = 70
        actors = 56
        classes = 101
        skills = 1250
        items = 500
        weapons = 1250
        armors = 400
        enemies = 370
        troops = 175
        states = 400
    }
}

$SafeName = ($Name -replace '[^A-Za-z0-9._-]', '_')
$Output = Join-Path $OutputDirectory ("private-corpus.{0}.json" -f $SafeName)
$Manifest | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 -LiteralPath $Output
Write-Host "Registered a local private-corpus pointer without copying or hashing project content: $Output"
Write-Host "For this shell run: `$env:PLAYROMA_PRIVATE_MV_PROJECT = '$Resolved'"
