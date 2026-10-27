<#
.SYNOPSIS
    Первая установка сайта PsySite в IIS.

.DESCRIPTION
    Что делает скрипт:
      1. Проверяет, что установлены IIS и .NET 10 Hosting Bundle (и подсказывает, где их взять).
      2. Копирует опубликованный сайт в папку сайта (если указан -SourcePath).
      3. Создаёт appsettings.Production.json из шаблона и сам придумывает надёжный пароль админки.
      4. Создаёт пул приложений и сайт в IIS (или обновляет уже существующие).
      5. Выдаёт пулу права: чтение на папку сайта, запись — на App_Data и wwwroot\uploads.
      6. Запускает сайт и проверяет, что он отвечает.

    Запускать в PowerShell ОТ ИМЕНИ АДМИНИСТРАТОРА. Повторный запуск безопасен:
    существующие настройки, база и картинки не перезаписываются.

.EXAMPLE
    .\setup-iis.ps1 -SourcePath C:\Temp\publish-iis -HostName example.ru

.EXAMPLE
    # Без домена, для проверки на самом сервере по адресу http://localhost:8090
    .\setup-iis.ps1 -SourcePath C:\Temp\publish-iis -Port 8090
#>
[CmdletBinding()]
param(
    # Папка с опубликованным сайтом (результат публикации профилем IIS). Можно не указывать, если файлы уже в SitePath.
    [string]$SourcePath,

    # Куда установить сайт.
    [string]$SitePath = 'C:\inetpub\psysite',

    # Имя сайта и пула приложений в IIS.
    [string]$SiteName = 'PsySite',

    # Домен сайта, например example.ru. Пусто — сайт отвечает на любое имя (удобно для первой проверки).
    [string]$HostName = '',

    # Порт для http. HTTPS настраивается отдельно, после получения сертификата (см. инструкцию).
    [int]$Port = 80,

    # Установить роль IIS, если её нет (Windows Server или Windows 10/11).
    [switch]$InstallIis
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Write-Step([string]$text) { Write-Host ''; Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Ok([string]$text) { Write-Host "    OK: $text" -ForegroundColor Green }
function Write-Warn([string]$text) { Write-Host "    ВНИМАНИЕ: $text" -ForegroundColor Yellow }
function Stop-WithError([string]$text) { Write-Host ''; Write-Host "ОШИБКА: $text" -ForegroundColor Red; exit 1 }

# Надёжный случайный пароль без похожих символов (0/O, 1/l).
function New-StrongPassword([int]$length = 20) {
    $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'.ToCharArray()
    $bytes = New-Object byte[] $length
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
}

# Создать appsettings.Production.json из шаблона: свой пароль админки, домен. Возвращает пароль (или $null, если файл уже был).
function New-ProductionSettings([string]$templatePath, [string]$targetPath, [string]$hostName) {
    if (Test-Path $targetPath) { return $null }
    $json = Get-Content -Raw -Encoding UTF8 $templatePath | ConvertFrom-Json
    $password = New-StrongPassword
    $json.Admin.Password = $password
    if ($hostName) {
        $json.Site.BaseUrl = "https://$hostName"
        $json.AllowedHosts = "$hostName;www.$hostName;localhost"
    } else {
        $json.AllowedHosts = '*'
    }
    $json | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 $targetPath
    return $password
}

# --- Всё, что ниже, работает только на Windows с IIS. Функции выше можно проверять отдельно. ---
# Код ответа сайта: запрос на этот же компьютер с нужным доменом в заголовке Host —
# так проверка работает, даже если домен ещё не направлен на сервер. Работает в PowerShell 5.1 и 7.
function Get-SiteStatus([int]$port, [string]$hostHeader) {
    try {
        $req = [System.Net.HttpWebRequest]::Create("http://127.0.0.1:$port/")
        $req.Host = $hostHeader
        $req.Timeout = 20000
        $req.AllowAutoRedirect = $false
        $resp = $req.GetResponse()
        $code = [int]$resp.StatusCode
        $resp.Close()
        return $code
    } catch [System.Net.WebException] {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        return $null
    }
}

# Сайт работает, если ответил 2xx или 3xx: после настройки HTTPS на http-запрос он отвечает перенаправлением (301/307).
function Test-SiteOk($code) { return ($code -ge 200 -and $code -lt 400) }

# Кто слушает порт: $null — порт свободен (или его уже занимает сам наш сайт), иначе — описание того, кто занял.
function Get-PortOwner([int]$port, [string]$siteName) {
    $listen = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $listen) { return $null }
    # Порт слушает IIS (системный процесс 4): проверяем, не наш ли это сайт.
    $ours = Get-WebBinding -Name $siteName -Protocol http -ErrorAction SilentlyContinue |
        Where-Object { $_.bindingInformation.Split(':')[1] -eq "$port" }
    if ($ours) { return $null }
    if ($listen.OwningProcess -eq 4) {
        $other = Get-WebBinding -Protocol http -ErrorAction SilentlyContinue |
            Where-Object { $_.bindingInformation.Split(':')[1] -eq "$port" } | Select-Object -First 1
        if ($other) { return "другой сайт IIS ($($other.ItemXPath -replace '.*@name=''([^'']+)''.*', '$1'))" }
        return 'системная служба Windows'
    }
    $proc = Get-Process -Id $listen.OwningProcess -ErrorAction SilentlyContinue
    if ($proc) { return "программа $($proc.ProcessName) (PID $($proc.Id))" }
    return "процесс с PID $($listen.OwningProcess)"
}

# Первый свободный порт из привычных для сайтов.
function Find-FreePort([string]$siteName) {
    foreach ($p in 8090, 8088, 8085, 8081, 8888, 5080, 5180, 9080) {
        if (-not (Get-PortOwner $p $siteName)) { return $p }
    }
    return $null
}

if ($env:PSYSITE_SETUP_TEST) { return }

# ---------- 0. Права администратора ----------
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Stop-WithError 'Запустите PowerShell от имени администратора (правой кнопкой → «Запуск от имени администратора»).'
}

# ---------- 1. IIS ----------
Write-Step 'Проверяю IIS'
if (-not (Get-Module -ListAvailable -Name WebAdministration)) {
    if (-not $InstallIis) {
        Stop-WithError ("IIS не установлен. Запустите скрипт ещё раз с ключом -InstallIis или установите вручную: " +
            "Windows Server — «Диспетчер серверов» → «Добавить роли» → «Веб-сервер (IIS)»; " +
            "Windows 10/11 — «Включение или отключение компонентов Windows» → «Службы IIS».")
    }
    if (Get-Command Install-WindowsFeature -ErrorAction SilentlyContinue) {
        Install-WindowsFeature Web-Server, Web-Mgmt-Console, Web-AppInit | Out-Null
    } else {
        Enable-WindowsOptionalFeature -Online -NoRestart -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-ManagementConsole, IIS-ApplicationInit | Out-Null
    }
    Write-Ok 'IIS установлен'
} else {
    Write-Ok 'IIS есть'
}
Import-Module WebAdministration

# ---------- 2. .NET 10 Hosting Bundle ----------
Write-Step 'Проверяю .NET 10 Hosting Bundle'
$ancm = Join-Path $env:ProgramFiles 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
$runtimes = ''
if (Get-Command dotnet -ErrorAction SilentlyContinue) { $runtimes = (& dotnet --list-runtimes) -join "`n" }
if (-not (Test-Path $ancm) -or $runtimes -notmatch 'Microsoft\.AspNetCore\.App 10\.') {
    Stop-WithError ("Не найден .NET 10 Hosting Bundle. Скачайте «Hosting Bundle» на https://dotnet.microsoft.com/download/dotnet/10.0 " +
        "(раздел ASP.NET Core Runtime → Windows → Hosting Bundle), установите и выполните в этом окне: iisreset. " +
        "Затем запустите скрипт снова.")
}
Write-Ok 'Модуль ASP.NET Core и .NET 10 установлены'

# ---------- 3. Файлы сайта ----------
Write-Step "Готовлю папку сайта $SitePath"
New-Item -ItemType Directory -Force -Path $SitePath | Out-Null
if ($SourcePath) {
    if (-not (Test-Path (Join-Path $SourcePath 'PsySite.dll'))) {
        Stop-WithError "В папке $SourcePath нет PsySite.dll. Укажите папку с результатом публикации (bin\Release\publish-iis)."
    }
    # /E — с подпапками; App_Data, uploads и боевые настройки не трогаем, даже если они есть в источнике.
    robocopy $SourcePath $SitePath /E /NFL /NDL /NJH /NJS /NP /XD App_Data uploads /XF appsettings.Production.json app_offline.htm | Out-Null
    if ($LASTEXITCODE -ge 8) { Stop-WithError "Не удалось скопировать файлы (robocopy, код $LASTEXITCODE)." }
    Write-Ok 'Файлы сайта скопированы'
} elseif (-not (Test-Path (Join-Path $SitePath 'PsySite.dll'))) {
    Stop-WithError "В $SitePath нет файлов сайта. Укажите -SourcePath — папку с результатом публикации."
}
New-Item -ItemType Directory -Force -Path (Join-Path $SitePath 'App_Data') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $SitePath 'wwwroot\uploads') | Out-Null

# ---------- 4. Настройки боевого сайта ----------
Write-Step 'Настройки боевого сайта (appsettings.Production.json)'
$settingsPath = Join-Path $SitePath 'appsettings.Production.json'
$template = Join-Path $ScriptDir 'appsettings.Production.example.json'
$adminPassword = New-ProductionSettings $template $settingsPath $HostName
if ($adminPassword) { Write-Ok 'Файл создан, пароль админки сгенерирован (будет показан в конце)' }
else { Write-Ok 'Файл уже есть — не трогаю' }

# ---------- 5. Пул приложений ----------
Write-Step "Пул приложений $SiteName"
$poolPath = "IIS:\AppPools\$SiteName"
if (-not (Test-Path $poolPath)) { New-WebAppPool -Name $SiteName | Out-Null; Write-Ok 'Создан' } else { Write-Ok 'Уже есть — обновляю настройки' }
Set-ItemProperty $poolPath -Name managedRuntimeVersion -Value ''          # «Без управляемого кода»: .NET Core запускается модулем ASP.NET Core
Set-ItemProperty $poolPath -Name enable32BitAppOnWin64 -Value $false
Set-ItemProperty $poolPath -Name startMode -Value 'AlwaysRunning'          # сайт запускается сразу, первый посетитель не ждёт
Set-ItemProperty $poolPath -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty $poolPath -Name processModel.loadUserProfile -Value $true
Set-ItemProperty $poolPath -Name processModel.maxProcesses -Value 1         # один процесс: так требует SQLite и встроенный (in-process) режим

# ---------- 6. Сайт ----------
Write-Step "Сайт $SiteName"
$site = Get-Website -Name $SiteName -ErrorAction SilentlyContinue

# Свободен ли порт. С доменом (-HostName) порт 80 можно делить с другими сайтами IIS — проверяем только без домена.
if (-not $HostName) {
    $owner = Get-PortOwner $Port $SiteName
    if ($owner) {
        $free = Find-FreePort $SiteName
        $hint = if ($free) { "Например, свободен $free — запустите скрипт ещё раз с ключом -Port $free." } else { 'Укажите другой порт ключом -Port.' }
        Stop-WithError "Порт $Port уже занят: $owner. $hint"
    }
}

if (-not $site) {
    New-Website -Name $SiteName -PhysicalPath $SitePath -ApplicationPool $SiteName -Port $Port -HostHeader $HostName | Out-Null
    if ($HostName) { New-WebBinding -Name $SiteName -Protocol http -Port $Port -HostHeader "www.$HostName" }
    Write-Ok 'Создан'
} else {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $SitePath
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $SiteName
    # Порт сменился с прошлого запуска — переносим http-привязки сайта на новый порт.
    foreach ($b in @(Get-WebBinding -Name $SiteName -Protocol http)) {
        $parts = $b.bindingInformation.Split(':')            # «*:8080:example.ru»
        if ($parts[1] -ne "$Port") {
            Set-WebBinding -Name $SiteName -BindingInformation $b.bindingInformation -PropertyName Port -Value $Port
            Write-Ok "Порт сайта изменён: $($parts[1]) → $Port"
        }
    }
    Write-Ok 'Уже есть — обновил папку и пул'
}
try { Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationDefaults.preloadEnabled -Value $true } catch { }

# ---------- 7. Права на папки ----------
Write-Step 'Права на папки'
$identity = "IIS AppPool\$SiteName"
icacls $SitePath /grant "${identity}:(OI)(CI)RX" /T /Q | Out-Null
icacls (Join-Path $SitePath 'App_Data') /grant "${identity}:(OI)(CI)M" /T /Q | Out-Null
icacls (Join-Path $SitePath 'wwwroot\uploads') /grant "${identity}:(OI)(CI)M" /T /Q | Out-Null
# Файл с паролями читают только администраторы, система и сам сайт.
# Группы указаны кодами (SID), а не именами: на русской Windows «Administrators» называется «Администраторы».
icacls $settingsPath /inheritance:r /grant:r '*S-1-5-32-544:F' '*S-1-5-18:F' "${identity}:R" /Q | Out-Null
Write-Ok "Пул ${identity}: чтение сайта, запись в App_Data и wwwroot\uploads; appsettings.Production.json закрыт от остальных"

# ---------- 8. Запуск и проверка ----------
Write-Step 'Запускаю сайт'
Restart-WebAppPool -Name $SiteName -ErrorAction SilentlyContinue
if ((Get-WebsiteState -Name $SiteName).Value -ne 'Started') { Start-Website -Name $SiteName }
Start-Sleep -Seconds 3

$hostHeader = $HostName
if (-not $hostHeader) { $hostHeader = 'localhost' }
$status = $null
for ($i = 0; $i -lt 10 -and -not (Test-SiteOk $status); $i++) {
    $status = Get-SiteStatus $Port $hostHeader
    if (-not (Test-SiteOk $status)) { Start-Sleep -Seconds 3 }
}

Write-Host ''
if (Test-SiteOk $status) {
    Write-Host 'ГОТОВО: сайт работает.' -ForegroundColor Green
} else {
    Write-Warn "Сайт ответил кодом '$status'. Откройте журнал: $SitePath\App_Data\logs — и раздел «Частые ошибки» в инструкции."
}
$url = "http://localhost:$Port/"
if ($HostName) { $url = "http://$HostName/" }
Write-Host "    Адрес:   $url"
Write-Host "    Админка: ${url}admin  (логин admin)"
if ($adminPassword) {
    Write-Host "    Пароль админки: $adminPassword" -ForegroundColor Yellow
    Write-Host '    Сохраните его сейчас — он показан один раз. Сменить можно в appsettings.Production.json.' -ForegroundColor Yellow
}
Write-Host ''
Write-Host 'Дальше: заполните почту и оплату в appsettings.Production.json и настройте HTTPS (см. инструкцию).'
