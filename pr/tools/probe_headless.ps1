# Formula probe, headless: no window, no sound, then the formula check.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File pr\tools\probe_headless.ps1
#
# Only with the user's OK (PORTING.md "Formula probe"): it starts the game, so it must not
# run while the user plays. What it does:
#  - writes BepInEx\bnw_probe.request (the plugin deletes it at start: one run only);
#  - adds steam_appid.txt so Steamworks doesn't relaunch the game through Steam (which would
#    open a normal window); removes it afterwards if this script added it;
#  - sets the previous BepInEx log aside (an old "[probe] done" line must not end the run);
#  - starts the game with -batchmode -nographics (no window); the plugin mutes the sound
#    engine (SEAD) and Unity audio while probing;
#  - stops its own game (only that process) if it shows a window, if someone else starts the
#    game meanwhile, or after 15 minutes;
#  - copies BepInEx\bnw_probe.csv to pr\probe_last.csv and runs tools\probe_check.py on it.
#  -Live <dir>: instead of the probe, apply a Magicite-format folder live (the WC plugin's
#     LiveData.cs self-test)
#  -WcTest "seed=a;preset=moogle cup;seed2=b;load=1": the Worlds Collide plugin's self-test
#     (WcTest.cs: generate seeds in game, apply live, check, restore, load a save)
#  Brave New World and Worlds Collide are separate mods, one installed at a time
#  (tools/deploy.sh --bnw / --wc): the probe and the harness need BNW installed, -WcTest and
#  -Live need Worlds Collide (the run says so and stops otherwise).
#  -Graphics: keep the graphics device (still -batchmode, no window): the harness's "shot <png>"
#     renders the game's picture
#  -Minutes N: stop the run (its own game) after N minutes; default 15
#  -Until "<regex>": stop as soon as the log has a line matching it (with the probe: a startup
#     check, without waiting for the whole probe); prints the matching lines
#  -Extra "key=value;key=value": more lines for the probe's request, e.g.
#     "shopcheck=298:1:sc_npc_20451_2_bnw_shop" (warp, run a merchant's script, see the shop open)
param([int]$Party = 7, [int]$Weapon = 9, [int]$Samples = 8, [string]$Live = "", [string]$WcTest = "",
      [string]$Until = "", [int]$Minutes = 15, [string]$Extra = "", [switch]$Graphics)

$ErrorActionPreference = "Stop"
$game = "D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR"
$pr = Split-Path -Parent $PSScriptRoot
$name = "FINAL FANTASY VI"
$log = Join-Path $game "BepInEx\LogOutput.log"
$csv = Join-Path $game "BepInEx\bnw_probe.csv"
$appid = Join-Path $game "steam_appid.txt"

if (Get-Process -Name $name -ErrorAction SilentlyContinue) { Write-Output "the game is running: not starting a probe"; exit 1 }
# One run at a time. A run still finishing removes steam_appid.txt at its end: a game started
# meanwhile is relaunched by Steam outside the run, without its request (no save block) -
# 2026-11-07. Waits up to a minute for the other run, then gives up.
$lock = Join-Path $game "BepInEx\probe_run.lock"
for ($i = 0; $i -lt 60 -and (Test-Path $lock); $i++) {
    $other = 0
    [void][int]::TryParse(((Get-Content $lock -ErrorAction SilentlyContinue) -join ""), [ref]$other)
    if (-not $other -or -not (Get-Process -Id $other -ErrorAction SilentlyContinue)) { Remove-Item $lock -ErrorAction SilentlyContinue; break }
    Start-Sleep -Seconds 1
}
if (Test-Path $lock) { Write-Output "another probe run is still finishing: not starting"; exit 1 }
"$PID" | Set-Content -Encoding ascii $lock
$need = if ($WcTest -or $Live) { "WorldsCollide" } else { "BraveNewWorld" }
if (-not (Test-Path (Join-Path $game "BepInEx\plugins\$need"))) {
    Write-Output "$need isn't installed (one mod per install): pr/tools/deploy.sh $(if ($need -eq 'WorldsCollide') { '--wc' } else { '--bnw' }) --pc first"
    exit 1
}
if ($WcTest) {
    ($WcTest -split ";") -join "`n" | Set-Content -Encoding ascii (Join-Path $game "BepInEx\wc_test.request")
    $doneMark = "\[wc test\] done"
}
elseif ($Live) {
    $Live | Set-Content -Encoding ascii (Join-Path $game "BepInEx\live_apply.request")
    $doneMark = "\[live\] done"
} else {
    $req = "party=$Party`nweapon=$Weapon`nsamples=$Samples`nquit=1"
    if ($Extra) { $req += "`n" + (($Extra -split ";") -join "`n") }
    $req | Set-Content -Encoding ascii (Join-Path $game "BepInEx\bnw_probe.request")
    $doneMark = "\[probe\] done"
}
if ($Until) { $doneMark = $Until }
$addedAppId = -not (Test-Path $appid)
if ($addedAppId) { "1173820" | Set-Content -Encoding ascii $appid }
if (Test-Path $log) { Move-Item $log (Join-Path $game "BepInEx\LogOutput.prev.log") -Force }
if (Test-Path $csv) { Remove-Item $csv }

# only ever watch and stop the process started here: if you start the game yourself during
# a run, the run stops its own game and leaves yours alone
# -Graphics: still no window (-batchmode), but with a graphics device, so the harness's "shot" can
# render the game's picture to a PNG (the user, 2026-11-20: screenshots on the PC, not the Deck)
# (a screen size: without a window the game sizes its pixel buffers from a tiny default screen)
$gameArgs = if ($Graphics) { @("-batchmode", "-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0") } else { @("-batchmode", "-nographics") }
# the log's complete lines written since the last look: a warm game's log grows for hours (tools/test_batch.py keeps
# a harness game up between batches), so not the whole file through Select-String every half second (2026-11-20)
function Read-NewLogLines([string]$path, [ref]$pos) {
    try {
        $fs = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]'ReadWrite, Delete')
        try {
            if ($fs.Length -lt $pos.Value) { $pos.Value = 0 }
            if ($fs.Length -le $pos.Value) { return @() }
            $fs.Position = $pos.Value
            $buf = New-Object byte[] ([int]($fs.Length - $pos.Value))
            $n = $fs.Read($buf, 0, $buf.Length)
            $cut = [Array]::LastIndexOf($buf, [byte]10, $n - 1)
            if ($cut -lt 0) { return @() }
            $pos.Value += $cut + 1
            return [System.Text.Encoding]::UTF8.GetString($buf, 0, $cut + 1) -split "`r?`n"
        } finally { $fs.Dispose() }
    } catch { return @() }
}
$logPos = 0

$proc = Start-Process -FilePath (Join-Path $game "FINAL FANTASY VI.exe") -ArgumentList $gameArgs `
    -WorkingDirectory $game -WindowStyle Hidden -PassThru
$start = Get-Date
$result = "timeout"
try {
    while (((Get-Date) - $start).TotalSeconds -lt ($Minutes * 60)) {
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        if ($proc.HasExited) {
            $result = "the game exited"
            # exited at once: Steam may be relaunching it outside this run (no request, no save
            # block): any game started since this run began is stopped
            if (((Get-Date) - $start).TotalSeconds -lt 60) {
                Start-Sleep -Seconds 20
                foreach ($p in @(Get-Process -Name $name -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt $start })) {
                    Stop-Process -Id $p.Id -Force
                    $result = "the game exited at start and was relaunched (by Steam?): stopped the relaunched game"
                }
            }
            break
        }
        if ($proc.MainWindowHandle -ne 0) {
            Stop-Process -Id $proc.Id -Force
            $result = "a window appeared: stopped the run's game"
            break
        }
        $others = @(Get-Process -Name $name -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $proc.Id })
        if ($others.Count -gt 0) {
            Stop-Process -Id $proc.Id -Force
            # who started it (2026-11-20: bnw-11's self-tests were stopped by a game nobody could account for)
            $who = @($others | ForEach-Object {
                $o = $_
                try {
                    $w = Get-CimInstance Win32_Process -Filter "ProcessId = $($o.Id)" -ErrorAction Stop
                    $par = Get-CimInstance Win32_Process -Filter "ProcessId = $($w.ParentProcessId)" -ErrorAction SilentlyContinue
                    $by = if ($par) { "$($par.Name) pid $($par.ProcessId): $("$($par.CommandLine)".Substring(0, [Math]::Min(200, "$($par.CommandLine)".Length)))" } else { "pid $($w.ParentProcessId) (gone)" }
                    "pid $($o.Id) since $($w.CreationDate.ToString('HH:mm:ss')), started by $by"
                } catch { "pid $($o.Id)" }
            }) -join "; "
            $result = "the game was started by someone else: stopped the run (that game left alone): $who"
            break
        }
        if ((Test-Path $log) -and @(Read-NewLogLines $log ([ref]$logPos) | Where-Object { $_ -match $doneMark }).Count -gt 0) {
            $result = "probe finished"
            # the game quits by itself after "[probe] done": up to 10 s for it (no fixed wait)
            for ($w = 0; $w -lt 20 -and -not $proc.HasExited; $w++) { Start-Sleep -Milliseconds 500; $proc.Refresh() }
            break
        }
    }
}
finally {
    $proc.Refresh()
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; $result += " (stopped the run's game)" }
    if ($addedAppId) { Remove-Item $appid -ErrorAction SilentlyContinue }
    Remove-Item $lock -ErrorAction SilentlyContinue
}
Write-Output "$result after $([int]((Get-Date) - $start).TotalSeconds) s"
if ($WcTest) {
    Select-String -Path $log -Pattern "\[wc test\]|\[wc\] |\[Error|Exception" | ForEach-Object { $_.Line }
    exit
}
if ($Until) {
    Select-String -Path $log -Pattern "$Until|\[verify\]|\[patch\]|\[Error|Exception" | ForEach-Object { $_.Line }
    Remove-Item (Join-Path $game "BepInEx\bnw_probe.request") -ErrorAction SilentlyContinue
    exit
}
Select-String -Path $log -Pattern "\[probe\]|\[live\]" | Select-Object -Last 6 | ForEach-Object { $_.Line }
if (-not $Live -and (Test-Path $csv)) {
    Copy-Item $csv (Join-Path $pr "probe_last.csv") -Force
    $env:PYTHONIOENCODING = "utf-8"
    & (Join-Path $pr ".venv\Scripts\python.exe") (Join-Path $pr "tools\probe_check.py") (Join-Path $pr "probe_last.csv")
}
