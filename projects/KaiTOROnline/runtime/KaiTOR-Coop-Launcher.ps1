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

$EN = @{
    title='KaiTOR Co-op - Server Control'
    bannerlord='Bannerlord root'
    browse='Browse'
    save='Campaign save'
    servername='Server name'
    password='Password'
    visibility='Visibility'
    private='Private'
    friends='Friends only'
    public='Public'
    port='Port'
    players='Players'
    server='Server'
    steam='Steam'
    offline='OFFLINE'
    online='ONLINE'
    steam_starting='STARTING'
    steam_ready='LOBBY READY'
    steam_error='ERROR'
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
    footer='KaiTOR Co-op v1.3.15.13 | Bannerlord/TOR 1.3.15 | Steam Lobby / UDP 4200'
}
$RU = @{
    title='KaiTOR Co-op — Панель сервера'
    bannerlord='Путь к Bannerlord'
    browse='Обзор'
    save='Сохранение кампании'
    servername='Имя сервера'
    password='Пароль'
    visibility='Доступ'
    private='Приватный'
    friends='Только друзья'
    public='Публичный'
    port='Порт'
    players='Игроки'
    server='Сервер'
    steam='Steam'
    offline='НЕ ЗАПУЩЕН'
    online='ЗАПУЩЕН'
    steam_starting='ЗАПУСК'
    steam_ready='ЛОББИ ГОТОВО'
    steam_error='ОШИБКА'
    preflight='Проверить'
    start='Запустить сервер'
    stop='Остановить сервер'
    log='Открыть лог'
    openfolder='Открыть папку'
    language='Язык'
    ready='Готово. Перед запуском сервера выполни проверку.'
    preflight_run='Выполняется проверка совместимости...'
    preflight_ok='Проверка пройдена. Сервер готов к запуску.'
    preflight_fail='Проверка не пройдена: '
    start_ok='Команда запуска отправлена. Ожидается запуск Bannerlord.'
    start_fail='Не удалось запустить сервер: '
    stop_ok='Серверный процесс остановлен.'
    stop_none='Серверный процесс KaiTOR не найден.'
    log_none='Лог сервера пока не найден.'
    select_root='Выберите корневую папку Mount & Blade II Bannerlord'
    invalid_root='В выбранной папке не найден bin\Win64_Shipping_Client\Bannerlord.exe.'
    save_required='Укажите имя сохранения кампании.'
    status='Статус'
    unknown_players='— / 4'
    footer='KaiTOR Co-op v1.3.15.13 | Bannerlord/TOR 1.3.15 | Steam Lobby / UDP 4200'
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

function Get-SteamLobbyState {
    if (@(Get-KaiTORServerProcesses).Count -eq 0) {
        return [pscustomobject]@{ Text = $script:Text.offline; State = 'offline' }
    }

    $log = Get-ServerLog
    if ($null -eq $log) {
        return [pscustomobject]@{ Text = $script:Text.steam_starting; State = 'starting' }
    }

    try {
        $lines = @(Get-Content -LiteralPath $log.FullName -Tail 3500 -ErrorAction Stop)
        $errorPattern = 'GameServer\.Init returned false|Game-server logon failed|Server Steam integration inactive|Could not start the standalone Steam advertisement|Could not create a Steam lobby|Steam lobby data writes failed'
        if (@($lines | Where-Object { $_ -match $errorPattern }).Count -gt 0) {
            return [pscustomobject]@{ Text = $script:Text.steam_error; State = 'error' }
        }

        $loggedOn = @($lines | Where-Object { $_ -match 'Game server logged on:' }).Count -gt 0
        $lobbyCreated = @($lines | Where-Object { $_ -match 'Steam lobby [0-9]+ created' }).Count -gt 0
        $tunnelListening = @($lines | Where-Object { $_ -match 'Steam tunnel host listening' }).Count -gt 0

        if ($loggedOn -and $lobbyCreated -and $tunnelListening) {
            return [pscustomobject]@{ Text = $script:Text.steam_ready; State = 'ready' }
        }

        return [pscustomobject]@{ Text = $script:Text.steam_starting; State = 'starting' }
    }
    catch {
        return [pscustomobject]@{ Text = $script:Text.steam_starting; State = 'starting' }
    }
}

function Set-Status([string]$Key, [string]$Suffix = '') {
    $script:LastStatusKey = $Key
    $lblMessage.Text = $script:Text[$Key] + $Suffix
}

$form = [Windows.Forms.Form]::new()
$form.ClientSize = [Drawing.Size]::new(820,615)
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

$lblServerName = New-Label 30 180 180 28
$form.Controls.Add($lblServerName)
$txtServerName = [Windows.Forms.TextBox]::new()
$txtServerName.SetBounds(210,177,300,30)
$txtServerName.Text = 'KaiTOR Co-op | TOR RU Test'
$txtServerName.MaxLength = 64
$form.Controls.Add($txtServerName)

$lblPassword = New-Label 30 225 180 28
$form.Controls.Add($lblPassword)
$txtPassword = [Windows.Forms.TextBox]::new()
$txtPassword.SetBounds(210,222,300,30)
$txtPassword.UseSystemPasswordChar = $true
$txtPassword.MaxLength = 128
$form.Controls.Add($txtPassword)

$lblVisibility = New-Label 30 270 180 28
$form.Controls.Add($lblVisibility)
$cmbVisibility = [Windows.Forms.ComboBox]::new()
$cmbVisibility.SetBounds(210,267,300,30)
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

$lblSteamCaption = New-Label 550 270 80 28
$form.Controls.Add($lblSteamCaption)
$lblSteam = New-Label 630 270 160 28
$lblSteam.Font = [Drawing.Font]::new('Segoe UI Semibold',10,[Drawing.FontStyle]::Bold)
$form.Controls.Add($lblSteam)

$panel = [Windows.Forms.Panel]::new()
$panel.SetBounds(25,325,765,2)
$panel.BackColor = [Drawing.Color]::FromArgb(115,54,38)
$form.Controls.Add($panel)

$btnPreflight = New-Button 30 350 175 42
$form.Controls.Add($btnPreflight)
$btnStart = New-Button 220 350 175 42
$btnStart.BackColor = [Drawing.Color]::FromArgb(94,24,18)
$form.Controls.Add($btnStart)
$btnStop = New-Button 410 350 175 42
$form.Controls.Add($btnStop)
$btnLog = New-Button 600 350 190 42
$form.Controls.Add($btnLog)

$btnFolder = New-Button 600 405 190 38
$form.Controls.Add($btnFolder)

$lblStatusCaption = New-Label 30 410 100 28
$form.Controls.Add($lblStatusCaption)
$lblMessage = New-Label 130 407 450 75
$lblMessage.ForeColor = [Drawing.Color]::FromArgb(205,184,145)
$form.Controls.Add($lblMessage)

$footer = New-Label 30 565 760 30
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
    $lblServerName.Text = $script:Text.servername
    $lblPassword.Text = $script:Text.password
    $lblVisibility.Text = $script:Text.visibility
    $lblPort.Text = $script:Text.port
    $lblPlayersCaption.Text = $script:Text.players
    $lblServerCaption.Text = $script:Text.server
    $lblSteamCaption.Text = $script:Text.steam
    $lblStatusCaption.Text = $script:Text.status
    $btnPreflight.Text = $script:Text.preflight
    $btnStart.Text = $script:Text.start
    $btnStop.Text = $script:Text.stop
    $btnLog.Text = $script:Text.log
    $btnFolder.Text = $script:Text.openfolder
    $footer.Text = $script:Text.footer

    # Public is the first-run default so the session appears in KaiTOR's in-game Steam server browser.
    # A later language refresh preserves the user's current selection.
    $currentVisibility = if ($cmbVisibility.SelectedIndex -ge 0) { $cmbVisibility.SelectedIndex } else { 2 }
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
        $readinessArgs = @{
            PackageRoot = $PackageRoot
            BannerlordRoot = $root
            SaveName = $txtSave.Text.Trim()
        }
        if ($cmbVisibility.SelectedIndex -gt 0) {
            $readinessArgs.RequireSteamHost = $true
        }
        $output = & $Readiness @readinessArgs 3>&1 2>&1 | Out-String
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

    $serverName = $txtServerName.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($serverName)) {
        $serverName = "KaiTOR Co-op | $save"
    }

    $visibility = @('none','friends_only','public')[$cmbVisibility.SelectedIndex]
    $args = @(
        '-NoProfile',
        '-ExecutionPolicy','Bypass',
        '-File',('"' + $TorLauncher + '"'),
        '-BannerlordRoot',('"' + $root + '"'),
        '-SaveName',('"' + $save.Replace('"','') + '"'),
        '-ServerName',('"' + $serverName.Replace('"','') + '"'),
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

    $steamState = Get-SteamLobbyState
    $lblSteam.Text = $steamState.Text
    $lblSteam.ForeColor = switch ($steamState.State) {
        'ready' { [Drawing.Color]::FromArgb(129,190,111) }
        'error' { [Drawing.Color]::FromArgb(205,100,82) }
        'starting' { [Drawing.Color]::FromArgb(218,196,154) }
        default { [Drawing.Color]::FromArgb(150,127,100) }
    }

    $btnStart.Enabled = -not $online
    $btnStop.Enabled = $online
})
$timer.Start()

Refresh-Texts
Set-Status 'ready'
$form.Add_FormClosed({ $timer.Stop(); $timer.Dispose() })
[void]$form.ShowDialog()
