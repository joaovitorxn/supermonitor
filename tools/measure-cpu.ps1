param([int]$Seconds = 60)

$process = Get-Process Supermonitor -ErrorAction Stop | Select-Object -First 1
$start = $process.TotalProcessorTime
Start-Sleep -Seconds $Seconds
$process.Refresh()
$used = ($process.TotalProcessorTime - $start).TotalMilliseconds
$percent = $used / ($Seconds * 1000 * [Environment]::ProcessorCount) * 100
'CPU: {0:N2}% | Working set: {1:N0} MB | Private: {2:N0} MB' -f $percent, ($process.WorkingSet64 / 1MB), ($process.PrivateMemorySize64 / 1MB)
