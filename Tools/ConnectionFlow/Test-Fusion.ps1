param(
    [ValidateSet('Editor','Assets','Standalone','WeakNetwork','MapSwap','Soak','Fruit')]
    [string]$Suite = 'Editor',
    [ValidateRange(1,3600)][int]$SoakSeconds = 600,
    [ValidateSet(60,100)][int]$NpcCount = 60
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$logRoot = Join-Path $projectRoot '.tmp/FusionAcceptance'
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
Push-Location -LiteralPath $projectRoot

function Invoke-Unity([string[]]$Arguments) {
    $command = $Arguments[0]
    $parameters = @($Arguments | Select-Object -Skip 1)
    $response = & unity command $command --caller plugin --skill unity-cli --format json @parameters | ConvertFrom-Json
    if (!$response.success -or $response.data.result.success -eq $false) {
        throw ($response | ConvertTo-Json -Depth 12)
    }
    $response.data.result
}

function Wait-Mode([string]$Mode) {
    $deadline = [DateTime]::UtcNow.AddMinutes(2)
    do {
        # A domain reload can temporarily close the Pipeline connection. Only status reads are retried.
        try { $state = Invoke-Unity @('editor_status') }
        catch { $state = $null }
        if ($state -and ($Mode -eq '*' -or $state.playMode -eq $Mode) -and !$state.compiling -and !$state.domainReloadInProgress) { return }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Editor did not reach $Mode mode."
}

function Stop-Test {
    Wait-Mode '*'
    Invoke-Unity @('menu','--','--path','LiveStock/Connection Test/End Test') | Out-Null
    Wait-Mode 'stopped'
}

function Open-Menu([string]$Role = 'Desktop') {
    Stop-Test
    $menu = if ($Role -eq 'VR') { 'Open VR Client Menu (no headset)' } else { 'Open Desktop Client Menu' }
    Invoke-Unity @('menu','--','--path',('LiveStock/Connection Test/'+$menu)) | Out-Null
    Wait-Mode 'playing'
    # Let scene Start callbacks create the offline client before loading a test assembly.
    Start-Sleep -Seconds 2
}

function Run-Test([string]$File, [string]$Entry, [string]$TestArguments = '[]') {
    $response = & unity command run_script --caller plugin --skill unity-cli --timeout 3900 --format json -- `
        --file ('Tools/ConnectionFlow/'+$File+'.cs') --entry $Entry --args $TestArguments --timeout_ms 3800000
    $response | Set-Content -LiteralPath (Join-Path $logRoot ($Entry+'.json')) -Encoding utf8
    $parsed = $response | ConvertFrom-Json
    if (!$parsed.success -or $parsed.data.result.success -ne $true) { throw $response }
    Write-Output ('PASS '+$Entry)
}

try {
    if ($Suite -eq 'Standalone') {
        & (Join-Path $PSScriptRoot 'TestFusionStandalone.ps1') -SampleSeconds 30 -UseGraphics
    }
    else {
        try {
            switch ($Suite) {
                'Editor' {
                    foreach ($test in @(
                        @('Desktop','FusionSyncMixedAcceptance','DesktopFirst'),
                        @('VR','FusionSyncMixedAcceptance','VRFirst'),
                        @('Desktop','NetworkWorldAcceptance','Run'),
                        @('Desktop','FusionSyncLoadingEdges','Run'),
                        @('Desktop','FusionSynchronizationAcceptance','Run')
                    )) {
                        Open-Menu $test[0]
                        Run-Test $test[1] ($test[1]+'.'+$test[2])
                    }
                }
                'Assets' {
                    Stop-Test
                    Run-Test 'ValidateFusionAssets' 'ValidateFusionAssets.RunContractChecks'
                    Run-Test 'ValidateFusionAssets' 'ValidateFusionAssets.Run'
                }
                'Fruit' { Open-Menu; Run-Test 'FruitGameplayAcceptance' 'FruitGameplayAcceptance.Run' }
                'WeakNetwork' { Open-Menu; Run-Test 'FusionWeakNetworkAcceptance' 'FusionWeakNetworkAcceptance.Run' }
                'MapSwap' {
                    Stop-Test
                    if (Test-Path -LiteralPath '.tmp/MapSwapSettings.json') { throw 'Restore the previous map test before starting another.' }
                    try {
                        Run-Test 'MapSwapAcceptance' 'MapSwapAcceptance.Prepare'
                        Open-Menu
                        Run-Test 'FusionSynchronizationAcceptance' 'FusionSynchronizationAcceptance.Run'
                    }
                    finally {
                        Stop-Test
                        if (Test-Path -LiteralPath '.tmp/MapSwapSettings.json') {
                            Run-Test 'MapSwapAcceptance' 'MapSwapAcceptance.Restore'
                        }
                    }
                }
                'Soak' {
                    Open-Menu
                    Run-Test 'FusionSynchronizationAcceptance' 'FusionSynchronizationAcceptance.Run'
                    Run-Test 'FusionSynchronizationAcceptance' 'FusionSynchronizationAcceptance.Soak' "[$SoakSeconds,$NpcCount]"
                }
            }
        }
        finally { Stop-Test }
    }
}
finally { Pop-Location }
