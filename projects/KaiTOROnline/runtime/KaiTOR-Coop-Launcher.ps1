[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$PackageRoot = Split-Path -Parent $PSScriptRoot
$DefaultBannerlordRoot = $PackageRoot
$TorLauncher = Join-Path $PSScriptRoot 'Start-KaiTORTorCampaignServer.ps1'
$Readiness = Join-Path $PSScriptRoot 'Test-KaiTORTorLiveReadiness.ps1'

function Decode-Utf8([string]$Value) {
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Value))
}

$EN = @{
    title='KaiTOR Co-op - Server Control'
    bannerlord='Bannerlord root'
    browse='Browse'
    save='Campaign save'
    password='Password'
    visibility='Visibility'
    private='Private'
    friends='Friends only'
    public='Public'
    port='Port'
    players='Players'
    server='Server'
    offline='OFFLINE'
    online='ONLINE'
    preflight='Preflight'
    start='Start Server'
    stop='Stop Server'
    log='Open Log'
    openfolder='Open Folder'
    language='Language'
    ready='Ready. Run Preflight before starting the server.'
    preflight_run='Running compatibility preflight...'
    preflight_ok='Preflight PASS. Server is ready to start.'
    preflight_fail='Preflight failed: '
    start_ok='Server launch command sent. Waiting for Bannerlord.'
    start_fail='Could not start server: '
    stop_ok='Server process stopped.'
    stop_none='No KaiTOR server process found.'
    log_none='Server log has not been created yet.'
    select_root='Select Mount & Blade II Bannerlord root folder'
    invalid_root='Selected folder does not contain bin\Win64_Shipping_Client\Bannerlord.exe.'
    save_required='Enter the campaign save name.'
    status='Status'
    unknown_players='- / 4'
    footer='KaiTOR Co-op v1.3.15.10 | Bannerlord/TOR 1.3.15 | UDP 4200'
}
$RU = @{
    title=Decode-Utf8 'S2FpVE9SIENvLW9wIOKAlCDQn9Cw0L3QtdC70Ywg0YHQtdGA0LLQtdGA0LA='
    bannerlord=Decode-Utf8 '0J/Rg9GC0Ywg0LogQmFubmVybG9yZA=='
    browse=Decode-Utf8 '0J7QsdC30L7RgA=='
    save=Decode-Utf8 '0KHQvtGF0YDQsNC90LXQvdC40LUg0LrQsNC80L/QsNC90LjQuA=='
    password=Decode-Utf8 '0J/QsNGA0L7Qu9GM'
    visibility=Decode-Utf8 '0JTQvtGB0YLRg9C/'
    private=Decode-Utf8 '0J/RgNC40LLQsNGC0L3Ri9C5'
    friends=Decode-Utf8 '0KLQvtC70YzQutC+INC00YDRg9C30YzRjw=='
    public=Decode-Utf8 '0J/Rg9Cx0LvQuNGH0L3Ri9C5'
    port=Decode-Utf8 '0J/QvtGA0YI='
    players=Decode-Utf8 '0JjQs9GA0L7QutC4'
    server=Decode-Utf8 '0KHQtdGA0LLQtdGA'
    offline=Decode-Utf8 '0J3QlSDQl9CQ0J/Qo9Cp0JXQnQ=='
    online=Decode-Utf8 '0JfQkNCf0KPQqdCV0J0='
    preflight=Decode-Utf8 '0J/RgNC+0LLQtdGA0LjRgtGM'
    start=Decode-Utf8 '0JfQsNC/0YPRgdGC0LjRgtGMINGB0LXRgNCy0LXRgA=='
    stop=Decode-Utf8 '0J7RgdGC0LDQvdC+0LLQuNGC0Ywg0YHQtdGA0LLQtdGA'
    log=Decode-Utf8 '0J7RgtC60YDRi9GC0Ywg0LvQvtCz'
    openfolder=Decode-Utf8 '0J7RgtC60YDRi9GC0Ywg0L/QsNC/0LrRgw=='
    language=Decode-Utf8 '0K/Qt9GL0Lo='
    ready=Decode-Utf8 '0JPQvtGC0L7QstC+LiDQn9C10YDQtdC0INC30LDQv9GD0YHQutC+0Lwg0YDQtdC60L7QvNC10L3QtNGD0LXRgtGB0Y8g0LLRi9C/0L7Qu9C90LjRgtGMINC/0YDQvtCy0LXRgNC60YMu'
    preflight_run=Decode-Utf8 '0JLRi9C/0L7Qu9C90Y/QtdGC0YHRjyDQv9GA0L7QstC10YDQutCwINGB0L7QstC80LXRgdGC0LjQvNC+0YHRgtC4Li4u'
    preflight_ok=Decode-Utf8 '0J/RgNC+0LLQtdGA0LrQsCDQv9GA0L7QudC00LXQvdCwLiDQodC10YDQstC10YAg0LPQvtGC0L7QsiDQuiDQt9Cw0L/Rg9GB0LrRgy4='
    preflight_fail=Decode-Utf8 '0J/RgNC+0LLQtdGA0LrQsCDQvdC1INC/0YDQvtC50LTQtdC90LA6IA=='
    start_ok=Decode-Utf8 '0JrQvtC80LDQvdC00LAg0LfQsNC/0YPRgdC60LAg0L7RgtC/0YDQsNCy0LvQtdC90LAuINCe0LbQuNC00LDQtdGC0YHRjyDQt9Cw0L/Rg9GB0LogQmFubmVybG9yZC4='
    start_fail=Decode-Utf8 '0J3QtSDRg9C00LDQu9C+0YHRjCDQt9Cw0L/Rg9GB0YLQuNGC0Ywg0YHQtdGA0LLQtdGAOiA='
    stop_ok=Decode-Utf8 '0KHQtdGA0LLQtdGA0L3Ri9C5INC/0YDQvtGG0LXRgdGBINC+0YHRgtCw0L3QvtCy0LvQtdC9Lg=='
    stop_none=Decode-Utf8 '0KHQtdGA0LLQtdGA0L3Ri9C5INC/0YDQvtGG0LXRgdGBIEthaVRPUiDQvdC1INC90LDQudC00LXQvS4='
    log_none=Decode-Utf8 '0JvQvtCzINGB0LXRgNCy0LXRgNCwINC/0L7QutCwINC90LUg0L3QsNC50LTQtdC9Lg=='
    select_root=Decode-Utf8 '0JLRi9Cx0LXRgNC40YLQtSDQutC+0YDQvdC10LLRg9GOINC/0LDQv9C60YMgTW91bnQgJiBCbGFkZSBJSSBCYW5uZXJsb3Jk'
    invalid_root=Decode-Utf8 '0JIg0LLRi9Cx0YDQsNC90L3QvtC5INC/0LDQv9C60LUg0L3QtSDQvdCw0LnQtNC10L0gYmluXFdpbjY0X1NoaXBwaW5nX0NsaWVudFxCYW5uZXJsb3JkLmV4ZS4='
    save_required=Decode-Utf8 '0KPQutCw0LbQuNGC0LUg0LjQvNGPINGB0L7RhdGA0LDQvdC10L3QuNGPINC60LDQvNC/0LDQvdC40Lgu'
    status=Decode-Utf8 '0KHRgtCw0YLRg9GB'
    unknown_players=Decode-Utf8 '4oCUIC8gNA=='
    footer=Decode-Utf8 'S2FpVE9SIENvLW9wIHYxLjMuMTUuMTAg4oCiIEJhbm5lcmxvcmQvVE9SIDEuMy4xNSDigKIgVURQIDQyMDA='
}

$script:Text = $RU
$script:LastStatusKey = 'ready'

function New-Label([int]$x,[int]$y,[int]$w,[int]$h) {
    $c = [Windows.Forms.Label]::new()
    $c.SetBounds($x,$y,$w,$h)
    $c.ForeColor = [Drawing.Color]::FromArgb(218,196,154)
    $c.BackColor = [Drawing.Color]::Transparent
    $c.Font = [Drawing.Font]::new('Segoe UI',10,[Drawing.FontStyle]::Regular)
    return $c
}

function New-Button([int]$x,[int]$y,[int]$w,[int]$h) {
    $c = [Windows.Forms.Button]::new()
    $c.SetBounds($x,$y,$w,$h)
    $c.FlatStyle = [Windows.Forms.FlatStyle]::Flat
    $c.FlatAppearance.BorderSize = 1
    $c.FlatAppearance.BorderColor = [Drawing.Color]::FromArgb(139,91,53)
    $c.BackColor = [Drawing.Color]::FromArgb(47,25,21)
    $c.ForeColor = [Drawing.Color]::FromArgb(232,211,170)
    $c.Font = [Drawing.Font]::new('Segoe UI Semibold',10)
    return $c
}

function Get-BannerlordRoot {
    return $txtRoot.Text.Trim()
}

function Test-BannerlordRoot([string]$Root) {
    if ([string]::IsNullOrWhiteSpace($Root)) { return $false }
    return Test-Path -LiteralPath (Join-Path $Root 'bin\Win64_Shipping_Client\Bannerlord.exe') -PathType Leaf
}

function Get-KaiTORServerProcesses {
    try {
        return @(Get-CimInstance Win32_Process -Filter "Name='Bannerlord.exe'" -ErrorAction Stop |
            Where-Object {
                $_.CommandLine -and
                $_.CommandLine -match '(?i)/server' -and
                $_.CommandLine -match '(?i)(/coopsave|_MODULES_.*\*Coop)'
            })
    }
    catch {
        return @()
    }
}

function Get-ServerLog {
    $root = Get-BannerlordRoot
    if (-not $root) { return $null }
    $bin = Join-Path $root 'bin\Win64_Shipping_Client'
    return Get-ChildItem -LiteralPath $bin -Filter 'Coop_server*.log' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

function Get-ConnectedPlayerText {
    $log = Get-ServerLog
    if ($null -eq $log) { return $script:Text.unknown_players }

    try {
        $lines = @(Get-Content -LiteralPath $log.FullName -Tail 2500 -ErrorAction Stop)
        $snapshotLine = $lines | Where-Object { $_ -match 'KAITOR_4P_SNAPSHOT_JSON=' } | Select-Object -Last 1
        if (-not $snapshotLine) { return $script:Text.unknown_players }

        $json = ($snapshotLine -replace '^.*KAITOR_4P_SNAPSHOT_JSON=','').Trim()
        $snapshot = $json | ConvertFrom-Json
        $count = @($snapshot.players | Where-Object { $_.connected -eq $true }).Count
        return "$count / 4"
    }
    catch {
        return $script:Text.unknown_players
    }
}

function Set-Status([string]$Key, [string]$Suffix = '') {
    $script:LastStatusKey = $Key
    $lblMessage.Text = $script:Text[$Key] + $Suffix
}

$form = [Windows.Forms.Form]::new()
$form.ClientSize = [Drawing.Size]::new(820,570)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedSingle'
$form.MaximizeBox = $false
$form.BackColor = [Drawing.Color]::FromArgb(18,14,13)
$form.ForeColor = [Drawing.Color]::FromArgb(232,211,170)

$header = New-Label 28 20 560 42
$header.Font = [Drawing.Font]::new('Segoe UI Semibold',20,[Drawing.FontStyle]::Bold)
$form.Controls.Add($header)

$lblLang = New-Label 620 24 80 25
$form.Controls.Add($lblLang)
$cmbLang = [Windows.Forms.ComboBox]::new()
$cmbLang.SetBounds(690,20,100,30)
$cmbLang.DropDownStyle = 'DropDownList'
[void]$cmbLang.Items.Add('RU')
[void]$cmbLang.Items.Add('ENG')
$cmbLang.SelectedIndex = 0
$form.Controls.Add($cmbLang)

$separator = [Windows.Forms.Panel]::new()
$separator.SetBounds(25,68,765,2)
$separator.BackColor = [Drawing.Color]::FromArgb(115,54,38)
$form.Controls.Add($separator)

$lblRoot = New-Label 30 90 180 28
$form.Controls.Add($lblRoot)
$txtRoot = [Windows.Forms.TextBox]::new()
$txtRoot.SetBounds(210,87,455,30)
$txtRoot.Text = $DefaultBannerlordRoot
$form.Controls.Add($txtRoot)
$btnBrowse = New-Button 675 86 115 32
$form.Controls.Add($btnBrowse)

$lblSave = New-Label 30 135 180 28
$form.Controls.Add($lblSave)
$txtSave = [Windows.Forms.TextBox]::new()
$txtSave.SetBounds(210,132,300,30)
$txtSave.Text = 'KaiTOR-Live-Test'
$form.Controls.Add($txtSave)

$lblPassword = New-Label 30 180 180 28
$form.Controls.Add($lblPassword)
$txtPassword = [Windows.Forms.TextBox]::new()
$txtPassword.SetBounds(210,177,300,30)
$txtPassword.UseSystemPasswordChar = $true
$txtPassword.MaxLength = 128
$form.Controls.Add($txtPassword)

$lblVisibility = New-Label 30 225 180 28
$form.Controls.Add($lblVisibility)
$cmbVisibility = [Windows.Forms.ComboBox]::new()
$cmbVisibility.SetBounds(210,222,300,30)
$cmbVisibility.DropDownStyle = 'DropDownList'
$form.Controls.Add($cmbVisibility)

$lblPort = New-Label 550 135 80 28
$form.Controls.Add($lblPort)
$txtPort = [Windows.Forms.TextBox]::new()
$txtPort.SetBounds(630,132,160,30)
$txtPort.Text = '4200 UDP'
$txtPort.ReadOnly = $true
$form.Controls.Add($txtPort)

$lblPlayersCaption = New-Label 550 180 80 28
$form.Controls.Add($lblPlayersCaption)
$lblPlayers = New-Label 630 180 160 28
$lblPlayers.Font = [Drawing.Font]::new('Segoe UI Semibold',11,[Drawing.FontStyle]::Bold)
$form.Controls.Add($lblPlayers)

$lblServerCaption = New-Label 550 225 80 28
$form.Controls.Add($lblServerCaption)
$lblServer = New-Label 630 225 160 28
$lblServer.Font = [Drawing.Font]::new('Segoe UI Semibold',11,[Drawing.FontStyle]::Bold)
$form.Controls.Add($lblServer)

$panel = [Windows.Forms.Panel]::new()
$panel.SetBounds(25,280,765,2)
$panel.BackColor = [Drawing.Color]::FromArgb(115,54,38)
$form.Controls.Add($panel)

$btnPreflight = New-Button 30 305 175 42
$form.Controls.Add($btnPreflight)
$btnStart = New-Button 220 305 175 42
$btnStart.BackColor = [Drawing.Color]::FromArgb(94,24,18)
$form.Controls.Add($btnStart)
$btnStop = New-Button 410 305 175 42
$form.Controls.Add($btnStop)
$btnLog = New-Button 600 305 190 42
$form.Controls.Add($btnLog)

$btnFolder = New-Button 600 360 190 38
$form.Controls.Add($btnFolder)

$lblStatusCaption = New-Label 30 365 100 28
$form.Controls.Add($lblStatusCaption)
$lblMessage = New-Label 130 362 450 75
$lblMessage.ForeColor = [Drawing.Color]::FromArgb(205,184,145)
$form.Controls.Add($lblMessage)

$footer = New-Label 30 520 760 30
$footer.TextAlign = [Drawing.ContentAlignment]::MiddleCenter
$footer.ForeColor = [Drawing.Color]::FromArgb(150,127,100)
$form.Controls.Add($footer)

function Refresh-Texts {
    $script:Text = if ($cmbLang.SelectedItem -eq 'ENG') { $EN } else { $RU }
    $form.Text = $script:Text.title
    $header.Text = $script:Text.title
    $lblLang.Text = $script:Text.language
    $lblRoot.Text = $script:Text.bannerlord
    $btnBrowse.Text = $script:Text.browse
    $lblSave.Text = $script:Text.save
    $lblPassword.Text = $script:Text.password
    $lblVisibility.Text = $script:Text.visibility
    $lblPort.Text = $script:Text.port
    $lblPlayersCaption.Text = $script:Text.players
    $lblServerCaption.Text = $script:Text.server
    $lblStatusCaption.Text = $script:Text.status
    $btnPreflight.Text = $script:Text.preflight
    $btnStart.Text = $script:Text.start
    $btnStop.Text = $script:Text.stop
    $btnLog.Text = $script:Text.log
    $btnFolder.Text = $script:Text.openfolder
    $footer.Text = $script:Text.footer

    $currentVisibility = if ($cmbVisibility.SelectedIndex -ge 0) { $cmbVisibility.SelectedIndex } else { 0 }
    $cmbVisibility.Items.Clear()
    [void]$cmbVisibility.Items.Add($script:Text.private)
    [void]$cmbVisibility.Items.Add($script:Text.friends)
    [void]$cmbVisibility.Items.Add($script:Text.public)
    $cmbVisibility.SelectedIndex = [Math]::Min(2,$currentVisibility)

    if ($script:LastStatusKey) {
        $lblMessage.Text = $script:Text[$script:LastStatusKey]
    }
}

$cmbLang.Add_SelectedIndexChanged({ Refresh-Texts })

$btnBrowse.Add_Click({
    $dlg = [Windows.Forms.FolderBrowserDialog]::new()
    $dlg.Description = $script:Text.select_root
    $dlg.SelectedPath = $txtRoot.Text
    if ($dlg.ShowDialog() -eq [Windows.Forms.DialogResult]::OK) {
        $txtRoot.Text = $dlg.SelectedPath
    }
})

$btnPreflight.Add_Click({
    $root = Get-BannerlordRoot
    if (-not (Test-BannerlordRoot $root)) {
        Set-Status 'preflight_fail' $script:Text.invalid_root
        return
    }
    if ([string]::IsNullOrWhiteSpace($txtSave.Text)) {
        Set-Status 'preflight_fail' $script:Text.save_required
        return
    }

    $form.UseWaitCursor = $true
    $btnPreflight.Enabled = $false
    Set-Status 'preflight_run'
    [Windows.Forms.Application]::DoEvents()
    try {
        $output = & $Readiness -PackageRoot $PackageRoot -BannerlordRoot $root -SaveName $txtSave.Text.Trim() 3>&1 2>&1 | Out-String
        if ($output -notmatch 'KaiTOR TOR live readiness: PASS') {
            throw ($output.Trim())
        }
        Set-Status 'preflight_ok'
    }
    catch {
        Set-Status 'preflight_fail' $_.Exception.Message
    }
    finally {
        $btnPreflight.Enabled = $true
        $form.UseWaitCursor = $false
    }
})

$btnStart.Add_Click({
    $root = Get-BannerlordRoot
    if (-not (Test-BannerlordRoot $root)) {
        Set-Status 'start_fail' $script:Text.invalid_root
        return
    }
    $save = $txtSave.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($save)) {
        Set-Status 'start_fail' $script:Text.save_required
        return
    }

    $visibility = @('none','friends_only','public')[$cmbVisibility.SelectedIndex]
    $args = @(
        '-NoProfile',
        '-ExecutionPolicy','Bypass',
        '-File',('"' + $TorLauncher + '"'),
        '-BannerlordRoot',('"' + $root + '"'),
        '-SaveName',('"' + $save.Replace('"','') + '"'),
        '-Visibility',$visibility
    )
    if ($txtPassword.Text.Length -gt 0) {
        $args += @('-Password',('"' + $txtPassword.Text.Replace('"','') + '"'))
    }

    try {
        Start-Process -FilePath 'powershell.exe' -ArgumentList $args -WorkingDirectory $PackageRoot -WindowStyle Normal | Out-Null
        Set-Status 'start_ok'
    }
    catch {
        Set-Status 'start_fail' $_.Exception.Message
    }
})

$btnStop.Add_Click({
    $processes = @(Get-KaiTORServerProcesses)
    if ($processes.Count -eq 0) {
        Set-Status 'stop_none'
        return
    }
    foreach ($process in $processes) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }
    Set-Status 'stop_ok'
})

$btnLog.Add_Click({
    $log = Get-ServerLog
    if ($null -eq $log) {
        Set-Status 'log_none'
        return
    }
    Start-Process -FilePath 'notepad.exe' -ArgumentList ('"' + $log.FullName + '"') | Out-Null
})

$btnFolder.Add_Click({
    $root = Get-BannerlordRoot
    if (Test-Path -LiteralPath $root -PathType Container) {
        Start-Process -FilePath 'explorer.exe' -ArgumentList ('"' + $root + '"') | Out-Null
    }
})

$timer = [Windows.Forms.Timer]::new()
$timer.Interval = 1500
$timer.Add_Tick({
    $online = @(Get-KaiTORServerProcesses).Count -gt 0
    $lblServer.Text = if ($online) { $script:Text.online } else { $script:Text.offline }
    $lblServer.ForeColor = if ($online) { [Drawing.Color]::FromArgb(129,190,111) } else { [Drawing.Color]::FromArgb(205,100,82) }
    $lblPlayers.Text = Get-ConnectedPlayerText
    $btnStart.Enabled = -not $online
    $btnStop.Enabled = $online
})
$timer.Start()

Refresh-Texts
Set-Status 'ready'
$form.Add_FormClosed({ $timer.Stop(); $timer.Dispose() })
[void]$form.ShowDialog()
