# Публикация PsySite на IIS (Windows Server)

Сайт ставится на IIS тремя действиями: опубликовать проект у себя, перенести две папки на сервер,
запустить один скрипт. Обновления потом — одной командой.

| Файл | Зачем |
|---|---|
| `Properties/PublishProfiles/IIS.pubxml` | Профиль публикации: сборка под Windows x64 в папку `bin\Release\publish-iis` |
| `web.config` | Настройки IIS: запуск через модуль ASP.NET Core (in-process), лимит загрузки 15 МБ, скрытые заголовки версии |
| `deploy/setup-iis.ps1` | Первая установка: проверка IIS и .NET, пул, сайт, права на папки, боевые настройки с паролем админки |
| `deploy/update-site.ps1` | Обновление: страница «Сайт обновляется», резервная копия, замена файлов, проверка |
| `deploy/appsettings.Production.example.json` | Шаблон боевых настроек (почта, оплата, база) |
| `deploy/app_offline.htm` | Страница, которую видят посетители во время обновления |

---

## Что понадобится

- **Windows Server 2019 или 2022** (подойдёт и Windows 10/11 для проверки). На Windows Server 2016 тоже
  работает, но из `web.config` нужно убрать `removeServerHeader="true"` — иначе ошибка 500.19.
- Вход на сервер под администратором (обычно по удалённому рабочему столу).
- Домен, у которого A-запись указывает на IP сервера (можно настроить позже: сначала проверить по IP).

---

## 1. Опубликовать сайт (на своём компьютере)

**Visual Studio:** правой кнопкой по проекту → «Опубликовать» → профиль **IIS** → «Опубликовать».

**Или в терминале** из папки с `PsySite.csproj`:

```
dotnet publish -c Release -p:PublishProfile=IIS
```

Результат — папка `bin\Release\publish-iis`. Настройки разработки (с паролем `admin123`), база
и логи в неё не попадают.

## 2. Перенести на сервер

Скопируйте на сервер, например в `C:\Temp`, две папки:

- `bin\Release\publish-iis` → `C:\Temp\publish-iis`
- `deploy` → `C:\Temp\deploy`

Через удалённый рабочий стол можно просто скопировать и вставить.

## 3. Подготовить сервер (один раз)

Порядок важен: **сначала IIS, потом .NET**. Если .NET поставить раньше IIS, модуль ASP.NET Core
не встроится в IIS — тогда запустите установщик Hosting Bundle ещё раз и выберите «Исправить».

1. **IIS.** Скрипт может поставить его сам (ключ `-InstallIis` в шаге 4). Вручную: «Диспетчер серверов»
   → «Добавить роли и компоненты» → «Веб-сервер (IIS)», в компонентах отметьте «Инициализация приложений».
2. **.NET 10 Hosting Bundle.** На странице https://dotnet.microsoft.com/download/dotnet/10.0 в разделе
   «ASP.NET Core Runtime» → Windows → **Hosting Bundle**. Установите, затем в PowerShell от администратора:
   `iisreset`.

## 4. Установить сайт

PowerShell **от имени администратора**:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
cd C:\Temp\deploy
.\setup-iis.ps1 -SourcePath C:\Temp\publish-iis -HostName example.ru
```

- Нет домена или хотите сначала проверить: вместо `-HostName example.ru` укажите `-Port 8090` (если порт занят, скрипт сам подскажет свободный)
  и откройте на сервере http://localhost:8090.
- Если IIS ещё нет — добавьте `-InstallIis`.

Скрипт:
- копирует сайт в `C:\inetpub\psysite`;
- создаёт пул и сайт `PsySite`;
- выдаёт пулу права записи только на `App_Data` и `wwwroot\uploads`;
- создаёт `appsettings.Production.json` и придумывает пароль админки;
- запускает сайт и проверяет, что он отвечает.

**Пароль админки показывается один раз в конце — сохраните его.** Повторный запуск скрипта
безопасен: база, картинки и настройки не перезаписываются.

## 5. Заполнить боевые настройки

Откройте `C:\inetpub\psysite\appsettings.Production.json` в Блокноте и заполните:

- `Site` — имя, почта для уведомлений, адрес сайта;
- `Smtp` — почта для отправки писем (для Яндекса нужен «пароль приложения»);
- `Payments` — платёжная система (пока стоит `Fake`, онлайн-оплата на боевом сайте выключена);
- `Database` и `ConnectionStrings` — если нужна не SQLite (см. «База данных» ниже).

После правки перезапустите сайт: `Restart-WebAppPool PsySite`.

Этот файл не попадает в git и не перезаписывается при обновлениях. Читать его могут только
администраторы и сам сайт.

## 6. Включить HTTPS

Бесплатный сертификат Let's Encrypt проще всего получить программой **win-acme**:

1. Скачайте с https://www.win-acme.com/ и распакуйте, например в `C:\win-acme`.
2. Запустите `wacs.exe` от имени администратора → `N` (новый сертификат) → выберите сайт `PsySite` → согласитесь с условиями.
3. win-acme сам добавит https-привязку на порт 443 и создаст задачу, которая продлевает сертификат.

Сайт сам перенаправляет посетителей с http на https. Проверьте, что в `appsettings.Production.json`
`Site:BaseUrl` начинается с `https://`. В брандмауэре Windows должны быть открыты порты 80 и 443
(правило «Службы Интернета (входящий трафик HTTP/HTTPS)» обычно включается вместе с IIS).

---

## Обновление сайта

1. На своём компьютере опубликуйте новую версию (шаг 1).
2. Скопируйте `publish-iis` на сервер поверх старой `C:\Temp\publish-iis`.
3. На сервере, PowerShell от администратора:

```powershell
cd C:\Temp\deploy
.\update-site.ps1 -SourcePath C:\Temp\publish-iis
```

Скрипт:
- показывает посетителям страницу «Сайт обновляется»;
- делает резервную копию базы, картинок и настроек в `C:\Backups\psysite` и хранит 10 последних;
- заменяет файлы программы, не трогая данные;
- проверяет, что сайт снова отвечает.

Обычно сайт недоступен 10–20 секунд.

---

## База данных

**SQLite** (по умолчанию) подходит для сайта одного специалиста. Ничего ставить не нужно, файл —
`C:\inetpub\psysite\App_Data\site.db`.

**SQL Server Express** — если хотите привычный SQL Server. LocalDB под IIS не подходит: он работает
от имени пользователя, а не службы. Дайте пулу сайта вход в SQL Server (в SSMS → «Создать запрос»):

```sql
CREATE LOGIN [IIS APPPOOL\PsySite] FROM WINDOWS;
CREATE DATABASE PsySite;
GO
USE PsySite;
CREATE USER [IIS APPPOOL\PsySite] FOR LOGIN [IIS APPPOOL\PsySite];
ALTER ROLE db_owner ADD MEMBER [IIS APPPOOL\PsySite];
```

В `appsettings.Production.json`:

```json
"Database": { "Provider": "SqlServer" },
"ConnectionStrings": {
  "Default": "Server=localhost\\SQLEXPRESS;Database=PsySite;Trusted_Connection=True;TrustServerCertificate=True"
}
```

Таблицы сайт создаст сам. Подробнее о базах — в основном README, раздел «База данных».

---

## Если что-то не работает

Сначала загляните в `C:\inetpub\psysite\App_Data\logs`. В `startup-*.log` и `errors-*.log`
сайт пишет причину по-русски.

| Ошибка | Причина | Что сделать |
|---|---|---|
| **500.19** сразу при открытии | Не установлен Hosting Bundle, или он поставлен раньше IIS, или Windows Server 2016 | Установите или «Исправьте» Hosting Bundle, затем `iisreset`. На Server 2016 уберите `removeServerHeader="true"` из `web.config` |
| **500.30** In-process start failure | Сайт упал при запуске | Причина — в `App_Data\logs\startup-*.log`. Если файла нет: в `web.config` поставьте `stdoutLogEnabled="true"`, откройте сайт и смотрите `App_Data\logs\stdout_*.log`. Потом верните `false` |
| **500.31** Failed to find native dependencies | На сервере нет .NET 10 | Установите .NET 10 Hosting Bundle, затем `iisreset` |
| **503** Service Unavailable | Пул приложений остановился | «Диспетчер IIS» → «Пулы приложений» → `PsySite` → «Запустить». Причина — в «Просмотре событий» → «Журналы Windows» → «Приложение» |
| **400** Bad Request — Invalid Hostname | Домен не указан в `AllowedHosts` | Добавьте его в `appsettings.Production.json` через `;`, затем `Restart-WebAppPool PsySite` |
| **403.14** или список файлов | Сайт смотрит не в ту папку | В «Диспетчере IIS» у сайта «Основные настройки» → путь `C:\inetpub\psysite`, в ней должны быть `web.config` и `PsySite.exe` |
| В админку не пускает, в журнале «Небезопасная настройка» | Пароль короче 10 символов или остался из шаблона | Поменяйте `Admin:Password` в `appsettings.Production.json`, затем `Restart-WebAppPool PsySite` |
| `unable to open database file`, `readonly database` | У пула нет права записи в `App_Data` | Запустите `setup-iis.ps1` ещё раз — он заново выдаст права |
| Скрипт: «выполнение сценариев отключено в этой системе» | Политика PowerShell | Сначала выполните `Set-ExecutionPolicy -Scope Process Bypass` в том же окне |

Не получилось — пришлите текст ошибки из браузера и последние строки `App_Data\logs\startup-*.log`
или `errors-*.log`.
