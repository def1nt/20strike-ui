# Архитектура 20strike-ui

## Обзор

Blazor Server приложение (net10.0, `ServerPrerendered`). Четыре функциональные области:

1. **Авторизация** — вход/выход через `IAuthenticationService` (маршрут `/`).
2. **ComputersV2** — список компьютеров с пингом, расширенный поиск, подробная карточка (маршрут `/v2/computers`).
3. **Problems** — сканирование всех компьютеров на проблемы (маршрут `/problems`).
4. **LocationMap** — карта расположения компьютеров: маркеры поверх изображения, добавление/удаление/перемещение, описание, поиск с панорамированием (маршрут `/v2/map[/{ComputerName}]`).

Код организован по слоям в рамках одного проекта: **контракты → инфраструктура → представление**.
Composition root — `Program.cs`.

## Слои и модули

### Контракты — `Data/ComputersV2/`

| Файл | Ответственность |
|---|---|
| `ComputersV2Models.cs` | Модели UI (`ComputerSummary`, `PingInfo`, `ComputerDetail`, `DetailSummary`, `LargeSection`, `ComputerSearchType`, ...). Не зависят от Blazor/HTTP. |
| `Contracts/IComputersV2Client.cs` | Интерфейс клиента v2 API, включая location-операции (`/v2/location/{name}`: список с позициями, чтение, сохранение POST, удаление DELETE). Страницы и сервисы зависят только от него. |
| `ComputersV2Exceptions.cs` | Типизированные исключения: `ComputersV2TimeoutException`, `ComputersV2ApiException`, `ComputersV2RateLimitedException`. |
| `ComputersV2Options.cs` | Конфигурация подключения (секция `ComputersV2` в `appsettings.json`). |

### Инфраструктура — `Data/`, `Infrastructure/`

| Файл | Ответственность |
|---|---|
| `Data/ComputersV2/ComputersV2Client.cs` | HTTP-клиент v2 API (реализация `IComputersV2Client`): таймауты, классификация ошибок, дедупликация и back-off `/ping`. |
| `Data/ComputersV2/ComputersV2Dtos.cs` | Внутренние DTO ответов бэкенда (не публикуются наружу). |
| `Data/ComputersV2/ComputersV2Mapper.cs` | Нормализация ответов API в модели UI (camelCase, форматирование WMI-значений при маппинге). |
| `Data/ComputersV2/ComputersV2Cache.cs` | Per-circuit кэш деталей, проблем и карты пользователей; дедупликация параллельных загрузок, инвалидация для retry. |
| `Data/Authentication/WebsiteAuthenticator.cs` | `AuthenticationStateProvider` + реализация `IAuthenticationService`; хранение сессии в `ProtectedLocalStorage`. |
| `Data/Authentication/SimulatedUserStore.cs` | In-memory хранилище пользователей (`FindUser`). Заменяется реальным провайдером при внедрении бэкенд-аутентификации. |
| `Data/Authentication/PasswordHasher.cs` | SHA-256 хеширование паролей (верхний регистр hex). |
| `Data/Authentication/UserIdentityFactory.cs` | Построение `ClaimsIdentity` из пользователя. |
| `Infrastructure/ComputerPingMonitor.cs` | Фоновый опрос `/ping`: сразу при старте, далее 30 с (5 с при сбое/троттлинге). События `PingsUpdated`/`PingFailed`. |
| `Infrastructure/RetryHelper.cs` | Повтор операции с линейной паузой (1с, 2с, ...) и учётом отмены. |

### Представление — `Pages/`, `Formatting/`

| Файл | Ответственность |
|---|---|
| `Pages/ComputersV2/ComputersV2.razor` + `.razor.cs` | Страница списка: загрузка списка, поиск, фильтры, подписка на пинг, scroll-to-top. Разметка отделена от логики (code-behind). |
| `Pages/ComputersV2/ComputerRow.razor` | Строка списка: статус, IP (копирование), раскрытие карточки. |
| `Pages/ComputersV2/DetailCard.razor` | Карточка деталей: загрузка через кэш, вкладки, клавиатурная навигация. |
| `Pages/ComputersV2/LargeTab.razor` | Виртуализированная таблица с поиском (программы/процессы). |
| `Pages/ComputersV2/ComputerDetailViewFactory.cs` | Чистое построение view-моделей карточки: сводка, вкладки, секции, большие таблицы. |
| `Pages/ComputersV2/ComputerListFilter.cs` | Чистый фильтр списка (имя + «скрыть офлайн»). |
| `Pages/Map/LocationMap.razor` + `.razor.cs` + `.razor.css` | Страница карты: маркеры по `Location` из `GET /v2/computers`, добавление (клик по карте), удаление (DELETE), перемещение (drag + сохранение), редактирование описания, поиск с панорамированием, маршрутный параметр компьютера. |
| `Pages/Map/MapGeometry.cs` | Чистая математика карты: нормализованное пространство координат 0..1000 (X вправо, Y вниз) ↔ CSS-проценты, клампинг, округление. |
| `Pages/Map/MapComputerFilter.cs` | Чистый поиск компьютеров по имени (подстрока, без учёта регистра). |
| `Pages/Problems.razor` | Последовательное сканирование проблем по всем компьютерам. |
| `Pages/Index.razor` | Вход/выход через `IAuthenticationService`. |
| `Formatting/ValueFormatters.cs` | Чистые функции форматирования: даты WMI, байты, логины, заголовки, обрезка. |

## Правила зависимостей

```
Представление (Pages) ──► Контракты (интерфейсы, модели) + чистые хелперы (Formatting, ViewFactory, Filter)
Представление ──► Инфраструктура ТОЛЬКО через интерфейсы (IComputersV2Client, IAuthenticationService)
Инфраструктура ──► Контракты (реализует интерфейсы, использует модели и исключения)
Контракты ──► ничего (без Blazor/HTTP-зависимостей)
```

- Страницы не знают о `ComputersV2Client`/`WebsiteAuthenticator` как о конкретных классах.
- `ComputersV2Cache` и `ComputerPingMonitor` — внутренние сервисы инфраструктуры; страницы используют их напрямую (кэш) или через события (монитор).
- Вся конфигурация внешних адресов — в `appsettings.json`.

## DI-регистрации (`Program.cs`)

| Сервис | Lifetime | Примечание |
|---|---|---|
| `SimulatedDataProviderService` | Singleton | Хранилище пользователей |
| `WebsiteAuthenticator` | Scoped | Один экземпляр на оба интерфейса |
| `IAuthenticationService` | Scoped | → `WebsiteAuthenticator` |
| `AuthenticationStateProvider` | Scoped | → `WebsiteAuthenticator` |
| `HttpClient` | Transient | `AddHttpClient()` |
| `IComputersV2Client` | Scoped | → `ComputersV2Client` с `ComputersV2Options.BaseUrl` |
| `ComputersV2Cache` | Scoped | Per-circuit кэш |
| `ComputerPingMonitor` | Scoped | Per-circuit опрос пинга |

## Точки расширения

1. **Новый бэкенд / URL** — изменить `ComputersV2:BaseUrl` в `appsettings.json`; для другого API — новая реализация `IComputersV2Client`.
2. **Новые секции деталей (WMI-классы)** — добавить секцию в `ComputerDetailViewFactory.BuildTabs` (и при необходимости поле в `ComputerDetail` + маппинг в `ComputersV2Mapper.MapDetail`).
3. **Новые типы поиска** — добавить значение в `ComputerSearchType`, placeholder в `ComputersV2.razor.cs` (`SearchPlaceholder`) и URL-сегмент в `ComputersV2Client.SearchComputersAsync`.
4. **Реальная аутентификация** — заменить `SimulatedDataProviderService` на реальный провайдер, сохранив контракт `IAuthenticationService`; страницы не меняются.
5. **Новые страницы со списком + пингом** — использовать `IComputersV2Client` и `ComputerPingMonitor` (подписка на `PingsUpdated`/`PingFailed`).
6. **Новые большие табличные секции** — следовать паттерну `LargeSection`/`LargeRow` (см. `BuildSoftwareSection`/`BuildProcessSection`).
7. **Источник карты пользователей** — заменить загрузку в `ComputersV2Client.GetUsersAsync`/`ComputersV2Cache.GetOrLoadUsersAsync`.
8. **Семантика координат / карта** — компонент сам определяет систему координат: координаты X/Y хранятся в нормализованном пространстве 0..1000 (см. `Pages/Map/MapGeometry.cs`) и рендерятся как проценты изображения; изображение — файл `wwwroot/images/terra.jpg` (константа `MapImageUrl` в `LocationMap.razor.cs`).
9. **Удаление маркера** — реализовано через `DELETE /v2/location/{name}` (в `v2-location-spec` конечная точка не описана, но предполагается на бэкенде; 404 трактуется как успех).

## Миграционный план для будущих фич

1. **Новая секция в карточке** (например, «Службы»):
   - добавить поле в `ComputerDetail` и маппинг в `ComputersV2Mapper.MapDetail`;
   - добавить вкладку/секцию в `ComputerDetailViewFactory.BuildTabs`;
   - при необходимости — форматирование полей в `ValueFormatters`/`FormatField`.
2. **Новый тип поиска**:
   - `ComputerSearchType` + `SearchPlaceholder` + URL-сегмент в клиенте (3 точки, см. выше).
3. **Новая страница** (например, «Аудит»):
   - страница в `Pages/`, использует `IComputersV2Client` (+ `ComputerPingMonitor` при необходимости);
   - пункт меню в `Shared/NavMenu.razor`.
4. **Бэкенд-аутентификация**:
   - новая реализация `IAuthenticationService` + `AuthenticationStateProvider`;
   - регистрация в `Program.cs` вместо `WebsiteAuthenticator`;
   - удаление `SimulatedDataProviderService`/`PasswordHasher`/`UserIdentityFactory` (или их перенос в тестовый контур).

## Тестирование

- `tests/20strike-ui.Tests` — xUnit, net10.0, ссылается на основной проект.
- `InternalsVisibleTo` в `20strike-ui.csproj` открывает внутренние типы (DTO, маппер) для тестов.
- Покрытие: маппер, форматтеры, view-фабрика, фильтр списка, кэш, retry, монитор пинга, аутентификация.
- Запуск: `dotnet test 20strike-ui.sln`.