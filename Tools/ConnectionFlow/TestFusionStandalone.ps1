param([int]$SampleSeconds = 30)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$executable = Join-Path $projectRoot 'Builds/FusionValidation/LiveStockFusion.exe'
$runRoot = Join-Path $projectRoot ('.tmp/FusionStandalone-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$reportPath = Join-Path $projectRoot 'Docs/Fusion_Sync_Standalone_Report.json'
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$processes = @{}
$checks = [Collections.Generic.List[object]]::new()

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
    foreach ($role in @('server','desktop','vr-preview')) {
        $arguments = @('-batchmode','-nographics',('-flow-'+$role),'-logFile',
            (Join-Path $runRoot ($role+'.log')),'-fusion-probe-output',(Join-Path $runRoot ($role+'.json')))
        $processes[$role] = Start-Process -FilePath $executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    }
    Wait-For { (Read-Peer 'server').connected -and (Read-Peer 'desktop') -and (Read-Peer 'vr-preview') } 'process startup'
    Check 'Three independent Players start with zero Lobby NPCs' ((Read-Peer 'server').npcs -eq 0 -and $processes.Count -eq 3)
    Send-Command 'desktop' 'connect'
    Wait-For { (Read-Peer 'server').loaded -eq 1 -and (Read-Peer 'desktop').connected } 'Desktop connection'
    Check 'Desktop joins the independent Server' ((Read-Peer 'server').players -eq 1)
    Send-Command 'vr-preview' 'connect'
    Wait-For { (Read-Peer 'server').loaded -eq 2 -and (Read-Peer 'vr-preview').connected } 'VR connection'
    Wait-For { (Read-Peer 'server').vr.Count -eq 1 -and (Read-Peer 'desktop').vr.Count -eq 1 -and (Read-Peer 'vr-preview').desktop.Count -eq 1 } 'mixed replicas'
    Check 'Both roles replicate across three processes' ((Read-Peer 'server').canStart -and (Read-Peer 'server').players -eq 2)
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
    Send-Command 'server' 'start'
    Wait-For {
        $server = Read-Peer 'server'; $pc = Read-Peer 'desktop'; $vr = Read-Peer 'vr-preview'
        $server.phase -eq 'InMainGame' -and $pc.phase -eq 'InMainGame' -and $vr.phase -eq 'InMainGame' -and
            $server.npcs -eq 60 -and $pc.npcs -eq 60 -and $vr.npcs -eq 60
    } 'MainGame and 60 NPCs' 150
    Check 'All processes enter MainGame and receive 60 NPCs' $true
    $start = Read-Peer 'server'; $timer = [Diagnostics.Stopwatch]::StartNew()
    $minimumNPCs = 60
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
    Check 'Independent processes remain connected with stable NPC count' ($minimumNPCs -eq 60) @{
        seconds=$timer.Elapsed.TotalSeconds; minimumNPCs=$minimumNPCs
        observedServerTicksPerSecond=($end.tick-$start.tick)/$timer.Elapsed.TotalSeconds
        peers=$measurements; scope='Three headless Development Players on one Windows computer. Frame intervals include the 60 FPS cap and startup. GPU and HMD are unmeasured.'
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
finally {
    foreach ($role in $processes.Keys) {
        if (!$processes[$role].HasExited) { Send-Command $role 'quit' }
    }
    foreach ($process in $processes.Values) {
        if (!$process.WaitForExit(10000)) { $process.Kill() }
    }
}
