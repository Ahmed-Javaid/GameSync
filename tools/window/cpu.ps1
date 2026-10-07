param([Parameter(Mandatory)][int]$ProcessId, [int]$Seconds = 10, [string]$Label = '')

# How busy a process is over a few seconds, as a share of one core and of the whole CPU, with its memory: for measuring
# GameSync's window on a page (PERF-07). Light that keeps moving holds still while the window isn't the active one, so keep
# it in front while this runs.
#   powershell -File tools\window\cpu.ps1 -ProcessId 1234 -Label "Home"
$p = Get-Process -Id $ProcessId
$before = $p.TotalProcessorTime
$clock = [Diagnostics.Stopwatch]::StartNew()
Start-Sleep -Seconds $Seconds
$p.Refresh()
$core = 100 * ($p.TotalProcessorTime - $before).TotalMilliseconds / $clock.Elapsed.TotalMilliseconds
'{0,-28} {1,5:N1}% of one core, {2,4:N1}% of all {3} ({4:N0} MB working set)' -f $Label, $core, ($core / [Environment]::ProcessorCount), [Environment]::ProcessorCount, ($p.WorkingSet64 / 1MB)
