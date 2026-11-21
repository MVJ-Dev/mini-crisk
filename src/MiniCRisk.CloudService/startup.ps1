# Installs R (silent) and the plumber package, then registers a scheduled task that keeps the
# scoring API listening on port 8000. Idempotent: safe to run on every role recycle.
$ErrorActionPreference = 'Stop'

$rVersion = '4.3.3'
$rHome    = "C:\Program Files\R\R-$rVersion"
$rscript  = Join-Path $rHome 'bin\Rscript.exe'
$appDir   = Join-Path $env:RoleRoot 'approot\r-scoring'
if (-not (Test-Path $appDir)) { $appDir = Join-Path $PSScriptRoot '..\..\r-scoring' }

if (-not (Test-Path $rscript)) {
    $installer = Join-Path $env:TEMP "R-$rVersion-win.exe"
    Invoke-WebRequest -Uri "https://cran.r-project.org/bin/windows/base/R-$rVersion-win.exe" -OutFile $installer
    Start-Process -FilePath $installer -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait
}

& $rscript -e "if (!requireNamespace('plumber', quietly = TRUE)) install.packages('plumber', repos = 'https://cloud.r-project.org')"

# Firewall rule only needed if the scoring API is reached from another host
if (-not (Get-NetFirewallRule -DisplayName 'Scoring API 8000' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'Scoring API 8000' -Direction Inbound -Protocol TCP -LocalPort 8000 -Action Allow | Out-Null
}

$taskName = 'MiniCRiskScoring'
if (-not (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) {
    $action  = New-ScheduledTaskAction -Execute $rscript -Argument "`"$appDir\run.R`"" -WorkingDirectory $appDir
    $trigger = New-ScheduledTaskTrigger -AtStartup
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -User 'SYSTEM' -RunLevel Highest | Out-Null
}
Start-ScheduledTask -TaskName $taskName
