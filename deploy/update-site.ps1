<#
.SYNOPSIS
    Обновление уже установленного сайта PsySite на новую версию.

.DESCRIPTION
    1. Кладёт в папку сайта app_offline.htm — IIS останавливает сайт и показывает посетителям «Сайт обновляется».
    2. Делает резервную копию: база (App_Data), загруженные картинки, appsettings.Production.json.
    3. Копирует новую версию. База, логи, картинки и appsettings.Production.json НЕ трогаются;
       файлы старой версии, которых нет в новой, удаляются.
    4. Убирает app_offline.htm и проверяет, что сайт снова отвечает.

    Запускать в PowerShell ОТ ИМЕНИ АДМИНИСТРАТОРА.
    Если что-то пошло не так — в конце скрипт пишет, где лежит резервная копия и как откатиться.

.EXAMPLE
    .\update-site.ps1 -SourcePath C:\Temp\publish-iis
#>
[CmdletBinding()]
param(
    # Папка с новой опубликованной версией (bin\Release\publish-iis).
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [string]$SitePath = 'C:\inetpub\psysite',
    [string]$SiteName = 'PsySite',

    # Куда складывать резервные копии. Лучше на другой диск или в папку, которая копируется в облако.
    [string]$BackupRoot = 'C:\Backups\psysite',

    # Сколько последних копий хранить.
    [int]$KeepBackups = 10
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Write-Step([string]$text) { Write-Host ''; Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Ok([string]$text) { Write-Host "    OK: $text" -ForegroundColor Green }
function Stop-WithError([string]$text) { Write-Host ''; Write-Host "ОШИБКА: $text" -ForegroundColor Red; exit 1 }

# Удалить старые копии, оставив последние $keep. Вынесено в функцию, чтобы проверить без сервера.
function Remove-OldBackups([string]$root, [int]$keep) {
    Get-ChildItem -Path $root -Filter 'psysite-*.zip' -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -Skip $keep | Remove-Item -Force
}

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

if ($env:PSYSITE_SETUP_TEST) { return }

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Stop-WithError 'Запустите PowerShell от имени администратора.'
}
if (-not (Test-Path (Join-Path $SourcePath 'PsySite.dll'))) {
    Stop-WithError "В папке $SourcePath нет PsySite.dll. Укажите папку с результатом публикации (bin\Release\publish-iis)."
}
if (-not (Test-Path (Join-Path $SitePath 'PsySite.dll'))) {
    Stop-WithError "В $SitePath не установлен сайт. Для первой установки запустите setup-iis.ps1."
}

$offline = Join-Path $SitePath 'app_offline.htm'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $BackupRoot "psysite-$stamp.zip"

# ---------- 1. Останавливаем сайт ----------
Write-Step 'Включаю страницу «Сайт обновляется»'
Copy-Item (Join-Path $ScriptDir 'app_offline.htm') $offline -Force
Start-Sleep -Seconds 5   # IIS завершает текущие запросы и отпускает файлы
Write-Ok 'Сайт остановлен для посетителей'

try {
    # ---------- 2. Резервная копия ----------
    Write-Step "Резервная копия → $backup"
    New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null
    $items = @('App_Data', 'wwwroot\uploads', 'appsettings.Production.json') |
        ForEach-Object { Join-Path $SitePath $_ } | Where-Object { Test-Path $_ }
    $copied = $false
    for ($i = 0; $i -lt 5 -and -not $copied; $i++) {
        try { Compress-Archive -Path $items -DestinationPath $backup -Force; $copied = $true }
        catch { Start-Sleep -Seconds 3 }   # файл базы мог быть ещё занят
    }
    if (-not $copied) { throw 'Не удалось сделать резервную копию: файлы заняты. Обновление отменено, сайт не изменён.' }
    Remove-OldBackups $BackupRoot $KeepBackups
    Write-Ok ('Копия сделана, {0:N1} МБ' -f ((Get-Item $backup).Length / 1MB))

    # ---------- 3. Копируем новую версию ----------
    Write-Step 'Копирую новую версию'
    # /MIR — точная копия новой версии (лишние старые файлы удаляются),
    # кроме исключённого: данные, картинки, боевые настройки и сама страница обновления.
    robocopy $SourcePath $SitePath /MIR /R:3 /W:2 /NFL /NDL /NJH /NJS /NP `
        /XD App_Data uploads `
        /XF appsettings.Production.json app_offline.htm | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy завершился с ошибкой (код $LASTEXITCODE)." }
    Write-Ok 'Файлы обновлены'
}
catch {
    Write-Host ''
    Write-Host "ОШИБКА: $($_.Exception.Message)" -ForegroundColor Red
    if (Test-Path $backup) {
        Write-Host "Резервная копия данных: $backup" -ForegroundColor Yellow
        Write-Host 'Чтобы вернуть прежнюю версию: опубликуйте её и запустите этот скрипт с её папкой.' -ForegroundColor Yellow
    }
    Write-Host "Страница «Сайт обновляется» оставлена. Когда разберётесь, удалите файл: $offline" -ForegroundColor Yellow
    exit 1
}

# ---------- 4. Запускаем ----------
Write-Step 'Запускаю сайт'
Remove-Item $offline -Force

# Порт и домен берём из привязки сайта в IIS, чтобы проверочный запрос попал именно в этот сайт.
$port = 80; $hostHeader = 'localhost'
try {
    Import-Module WebAdministration
    $binding = Get-WebBinding -Name $SiteName -Protocol http | Select-Object -First 1
    if ($binding) {
        $parts = $binding.bindingInformation.Split(':')   # «*:80:example.ru»
        $port = [int]$parts[1]
        if ($parts[2]) { $hostHeader = $parts[2] }
    }
} catch { }

$status = $null
for ($i = 0; $i -lt 10 -and -not (Test-SiteOk $status); $i++) {
    Start-Sleep -Seconds 2
    $status = Get-SiteStatus $port $hostHeader
}

Write-Host ''
if (Test-SiteOk $status) {
    Write-Host 'ГОТОВО: сайт обновлён и работает.' -ForegroundColor Green
} else {
    Write-Host "Сайт ответил кодом '$status'. Посмотрите $SitePath\App_Data\logs (файлы errors-*.log и startup-*.log)." -ForegroundColor Yellow
    Write-Host "Резервная копия данных: $backup" -ForegroundColor Yellow
}
