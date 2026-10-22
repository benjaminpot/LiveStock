param(
    [string]$EditorData = 'C:/Program Files/Unity/Hub/Editor/6000.3.25f1-x86_64/Editor/Data'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testFolder = Join-Path ([IO.Path]::GetTempPath()) ('LiveStock-CCTVTest-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testFolder | Out-Null
$dotnetPath = Join-Path $EditorData 'NetCoreRuntime/dotnet.exe'
$compilerPath = Join-Path $EditorData 'DotNetSdkRoslyn/csc.dll'
$outputPath = Join-Path $testFolder 'CCTVTests.dll'
& $dotnetPath $compilerPath /nologo /target:exe ("/out:$outputPath") `
    ('/r:' + (Join-Path $EditorData 'NetStandard/ref/2.1.0/netstandard.dll')) `
    (Join-Path $projectRoot 'Assets/_Project/Scripts/CCTVMonitorState.cs') `
    (Join-Path $PSScriptRoot 'CCTVMonitorStateTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'CCTV rule tests did not compile.' }
$runtimeFolder = Get-ChildItem -LiteralPath (Join-Path $EditorData 'NetCoreRuntime/shared/Microsoft.NETCore.App') -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$runtimeConfig = @{ runtimeOptions = @{ tfm = 'net6.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $runtimeFolder.Name } } }
$runtimeConfig | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $testFolder 'CCTVTests.runtimeconfig.json')
& $dotnetPath $outputPath
if ($LASTEXITCODE -ne 0) { throw 'CCTV rule tests failed.' }
