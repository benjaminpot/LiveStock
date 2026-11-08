param([switch]$WithDiagnostics)
$ErrorActionPreference = 'Stop'
$flowRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location -LiteralPath $flowRoot
unity command recompile --caller plugin --skill unity-cli --format json | Out-Null
$flowCompileDeadline = [DateTime]::UtcNow.AddMinutes(3)
do {
    $flowCompile = unity command recompile_status --caller plugin --skill unity-cli --format json | ConvertFrom-Json
    if ($flowCompile.success -and $flowCompile.data.result.compilationFailed) { throw 'Editor script compilation failed.' }
    if ($flowCompile.success -and $flowCompile.data.result.status -ne 'compiling') { break }
    if ([DateTime]::UtcNow -gt $flowCompileDeadline) { throw 'Editor compilation did not settle.' }
    Start-Sleep -Seconds 2
} while ($true)
$flowConfigPath = Join-Path $flowRoot 'ProjectSettings/Packages/com.unity.pipeline/RuntimePipelineConfig.json'
$flowConfigExisted = Test-Path -LiteralPath $flowConfigPath
$flowConfigBefore = if ($flowConfigExisted) { [System.IO.File]::ReadAllBytes($flowConfigPath) } else { $null }
$flowXRStateFile = Join-Path $flowRoot '.tmp/flow_build_xr_state.json'
if (Test-Path -LiteralPath $flowXRStateFile) { Remove-Item -LiteralPath $flowXRStateFile }
try {
    if ($WithDiagnostics) {
        $flowConfig = if ($flowConfigExisted) { Get-Content -Raw -Encoding UTF8 -LiteralPath $flowConfigPath | ConvertFrom-Json } else {
            [pscustomobject]@{enableInBuilds=$false;port=0;requestTimeoutMs=30000;enableAuditLogging=$true;autoStart=$true;maxWorkItemsPerFrame=10}
        }
        $flowConfig.enableInBuilds = $true
        $flowConfig.autoStart = $true
        [System.IO.Directory]::CreateDirectory((Split-Path $flowConfigPath)) | Out-Null
        [System.IO.File]::WriteAllText($flowConfigPath, ($flowConfig | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
    }
    # Input System enumerates assembly CodeBase while building. Drop all temporary eval assemblies first.
    $flowReloadFile = Join-Path $flowRoot '.tmp/flow_build_reload.cs'
    $flowReloadToken = [Guid]::NewGuid().ToString('N')
    $flowReloadDoneFile = Join-Path $flowRoot '.tmp/flow_build_reload_done.txt'
    $flowReloadCode = @'
var settings=UnityEngine.XR.Management.XRGeneralSettings.Instance;
if(settings!=null){
 var path=UnityEditor.AssetDatabase.GetAssetPath(settings);
 System.IO.File.Copy(path,".tmp/flow_build_xr_before.asset",true);
 System.IO.File.WriteAllText(".tmp/flow_build_xr_state.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{assetPath=path}));
 settings.InitManagerOnStart=false;UnityEditor.EditorUtility.SetDirty(settings);UnityEditor.AssetDatabase.SaveAssetIfDirty(settings);
}
UnityEditor.SessionState.SetString("LiveStock.FlowBuildReloadToken","FLOW_RELOAD_TOKEN");
UnityEditor.EditorUtility.RequestScriptReload(); return "Reload queued before native build";
'@
    $flowReloadCode = $flowReloadCode.Replace('FLOW_RELOAD_TOKEN', $flowReloadToken)
    [System.IO.File]::WriteAllText($flowReloadFile, $flowReloadCode)
    $flowReload = unity command eval_file --caller plugin --skill unity-cli --format json --file $flowReloadFile -- --timeout 10000 | ConvertFrom-Json
    # A domain reload may close the RPC connection before it returns; the token below is the authority.
    $flowReloadDeadline = [DateTime]::UtcNow.AddMinutes(3)
    while (!(Test-Path -LiteralPath $flowReloadDoneFile) -or [System.IO.File]::ReadAllText($flowReloadDoneFile) -ne $flowReloadToken) {
        if ([DateTime]::UtcNow -gt $flowReloadDeadline) { throw 'Script domain reload did not complete before the build.' }
        Start-Sleep -Seconds 1
    }
    $flowReady = unity status --until-ready --format json | ConvertFrom-Json
    if (!$flowReady.success) { throw 'Editor not ready after domain reload.' }
    $flowBindings = Join-Path $flowRoot 'Builds/ConnectionTest/RuntimeActionBindings.json'
    if (Test-Path -LiteralPath $flowBindings) { Remove-Item -LiteralPath $flowBindings }
    $flowBuild = unity command build --caller plugin --skill unity-cli --format json --target StandaloneWindows64 --outputPath Builds/ConnectionTest/LiveStockFlow.exe --options '[\"Development\"]' --confirm true | ConvertFrom-Json
    if (!$flowBuild.success -or $flowBuild.data.result.status -ne 'queued') { throw ($flowBuild | ConvertTo-Json -Depth 6) }
    $flowDeadline = [DateTime]::UtcNow.AddMinutes(25)
    do {
        Start-Sleep -Seconds 3
        $flowStatus = unity command build_status --caller plugin --skill unity-cli --format json | ConvertFrom-Json
        if ($flowStatus.success -and $flowStatus.data.result.status -eq 'completed') { break }
        if ([DateTime]::UtcNow -gt $flowDeadline) { throw 'Native Windows build timed out.' }
    } while ($true)
    $flowStatus.data.result | ConvertTo-Json -Depth 50 | Set-Content -Encoding UTF8 Docs/VR_Lobby_Build_Report.json
    $flowStatus.data.result | Select-Object status,success,result,totalErrors,totalWarnings,outputPath | ConvertTo-Json
    if ($flowStatus.data.result.result -ne 'Succeeded') { throw 'Windows connection-test build failed; see Docs/VR_Lobby_Build_Report.json.' }
}
finally {
    # Restore exact prior bytes on success, failure, timeout or cancellation; never leave diagnostics enabled in the project.
    if ($flowConfigExisted) { [System.IO.File]::WriteAllBytes($flowConfigPath, $flowConfigBefore) }
    elseif (Test-Path -LiteralPath $flowConfigPath) { Remove-Item -LiteralPath $flowConfigPath }
    if (Test-Path -LiteralPath $flowXRStateFile) {
        $flowXRState = Get-Content -Raw -Encoding UTF8 -LiteralPath $flowXRStateFile | ConvertFrom-Json
        $flowXRAssetPath = [System.IO.Path]::GetFullPath((Join-Path $flowRoot $flowXRState.assetPath))
        if (!$flowXRAssetPath.StartsWith($flowRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'XR restoration path escaped the project.' }
        [System.IO.File]::WriteAllBytes($flowXRAssetPath, [System.IO.File]::ReadAllBytes((Join-Path $flowRoot '.tmp/flow_build_xr_before.asset')))
        $flowRestoreFile = Join-Path $flowRoot '.tmp/flow_build_restore.cs'
        [System.IO.File]::WriteAllText($flowRestoreFile, 'var state=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(".tmp/flow_build_xr_state.json")); UnityEditor.AssetDatabase.ImportAsset((string)state["assetPath"],UnityEditor.ImportAssetOptions.ForceUpdate); return "XR asset restored";')
        unity command eval_file --caller plugin --skill unity-cli --format json --file $flowRestoreFile -- --timeout 10000 | Out-Null
    }
}

