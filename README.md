# AEGIS — ситуационный центр мониторинга аномалий

ASP.NET Core 8 (Razor Pages) · EF Core + SQLite · Identity · SignalR · REST API (JWT, v1/v2, Swagger).
Стиль: ретро-футуризм 70–80-х, секретная лаборатория («Нечто», «Чужой»).

## Запуск

Нужен [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
cd Aegis
dotnet restore
dotnet run
```

Открыть: **http://localhost:5080**

При первом запуске база `aegis.db` создаётся сама, затем заполняется данными (роли, пользователи, 5 аномалий, 4 экспедиции, отчёты).

| Что | Адрес |
|---|---|
| Сайт | http://localhost:5080 |
| Swagger | http://localhost:5080/swagger |
| SignalR-хаб | /hubs/situation |

## Новые интерактивные функции

| # | Функция | Где смотреть |
|---|---------|--------------|
| 1 | **Протокол сканирования** — мини-игра: поймать ползунок в зелёной зоне шкалы, определяется тип аномалии (тепловая / электромагнитная / биологическая / гравитационная). Результат проверяет сервер (подписанный токен на 3 минуты), клиент подделать не может | «Создать отчёт» → кнопка «Запустить сканирование» |
| 2 | **Карта операций** — агенты на SVG-карте, положения обновляются каждые 30 с через SignalR (`Simulation:LocationIntervalSeconds` в appsettings) | меню «Карта операций» (модератор/админ), блок на «Штабе» |
| 3 | **Протокол связи** — чат штаб ⇄ поле (`/hubs/comms`), история сохраняется в `CommunicationLog`, непрочитанные видны в списке экспедиций | страница экспедиции |
| 4 | **Допуски** — `[RequireClearance(n)]`: карта операций — 3, отчёты — 4, админ-панель — 5. Не хватает уровня → страница 403 с анимацией. Админ не может понизить себе допуск ниже 5 | значок допуска в шапке |
| 5 | **Журнал агента** — личные записи (наблюдение / тревога / отчёт), модерация координатором, одобренные попадают в общий журнал экспедиции | «Мой журнал», «Модерация журналов» |

**Важно:** в БД появились новые таблицы и колонки. Если у вас осталась старая `aegis.db` — удалите её и запустите заново
(либо создайте миграцию: `dotnet ef migrations add AddInteractiveFeatures`). Иначе приложение при старте
выдаст понятное сообщение об этом.

Новые AJAX-эндпоинты: `POST /ajax/scan/start`, `POST /ajax/scan/lock`, `GET /ajax/map/state`,
`PUT /ajax/journal/{id}/approve|reject`. SignalR: `/hubs/situation`, `/hubs/comms`.

## Учётные записи



Новые пользователи через «Регистрацию» автоматически получают роль Agent.

## Миграции EF Core

Проект стартует и без миграций (схема создаётся автоматически). Чтобы работать через миграции, как в ТЗ:

```bash
dotnet tool install --global dotnet-ef --version 8.*

# ВАЖНО: сначала удалить базу, созданную автоматически
rm aegis.db            # Windows: del aegis.db

dotnet ef migrations add InitialCreate
dotnet ef database update
dotnet run
```

Если папка `Migrations` есть, приложение при старте само вызывает `Migrate()`.

## Как проверить требования

* **Роли:** войдите под `agent1`. В меню «Мои миссии», «Создать отчёт». Под `moderator` — «Все аномалии», «Управление экспедициями», «Отчёты». Под `admin` — «Админ-панель». `/Admin` под агентом даёт 403, без входа — редирект на логин.
* **SOS / resource-based политика:** `agent1` — руководитель ЭКСП-001 (`/Expeditions/Details/1`), только ему видна кнопка прерывания. У `agent2` её нет, а прямой POST на `/ajax/expeditions/1/abort` вернёт 403.
* **AJAX:** SOS, «Принято» у отчёта (под модератором), добавление агента в экипаж, смена угрозы на странице аномалии.
* **SignalR:** откройте дашборд в двух окнах (разные браузеры). Смените уровень угрозы на странице аномалии под модератором — индикаторы на обоих дашбордах перекрасятся. Телеметрия со страницы экспедиции видна только участникам и модераторам.
* **CSRF:** AJAX-запрос без заголовка `X-CSRF-TOKEN` возвращает 400.
* **XSS:** в отчёте ЭКСП-002 лежит `<script>alert('XSS')</script>`. Он показывается как текст.
* **Заголовки безопасности:** `curl -I http://localhost:5080/Account/Login`.
* **API:** см. `requests.http` и Swagger (кнопка **Authorize**, вставить токен из `/api/auth/login`).

## Структура

```
Aegis/
├── Program.cs                  # конфигурация: Identity, JWT+Cookie, CSRF, CSP, SignalR, Swagger
├── appsettings.json            # строка подключения, JWT-ключ
├── requests.http               # примеры запросов к API
├── Models/                     # Agent, Anomaly, AnomalyLog, Expedition, ExpeditionMember, FieldReport, enum'ы
├── Data/                       # AppDbContext (Fluent API, индексы), DbSeeder
├── Dtos/                       # AnomalyDto, CreateAnomalyDto, UpdateAnomalyDto, LoginRequest...
├── Services/                   # AnomalyService (IHubContext), JwtTokenService, PresenceTracker
├── Hubs/                       # SituationRoomHub
├── Authorization/              # CanAbortExpeditionHandler
├── Controllers/Api/            # /api/auth/login, /api/v1|v2/anomalies
├── Controllers/Ajax/           # fetch-эндпоинты интерфейса
├── Pages/                      # Razor Pages + _Layout
└── wwwroot/                    # css, js (site, realtime, expedition, reports), signalr.min.js
```

## Примечания

* CSP запрещает inline-скрипты и стили: вся логика в файлах `wwwroot/js`. Для `/swagger` политика мягче, потому что Swagger UI использует inline.
* JWT-ключ в `appsettings.json` нужно заменить на свой перед любым реальным использованием.
* Для SignalR и AJAX при отсутствии входа возвращается 401, а не редирект на HTML.
