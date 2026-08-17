$ErrorActionPreference = 'Stop'

$crossPlatform = @(
  'tests/Playroma.JRPG.Baseline.Tests',
  'tests/Playroma.JRPG.ProjectLoading.Tests',
  'tests/Playroma.JRPG.LosslessDocuments.Tests',
  'tests/Playroma.JRPG.Phase3.Tests',
  'tests/Playroma.JRPG.Phase5.Tests',
  'tests/Playroma.JRPG.Phase6.Tests',
  'tests/Playroma.JRPG.Phase7.Tests',
  'tests/Playroma.JRPG.Phase8.Tests',
  'tests/Playroma.JRPG.Phase9A.Tests',
  'tests/Playroma.JRPG.Phase9B.Tests',
  'tests/Playroma.JRPG.Phase9B.FormulaHost.Tests',
  'tests/Playroma.JRPG.Phase9C.Tests',
  'tests/Playroma.JRPG.Phase9D.Tests',
  'tests/Playroma.JRPG.Phase9E.Tests',
  'tests/Playroma.JRPG.Eventing.Tests',
  'tests/Playroma.JRPG.Phase10.Tests',
  'tests/Playroma.JRPG.Phase10.FuzzTests',
  'tests/Playroma.JRPG.PluginTags.Phase10.Tests',
  'tests/Playroma.Sprite.Core.Tests',
  'tests/Playroma.Sprite.Formats.Tests',
  'tests/Playroma.Sprite.Resilience.Tests',
  'tests/Playroma.Sprite.Automation.Tests',
  'tests/Playroma.Text.Phase12.Tests',
  'tests/Playroma.Text.Phase14.FuzzTests'
)

$windowsOnly = @(
  'tests/Playroma.JRPG.Editor.WinForms.SmokeTests',
  'tests/Playroma.JRPG.AssetPreview.Windows.Tests',
  'tests/Playroma.JRPG.MapRendering.Windows.Tests',
  'tests/Playroma.JRPG.Phase9A.WinForms.Tests',
  'tests/Playroma.JRPG.Phase9B.WinForms.Tests',
  'tests/Playroma.JRPG.Phase9C.WinForms.Tests',
  'tests/Playroma.JRPG.Phase9D.WinForms.Tests',
  'tests/Playroma.JRPG.Phase9E.WinForms.Tests',
  'tests/Playroma.JRPG.Phase10.WinForms.Tests',
  'tests/Playroma.JRPG.Phase11.WinForms.Tests',
  'tests/Playroma.Sprite.Editor.WinForms.SmokeTests',
  'tests/Playroma.Text.Phase14.Tests'
)

foreach ($project in $crossPlatform) {
  Write-Host "==> $project"
  dotnet run --project $project --configuration Release --no-build
}

if ($IsWindows) {
  foreach ($project in $windowsOnly) {
    Write-Host "==> $project"
    dotnet run --project $project --configuration Release --no-build
  }
}
