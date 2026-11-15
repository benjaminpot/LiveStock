$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Wait-Idle {
    $deadline = [DateTime]::UtcNow.AddMinutes(2)
    $stable = 0
    while ($stable -lt 2) {
        try {
            $status = & unity command editor_status --caller plugin --skill unity-cli --format json | ConvertFrom-Json
            $idle = $status.success -and !$status.data.result.compiling -and !$status.data.result.domainReloadInProgress
            $stable = if ($idle) { $stable + 1 } else { 0 }
        }
        catch { $stable = 0 }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Editor did not settle before building.' }
        Start-Sleep -Seconds 2
    }
}
Push-Location -LiteralPath $projectRoot
$original = @{}
# Build callbacks generate or cache these files. Preserve any user-authored pre-build bytes, including meta GUIDs.
foreach ($path in @('Assets/_Project/Settings/PC_RPAsset.asset','Assets/_Project/Settings/UniversalRenderPipelineGlobalSettings.asset','ProjectSettings/ProjectSettings.asset',
    'Assets/Resources/PerformanceTestRunInfo.json','Assets/Resources/PerformanceTestRunInfo.json.meta',
    'Assets/Resources/PerformanceTestRunSettings.json','Assets/Resources/PerformanceTestRunSettings.json.meta',
    'Assets/StreamingAssets/RuntimeActionBindings.json','Assets/StreamingAssets/RuntimeActionBindings.json.meta')) {
    $original[$path] = if (Test-Path -LiteralPath $path) { [IO.File]::ReadAllBytes((Join-Path $projectRoot $path)) } else { $null }
}
try {
    $compile = & unity recompile --format json | ConvertFrom-Json
    if (!$compile.success -or $compile.data.failed) { throw ($compile | ConvertTo-Json -Depth 8) }
    Wait-Idle
    # Roslyn's temporary Pipeline assemblies can fail GetName() in SDK build callbacks on this Mono runtime.
    # Check unsaved scenes, then unload those assemblies before invoking the compiled build menu.
    $code = 'if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Stop Play Mode before building."); for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++) if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new System.InvalidOperationException("Save modified scenes before building."); UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorUtility.RequestScriptReload(); return true;'
    $reload = & unity command eval --caller plugin --skill unity-cli --timeout 30 --format json -- --code $code --timeout 15000 | ConvertFrom-Json
    if (!$reload.success -or $reload.data.result.success -ne $true) { throw ($reload | ConvertTo-Json -Depth 8) }
    Start-Sleep -Seconds 3
    $ready = & unity status --until-ready --format json | ConvertFrom-Json
    if (!$ready.success) { throw ($ready | ConvertTo-Json -Depth 8) }
    # The listener may reconnect before the compiler and delayed domain reload have settled.
    Wait-Idle
    $started = [DateTime]::UtcNow
    $response = & unity command menu --caller plugin --skill unity-cli --timeout 1900 --format json -- --path 'LiveStock/Connection Test/Build Fusion Validation Player' | ConvertFrom-Json
    if (!$response.success) { throw ($response | ConvertTo-Json -Depth 8) }
    $report = Get-Item -LiteralPath 'Docs/Fusion_Sync_Build_Report.json'
    $result = Get-Content -LiteralPath $report.FullName -Raw | ConvertFrom-Json
    if ($report.LastWriteTimeUtc -lt $started -or $result.result -ne 'Succeeded') {
        throw 'No fresh successful build report. Read Docs/Fusion_Sync_Build_Report.json.'
    }
    $result | ConvertTo-Json -Depth 8
}
finally {
    foreach ($path in $original.Keys) {
        $absolute = Join-Path $projectRoot $path
        if ($null -ne $original[$path]) { [IO.File]::WriteAllBytes($absolute, [byte[]]$original[$path]) }
        elseif (Test-Path -LiteralPath $absolute) { Remove-Item -LiteralPath $absolute -Force }
    }
    Pop-Location
}
