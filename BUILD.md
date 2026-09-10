# Сборка DLL мода (для форка)

Мод на C# — чтобы изменения в `src/` попали в игру, нужно собрать `ValheimRandomizer.dll`.
Делается один раз за ~10 минут, дальше — одна команда. apworld (`.apworld`) при этом
пересобирать не надо, он уже обновлён в репозитории.

## Что понадобится

1. **.NET 8 SDK** (бесплатно, ~200 МБ): https://dotnet.microsoft.com/download
   - Windows x64 → скачать, установить, перезапустить терминал.
   - Проверка: `dotnet --version` показывает номер версии.
2. **Установленный Valheim** в Steam (нужны его DLL для компиляции, сама игра не запускается).
3. **Профиль r2modman с Jotunn** — Jotunn должен быть установлен в профиле
   (иначе сборка упадёт с `Jotunn.dll not found`).
4. **Код форка** на компьютере:
   ```powershell
   git clone -b arena/01a08b51-valheimarchipelagorandomizer <адрес-твоего-форка>
   cd ValheimArchipelagoRandomizer
   ```
   (или скачай ZIP ветки с GitHub и распакуй).

## Как найти нужные папки

- **GameDir** — папка игры: Steam → Valheim → шестерёнка → Свойства → Установленные файлы →
  «Обзор». Обычно `C:\Program Files (x86)\Steam\steamapps\common\Valheim`.
  Признак: внутри есть `valheim.exe` и папка `valheim_Data`.
- **ProfileDir** — профиль r2modman: r2modman → профиль → Settings → Browse profile folder.
  Признак: внутри есть папка `BepInEx`.

## Сборка и установка (r2modman)

В PowerShell из папки репозитория:

```powershell
.\build.ps1 -GameDir "C:\...\common\Valheim" -ProfileDir "C:\...\profiles\arta" -Install
```

Что произойдёт:
1. Проверка папок и поиск `Jotunn.dll` в профиле.
2. `dotnet build` — компиляция. Успех = `Built: ...\ValheimRandomizer.dll`.
3. `-Install` скопирует в `BepInEx/plugins/ValheimRandomizer/` профиля:
   `ValheimRandomizer.dll`, `Archipelago.MultiClient.Net.dll`, `Newtonsoft.Json.dll`,
   а `research.tsv`/`trophies.tsv` — только если их там нет (твои правки не затрутся).

Дальше:
1. В r2modman **отключи старый мод ValheimRandomizer**, если он был (чтобы не было двух копий).
2. Запусти игру с профиля.
3. Проверка: в `BepInEx/LogOutput.log` есть `Loaded and registered 223 research definition(s)`
   и версия мода 0.2.6 в логе BepInEx (`[Info :ValheimRandomizer]` / список плагинов).

## Без r2modman (классический BepInEx в папке игры)

```powershell
.\build.ps1 -GameDir "C:\...\common\Valheim"
```

`-Install` без профиля не работает — скопируй DLL из
`src/bin/Release/netstandard2.1/` в `BepInEx/plugins/ValheimRandomizer/` вручную.

## Передача другу

DLL собирается один раз — дальше просто отправь другу файлы, собирать ему ничего не надо:
- `ValheimRandomizer.dll` (из `src/bin/Release/netstandard2.1/`),
- `apworld/valheim.apworld` (уже готов в репозитории, версия 0.2.6),
- скажи ему обновить apworld у хоста и сгенерировать новый сид.

## Автосборка через GitHub Actions

**Проверка apworld — работает в облаке автоматически.**
Воркфлоу `.github/workflows/apworld-test.yml` запускается при каждом пуше,
трогающем `apworld/` или `src/`, и делает три вещи:
1. Пересобирает `valheim.apworld` из исходников (бинарник и код не разъедутся).
2. Генерирует 3 тестовых сида настоящим ядром Archipelago 0.6.7
   (`tests/ci/`: соло с максимумом подарков, соло без подарков, мультиигра 2p).
3. Проверяет контракт имён: подарки в `Items.py` = подаркам в `GiftSpawning.cs`,
   биом-локации = `CreateEventLocation` в моде.
Зелёная галка = генерация не сломана. Смотреть: вкладка Actions в репозитории.

**Сборка DLL в облаке — невозможна.** Для компиляции нужны DLL самой игры Valheim
(`Assembly-CSharp.dll` и др.), а это копирайтные файлы — их нельзя заливать на GitHub.
Поэтому DLL собирается либо вручную (см. выше), либо автосборкой на твоём ПК:

### Self-hosted runner (автосборка DLL на своём компьютере)

1. Поставь .NET 8 SDK и убедись, что `.\build.ps1` работает вручную.
2. В репозитории: Settings → Actions → Runners → New self-hosted runner → Windows x64.
3. Выполни показанные команды в PowerShell (скачать, распаковать, `./config.cmd`,
   `./run.cmd`). Пока запущен `run.cmd` — ПК может собирать.
4. В репозитории: Settings → Actions → Variables → New repository variable:
   - `VALHEIM_GAME_DIR` = путь к папке игры (где `valheim.exe`),
   - `R2_PROFILE_DIR` = путь к профилю r2modman (можно пустым).
5. Готово: при каждом пуше в `src/` запустится `build DLL`,
   собранная DLL появится в Actions → последний запуск → Artifacts.
   Без переменной `VALHEIM_GAME_DIR` задача молча пропускается.

## Частые ошибки

| Ошибка | Причина и лечение |
|---|---|
| `dotnet` не найден | Не установлен SDK или не перезапущен терминал после установки |
| `Jotunn.dll not found` | Поставь Jotunn в этот профиль r2modman и запусти игру хоть раз |
| `missing valheim_Data/...` | GameDir указывает не на папку игры (проверь `valheim.exe` рядом) |
| `missing BepInEx/core/...` | ProfileDir указывает не на корень профиля (нужна папка с `BepInEx` внутри) |
| Ошибки `CS0246` (тип не найден) | Устаревшие пути в `src/ValheimRandomizer.csproj` — напиши мне, поправлю |
| В игре старый 0.2.5 | В r2modman остался включён старый мод — отключи, проверь дату файла DLL |

## Ручная сборка без скрипта (для понимания)

```powershell
$env:ValheimInstallDir = "C:\...\common\Valheim"
$env:BepInExDir = "C:\...\profiles\arta"   # только для r2modman
dotnet build src/ValheimRandomizer.csproj -c Release
```

Результат: `src/bin/Release/netstandard2.1/ValheimRandomizer.dll`.
