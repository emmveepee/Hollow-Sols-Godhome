[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('HollowKnight','NineSols')][string]$Game,
      [ValidateRange(0,600)][int]$Seconds = 30,
      [switch]$Unlimited,
      [switch]$EnginePerf, [switch]$NativePerf,
      [switch]$PerformanceProbe, [switch]$CanvasBaseline, [switch]$SwitchProbe, [ValidateRange(0,4)][int]$Pantheon=0, [ValidateRange(0,20)][int]$PantheonStart=0, [switch]$PureVesselProbe, [switch]$ReflectionFinishProbe, [switch]$MarkothShieldProbe, [switch]$MarkothProjectileProbe, [switch]$HomingProbe, [switch]$StaggerProbe, [switch]$PauseProbe,
      [ValidateSet('Preparing','Active','Victory','Defeated','Returned')][string]$FailureBoundary,
      [switch]$Bridge,
      [switch]$InspectHollowKnightSlot4,
      [switch]$PreviewGodhome,
      [switch]$HumanGodhome,
      [switch]$InspectNineSolsTemplate,
      [switch]$PreviewBattleMemories,
      [string]$GeometrySnapshot,
      [switch]$ExportGeometry,
      [switch]$PoseProbe,
      [switch]$InputProbe,
      [switch]$ScriptedInput,
      [switch]$RestProbe, [switch]$LeverProbe, [switch]$TravelProbe,
      [switch]$CheatProbe, [switch]$InvulnerableProbe,
      [switch]$ConfigurationProbe, [switch]$TraceProbe, [switch]$HudReferenceProbe, [switch]$KickProbe, [switch]$TalismanTraversalProbe, [switch]$WallProbe, [switch]$VoidProbe,
      [switch]$VisualProbe,
      [switch]$ProxyProbe,
      [switch]$AwaitGeometry,
      [ValidateRange(0,2)][int]$Tier = 0,
      [switch]$EncounterAudit,
      [switch]$CombatAudit,
      [switch]$ContentAudit,
      [switch]$CanvasServiceAudit,
      [switch]$JadeProbe,
      [switch]$EnemyProbe,
      [switch]$ContactProbe,
      [switch]$ArenaProbe,
      [switch]$HubProbe,
      [switch]$HubCombat,
      [switch]$ContactBridge,
      [switch]$TargetProbe,
      [switch]$HitProbe,
      [switch]$DeathProbe,
      [switch]$DefeatProbe,
      [switch]$LifecycleProbe,
      [switch]$RetryProbe,
      [switch]$EffectsProbe,
      [switch]$CombatProbe,
      [switch]$CrimsonProbe,
      [ValidateSet('GruzMother','Xero','VengeflyKing','BroodingMawlek','FalseKnightAudit','FailedChampionAudit','PureVessel','MantisLords','SistersOfBattle','Grimm','NightmareKingGrimm','NailsageSly','WatcherKnights','HornetProtector','HornetSentinel','BrokenVessel','LostKin','CrystalGuardian','EnragedGuardian','DungDefender','WhiteDefender','HiveKnight','TraitorLord','MassiveMossCharger','SoulWarrior','Nosk','WingedNosk','OroMato','Sheo','Oblobbles','GodTamer','Galien','Gorb','ElderHu','Markoth','Marmu','NoEyes','SoulMaster','SoulTyrant','Collector','GreyPrinceZote','Flukemarm','FalseKnight','FailedChampion','Uumuu','AbsoluteRadiance')][string]$Boss = 'GruzMother',
      [switch]$PassThru,
      [Guid]$SharedSession = [Guid]::Empty)
$ErrorActionPreference = 'Stop'
if ($NativePerf) { if ($Game -ne "HollowKnight" -or $Boss -ne "NightmareKingGrimm") { throw "Native benchmark is NKG-only." }; $PreviewGodhome=$EncounterAudit=$EnginePerf=$true }
$workspace = Split-Path $PSScriptRoot -Parent
if ($ReflectionFinishProbe -and (-not $ScriptedInput -or -not $CheatProbe -or -not $HubCombat -or $Boss -ne 'PureVessel' -or $Unlimited)) { throw 'Reflected finish requires bounded scripted PV HubCombat cheats.' }
if ($PauseProbe -and (-not $ScriptedInput -or -not $CheatProbe -or -not $HubCombat -or $PerformanceProbe -or $Unlimited)) { throw 'PauseProbe requires separate bounded scripted combat grants.' }
if ($PureVesselProbe -and (-not $ScriptedInput -or -not $CheatProbe -or -not $HubCombat -or $Boss -ne 'PureVessel')) { throw 'PV probe requires bounded scripted Pure Vessel HubCombat cheats.' }
if (-not $Unlimited -and $Seconds -lt 10) { throw 'Bounded diagnostics require at least 10 seconds.' }
if ($Unlimited) {
    if (-not $InputProbe -or $ScriptedInput) { throw 'Unlimited sessions require human input mode.' }
    $Seconds = 0
}
if ($Boss -in @('FalseKnightAudit','FailedChampionAudit') -and (-not $EncounterAudit -or -not $PreviewGodhome -or $Game -ne 'HollowKnight')) { throw 'False Knight is a native-only audit profile.' }
$pairedPhysics = $PoseProbe -or $InputProbe
if ($HubProbe -and -not $HubCombat -and (-not $ProxyProbe -or $ArenaProbe -or $ContactBridge)) { throw 'Hub traversal diagnostic requires the proxy and excludes combat until native interactions pass.' }
if ($HubCombat -and (-not $HubProbe -or -not $HitProbe -or -not $CombatProbe)) { throw 'Hub combat requires hub, hits and simultaneous combat.' }
if ($ContactBridge -and -not $ArenaProbe) { throw 'Incoming contact bridge requires the paired arena diagnostic.' }
if ($TargetProbe -and -not $ContactBridge) { throw 'Target projection requires the paired contact diagnostic.' }
if ($HitProbe -and -not $TargetProbe) { throw 'Native outgoing hits require projected target validation.' }
if ($LifecycleProbe -and -not $HitProbe) { throw 'Paired lifecycle audit requires the native hit bridge.' }
if ($RetryProbe -and -not $LifecycleProbe) { throw 'Retry audit requires paired lifecycle validation.' }
if ($DeathProbe -and ($Game -ne 'NineSols' -or -not $EnemyProbe -or -not $PreviewBattleMemories -or $ContactBridge)) { throw 'Death audit requires standalone native enemy preview.' }
if ($DefeatProbe -and ($Game -ne 'HollowKnight' -or -not $EncounterAudit -or -not $PreviewGodhome)) { throw 'Defeat audit requires the protected native HK encounter audit.' }
if ($ArenaProbe -and (-not $InputProbe -or -not $ProxyProbe)) { throw 'Arena transfer requires paired input and proxy diagnostics.' }
if ($EncounterAudit -and ($Game -ne 'HollowKnight' -or -not $PreviewGodhome)) { throw 'Encounter audit requires the protected HK preview.' }
if ($CombatAudit -and ($Game -ne 'NineSols' -or -not $PreviewBattleMemories)) { throw 'Combat audit requires the protected native Yi preview.' }
if ($CanvasServiceAudit) { if ($Game -ne 'NineSols' -or -not $PreviewBattleMemories -or $Seconds -ne 120 -or $Unlimited -or $CombatAudit -or $ContentAudit -or $InputProbe) { throw 'Canvas service audit requires isolated120-second native bootstrap preview.' }; $EnemyProbe=$true }
if ($JadeProbe) { if ($Game -ne 'NineSols' -or -not $PreviewBattleMemories -or $Seconds -ne 120 -or $Unlimited -or $CombatAudit -or $ContentAudit -or $CanvasServiceAudit -or $ContactProbe -or $DeathProbe -or $InputProbe) { throw 'Jade comparison requires isolated120-second native contact preview.' }; $EnemyProbe=$true }
if ($ContentAudit -and ($Game -ne 'NineSols' -or -not $PreviewBattleMemories -or $Seconds -ne 120 -or $Unlimited -or $CombatAudit -or $EnemyProbe -or $InputProbe)) { throw 'Content audit requires isolated120-second native Yi preview.' }
if ($EnemyProbe -and ($Game -ne 'NineSols' -or -not $PreviewBattleMemories)) { throw 'Native enemy fixture requires the protected Yi preview.' }
if ($ContactProbe -and -not $EnemyProbe) { throw 'Translated contact fixture requires the native enemy fixture.' }
if ($PoseProbe -and $InputProbe) { throw 'Choose pose or input diagnostic.' }
if ($RestProbe -and (-not $ScriptedInput -or -not $HubCombat)) { throw 'RestProbe requires scripted HubCombat.' }
if ($LeverProbe -and (-not $ScriptedInput -or -not $HubCombat -or $CheatProbe)) { throw 'LeverProbe requires bounded scripted HubCombat without grants.' }
if ($TravelProbe -and (-not $ScriptedInput -or -not $HubCombat -or $CheatProbe)) { throw 'TravelProbe requires bounded scripted HubCombat without grants.' }
if ($StaggerProbe -and (-not $CheatProbe -or -not $ScriptedInput -or $Boss -notin @('Grimm','NightmareKingGrimm','FalseKnight','FailedChampion'))) { throw 'StaggerProbe requires bounded scripted Grimm/armor-family combat.' }
if ($HomingProbe -and (-not $CheatProbe -or -not $ScriptedInput -or $Boss -ne 'WatcherKnights')) { throw 'HomingProbe requires bounded scripted Watcher Knights.' }
if ($MarkothProjectileProbe -and (-not $CheatProbe -or -not $ScriptedInput -or $Boss -ne 'Markoth' -or $MarkothShieldProbe)) { throw 'MarkothProjectileProbe requires bounded scripted Markoth cheat authorization, separately from counter testing.' }
if ($MarkothShieldProbe -and (-not $CheatProbe -or -not $ScriptedInput -or $Boss -ne 'Markoth')) { throw 'MarkothShieldProbe requires bounded scripted Markoth cheat authorization.' }
if ($TalismanTraversalProbe -and (-not $CheatProbe -or -not $ScriptedInput -or $Boss -ne 'SistersOfBattle' -or $Pantheon)) { throw 'Talisman traversal requires bounded scripted Sisters cheat diagnostic.' }
if ($InvulnerableProbe -and (-not $CheatProbe -or -not $ScriptedInput -or -not $HubCombat -or $Tier -ne 2 -or $Unlimited -or $PerformanceProbe -or $FailureBoundary -or $KickProbe -or $CrimsonProbe -or $PureVesselProbe -or $ReflectionFinishProbe -or $MarkothShieldProbe -or $MarkothProjectileProbe -or $StaggerProbe -or $TalismanTraversalProbe -or $Pantheon)) { throw 'InvulnerableProbe requires bounded Radiant victory-only combat.' }
if ($KickProbe -and (-not $CheatProbe -or -not $ScriptedInput)) { throw 'KickProbe requires explicit scripted cheat diagnostic.' }
if ($CheatProbe -and (-not $ScriptedInput -or -not $HubCombat)) { throw 'CheatProbe requires scripted HubCombat.' }
if ($ScriptedInput -and -not $InputProbe) { throw 'Scripted input requires the paired input diagnostic.' }
if ($VisualProbe -and -not $InputProbe) { throw 'Visual capture requires the paired input diagnostic.' }
if ($EffectsProbe -and -not $VisualProbe) { throw 'Attached effects require the visual diagnostic.' }
if ($CombatProbe -and -not $HitProbe) { throw 'Simultaneous combat requires the native hit bridge.' }
if ($CrimsonProbe -and (-not $CombatProbe -or -not $ScriptedInput)) { throw 'Crimson diagnostic requires scripted input and the full combat bridge.' }
if ($ProxyProbe -and -not $VisualProbe) { throw 'Proxy movement requires the visual diagnostic.' }
if ($AwaitGeometry -and (-not $pairedPhysics -or $Game -ne 'NineSols' -or $GeometrySnapshot)) { throw 'Deferred geometry is only for a paired Nine Sols diagnostic without a staged snapshot.' }
if ($pairedPhysics -and ($Bridge -or $InspectHollowKnightSlot4 -or $PreviewGodhome -or $HumanGodhome -or $PreviewBattleMemories -or $InspectNineSolsTemplate -or $ExportGeometry)) { throw 'Pose probe is a separate paired diagnostic mode.' }
if ($pairedPhysics -and $Game -eq 'NineSols' -and -not $GeometrySnapshot -and -not $AwaitGeometry) { throw 'Nine Sols pose probe requires this session geometry.' }
if ($pairedPhysics -and $Game -eq 'HollowKnight' -and $GeometrySnapshot) { throw 'Hollow Knight exports its own geometry.' }
if ($ExportGeometry -and -not $PreviewGodhome) { throw 'Geometry export requires the protected Godhome preview.' }
if ($GeometrySnapshot) {
    if (-not $PreviewBattleMemories -and -not ($pairedPhysics -and $Game -eq 'NineSols')) { throw 'Geometry landing requires the native Yi preview.' }
    $geometryPath = [IO.Path]::GetFullPath($GeometrySnapshot)
    $geometryRoot = [IO.Path]::GetFullPath((Join-Path $workspace '.local/runs/HollowKnight')) + [IO.Path]::DirectorySeparatorChar
    if (-not $geometryPath.StartsWith($geometryRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($geometryPath) -ne 'godhome-geometry.bin') { throw 'Use a geometry snapshot exported by a protected Hollow Knight run.' }
    if ((Get-Item -LiteralPath $geometryPath).Length -gt 4194304) { throw 'Geometry snapshot exceeds the size limit.' }
}
if ($PreviewBattleMemories -and ($Game -ne 'NineSols' -or $Bridge -or $InspectHollowKnightSlot4 -or $PreviewGodhome -or $HumanGodhome -or $InspectNineSolsTemplate)) { throw 'Battle Memories preview is a separate Nine Sols-only mode.' }
if ($InspectNineSolsTemplate -and ($Game -ne 'NineSols' -or $Bridge -or $InspectHollowKnightSlot4 -or $PreviewGodhome -or $HumanGodhome)) { throw 'Template inspection is a separate Nine Sols-only mode.' }
if ($Seconds -gt 120 -and -not $HumanGodhome -and -not ($HubProbe -and -not $ScriptedInput) -and -not ($HitProbe -and ($Seconds -le 200 -or $ScriptedInput -and $Seconds -le 600))) { throw 'Long sessions require a human Godhome test or bounded native-hit diagnostic.' }
if ($HumanGodhome -and ($Game -ne 'HollowKnight' -or $Bridge -or $InspectHollowKnightSlot4 -or $PreviewGodhome)) { throw 'Human Godhome test is a separate HK-only mode.' }
if ($InspectHollowKnightSlot4 -and ($Game -ne 'HollowKnight' -or $Bridge)) { throw 'Read-only inspection is HK-only and cannot combine with bridge mode.' }
if ($PreviewGodhome -and ($Game -ne 'HollowKnight' -or $Bridge -or $InspectHollowKnightSlot4)) { throw 'Godhome preview is a separate HK-only mode.' }
if (-not $Bridge -and -not $pairedPhysics -and (Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue)) { throw 'Run each baseline independently; close both first.' }
if (($Bridge -or $pairedPhysics) -and $SharedSession -eq [Guid]::Empty) { throw 'Bridge tests require an explicit shared session GUID.' }
$processName = if ($Game -eq 'HollowKnight') { 'hollow_knight' } else { 'NineSols' }
if (Get-Process $processName -ErrorAction SilentlyContinue) { throw "$Game is already running." }
$manifest = Get-Content -LiteralPath (Join-Path $workspace ".local/deployments/$Game.json") -Raw | ConvertFrom-Json
if ($manifest.mode -ne 'no-save-io') { throw 'Unexpected deployment mode.' }
if ((Get-FileHash -LiteralPath $manifest.assemblyPath).Hash -ne $manifest.assemblyHash) { throw 'Game changed; re-audit required.' }
foreach ($file in $manifest.files) {
    # BepInEx expands its own config with documented defaults. Check effective safety settings below.
    if ($file.relative -eq 'BepInEx\config\BepInEx.cfg') { continue }
    if ((Get-FileHash -LiteralPath (Join-Path $manifest.root $file.relative)).Hash -ne $file.sha256) { throw "Deployment changed: $($file.relative)" }
}
$config = Get-Content -LiteralPath (Join-Path $manifest.root 'BepInEx/config/BepInEx.cfg') -Raw
if ($config -notmatch '(?m)^DumpAssemblies = false\s*$' -or $config -notmatch '(?m)^LoadDumpedAssemblies = false\s*$') { throw 'Unsafe assembly dump configuration.' }
if ($Game -eq 'NineSols' -and $config -notmatch '(?m)^HideManagerGameObject = true\s*$') { throw 'Nine Sols requires the verified loader-lifetime setting.' }
foreach ($directory in @('plugins','patchers')) {
    $dlls = @(Get-ChildItem -LiteralPath (Join-Path $manifest.root "BepInEx/$directory") -Filter '*.dll' -Recurse -File)
    if ($dlls.Count -ne 1) { throw "Unexpected $directory assemblies." }
}
$session = if ($Bridge -or $pairedPhysics) { $SharedSession.ToString() } else { [Guid]::NewGuid().ToString() }
$logDir = Join-Path $workspace ".local/runs/$Game/$session"
$null = New-Item -ItemType Directory -Path $logDir -Force
if ($EnginePerf) { if ($Unlimited -or $Seconds -gt 120 -or $InputProbe -or $PoseProbe) { throw 'EnginePerf requires bounded native/baseline gameplay.' }; 'fixed-engine-stages' | Set-Content -LiteralPath (Join-Path $logDir 'engine-perf.request') }
if ($NativePerf) { 'native-NKG-authored-hit-workload' | Set-Content -LiteralPath (Join-Path $logDir 'native-perf.request') }
if ($Unlimited) { 'Human playtest: manual exit; save guards unchanged.' | Set-Content -LiteralPath (Join-Path $logDir 'unlimited-human.request') }
if ($ExportGeometry -or ($pairedPhysics -and $Game -eq 'HollowKnight')) { 'Export frozen Godhome terrain only.' | Set-Content -LiteralPath (Join-Path $logDir 'export-geometry.request') }
if ($ScriptedInput) { 'Explicit automated control fixture.' | Set-Content -LiteralPath (Join-Path $logDir 'scripted-input.request') }
if ($Pantheon) { if (-not $ScriptedInput -or -not $HubCombat -or $Unlimited -or $PerformanceProbe) { throw 'Pantheon fixture requires bounded scripted HubCombat.' }; "${Pantheon}:${PantheonStart}" | Set-Content -LiteralPath (Join-Path $logDir 'pantheon-probe.request') }
if ($SwitchProbe) { if (-not $ScriptedInput -or -not $HubCombat -or $Unlimited) { throw 'Boss switching diagnostic requires bounded scripted HubCombat.' }; 'native-statue-second-boss' | Set-Content -LiteralPath (Join-Path $logDir 'switch-probe.request') }
if ($FailureBoundary) { if (-not $ScriptedInput -or -not $HubCombat -or $Unlimited) { throw 'Failure injection requires bounded scripted HubCombat.' }; $FailureBoundary | Set-Content -LiteralPath (Join-Path $logDir 'failure-probe.request') }
if ($CanvasBaseline) { if (-not $PerformanceProbe -or -not $HubCombat) { throw 'Canvas baseline requires bounded fixed-idle combat services.' }; 'Audited bootstrap comparison only.' | Set-Content -LiteralPath (Join-Path $logDir 'canvas-baseline.request') }
if ($PerformanceProbe) { if (-not $ScriptedInput -or -not $HubProbe -or $Unlimited) { throw 'PerformanceProbe requires bounded scripted hub input.' }; 'atrium-idle' | Set-Content -LiteralPath (Join-Path $logDir 'performance-probe.request') }
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $logDir 'deployment.json')
if ($VisualProbe) { 'Native Yi visual diagnostic.' | Set-Content -LiteralPath (Join-Path $logDir 'visual.request') }
if ($EffectsProbe) { 'Audit native attached effects with bounded alpha capture.' | Set-Content -LiteralPath (Join-Path $logDir 'effects-probe.request') }
if ($CombatProbe) { 'Keep incoming contact enabled throughout native combat.' | Set-Content -LiteralPath (Join-Path $logDir 'combat-probe.request') }
if ($ReflectionFinishProbe) { 'bounded-native-reflected-finish' | Set-Content -LiteralPath (Join-Path $logDir 'reflection-finish-probe.request') }
if ($MarkothProjectileProbe) { 'bounded-native-Markoth-arrow-fixture' | Set-Content -LiteralPath (Join-Path $logDir 'markoth-projectile-probe.request') }
if ($HomingProbe) { 'bounded-native-Watcher-homing-fixture' | Set-Content -LiteralPath (Join-Path $logDir 'homing-probe.request') }
if ($StaggerProbe) { "bounded-native-$Boss-stagger-fixture" | Set-Content -LiteralPath (Join-Path $logDir 'stagger-probe.request') }
if ($MarkothShieldProbe) { 'bounded-native-Markoth-shield-fixture' | Set-Content -LiteralPath (Join-Path $logDir 'markoth-shield-probe.request') }
if ($PureVesselProbe) { 'bounded-native-PV-attack-fixture' | Set-Content -LiteralPath (Join-Path $logDir 'pv-tuning-probe.request') }
if ($CheatProbe) { 'explicit-diagnostic-only' | Set-Content -LiteralPath (Join-Path $logDir 'cheat-probe.request') }
if ($ConfigurationProbe) { if (-not $ScriptedInput -or -not $HubCombat -or -not $CheatProbe -or $Seconds -le 0) { throw 'ConfigurationProbe requires bounded scripted combat.' }; 'explicit-diagnostic-only' | Set-Content -LiteralPath (Join-Path $logDir 'configuration-probe.request') }
if ($HudReferenceProbe -and -not $TraceProbe) { throw 'HudReferenceProbe requires the bounded scripted TraceProbe.' }
if ($TraceProbe) { if (-not $ScriptedInput -or -not $HubCombat -or $Seconds -le 0) { throw 'TraceProbe requires bounded scripted combat.' }; 'explicit-diagnostic-only' | Set-Content -LiteralPath (Join-Path $logDir 'trace-probe.request') }
if ($HudReferenceProbe) { 'explicit-diagnostic-only' | Set-Content -LiteralPath (Join-Path $logDir 'hud-reference-probe.request') }
if ($TalismanTraversalProbe) { 'Bounded grounded native talisman traversal.' | Set-Content -LiteralPath (Join-Path $logDir 'talisman-traversal-probe.request') }
if ($InvulnerableProbe) { 'explicit-diagnostic-only' | Set-Content -LiteralPath (Join-Path $logDir 'invulnerable-probe.request') }
if ($KickProbe) { 'explicit-diagnostic-only' | Set-Content -LiteralPath (Join-Path $logDir 'kick-probe.request') }
if ($VoidProbe) { if (-not $ScriptedInput -or -not $HubCombat) { throw "VoidProbe requires bounded scripted HubCombat." }; "explicit-diagnostic-only" | Set-Content -LiteralPath (Join-Path $logDir "void-probe.request") }
if ($WallProbe) { if (-not $ScriptedInput) { throw 'WallProbe requires scripted input.' }; 'explicit-diagnostic-only' | Set-Content -LiteralPath (Join-Path $logDir 'wall-probe.request') }
if ($Boss -ne 'GruzMother') { $Boss | Set-Content -LiteralPath (Join-Path $logDir 'encounter-profile.request') }
if ($CrimsonProbe) { 'Exercise native hold/release unbounded counter through routed input.' | Set-Content -LiteralPath (Join-Path $logDir 'crimson-probe.request') }
if ($ProxyProbe) { 'Native bench exit and camera/target diagnostic.' | Set-Content -LiteralPath (Join-Path $logDir 'proxy.request') }
if ($RestProbe) { 'Bounded native rest diagnostic.' | Set-Content -LiteralPath (Join-Path $logDir 'rest-probe.request') }
if ($LeverProbe) { 'Bounded native melee/lever diagnostic.' | Set-Content -LiteralPath (Join-Path $logDir 'lever-probe.request') }
if ($PauseProbe) { 'Bounded native combat effect pause.' | Set-Content -LiteralPath (Join-Path $logDir 'pause-probe.request') }
if ($TravelProbe) { 'Bounded native cancellation/travel diagnostic.' | Set-Content -LiteralPath (Join-Path $logDir 'travel-probe.request') }
if ($HubCombat) { 'Native statue hub-to-combat lifecycle.' | Set-Content -LiteralPath (Join-Path $logDir 'hub-combat.request') }
if ($HubProbe) { 'Bounded native Yi hub traversal and interaction diagnostic.' | Set-Content -LiteralPath (Join-Path $logDir 'hub-probe.request') }
if ($AwaitGeometry) { 'Wait only for the same session Hollow Knight geometry export.' | Set-Content -LiteralPath (Join-Path $logDir 'await-geometry.request') }
if ($Tier -gt 0 -and -not $HubCombat -and -not $VoidProbe) { throw 'Tier diagnostics require native Hall boss selection in a protected combat session.' }
if ($Tier -gt 0) { "$Tier" | Set-Content -LiteralPath (Join-Path $logDir 'difficulty-probe.request') }
if ($EncounterAudit) { 'Audit native encounter entry without save writes.' | Set-Content -LiteralPath (Join-Path $logDir 'encounter-audit.request') }
if ($DefeatProbe) { 'Audit native dream defeat without altering health or save files.' | Set-Content -LiteralPath (Join-Path $logDir 'defeat-probe.request') }
if ($LifecycleProbe) { 'Observe actual native Yi death and paired restoration.' | Set-Content -LiteralPath (Join-Path $logDir 'lifecycle-probe.request') }
if ($RetryProbe) { 'Verify a fresh second encounter after native defeat and exit on acknowledged native melee.' | Set-Content -LiteralPath (Join-Path $logDir 'retry-probe.request') }
if ($CombatAudit) { 'Read native combat component configuration.' | Set-Content -LiteralPath (Join-Path $logDir 'combat-audit.request') }
if ($ContentAudit) { 'Bounded native canvas/content acquisition proof.' | Set-Content -LiteralPath (Join-Path $logDir 'content-audit.request') }
if ($CanvasServiceAudit) { 'Bounded read-only native bootstrap service inventory.' | Set-Content -LiteralPath (Join-Path $logDir 'canvas-service-audit.request') }
if ($JadeProbe) { 'Bounded native Ricochet equipment/timing comparison; restore in-memory equipment.' | Set-Content -LiteralPath (Join-Path $logDir 'jade-probe.request') }
if ($EnemyProbe) { 'Native Battle Memories enemy fixture.' | Set-Content -LiteralPath (Join-Path $logDir 'enemy-probe.request') }
if ($ContactProbe) { 'Translate native attack collider placement only.' | Set-Content -LiteralPath (Join-Path $logDir 'contact-probe.request') }
if ($ArenaProbe) { 'Native Gruz Mother arena transfer; synthetic encounter damage disabled.' | Set-Content -LiteralPath (Join-Path $logDir 'arena.request') }
if ($ContactBridge) { 'Audited incoming body contact translation only.' | Set-Content -LiteralPath (Join-Path $logDir 'contact-bridge.request') }
if ($TargetProbe) { 'Native target receiver projection; host damage disabled.' | Set-Content -LiteralPath (Join-Path $logDir 'target-probe.request') }
if ($HitProbe) { 'Native outgoing hit transport and boss death/return diagnostic.' | Set-Content -LiteralPath (Join-Path $logDir 'hit-probe.request') }
if ($Seconds -gt 200 -and $ScriptedInput -and $HitProbe) { 'bounded-by-ticket-600-seconds' | Set-Content -LiteralPath (Join-Path $logDir 'extended-diagnostic.request') }
if ($DeathProbe) { 'Observe native enemy damage, Yi death and native revival without defense or healing.' | Set-Content -LiteralPath (Join-Path $logDir 'death-probe.request') }
if ($GeometrySnapshot) { Copy-Item -LiteralPath $geometryPath -Destination (Join-Path $logDir 'godhome-geometry.bin') }
$ticketPath = Join-Path $manifest.root 'BepInEx/core/YiGodhome.session'
if (Test-Path -LiteralPath $ticketPath) { throw 'An unconsumed launch ticket exists; inspect it before another launch.' }
$ticket = @($session, [DateTime]::UtcNow.AddMinutes(2).ToString('O'), "$Seconds", (Join-Path $logDir 'bootstrap.log'), $(if ($InputProbe) { if ($Game -eq 'HollowKnight') { 'input-hk' } else { 'input-ns' } } elseif ($PoseProbe) { if ($Game -eq 'HollowKnight') { 'pose-hk' } else { 'pose-ns' } } elseif ($PreviewBattleMemories) { 'memory-ns' } elseif ($InspectNineSolsTemplate) { 'inspect-ns-template' } elseif ($HumanGodhome) { 'human-hk' } elseif ($PreviewGodhome) { 'preview-hk' } elseif ($InspectHollowKnightSlot4) { 'inspect-hk' } elseif ($Bridge) { 'bridge' } else { 'baseline' }))
$ticket | Set-Content -LiteralPath $ticketPath -Encoding ascii
$startedUtc = [DateTime]::UtcNow
[ordered]@{ game=$Game; session=$session; utc=$startedUtc.ToString('O'); seconds=$Seconds; logs=$logDir; launch='Steam without custom game arguments' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logDir 'run.json')
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = 'C:/Steam/steam.exe'
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.ArgumentList.Add('-applaunch')
$start.ArgumentList.Add($(if ($Game -eq 'HollowKnight') { '367520' } else { '1809540' }))
$null = [Diagnostics.Process]::Start($start)
Write-Output "Requested protected $Game through Steam with NO custom game arguments. Session: $session"
try {
    $name = [IO.Path]::GetFileNameWithoutExtension($manifest.executable)
    $process = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    while ([DateTime]::UtcNow -lt $deadline) {
        $candidate = @(Get-Process -Name $name -ErrorAction SilentlyContinue | Where-Object { $_.StartTime.ToUniversalTime() -ge $startedUtc.AddSeconds(-1) })
        if ($candidate.Count -gt 1) { throw 'Multiple game processes appeared; refusing ambiguous selection.' }
        if ($candidate.Count -eq 1) { $process = $candidate[0]; break }
        Start-Sleep -Milliseconds 250
    }
    if ($null -eq $process) { throw 'Steam did not start the game within 40 seconds.' }
    # Windows/.NET launcher counters work where Unity Mono Process counters are stubs.
    $resourcePath = Join-Path $logDir 'resource-samples.jsonl'
    $processDeadline = [DateTime]::UtcNow.AddSeconds($Seconds + 45)
    $terminalDeadline = $null
    $peerGame = if ($Game -eq 'HollowKnight') { 'NineSols' } else { 'HollowKnight' }
    $stopSignals = @((Join-Path $logDir 'session-stop.request'), (Join-Path $workspace ".local/runs/$peerGame/$session/session-stop.request"))
    while (-not $process.WaitForExit(1000)) {
        $process.Refresh()
        if ($process.HasExited) { break }
        foreach ($signal in $stopSignals) {
            if ((Test-Path -LiteralPath $signal -PathType Leaf) -and (Get-Item -LiteralPath $signal).Length -lt 128 -and
                 (Get-Content -LiteralPath $signal -Raw).Trim() -eq "$session" -and $null -eq $terminalDeadline) {
                $terminalDeadline = [DateTime]::UtcNow.AddSeconds(10)
                Write-Output "Exact-session terminal request observed; allowing up to ten seconds for native shutdown: $Game/$session"
            }
        }
        if ($null -ne $terminalDeadline -and [DateTime]::UtcNow -ge $terminalDeadline) {
            # This Process object is the exact guarded instance captured above,
            # never a name-wide kill or an unrelated vanilla process.
            $process.Kill(); $null = $process.WaitForExit(5000)
            throw "Protected terminal shutdown exceeded ten seconds; stopped owned $Game process $($process.Id). Session $session."
        }
        $pluginPath = Join-Path $logDir 'plugin.log'
        $bepPath = Join-Path $manifest.root 'BepInEx/LogOutput.log'
        $sample = @{ utc=[DateTime]::UtcNow.ToString('O'); pid=$process.Id; cpuMs=$process.TotalProcessorTime.TotalMilliseconds;
            privateBytes=$process.PrivateMemorySize64; workingBytes=$process.WorkingSet64; handles=$process.HandleCount; threads=$process.Threads.Count;
            pluginBytes=$(if(Test-Path -LiteralPath $pluginPath){(Get-Item -LiteralPath $pluginPath).Length}else{0});
            loaderLogBytes=$(if(Test-Path -LiteralPath $bepPath){(Get-Item -LiteralPath $bepPath).Length}else{0}) }
        if ((Test-Path -LiteralPath $resourcePath) -and (Get-Item -LiteralPath $resourcePath).Length -gt 8MB) { Move-Item -LiteralPath $resourcePath -Destination ($resourcePath+'.1') -Force }
        $sample | ConvertTo-Json -Compress | Add-Content -LiteralPath $resourcePath
        if (-not $Unlimited -and [DateTime]::UtcNow -gt $processDeadline) {
            $process.Kill(); $process.WaitForExit(); throw 'Protected smoke test timed out; stopped this test process.'
        }
    }
    $bepLog = Join-Path $manifest.root 'BepInEx/LogOutput.log'
    if (Test-Path -LiteralPath $bepLog) { Copy-Item -LiteralPath $bepLog -Destination (Join-Path $logDir 'bepinex.log') }
    $boot = Get-Content -LiteralPath (Join-Path $logDir 'bootstrap.log') -Raw
    $logs = (@('plugin.log.startup','plugin.log.2','plugin.log.1','plugin.log') | ForEach-Object { $part=Join-Path $logDir $_; if(Test-Path -LiteralPath $part){Get-Content -LiteralPath $part -Raw} }) -join "`n"
    if ($boot -notmatch "BOOTSTRAP_GUARD_VERIFIED session=$session" -or $boot -match 'FATAL' -or $logs -notmatch "session=$session" -or $logs -notmatch 'BASELINE_TICK' -or (-not $Unlimited -and -not $HumanGodhome -and $logs -notmatch 'SMOKE_EXIT_REQUEST')) { throw "Startup baseline incomplete. Inspect $logDir" }
    $titleScene = if ($Game -eq 'HollowKnight') { 'Menu_Title' } else { 'TitleScreenMenu' }
    if ($logs -notmatch "SCENE_READY game=$Game scene=$titleScene ") { throw "Title scene was not reached. Inspect $logDir" }
    if ($Bridge -and ($logs -notmatch 'IPC_CONNECTED' -or $logs -notmatch 'IPC_SAMPLE')) { throw "Bridge telemetry missing. Inspect $logDir" }
    if ($InspectHollowKnightSlot4 -and $logs -notmatch 'HK_SLOT4_INSPECTION result=(native-load-ok|absent-confirmed)') { throw "Slot-4 inspection unresolved. No save changes permitted. Inspect $logDir" }
    if ($InspectHollowKnightSlot4 -and $logs -notmatch 'HK_GODSEEKER_CANDIDATE validatedInMemory=True') { throw "Godseeker data preparation unresolved. No save changes permitted. Inspect $logDir" }
    $previewRoom = if ($NativePerf) { 'GG_(?:Atrium|Workshop)' } else { 'GG_Atrium' }
    if ($PreviewGodhome -and ($logs -notmatch "HK_GODHOME_PRESENTATION scene=$previewRoom fade=Normal state=PLAYING" -or $logs -match 'HK_GODHOME_PROBE_FAILED')) { throw "Godhome bench preview failed. Inspect $logDir" }
    if ($NativePerf -and $logs -notmatch 'HK_NATIVE_BENCHMARK_SAMPLE .*state=Fire Pause ') { throw "Native NKG benchmark did not sample balloon firing. Inspect $logDir" }
    if ($HumanGodhome -and ($logs -notmatch 'HK_GODHOME_PRESENTATION scene=GG_Atrium fade=Normal state=PLAYING' -or $logs -match 'HK_GODHOME_PROBE_FAILED')) { throw "Human Godhome startup failed. Inspect $logDir" }
    if ($InspectNineSolsTemplate -and ($logs -notmatch 'NS_TEMPLATE_INSPECTED .*applied=False saveIO=disabled' -or $logs -match 'NS_TEMPLATE_FAILED')) { throw "Battle Memories template inspection failed. Inspect $logDir" }
    if ($PreviewBattleMemories -and ($logs -notmatch 'NS_MEMORY_READY index=3 memory=True player=True' -or $logs -match 'NS_MEMORY_FAILED')) { throw "Native Yi initialization failed. Inspect $logDir" }
    if ($CanvasServiceAudit -and ($logs -notmatch 'NS_CANVAS_SERVICE_AUDIT .*readOnly=True once=True' -or $logs -match 'SESSION_TERMINAL|NS_MEMORY_FAILED' -or -not (Test-Path -LiteralPath (Join-Path $logDir 'canvas-service-audit.txt')))) { throw "Native bootstrap inventory incomplete. Inspect $logDir" }
    if ($ContentAudit -and ($logs -notmatch 'NS_CONTENT_COMPLETE assets=2 nativeGameplay=True sceneCreated=True normalMigration=False' -or $logs -match 'SESSION_TERMINAL|NS_CONTENT_FAILED')) { throw "Native content/canvas proof failed. Inspect $logDir" }
    if ($GeometrySnapshot -and $logs -notmatch 'NS_GEOMETRY_VERIFIED airborneToGrounded=True') { throw "Native geometry landing unverified. Inspect $logDir" }
    if ($ExportGeometry -and $logs -notmatch 'HK_GEOMETRY_EXPORTED') { throw "Native terrain export unverified. Inspect $logDir" }
    if ($PoseProbe -and ($logs -notmatch 'IPC_CONNECTED .*capabilities=3' -or $logs -notmatch 'IPC_POSE_(SENT|RECEIVED)' -or $logs -match 'NS_MEMORY_FAILED|HK_GODHOME_PROBE_FAILED')) { throw "Native pose diagnostic failed. Inspect $logDir" }
    if ($InputProbe -and ($logs -notmatch 'IPC_CONNECTED .*capabilities=7' -or $logs -notmatch 'IPC_POSE_(SENT|RECEIVED)' -or $logs -match 'NS_INPUT_FAILED|NS_MEMORY_FAILED|HK_GODHOME_PROBE_FAILED')) { throw "Native input diagnostic failed. Inspect $logDir" }
    if ($EncounterAudit -and $Boss -in @('FalseKnightAudit','FailedChampionAudit')) { if ($logs -notmatch 'HK_ENCOUNTER_HEALTH .*name=(False Knight New|False Knight Dream)' -or $logs -notmatch 'HK_PHASE_AUDIT') { throw 'False Knight native audit missing.' }; Write-Output 'Native False Knight phase audit recorded; no combat or victory pass claimed.' }
    if ($EncounterAudit -and $Boss -eq 'PureVessel') {
        $expectedHealth = if ($Tier -eq 0) {1600} else {1850}
        if ($logs -notmatch "HK_ENCOUNTER_ARENA tier=$Tier transitioned=True" -or $logs -notmatch "HK_ENCOUNTER_HEALTH .*name=HK Prime hp=$expectedHealth active=True" -or $logs -notmatch 'HK_ENCOUNTER_RETURN native=True bossDead=True') { throw 'Native Pure Vessel tier/HP/victory audit incomplete.' }
        Write-Output 'PASS: native Pure Vessel tier, HP, victory and Hall return. This native-only diagnostic does not prove Yi combat.'
    }
    if ($EncounterAudit -and -not $NativePerf -and -not $DefeatProbe -and $Boss -notin @('FalseKnightAudit','FailedChampionAudit') -and $Boss -ne 'PureVessel') { & (Join-Path $PSScriptRoot 'Test-EncounterEvidence.ps1') -Session ([Guid]$session) -Boss $Boss }
    if ($DefeatProbe) { & (Join-Path $PSScriptRoot 'Test-DefeatEvidence.ps1') -Session ([Guid]$session) }
    if ($CombatAudit -and ($logs -notmatch 'NS_COMBAT_AUDIT components=\d+ health=240 mutations=False' -or $logs -notmatch 'type=DamageReceiver .*Health.*DamageReceiver active=True')) { throw 'Native combat component audit incomplete.' }
    if ($JadeProbe) { & (Join-Path $PSScriptRoot 'Test-JadeEvidence.ps1') -Session ([Guid]$session) }
    Write-Output "Protected $Game smoke test completed. Logs: $logDir"
    if ($PassThru) { Write-Output ([pscustomobject]@{ Session=$session; Logs=$logDir }) }
}
finally {
    $bepLog = Join-Path $manifest.root 'BepInEx/LogOutput.log'
    if (Test-Path -LiteralPath $bepLog) { Copy-Item -LiteralPath $bepLog -Destination (Join-Path $logDir 'bepinex.log') }
    if (Test-Path -LiteralPath $ticketPath) {
        $remaining = Get-Content -LiteralPath $ticketPath
        if ($remaining[0] -eq $session) { Remove-Item -LiteralPath $ticketPath }
    }
}
