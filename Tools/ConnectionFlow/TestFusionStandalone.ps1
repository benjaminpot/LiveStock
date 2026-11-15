param([int]$SampleSeconds = 30, [switch]$UseGraphics, [switch]$WithBrowser, [switch]$BatchGraphics)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$executable = Join-Path $projectRoot 'Builds/FusionValidation/LiveStockFusion.exe'
$runRoot = Join-Path $projectRoot ('.tmp/FusionStandalone-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$reportPath = Join-Path $projectRoot 'Docs/Fusion_Map_Standalone_Report.json'
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$processes = @{}
$checks = [Collections.Generic.List[object]]::new()
$browserProcess = $null
$browserEvidence = Join-Path $runRoot 'browser-evidence.json'

function Read-Browser {
    if (!(Test-Path -LiteralPath $browserEvidence)) { return $null }
    try { Get-Content -LiteralPath $browserEvidence -Raw | ConvertFrom-Json } catch { return $null }
}

function Read-Peer([string]$Role) {
    $path = Join-Path $runRoot ($Role + '.json')
    if (!(Test-Path -LiteralPath $path)) { return $null }
    try { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
    catch { return $null } # A snapshot write may overlap this read.
}

function Send-Command([string]$Role, [string]$Command) {
    Set-Content -LiteralPath (Join-Path $runRoot ($Role + '.json.command')) -Value $Command
}

function Wait-For([scriptblock]$Condition, [string]$Label, [int]$Seconds = 90) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while (!( & $Condition)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Timed out waiting for $Label" }
        foreach ($process in $processes.Values) {
            if ($process.HasExited) { throw "Player $($process.Id) exited during $Label" }
        }
        Start-Sleep -Milliseconds 300
    }
}

function Check([string]$Label, [bool]$Pass, $Evidence = $null) {
    $checks.Add([pscustomobject]@{test=$Label; pass=$Pass; evidence=$Evidence})
    $checks | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $reportPath
    if (!$Pass) { throw $Label }
    Write-Output "PASS $Label"
}

function Distance($A, $B) {
    [Math]::Sqrt([Math]::Pow($A.x-$B.x,2) + [Math]::Pow($A.y-$B.y,2) + [Math]::Pow($A.z-$B.z,2))
}

try {
    '[]' | Set-Content -LiteralPath $reportPath
    if ($WithBrowser) {
        if (!$UseGraphics) { throw 'Browser video acceptance requires -UseGraphics.' }
        $browserHost = if ($BatchGraphics) { 'StandaloneBatchGraphics' } else { 'StandaloneHidden' }
        $browserProcess = Start-Process -FilePath 'node' -ArgumentList @(
            ('"'+(Join-Path $PSScriptRoot 'TestCctvBrowser.mjs')+'"'), ('"'+$browserEvidence+'"'), $browserHost
        ) -WindowStyle Hidden -PassThru
    }
    foreach ($role in @('server','desktop','vr-preview')) {
        $graphicsOption = if ($UseGraphics) { '-force-d3d11' } else { '-nographics' }
        # Keep batch graphics explicit: a graphics device alone does not prove that cameras produce video.
        $modeArguments = if ($UseGraphics -and !$BatchGraphics) { @('-screen-fullscreen','0','-screen-width','640','-screen-height','360') } else { @('-batchmode') }
        $arguments = @($modeArguments) + @($graphicsOption,('-flow-'+$role),'-logFile',
            (Join-Path $runRoot ($role+'.log')),'-fusion-probe-output',(Join-Path $runRoot ($role+'.json')))
        $processes[$role] = Start-Process -FilePath $executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    }
    Wait-For { (Read-Peer 'server').connected -and (Read-Peer 'desktop') -and (Read-Peer 'vr-preview') } 'process startup'
    Check 'All processes expose the current acceptance probe' (@('server','desktop','vr-preview').Where({(Read-Peer $_).probeVersion -ne 4}).Count -eq 0)
    Check 'Three independent Players start with zero Lobby NPCs' ((Read-Peer 'server').npcs -eq 0 -and $processes.Count -eq 3)
    Send-Command 'desktop' 'connect'
    Wait-For { (Read-Peer 'server').loaded -eq 1 -and (Read-Peer 'desktop').connected } 'Desktop connection'
    Check 'Desktop joins the independent Server' ((Read-Peer 'server').players -eq 1)
    Send-Command 'vr-preview' 'connect'
    Wait-For { (Read-Peer 'server').loaded -eq 2 -and (Read-Peer 'vr-preview').connected } 'VR connection'
    Wait-For { (Read-Peer 'server').vr.Count -eq 1 -and (Read-Peer 'desktop').vr.Count -eq 1 -and (Read-Peer 'vr-preview').desktop.Count -eq 1 } 'mixed replicas'
    Check 'Both roles replicate across three processes' (!(Read-Peer 'server').canStart -and (Read-Peer 'server').players -eq 2)
    $beforeVR = (Read-Peer 'vr-preview').local
    $beforePC = (Read-Peer 'desktop').local
    Send-Command 'desktop' 'move'
    Send-Command 'vr-preview' 'move'
    Wait-For {
        $server = Read-Peer 'server'; $pc = Read-Peer 'desktop'; $vr = Read-Peer 'vr-preview'
        $server.vr.Count -eq 1 -and (Distance $server.vr[0].root $vr.local.root) -lt .01 -and
            (Distance $server.desktop[0].root $pc.local.root) -lt .01 -and
            (Distance $beforeVR.root $vr.local.root) -gt .5 -and (Distance $beforePC.root $pc.local.root) -gt .5
    } 'client-selected movement'
    Start-Sleep -Seconds 2
    $server = Read-Peer 'server'; $pc = Read-Peer 'desktop'; $vr = Read-Peer 'vr-preview'
    $rootError = [Math]::Max((Distance $pc.vr[0].root $vr.local.root), (Distance $vr.desktop[0].root $pc.local.root))
    Check 'Client-selected roots reach the other Client' ($rootError -lt .01) $rootError
    $targetError = 0.0
    foreach ($target in @('head','left','right')) {
        $targetError = [Math]::Max($targetError,(Distance $server.vr[0].$target $vr.local.$target))
        $targetError = [Math]::Max($targetError,(Distance $pc.vr[0].$target $vr.local.$target))
    }
    Check 'All three VR targets reach Server and Desktop' ($targetError -lt .01) $targetError
    Send-Command 'server' 'load'
    Wait-For { (Read-Peer 'server').canStart -and (Read-Peer 'desktop').phase -eq 'AwaitingStart' -and (Read-Peer 'vr-preview').phase -eq 'AwaitingStart' } 'map readiness'
    Check 'Loaded map waits with zero NPCs' ((Read-Peer 'server').npcs -eq 0 -and (Read-Peer 'desktop').npcs -eq 0 -and (Read-Peer 'vr-preview').npcs -eq 0)
    Send-Command 'server' 'start'
    Wait-For {
        $server = Read-Peer 'server'; $pc = Read-Peer 'desktop'; $vr = Read-Peer 'vr-preview'
        $server.phase -eq 'Running' -and $pc.phase -eq 'Running' -and $vr.phase -eq 'Running' -and
            $server.npcs -ge $server.npcMinimum -and $server.npcs -gt 0 -and
            $server.npcs -le $server.npcMaximum -and $pc.npcs -eq $server.npcs -and $vr.npcs -eq $server.npcs
    } 'MainGame and configured map NPCs' 150
    $npcCount = (Read-Peer 'server').npcs
    Check 'All processes receive the authored map NPC count' $true $npcCount
    Wait-For { (Read-Peer 'server').apples -eq 3 -and (Read-Peer 'desktop').apples -eq 3 -and (Read-Peer 'vr-preview').apples -eq 3 } 'three replicated apples'
    Check 'Standalone Server keeps its world visible for CCTV rendering' ((Read-Peer 'server').runnerVisible)
    if ($WithBrowser) {
        Set-Content -LiteralPath ($browserEvidence+'.connect') -Value 'connect'
        Wait-For { (Read-Browser).ready -or (Read-Browser).errors.Count -gt 0 } 'two browser video streams' 150
        Send-Command 'server' 'capture-cctv'
        Start-Sleep -Seconds 1
        Check 'Two browsers decode advancing video and receive 0/3' ((Read-Browser).ready -and (Read-Browser).errors.Count -eq 0) (Read-Browser)
    }
    foreach ($index in 0..2) {
        $oldTarget = (Read-Peer 'desktop').targetFruit
        Send-Command 'desktop' ('approach-fruit '+$index)
        Wait-For { (Read-Peer 'desktop').targetFruit -ne 0 -and (Read-Peer 'desktop').targetFruit -ne $oldTarget } 'desktop camera aims at nearby apple'
        Start-Sleep -Seconds 2 # Let the body input reach the Server before the interaction RPC.
        if ($index -eq 0) {
            Send-Command 'desktop' 'input-off'
            Wait-For { !(Read-Peer 'desktop').inputActive } 'disabled input source'
            Start-Sleep -Seconds 1
            Send-Command 'desktop' 'collect-fruit'
            Wait-For { (Read-Peer 'desktop').feedback -eq '等待玩家追蹤資料' } 'Server rejection after input source is disabled'
            Check 'Disabled input source cannot collect with its old cached pose' ((Read-Peer 'server').collected -eq 0)
            Send-Command 'desktop' 'input-on'
            Wait-For { (Read-Peer 'desktop').inputActive } 'input source resumes'
            Start-Sleep -Seconds 1
            Check 'Input source resumes without reconnecting' ((Read-Peer 'desktop').connected)
        }
        Send-Command 'desktop' 'collect-fruit'
        $expected = $index + 1
        Wait-For { (Read-Peer 'server').collected -eq $expected -and (Read-Peer 'desktop').collected -eq $expected -and (Read-Peer 'vr-preview').collected -eq $expected } 'shared fruit progress'
        Check ('Apple '+$expected+' collection reaches all three processes') $true
        Send-Command 'desktop' 'repeat-fruit'
        Start-Sleep -Seconds 1
        Check 'Duplicate RPC does not increase progress' ((Read-Peer 'server').collected -eq $expected)
    }
    Check 'Both clients show completion from shared state' ((Read-Peer 'desktop').missionComplete -and (Read-Peer 'vr-preview').missionComplete -and (Read-Peer 'desktop').feedback -eq '蘋果已收集完成！')
    if ($WithBrowser) {
        Wait-For { (Read-Browser).reconnected -or (Read-Browser).errors.Count -gt 0 } 'browser completion and reconnect' 150
        Check 'Both browsers complete and reconnect with current state' ((Read-Browser).reconnected -and (Read-Browser).errors.Count -eq 0) (Read-Browser)
    }
    $start = Read-Peer 'server'; $timer = [Diagnostics.Stopwatch]::StartNew()
    $minimumNPCs = $npcCount
    while ($timer.Elapsed.TotalSeconds -lt $SampleSeconds) {
        Start-Sleep -Seconds 1
        foreach ($role in $processes.Keys) {
            $peer = Read-Peer $role
            if (!$peer) { continue }
            if (!$peer.connected) { throw "$role disconnected during sampling" }
            $minimumNPCs = [Math]::Min($minimumNPCs,$peer.npcs)
        }
    }
    $end = Read-Peer 'server'
    $measurements = foreach ($role in @('server','desktop','vr-preview')) {
        $peer = Read-Peer $role
        [pscustomobject]@{role=$role; npcs=$peer.npcs; frame=$peer.frame; averageFrameIntervalMs=$peer.averageFrameIntervalMs; allocatedBytes=$peer.allocatedBytes}
    }
    Check 'Independent processes remain connected with stable NPC count' ($minimumNPCs -eq $npcCount) @{
        seconds=$timer.Elapsed.TotalSeconds; minimumNPCs=$minimumNPCs
        observedServerTicksPerSecond=($end.tick-$start.tick)/$timer.Elapsed.TotalSeconds
        peers=$measurements; scope=('Three Development Players on one Windows computer. Graphics enabled = '+[bool]$UseGraphics+', batch graphics = '+[bool]$BatchGraphics+'. Frame intervals include the 60 FPS cap and startup. GPU and HMD frame time are unmeasured.')
    }
    Check 'Server continues accepting inputs during NPC simulation' ($end.acceptedInputs -gt $start.acceptedInputs)
    Send-Command 'server' 'stop'
    Wait-For { !(Read-Peer 'server').connected -and !(Read-Peer 'desktop').connected -and !(Read-Peer 'vr-preview').connected } 'Server shutdown'
    Wait-For { (Read-Peer 'server').npcs -eq 0 -and (Read-Peer 'desktop').npcs -eq 0 -and (Read-Peer 'vr-preview').npcs -eq 0 } 'replica cleanup'
    Check 'Stopping Server disconnects both Clients and removes NPCs' $true
    $errors = @(Get-ChildItem -LiteralPath $runRoot -Filter '*.log' | Select-String -Pattern '^(\w+Exception:|Can.t remove RunnerEnableVisibility|EndRenderPass:|BeginRenderPass:)')
    Check 'Player logs contain no detected runtime exceptions or render-pass errors' ($errors.Count -eq 0) @($errors | ForEach-Object {$_.Line})
    Write-Output "Report $reportPath"
}
catch {
    $checks.Add([pscustomobject]@{test='Standalone acceptance exception'; pass=$false; evidence=$_.Exception.Message})
    $checks | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $reportPath
    throw
}
finally {
    foreach ($role in $processes.Keys) {
        if (!$processes[$role].HasExited) { Send-Command $role 'quit' }
    }
    foreach ($process in $processes.Values) {
        if (!$process.WaitForExit(10000)) { $process.Kill() }
    }
    if ($browserProcess -and !$browserProcess.HasExited) {
        Set-Content -LiteralPath ($browserEvidence+'.stop') -Value 'stop'
        if (!$browserProcess.WaitForExit(10000)) { $browserProcess.Kill($true) }
    }
}
