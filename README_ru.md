# GenerateDocService

## Обзор

Рабочий сервис генерации и преобразования документов на .NET 10, построенный по принципам «чистой архитектуры» (Clean Architecture) и рассчитанный на высокую нагрузку. Проект уже прошёл стадию заготовки: у каждого переключаемого провайдера есть реальная реализация, а первая фаза плана улучшений (`docs/improvement-plan.md`) — персистентность, безопасность, устойчивость — завершена.

Текущее состояние сервиса:

- структура решения и проектов, разделенная на уровни: Domain, Application, Infrastructure, Presentation, Host и Client;
- абстракции для механизмов генерации (движков) с подключаемым реестром, который заполняется сканированием сборок;
- девять движков генерации: `fake`, `scriban`, `dotliquid`, `questpdf`, `miniexcel`, `miniword`, `minipdf`, `fop` и настраиваемый `api`;
- реестр парсеров шаблонов с возможностью подключения новых провайдеров и с выбором парсера, предпочитаемого самим движком;
- кэширование скомпилированных шаблонов: реализация в оперативной памяти и реализация на Redis;
- автоматический выбор движка на основе его возможностей и приоритета (если конкретный движок не указан явно);
- передача **как текстовых, так и двоичных** данных — `payload`/`template` либо их эквиваленты `payloadBase64`/`templateBase64`; именно это делает движки для файлов Office доступными через HTTP;
- контракты для асинхронного обмена сообщениями и фоновая обработка на базе MassTransit с транспортом InMemory или RabbitMQ;
- долговременное хранение задач через EF Core и PostgreSQL: миграции применяются при запуске под блокировкой advisory lock, отправка идёт через паттерн Outbox;
- дедупликация запросов и кэширование готовых документов — в памяти либо на Redis;
- ограниченное число повторных попыток с экспоненциальной задержкой, предельный срок обработки и возможность для движка отказаться от повторов через `NonRetryableGenerationException`;
- хранение артефактов с ограниченным сроком и фоновая очистка, включая перевод задач, зависших в состоянии `Processing`, в `Failed`;
- аутентификация по JWT-токену с ролевой авторизацией: отключена для локального `dotnet run` и обязательна в Docker-стеке;
- настраиваемые ограничения валидации запросов (размер полезной нагрузки, шаблона, метаданных, длина имён);
- адаптер объектного хранилища MinIO и локальная среда запуска через Docker Compose;
- проверки работоспособности (liveness и readiness) для движков и для каждого выбранного инфраструктурного провайдера;
- промежуточное ПО для идентификаторов корреляции (correlation ID) в HTTP-запросах и при асинхронной обработке;
- логирование в консоль через Serilog с включением идентификаторов корреляции;
- трассировка OpenTelemetry для ASP.NET Core, исходящих HTTP-запросов и источника активности MassTransit;
- пользовательские метрики OpenTelemetry для синхронных и асинхронных запросов, включая показатели успешности/ошибок и длительность генерации;
- метаданные OpenAPI и интерфейс Scalar, которые вне окружения Development включаются только через `DocumentProcessing:Api:ExposeOpenApi`;
- синхронные и асинхронные HTTP-эндпоинты;
- пример клиентского приложения на .NET с интерактивным меню из шестнадцати примеров и встроенными микробенчмарками BenchmarkDotNet;
- проект для оценки производительности (performance baseline) на базе NBomber;
- покрытие модульными (unit) и интеграционными тестами, включая тесты с Testcontainers для PostgreSQL, RabbitMQ, Redis и MinIO, которые корректно пропускаются без Docker.

## Краткий обзор архитектуры

`GenerateDocService` представляет собой модульный монолит, построенный на принципе четкого разделения ответственности (separation of concerns):

- `src/Host/GenerateDocService.Api` — уровень, предоставляющий HTTP-интерфейс, аутентификацию, поддержку идентификаторов корреляции, проверки работоспособности, интеграцию с OpenTelemetry и API-контракты.
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Application` отвечает за оркестрацию, выбор механизма генерации (движка), управление жизненным циклом задач, дедупликацию и рабочие процессы создания документов.
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Infrastructure` обеспечивает интеграцию с кэшами, хранилищами данных, механизмом Outbox, системами обмена сообщениями, объектным хранилищем (MinIO) и выполняет проверки работоспособности инфраструктурных компонентов.
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Presentation` выполняет преобразование моделей HTTP-запросов и ответов, поддерживая минималистичный уровень API.
- Модули `src/Engines/*` реализуют подключаемые (plugin-style) механизмы генерации документов. Их возможности определяются через метаданные, что позволяет системе маршрутизировать запросы динамически, без жесткой привязки к конкретным реализациям.
- `src/Host/GenerateDocService.Worker` отвечает за асинхронную генерацию документов, позволяя вынести трудоемкие операции за пределы основного пути обработки HTTP-запросов.

Такой подход упрощает локальный запуск решения, при этом позволяя легко заменить встроенные (in-memory) компоненты на полноценные системы — Redis, RabbitMQ, MinIO и PostgreSQL — без изменения основных контрактов приложения.

## Отказоустойчивость и надежность

Отказоустойчивость заложена в архитектуру сервиса изначально, а не добавлена как второстепенная функция:

- Асинхронная генерация использует модель надежных задач (durable tasks) и паттерн Outbox, что предотвращает потерю запросов при перезапуске API или воркера.
- В случае временных сбоев (брокера сообщений, Redis или самого процесса генерации) предусмотрены повторные попытки с ограничением по времени (backoff) и установленным предельным сроком выполнения, что исключает бесконечные циклы повторов.
- Движок может отказаться от повторов, выбросив `NonRetryableGenerationException`. Классификатор проверяет это исключение раньше любых типов временных сбоев и на каждом уровне цепочки вложенных исключений, поэтому отказ имеет приоритет. Так поступает движок `api`: настроенный целевой сервис — неизвестная третья сторона, чьи эндпоинты могут не быть идемпотентными, и запрос мог быть принят ещё до появления ошибки, поэтому автоматический повтор способен продублировать побочный эффект. Движок `fop`, напротив, повторяет вызовы, поскольку его конвертер идемпотентен.
- Конвертер FOP (HTTP-сервис) защищен стандартным механизмом обеспечения устойчивости: с таймаутами для каждой попытки и общим ограничением времени выполнения, экспоненциальной задержкой между повторами и паттерном «предохранитель» (circuit breaker).
- Механизмы дедупликации и кэширования готовых документов предотвращают выполнение избыточной работы: если для заданного набора входных данных (отпечатка полезной нагрузки) документ уже был создан, повторная генерация не выполняется.
- Для in-memory провайдеров установлены ограничения по времени жизни (TTL) и объему данных, что предотвращает их бесконтрольный рост под нагрузкой.
- Фоновая очистка переводит задачи, оставшиеся в состоянии `Processing` дольше `StuckProcessingTaskSeconds`, в `Failed`, поэтому аварийно завершившийся воркер не может оставить клиента в бесконечном опросе статуса.
- Проверки работоспособности (health checks и readiness probes) позволяют выявлять проблемы в инфраструктуре или движках генерации до того, как они приведут к сбоям, заметным для конечных пользователей.

Таким образом, проект спроектирован с учетом удобства разработки и надежности в промышленной эксплуатации, сохраняя при этом модульность, достаточную для последующего перехода к более распределенной модели развертывания.

## Документация

- Архитектура и схемы потоков вызовов: `docs/architecture.md` (на русском: `docs/architecture_ru.md`)
- План улучшений и дорожная карта: `docs/improvement-plan.md`
- Матрица бенчмарков и чтение отчётов: `docs/performance-baseline.md`
- Полный путь асинхронной генерации для `miniexcel`: `docs/miniexcel-async-sequence.md` (на русском: `docs/miniexcel-async-sequence_ru.md`)
- Английская версия этого файла: `README.md`

## Проекты

- `src/Engines/GenerateDocService.Engine.Abstractions`
- `src/Engines/GenerateDocService.Engine.Api`
- `src/Engines/GenerateDocService.Engine.Scriban`
- `src/Engines/GenerateDocService.Engine.DotLiquid`
- `src/Engines/GenerateDocService.Engine.QuestPdf`
- `src/Engines/GenerateDocService.Engine.MiniExcel`
- `src/Engines/GenerateDocService.Engine.MiniWord`
- `src/Engines/GenerateDocService.Engine.MiniPdf`
- `src/Engines/GenerateDocService.Engine.Fop`
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Domain`
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Application`
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Infrastructure`
- `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Presentation`
- `src/Host/GenerateDocService.Api`
- `src/Host/GenerateDocService.Worker`
- `src/Clients/GenerateDocService.SampleClient`
- `tests/Unit/GenerateDocService.DocumentProcessing.Application.Tests`
- `tests/Integration/GenerateDocService.Api.IntegrationTests`
- `tests/Performance/GenerateDocService.LoadTests.NBomber`

## Регистрация движков

Движки генерации документов регистрируются как подключаемые модули:

- каждый движок реализует `IDocumentGenerationEngine`;
- каждый движок помечен метаданными `DocumentEngineAttribute`;
- инфраструктура сканирует сборки и регистрирует движки в DI;
- реестр приложения предоставляет как экземпляры, так и дескрипторы для маршрутизации.

Доступные движки:

- `fake` — для простых проверок и smoke-сценариев;
- `scriban` — генерация текста, markdown, html и json из JSON-данных по шаблонам;
- `dotliquid` — то же на базе DotLiquid;
- `questpdf` — генерация PDF из JSON;
- `miniexcel` — генерация XLSX (Excel) из массивов или объектов JSON;
- `miniword` — заполнение шаблонов .docx из JSON-данных с синтаксисом `{{tag}}`;
- `minipdf` — преобразование .docx/.xlsx в PDF (исходный документ передаётся в base64);
- `fop` — преобразование FO/XSL-FO в PDF через внешний сервис FOP Converter;
- `api` — вызов настроенного внешнего HTTP-API с возвратом ответа в качестве документа.

Приоритеты и поддерживаемые форматы приведены в таблице в `docs/architecture_ru.md` (§4.1). Автовыбор берёт движок с наибольшим приоритетом, а при равенстве — первый по имени.

### Правила обработки данных в MiniExcel

Путь «таблица» (без `template`) записывает по одной строке на элемент и подчиняется трём правилам:

- **Числа форматируются инвариантно.** Движок фиксирует `CultureInfo.InvariantCulture`, поэтому `1234.56` записывается одинаково независимо от локали узла. Раньше путь с шаблоном использовал текущую культуру и выдавал `1234,56` на узле с `ru-RU`.
- **Вложенные значения отклоняются.** Объект или массив JSON там, где ожидается значение ячейки, приводит к `InvalidOperationException` с указанием пути в JSON, например `$.address`. Раньше в ячейку записывалось имя CLR-типа `System.Collections.Generic.Dictionary\`2[...]`, а запрос считался успешным — повреждённая книга отправлялась незаметно. Вложенные данные необходимо предварительно разворачивать в плоскую структуру в самой полезной нагрузке.
- **Неровные массивы выравниваются.** Заголовки образуют объединение ключей всех элементов, отсутствующие значения становятся пустыми ячейками. Раньше заголовки брались только из первого элемента, поэтому элемент без ключа вызывал необработанный `KeyNotFoundException`, а элемент с новым ключом этот ключ терял.

Путь с шаблоном также поддерживает **повторяющиеся строки**: разместите `{{items.name}}`, `{{items.qty}}` и подобные в одной строке шаблона и привяжите `items` к массиву JSON — MiniExcel развернёт эту строку по одной на запись, записывая числа как числовые ячейки. Синтаксис — `{{list.field}}`; символ `$` в него **не** входит и остаётся в ячейке как литерал. Пример 15 в клиентском приложении демонстрирует счёт, построенный таким образом.

Одно ограничение MiniExcel принято осознанно, а не исправлено: на пути с *шаблоном* плейсхолдер `{{placeholder}}`, привязанный к вложенному объекту или массиву, приводит к необработанному `IndexOutOfRangeException` внутри MiniExcel. Движок не может отклонить вложенность в этом случае, не сломав описанный выше синтаксис коллекций `{{list.field}}`, и не может отличить одно от другого без разбора токенов шаблона книги.

### Движок `api`

Обобщение движка `fop`: он отправляет полезную нагрузку методом POST во внешний HTTP-API и возвращает тело ответа как документ. В отличие от `fop`, адрес и учётные данные — это **конфигурация, а не данные запроса**. Запрос может лишь назвать заранее настроенный целевой сервис, поэтому движок невозможно превратить в открытый прокси, а секрет никогда не передаётся в теле запроса.

```jsonc
"DocumentProcessing": {
  "Api": {
    "DefaultTarget": "converter",
    "Targets": {
      "converter": {
        "BaseUrl": "http://localhost:8080/",
        "RequestPath": "api/v1/convert/sync?payloadType=XSL_FO&outputFormat=PDF&correlationId={correlationId}",
        "Method": "Post",
        "ContentType": "application/xml",
        "InputFormat": "xsl-fo",
        "OutputFormat": "pdf",
        "TimeoutSeconds": 120,
        "Authentication": { "Scheme": "None" }
      },
      "secure": {
        "BaseUrl": "https://api.example.com/",
        "RequestPath": "v1/documents",
        "InputFormat": "json",
        "OutputFormat": "pdf",
        "Authentication": {
          "Scheme": "OAuth2ClientCredentials",
          "TokenUrl": "https://id.example.com/connect/token",
          "ClientId": "gds",
          "ClientSecret": "set-this-through-configuration-not-source-control",
          "Scope": "documents.convert",
          "TokenExpirySkewSeconds": 30
        }
      }
    }
  }
}
```

Запрос выбирает цель через ключ метаданных `api.target`; без него используется `DefaultTarget`, а если не задан и он — первая подходящая по форматам цель в алфавитном порядке. Поля `InputFormat` и `OutputFormat` у цели необязательны: если их не указать, цель подходит для любых форматов. Подстановки `{requestId}` и `{correlationId}` в `RequestPath` заменяются для каждого запроса.

Полезная нагрузка отправляется как необработанное тело запроса с указанным `ContentType`, если только не задано поле `MultipartFieldName` — в этом случае она отправляется как часть `multipart/form-data` под этим именем, чего и ожидает конвертер FOP (`primaryDocument`). `Method` принимает значение `Post` (по умолчанию) или `Put`.

Возможные значения `Authentication:Scheme`:

| Схема | Обязательные поля | Отправляемый заголовок |
|-------|-------------------|------------------------|
| `None` | — | отсутствует |
| `Basic` | `Username`, `Password` | `Authorization: Basic base64(user:pass)` |
| `Bearer` | `Token` | `Authorization: Bearer <token>` |
| `OAuth2ClientCredentials` | `TokenUrl`, `ClientId`, `ClientSecret`, необязательно `Scope` | `Authorization: Bearer <полученный токен>` |

Токены client credentials кэшируются в разрезе адреса выдачи и клиента и обновляются за `TokenExpirySkewSeconds` до истечения срока, поэтому интенсивно используемая цель не обращается к поставщику идентификации на каждый документ. Параллельные запросы разделяют одну активную операцию получения токена.

Некорректно настроенные цели приводят к исключению при **запуске**, а не при первом запросе. Повторные попытки не выполняются **ни на одном уровне** намеренно: движок оборачивает сбои внешнего сервиса в `NonRetryableGenerationException`, поэтому повтор процесса фоновой обработки для временных сбоев их также пропускает. Настроенная цель — неизвестная третья сторона, чьи эндпоинты могут не быть идемпотентными, а запрос мог быть принят ещё до сбоя, поэтому автоматический повтор способен продублировать побочный эффект. Вместо этого для каждой цели задан предельный срок `TimeoutSeconds`, а решение о повторной отправке принимает вызывающая сторона.

Специализированный движок `fop` — противоположный случай: его конвертер идемпотентен, поэтому он сохраняет и конвейер устойчивости HTTP (см. раздел «Устойчивость и повторы»), и повторы процесса обработки.

Поскольку возможности движка определяются конфигурацией, он регистрируется с пустыми метаданными форматов: `/api/v1/engines` выводит его без форматов, однако автовыбор корректно находит его через `CanHandle`. Его приоритет равен 10, поэтому при возможности обработать запрос и тем, и другим движком побеждает специализированный.

Цель с именем `converter` предварительно настроена в `appsettings.Development.json` и в compose-стеке; она указывает на конвертер FOP и использует требуемый им формат multipart. Именно её вызывает пример 16 клиентского приложения, что позволяет сравнить универсальный движок со специализированным `fop` на одном и том же внешнем сервисе. Удалите её, если не хотите, чтобы движок `api` также обрабатывал пару `xsl-fo -> pdf`.

Учётные данные следует передавать через переменные окружения, а не через `appsettings.json`, который находится в системе контроля версий, — например `DocumentProcessing__Api__Targets__secure__Authentication__ClientSecret`.

## Основы асинхронного конвейера

Асинхронный поток использует явные контракты сообщений и абстракции обработки:

- `GenerateDocumentRequested`
- `DocumentGenerated`
- `DocumentGenerationFailed`

Фоновая обработка выполняется в отдельном узле-воркере:

- `src/Host/GenerateDocService.Worker` — выделенный сервис фоновой обработки;
- воркер потребляет сообщения `GenerateDocumentRequested` из шины;
- воркер использует те же уровни Application и Infrastructure, что и API;
- API и воркер могут масштабироваться независимо.

Инфраструктура размещает MassTransit напрямую:

- транспорт по умолчанию: `InMemory`;
- готовый к промышленному использованию вариант: `RabbitMQ`;
- потребитель: `GenerateDocumentRequestedConsumer`;
- при транспорте `InMemory` планировщик публикует (`Publish`) `GenerateDocumentRequested`, и MassTransit маршрутизирует сообщение потребителю по соглашению;
- при транспорте `RabbitMQ` планировщик *отправляет* (`Send`) его в очередь с именем из `DocumentProcessing:Messaging:QueueName` (по умолчанию `document-generation`), и конечная точка приёма потребителя привязана к тому же имени. Эти значения должны совпадать: при расхождении сообщения останутся в очереди без потребителей;
- события результата публикуются как `DocumentGenerated` / `DocumentGenerationFailed`.

MassTransit закреплён на версии **8.5.10**: версия 9 требует платной лицензии.

## Основы кэширования и дедупликации

Уровень приложения разделяет две задачи:

- дедупликация запросов по детерминированному отпечатку;
- кэш готовых документов по отпечатку и ссылке на артефакт.

Парсеры шаблонов также поддерживают кэширование скомпилированных шаблонов через `ICompiledTemplateCache`; реализация в оперативной памяти уже подключена для Scriban. При включённом кэшировании на Redis скомпилированные шаблоны могут храниться там же через ту же модель провайдеров.

Хранилище артефактов возвращает `DocumentArtifactReference` вместо необработанной строки пути, что делает абстракцию готовой к провайдерам в стиле MinIO/S3.

Инфраструктура поддерживает конфигурацию:

- `DocumentProcessing:Persistence:Provider = InMemory | PostgreSql`
- `DocumentProcessing:Caching:Provider = InMemory | Redis`
- `DocumentProcessing:Messaging:Transport = InMemory | RabbitMQ`
- `DocumentProcessing:Storage:Provider = InMemory | ObjectStorage`

Хранение задач поддерживает репозиторий на EF Core с PostgreSQL для долговременного хранения асинхронных задач.

Схема версионируется миграциями EF Core в каталоге `src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Infrastructure/Persistence/Migrations`. При выборе провайдера `PostgreSql` и API, и воркер применяют ожидающие миграции при запуске; они сериализуются за блокировкой advisory lock в PostgreSQL, поэтому несколько одновременно стартующих узлов не могут конфликтовать по DDL. Для провайдера `InMemory` ничего не применяется.

Чтобы добавить миграцию после изменения `DocumentGenerationDbContext`:

```powershell
dotnet tool restore
dotnet ef migrations add <Name> --project src/Modules/DocumentProcessing/GenerateDocService.DocumentProcessing.Infrastructure --output-dir Persistence/Migrations
```

`dotnet-ef` закреплён как локальный инструмент в `.config/dotnet-tools.json`, поэтому `dotnet tool restore` нужно выполнить один раз после клонирования. Для формирования миграций используется `DocumentGenerationDbContextFactory` — фабрика только времени проектирования, которая не позволяет `dotnet ef` запускать узел API.

Адаптер FOP вызывает `POST /api/v1/convert/sync` сервиса FOP Converter API с multipart-частью `primaryDocument`. Базовый адрес настраивается через `DocumentProcessing:Fop:BaseUrl`; по умолчанию это `http://localhost:8080/`, а документ OpenAPI конвертера доступен по адресу `/v3/api-docs`. Конвертер — отдельный стек, он не входит в `docker-compose.yml`.

Значение по умолчанию разрешается только при вызове с узла: внутри контейнера `localhost:8080` — это сам контейнер. Поэтому compose-стек задаёт `DocumentProcessing__Fop__BaseUrl` и для API, и для воркера как `${FOP_CONVERTER_BASE_URL:-http://host.docker.internal:8080/}` и добавляет запись узла `host.docker.internal:host-gateway`, чтобы движок `fop` мог обратиться к конвертеру, опубликованному на Docker-узле. Переопределите `FOP_CONVERTER_BASE_URL` в `deploy/docker/.env`, если конвертер слушает другой адрес или входит в эту compose-сеть.

## Локальная инфраструктура

Локальные зависимости, приближенные к промышленным, определены в:

- `deploy/docker/docker-compose.yml`
- `src/Host/GenerateDocService.Api/Dockerfile`
- `src/Host/GenerateDocService.Worker/Dockerfile`

Compose-стек запускает:

- **API** (`generatedocservice-api`) — HTTP-эндпоинты на порту узла **8081** (порт контейнера 8080); единственный сервис, публикуемый на всех интерфейсах;
- **Worker** (`generatedocservice-worker`) — фоновая обработка, публикуемых портов нет;
- **RabbitMQ** — брокер сообщений (5672, интерфейс управления 15672);
- **Redis** — кэширование и дедупликация (порт узла **6380**, порт контейнера 6379);
- **MinIO** — объектное хранилище (9000, консоль 9001);
- **PostgreSQL** — долговременное хранение задач (5432).

Порты узла 8081 и 6380 используются вместо привычных 8080 и 6379, чтобы стек мог работать параллельно с отдельным проектом `fopconverter`, который публикует именно эти два порта. Контейнеры по-прежнему обращаются друг к другу по имени сервиса через внутреннюю compose-сеть (`redis:6379`), поэтому переназначение действует только на стороне узла.

Все инфраструктурные порты привязаны к `127.0.0.1`: они доступны с узла, но не из сети. Контейнеры взаимодействуют через внутреннюю compose-сеть.

### Быстрый запуск через Docker

В Docker-стеке аутентификация **включена**, поэтому необходимо задать ключ подписи JWT. При его отсутствии compose завершается с понятным сообщением.

```powershell
# 1. Создайте файл env рядом с docker-compose.yml
copy .\deploy\docker\.env.example .\deploy\docker\.env

# 2. Поместите случайный секрет длиной не менее 32 символов в GDS_AUTH_SIGNING_KEY
#    в файле .\deploy\docker\.env  (этот файл игнорируется Git)

# 3. Соберите и запустите все сервисы
docker compose -f .\deploy\docker\docker-compose.yml up --build -d
```

### Доступ к сервисам

| Сервис | URL | Учётные данные |
|--------|-----|----------------|
| **API** | http://localhost:8081 | требуется JWT (см. ниже) |
| **Scalar API Reference** | http://localhost:8081/scalar/v1 | — |
| **OpenAPI JSON** | http://localhost:8081/openapi/v1.json | — |
| **RabbitMQ Management** | http://localhost:15672 | guest / guest |
| **MinIO Console** | http://localhost:9001 | minioadmin / minioadmin |
| **PostgreSQL** | localhost:5432 | postgres / postgres |
| **Redis** | localhost:6380 | — |

Эндпоинты проверки работоспособности (`/health/live`, `/health/ready`) остаются анонимными, чтобы пробы контейнера и оркестратора продолжали работать.

### Получение токена

Токены — это JWT с алгоритмом HS256, подписанные ключом `GDS_AUTH_SIGNING_KEY`. Роли читаются из стандартного утверждения `role`; роль `DocumentAdmin` удовлетворяет всем политикам.

```powershell
function New-GdsToken {
    param(
        [string]$Key = $env:GDS_AUTH_SIGNING_KEY,
        [string]$Role = "DocumentAdmin",
        [int]$TtlMinutes = 60
    )

    function ConvertTo-Base64Url([byte[]]$bytes) {
        [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }

    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $header = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
    $payloadJson = @{
        iss = "GenerateDocService"
        aud = "GenerateDocService.Api"
        sub = "local-dev"
        role = $Role
        nbf = $now
        iat = $now
        exp = $now + ($TtlMinutes * 60)
    } | ConvertTo-Json -Compress
    $payload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payloadJson))

    $hmac = [System.Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($Key))
    $signature = ConvertTo-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes("$header.$payload")))

    "$header.$payload.$signature"
}
```

### Примеры запросов к API

Все вызовы `/api/v1/*` требуют bearer-токен, если аутентификация включена:

```powershell
$token = New-GdsToken
$auth = "Authorization: Bearer $token"
```

**Список движков:**
```powershell
curl http://localhost:8081/api/v1/engines -H $auth
```

**Синхронная генерация документа (MiniExcel):**
```powershell
curl -X POST http://localhost:8081/api/v1/documents/sync `
  -H "Content-Type: application/json" -H $auth `
  -d '{
    "engine": "miniexcel",
    "inputFormat": "json",
    "outputFormat": "xlsx",
    "payload": "[{\"Name\": \"Alice\", \"Age\": 30}, {\"Name\": \"Bob\", \"Age\": 25}]"
  }'
```

**Синхронная генерация документа (FOP):**
```powershell
curl -X POST http://localhost:8081/api/v1/documents/sync `
  -H "Content-Type: application/json" -H $auth `
  -d '{
    "engine": "fop",
    "inputFormat": "xsl-fo",
    "outputFormat": "pdf",
    "payload": "<fo:root xmlns:fo=\"http://www.w3.org/1999/XSL/Format\"><fo:layout-master-set><fo:simple-page-master master-name=\"default\" page-height=\"11in\" page-width=\"8.5in\"><fo:region-body/></fo:simple-page-master></fo:layout-master-set><fo:page-sequence master-reference=\"default\"><fo:flow flow-name=\"xsl-region-body\"><fo:block>Hello from FOP</fo:block></fo:flow></fo:page-sequence></fo:root>"
  }' --output document.pdf
```

**Асинхронная генерация документа:**
```powershell
curl -X POST http://localhost:8081/api/v1/documents/async `
  -H "Content-Type: application/json" -H $auth `
  -d '{
    "engine": "scriban",
    "inputFormat": "json",
    "outputFormat": "txt",
    "templateFormat": "scriban",
    "payload": "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
    "template": "Document: {{ document }} for {{ customer.name }}"
  }'
```

**Проверка статуса задачи:**
```powershell
curl http://localhost:8081/api/v1/tasks/{taskId} -H $auth
```

**Скачивание созданного документа:**
```powershell
curl http://localhost:8081/api/v1/tasks/{taskId}/download -H $auth --output document.xlsx
```

### Остановка сервисов

```powershell
# Остановить все сервисы
docker compose -f .\deploy\docker\docker-compose.yml down

# Остановить и удалить тома (включая данные PostgreSQL)
docker compose -f .\deploy\docker\docker-compose.yml down -v
```

### Просмотр журналов

```powershell
# Все сервисы
docker compose -f .\deploy\docker\docker-compose.yml logs -f

# Конкретный сервис
docker compose -f .\deploy\docker\docker-compose.yml logs -f generatedocservice-api
```

## Валидация запросов

Эндпоинты API применяют настраиваемые ограничения, предотвращающие злоупотребление ресурсами:

- **Размер полезной нагрузки**: по умолчанию 10 МБ
- **Размер шаблона**: по умолчанию 5 МБ
- **Метаданные**: не более 50 записей, длина ключа 128, длина значения 1024
- **Длина названий формата и движка**: по умолчанию 64 символа
- **Длина идентификатора запроса**: по умолчанию 64 символа
- **Источник полезной нагрузки**: требуется ровно одно из полей `payload` (текст) или `payloadBase64` (двоичные данные)
- **Источник шаблона**: не более одного из полей `template` или `templateBase64`
- **Форма base64**: строгая — стандартный алфавит, длина кратна четырём, не более двух завершающих `=`, без встроенных пробельных символов

Ограничения размера применяются к **декодированному** числу байт для полей base64, поэтому кодирование нельзя использовать для обхода ограничения. Валидатор вычисляет эту длину арифметически, а не декодированием, поэтому тело запроса декодируется один раз — в сопоставителе моделей.

Некорректные запросы возвращают `400 Bad Request` с детализацией по полям.

Ограничения настраиваются через `DocumentProcessing:Validation` в `appsettings.json`.

## Аутентификация и авторизация

API поддерживает аутентификацию по JWT-bearer с ролевой авторизацией:

- **Отключена по умолчанию** при запуске через `dotnet run`, поэтому для локальной разработки токены не нужны
- **Включена в Docker-стеке** — `docker-compose.yml` задаёт `DocumentProcessing__Authentication__Enabled=true`
- **Роли**: `DocumentSubmit`, `DocumentRead`, `DocumentDownload`, `DocumentAdmin`
- **Сопоставление эндпоинтов**:
  - `POST /documents/*` → `DocumentSubmit` или `DocumentAdmin`
  - `GET /engines/*`, `GET /tasks/{taskId}` → `DocumentRead` или `DocumentAdmin`
  - `GET /tasks/{taskId}/download` → `DocumentDownload` или `DocumentAdmin`
  - `GET /health/*` → анонимно, чтобы пробы продолжали работать

Настройки JWT задаются через `DocumentProcessing:Authentication` (`Issuer`, `Audience`, `SigningKey`).

Ключ подписи **не имеет значения по умолчанию в репозитории**. Если аутентификация включена, узел аварийно завершает запуск, когда `SigningKey` пуст или короче 32 символов, поэтому отсутствующий секрет не может незаметно превратиться в широко известный ключ. Передавайте его через конфигурацию или переменную окружения `GDS_AUTH_SIGNING_KEY` (см. `deploy/docker/.env.example`); никогда не фиксируйте реальное значение в репозитории.

## Хранение артефактов

Сохранённые артефакты и записи задач автоматически очищаются фоновым сервисом:

- **Завершённые задачи**: удаляются после `RetentionDays` (по умолчанию 30 дней)
- **Неуспешные задачи**: удаляются после `FailedTaskRetentionDays` (по умолчанию 7 дней)
- **Покинутые задачи**: задача, остающаяся в состоянии `Processing` дольше `StuckProcessingTaskSeconds` (по умолчанию 1800), переводится в `Failed`, поэтому аварийно завершившийся воркер не может оставить клиента в бесконечном опросе
- **Интервал очистки**: выполняется каждые `CleanupIntervalHours` (по умолчанию 1 час)

Настраивается через `DocumentProcessing:Retention` в `appsettings.json`.

Политика очистки реализована в `ArtifactRetentionCleaner`; `ArtifactCleanupBackgroundService` является только её планировщиком.

## Устойчивость и повторы

Сбои генерации классифицируются до того, как будут признаны окончательными.

**Фоновая обработка** (`DocumentProcessing:Resilience`):

- **Таймаут обработки**: `ProcessingTimeoutSeconds` (по умолчанию 300) ограничивает одну генерацию. Его превышение является окончательным и завершает задачу ошибкой с пояснением.
- **Ограниченные повторы**: временные сбои повторяются до `MaxAttempts` (по умолчанию 3) с экспоненциальной задержкой от `InitialDelayMilliseconds` (250) до `MaxDelayMilliseconds` (5000) и множителем `BackoffMultiplier` (2.0).
- **Классификация**: `TransientFailureClassifier` считает повторяемыми `HttpRequestException`, `TimeoutException`, `IOException` и `SocketException`. Отмена всегда окончательна, поэтому истёкший предельный срок не приводит к ещё одной полной попытке. При включённом провайдере Redis классификатор дополнительно учитывает `RedisConnectionException`. Исключение `NonRetryableGenerationException` в любом месте цепочки отменяет повторы.
- Событие `DocumentGenerationFailed` публикуется один раз после последней попытки, а не на каждом повторе.

**Клиент конвертера FOP** (`DocumentProcessing:Fop`):

Вызов внешнего конвертера проходит через стандартный конвейер устойчивости (таймаут, повторы, предохранитель):

| Параметр | По умолчанию | Назначение |
|----------|--------------|------------|
| `AttemptTimeoutSeconds` | 30 | Таймаут одной попытки |
| `TotalTimeoutSeconds` | 120 | Бюджет на весь вызов с учётом повторов |
| `MaxRetryAttempts` | 2 | Повторы после первой попытки |
| `RetryBaseDelayMilliseconds` | 1000 | База экспоненциальной задержки |
| `CircuitBreakerMinimumThroughput` | 10 | Число сбоев до возможного срабатывания предохранителя |
| `CircuitBreakerSamplingDurationSeconds` | 60 | Окно выборки предохранителя |
| `CircuitBreakerBreakDurationSeconds` | 30 | Длительность разомкнутого состояния |

`HttpStandardResilienceOptions` требует, чтобы окно выборки покрывало не менее двух таймаутов попытки, а общий бюджет — все попытки. При нарушении этих условий значения автоматически расширяются, поэтому ошибочная настройка не может превратить первый же запрос к FOP в `OptionsValidationException`.

**In-memory провайдеры** (`DocumentProcessing:InMemory`):

Кэш и хранилища дедупликации в памяти ограничены, чтобы соответствовать эквивалентам на Redis, а не расти всё время жизни процесса:

- `GeneratedDocumentTtlMinutes` (по умолчанию 60) и `MaxCachedDocuments` (по умолчанию 1000) — при превышении объёма вытесняются самые старые записи
- `DeduplicationTtlMinutes` (по умолчанию 15) — без срока действия задача, прерванная в середине выполнения, закрепила бы свой отпечаток навсегда

Значение TTL `0` отключает истечение срока.

## API

Текущие эндпоинты:

- `GET /health/live`
- `GET /health/ready`
- `GET /api/v1/engines`
- `GET /api/v1/engines/{name}`
- `POST /api/v1/documents/sync`
- `POST /api/v1/documents/async`
- `GET /api/v1/tasks/{taskId}`
- `GET /api/v1/tasks/{taskId}/download`

Проверки работоспособности охватывают:

- зарегистрированные движки генерации документов;
- выбранный провайдер кэша — соединение с Redis при включённом Redis, иначе статическую проверку in-memory;
- выбранный провайдер хранения — соединение с PostgreSQL при включённом PostgreSQL, иначе статическую проверку in-memory;
- выбранный транспорт сообщений — готовность TCP-эндпоинта RabbitMQ при включённом RabbitMQ, иначе статическую проверку in-memory;
- выбранное хранилище артефактов — готовность корзины MinIO при включённом объектном хранилище, иначе статическую проверку in-memory;
- проверку `authentication`, сообщающую о настроенной авторизации.

Каждая регистрация помечена тегами `live`, `ready` либо обоими: `/health/live` выводит только проверки с тегом `live`, поэтому остаётся успешным, пока внешний зависимый компонент недоступен, а `/health/ready` включает проверки провайдеров, определяющие готовность принимать трафик. Резервные проверки in-memory несут оба тега, поскольку внешнего компонента для ожидания нет.

### Публикация поверхностей для разработчиков

`DocumentProcessing:Api` управляет двумя вещами, полезными локально, но раскрывающими детали реализации в промышленной среде. Обе принудительно включаются окружением Development, поэтому `dotnet run` не требует настройки:

| Параметр | По умолчанию | Эффект при значении true |
|----------|--------------|--------------------------|
| `ExposeOpenApi` | false | Публикует `/openapi/v1.json` и интерфейс Scalar |
| `ExposeHealthDetails` | false | Возвращает подробный JSON по каждой проверке из `/health/live` и `/health/ready` |

При `ExposeHealthDetails` равно false пробы отвечают только общим статусом, что не позволяет анонимным вызывающим сторонам увидеть имена классов реализации движков, например `ScribanDocumentGenerationEngine`. Compose-стек работает с `ASPNETCORE_ENVIRONMENT=Production` и задаёт `ExposeOpenApi=true`, поскольку Scalar описан выше; подробный вывод проверок работоспособности при этом остаётся выключенным. Запуск в режиме Production также убирает страницу исключений для разработчиков, поэтому необработанная ошибка больше не возвращает трассировку стека с абсолютными путями к исходным файлам.

Идентификатор корреляции:

- заголовок запроса: `X-Correlation-Id`
- если заголовок не передан, API формирует его автоматически;
- значение возвращается в заголовке ответа;
- то же значение передаётся в метаданные асинхронных сообщений как `correlationId`.

Наблюдаемость:

- Serilog пишет структурированные журналы в консоль;
- `CorrelationId` помещается в область логирования промежуточным ПО;
- трассировка OpenTelemetry включена для запросов ASP.NET Core и исходящих HTTP-вызовов;
- узел также слушает источник активности `MassTransit`;
- экспорт OTLP включается через `OpenTelemetry__Tracing__OtlpEndpoint`;
- пользовательские метрики формируются из источника `GenerateDocService.DocumentProcessing`.

Текущие пользовательские метрики:

- `document_generation_sync_requests_total`
- `document_generation_async_requests_total`
- `document_generation_completed_total`
- `document_generation_failed_total`
- `document_generation_duration_ms`

Все пользовательские метрики включают теги:

- `engine`
- `output_format`

Пример тела запроса:

```json
{
  "requestId": "optional-request-id",
  "engine": "scriban",
  "inputFormat": "json",
  "outputFormat": "txt",
  "templateFormat": "scriban",
  "payload": "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
  "template": "Document: {{ document }} for {{ customer.name }}",
  "metadata": {
    "client": "sample"
  }
}
```

Двоичные полезные нагрузки и шаблоны используют `payloadBase64` и `templateBase64` вместо текстовых полей. Именно это делает `miniword`, `minipdf` и путь MiniExcel с шаблоном вызываемыми через HTTP, поскольку файл `.docx` или `.xlsx` не переживает преобразование в строку UTF-8 и обратно:

```json
{
  "engine": "miniword",
  "inputFormat": "json",
  "outputFormat": "docx",
  "templateFormat": "docx",
  "payload": "{\"Name\":\"Ada Lovelace\",\"Score\":99}",
  "templateBase64": "UEsDBBQAAAAIAA...(base64-представление шаблона .docx)...AA=="
}
```

Каждая пара является взаимоисключающей: задайте `payload` **или** `payloadBase64`, а также `template` **или** `templateBase64`. Передача обоих полей либо отсутствие обоих полей полезной нагрузки возвращает `400`. Двоичные входные данные проходят через асинхронный конвейер без изменений, поскольку `GenerateDocumentRequested` уже переносит `byte[]`.

Поле `engine` можно не указывать — тогда сработает автовыбор. В этом случае приложение выбирает зарегистрированный движок с наивысшим приоритетом, способный обработать запрошенные `inputFormat`, `outputFormat` и `templateFormat`.

Пример ответа о состоянии асинхронной задачи:

```json
{
  "taskId": "d8f45ab3f8994fd68f1f1c84263dc8f0",
  "status": 2,
  "resultFileName": "d8f45ab3f8994fd68f1f1c84263dc8f0.txt",
  "resultStoragePath": "artifacts/d8f45ab3f8994fd68f1f1c84263dc8f0/d8f45ab3f8994fd68f1f1c84263dc8f0.txt",
  "downloadUrl": "http://localhost:5287/api/v1/tasks/d8f45ab3f8994fd68f1f1c84263dc8f0/download",
  "error": null,
  "createdAtUtc": "2026-01-01T12:00:00Z",
  "updatedAtUtc": "2026-01-01T12:00:01Z"
}
```

Когда `status` равно `Completed`, клиент может использовать `downloadUrl` для получения созданного артефакта.

## Тестирование

Модульные тесты покрывают:

- поведение реестра движков;
- поведение генерации движков;
- промежуточное ПО корреляции;
- поток постановки асинхронной задачи;
- повторы фоновой обработки, предельный срок обработки и обработку окончательных сбоев;
- классификацию временных сбоев и задержку повторов;
- ограничения TTL и объёма для кэша и дедупликации в памяти;
- очистку артефактов, включая перевод покинутых задач из `Processing`;
- регистрацию DI для устойчивости и HTTP-конвейера FOP;
- сервис скачивания артефактов;
- вспомогательные классы пользовательских метрик.

Успешные тесты движков сохраняют проверяемый результат в `artifacts/test-results/<engine>/`:

- `scriban` и `dotliquid` подставляют данные JSON в шаблоны TXT, HTML и Markdown; `fake` пишет текстовый вывод для smoke-проверок;
- `miniexcel` заполняет шаблон XLSX, а `miniword` — плейсхолдеры DOCX; тесты исследуют XML созданной книги/документа, чтобы убедиться в подстановке значений и отсутствии токенов шаблона;
- `questpdf` и `minipdf` создают PDF. Тест MiniPdf подтверждает отображение значений ячеек таблицы, включая файлы XLSX, созданные MiniExcel;
- `fop` формирует XSL-FO/XML-шаблон Scriban перед тем, как его необязательный интеграционный тест отправляет заполненный FO внешнему конвертеру и записывает `fop-template-generated.pdf`.

Каталог игнорируется Git и обновляется набором тестов. Для FOP также существует тест контракта с имитационным HTTP-сервисом. Чтобы получить реальный артефакт при локально запущенном конвертере:

```powershell
$env:FOP_CONVERTER_BASE_URL = "http://localhost:8080/"
dotnet test tests/Integration/GenerateDocService.Api.IntegrationTests/GenerateDocService.Api.IntegrationTests.csproj --filter "FullyQualifiedName~FopDocumentGenerationEngineIntegrationTests"
```

Интеграционные тесты в настоящее время покрывают:

- эндпоинт обнаружения движков;
- эндпоинт синхронной генерации;
- эндпоинт асинхронной генерации;
- опрос состояния задачи;
- эндпоинт скачивания артефакта;
- синхронный и асинхронный автовыбор при отсутствии `engine`;
- передачу заголовка correlation id в HTTP-ответах;
- JWT-аутентификацию и ролевую авторизацию: 401 без токена, 403 для роли, не удовлетворяющей политике, 200 для допустимой роли, отклонение просроченных и неверно подписанных токенов, анонимные пробы работоспособности и сбой запуска при ключе подписи короче 32 символов;
- репозиторий задач PostgreSQL и путь применения миграций при запуске — на реальном контейнере;
- асинхронный цикл через RabbitMQ, доказывающий соответствие конечной точки приёма потребителя значению `DocumentProcessing:Messaging:QueueName`;
- адаптеры Redis и MinIO на реальных контейнерах, включая дедупликацию, кэш готовых документов, кэш скомпилированных шаблонов и хранилище артефактов;
- движок `api` против реального имитационного HTTP-сервиса: все схемы аутентификации, путь без аутентификации, контракт multipart и доказательство того, что именованная цель не может перенаправить запрос на произвольный узел;
- двоичную передачу для `miniword`, `minipdf` и пути MiniExcel с шаблоном, включая прохождение base64-шаблона `.docx` через асинхронный конвейер и случаи отклонения некорректного base64.

Тесты с тегом `Category=RequiresDocker` запускают контейнеры PostgreSQL, RabbitMQ, Redis и MinIO через Testcontainers. Они выполняются автоматически, когда демон Docker доступен, и корректно пропускаются, когда недоступен, поэтому обычный `dotnet test` работает в обоих случаях. Запуск только этих тестов:

```powershell
dotnet test tests/Integration/GenerateDocService.Api.IntegrationTests/GenerateDocService.Api.IntegrationTests.csproj --filter "Category=RequiresDocker"
```

Запуск модульных тестов:

```powershell
dotnet test tests/Unit/GenerateDocService.DocumentProcessing.Application.Tests/GenerateDocService.DocumentProcessing.Application.Tests.csproj
```

Запуск интеграционных тестов:

```powershell
dotnet test tests/Integration/GenerateDocService.Api.IntegrationTests/GenerateDocService.Api.IntegrationTests.csproj
```

## Базовая оценка производительности

В репозитории есть проект базовой оценки на базе NBomber:

- `tests/Performance/GenerateDocService.LoadTests.NBomber`

Доступные профили:

- `sync_scriban_explicit`
- `async_scriban_explicit`
- `sync_scriban_auto`
- `async_scriban_auto`
- `sync_scriban_warm`
- `sync_scriban_cold`
- `sync_dotliquid_explicit`
- `async_dotliquid_explicit`
- `sync_dotliquid_auto`
- `async_dotliquid_auto`
- `sync_dotliquid_warm`
- `sync_dotliquid_cold`
- `sync_questpdf`

Первый аргумент командной строки — базовый адрес API. Второй выбирает синхронный профиль, третий — асинхронный. Отчёты по умолчанию записываются в `artifacts/perf/<timestamp>-<sync-profile>-<async-profile>`; четвёртый аргумент переопределяет корневой каталог.

```powershell
dotnet run --project tests/Performance/GenerateDocService.LoadTests.NBomber -- http://localhost:5287 sync_scriban_warm async_scriban_explicit
```

Полная матрица бенчмарков описана в `docs/performance-baseline.md`.

### Микробенчмарки внутри процесса

NBomber измеряет сервис целиком через HTTP. Для показателей отдельных компонентов клиентское приложение также размещает бенчмарки BenchmarkDotNet, которые вызывают движки напрямую, без API, Docker и конвертера FOP:

```powershell
# Всё сразу (занимает продолжительное время; требуется Release, BenchmarkDotNet отклоняет Debug)
dotnet run -c Release --project src/Clients/GenerateDocService.SampleClient -- benchmark

# Один класс или одна категория
dotnet run -c Release --project src/Clients/GenerateDocService.SampleClient -- benchmark --filter *TemplateParserCacheBenchmarks*
dotnet run -c Release --project src/Clients/GenerateDocService.SampleClient -- benchmark --anyCategories text

# Быстрая проверка набора бенчмарков без ожидания статистики
dotnet run -c Release --project src/Clients/GenerateDocService.SampleClient -- benchmark --job Dry --filter *Scriban*
```

`benchmark` также является командой `b` в интерактивном меню; любые аргументы после неё передаются непосредственно BenchmarkDotNet.

Три класса расположены в `src/Clients/GenerateDocService.SampleClient/Benchmarks/InProcessBenchmarks.cs`:

| Класс | Категории | Что измеряет |
|-------|-----------|--------------|
| `EngineGenerationBenchmarks` | `text`, `office`, `pdf` | scriban, dotliquid, questpdf, пути miniexcel с таблицей и с шаблоном, а также miniword |
| `TemplateParserCacheBenchmarks` | `scriban`, `dotliquid` | компиляция шаблона при промахе кэша в сравнении с попаданием в прогретый `InMemoryCompiledTemplateCache` |
| `RequestPipelineBenchmarks` | `fingerprint`, `transport` | вычисление отпечатка запроса для 1 КБ и 1 МБ, а также кодирование и декодирование base64 для полезной нагрузки 1 МБ |

Каждая полезная нагрузка, шаблон и запрос создаются в `[GlobalSetup]`, поэтому измеряемые методы выделяют только то, что выделяет сам движок. Отчёты записываются в `BenchmarkDotNet.Artifacts/results/`, каталог игнорируется Git.

Эти бенчмарки **не** покрывают `GenerateDocumentRequestValidator`, поскольку он находится в узле API, а ссылка на этот проект из клиентского приложения втянула бы весь веб-узел в демонстрационный исполняемый файл.

## Запуск

Запуск API:

```powershell
dotnet run --project src/Host/GenerateDocService.Api
```

API слушает `https://localhost:7025` и `http://localhost:5287` (см. `Properties/launchSettings.json`).

Запуск клиентского приложения:

```powershell
# Интерактивное меню с примерами для движков
dotnet run --project src/Clients/GenerateDocService.SampleClient -- http://localhost:5287

# Без интерактивного режима: один пример, все примеры или только список движков
dotnet run --project src/Clients/GenerateDocService.SampleClient -- http://localhost:5287 7
dotnet run --project src/Clients/GenerateDocService.SampleClient -- http://localhost:5287 all
dotnet run --project src/Clients/GenerateDocService.SampleClient -- http://localhost:5287 engines
```

Меню охватывает все девять движков в восемнадцати пунктах — шестнадцать пронумерованных примеров плюс два варианта, `14.1` и `15.1`, которые повторяют примеры 14 и 15, но берут шаблон из файла, а не формируют его в памяти: `fake`; `scriban` (текст, HTML, Markdown, а также полный асинхронный цикл отправка/опрос/скачивание); `dotliquid`; `questpdf`; `miniexcel` (путь с таблицей, путь с шаблоном и шаблон счёта с повторяющимися строками данных `{{items.*}}`); `miniword` (форма с одной записью и многострочный счёт); `minipdf`; `fop`; `api` (универсальный движок против настроенной цели `converter`); и два примера автовыбора без поля `engine`. Текстовый результат выводится непосредственно в консоль; PDF, XLSX и DOCX записываются в каталог `output/` рядом с исполняемым файлом клиента. Команды меню: `0`/`all`, `e` (движки), `m` (меню), `b` (бенчмарки), `q` (выход).

Чтобы приложить bearer-токен, задайте `GDS_SAMPLE_TOKEN` — он требуется в Docker-стеке:

```powershell
$env:GDS_SAMPLE_TOKEN = New-GdsToken   # см. раздел «Получение токена» выше
dotnet run --project src/Clients/GenerateDocService.SampleClient -- http://localhost:8081
```

Примеры 1–16 формируют файлы `.docx` и `.xlsx` в памяти (`OfficeFixtures`), поэтому они самодостаточны и не требуют файлов на диске. Пример `minipdf` выполняет два вызова подряд: сначала создаёт книгу через `miniexcel`, затем передаёт полученные байты как полезную нагрузку в base64.

Варианты `14.1` и `15.1` вместо этого читают настоящий шаблон с диска через `OfficeFixtures.TemplateRows(path)`, который вызывает `File.ReadAllBytes` с **относительным** путём:

| Пример | Читаемый файл |
|--------|---------------|
| `14.1` | `template.docx` |
| `15.1` | `template.xlsx` |

Оба файла находятся в репозитории в каталоге `src/Clients/GenerateDocService.SampleClient/`, и `.csproj` копирует их в выходной каталог. Однако эта копия **не** делает их находимыми: относительный путь разрешается относительно рабочего каталога процесса, а `dotnet run` сохраняет рабочий каталог оболочки, из которой был вызван. Поэтому при запуске из корня репозитория пример `14.1` завершается ошибкой `System.IO.FileNotFoundException: Could not find file '<корень репозитория>\template.docx'`; поскольку исключение возникает при формировании аргумента запроса, оно оказывается необработанным и завершает клиентское приложение, а не отображается как сбой одного примера.

Чтобы выполнить эти два примера сегодня, запускайте клиентское приложение из каталога проекта, чтобы в рабочем каталоге находились шаблоны:

```powershell
cd src\Clients\GenerateDocService.SampleClient
dotnet run -- http://localhost:5287 14.1
```

Разрешение пути относительно `AppContext.BaseDirectory` вместо рабочего каталога позволило бы запускать их из любого места; задача зафиксирована в `docs/improvement-plan.md`, раздел 11, пункт 29.

## Проверка

Сборка:

```powershell
dotnet build .\GenerateDocService.slnx
```

Тесты:

```powershell
dotnet test tests/Unit/GenerateDocService.DocumentProcessing.Application.Tests/GenerateDocService.DocumentProcessing.Application.Tests.csproj
```

Непрерывная интеграция выполняется `.github/workflows/ci.yml`: сборка Release с `-p:WarningsAsErrors=NU1902%3BNU1903` (экранирование `%3B` обязательно — литеральная `;` разделяется оболочкой, и MSBuild отклоняет аргумент с кодом MSB1006), затем оба набора тестов с загрузкой результатов как артефактов.

## Навыки Copilot

Руководство для Copilot хранится в:

- `.github/copilot-instructions.md`
- `.github/skills/project-bootstrap/SKILL.md`
- `.github/skills/documentation-work/SKILL.md`
- `.github/skills/service-design/SKILL.md`
- `.github/skills/document-generation/SKILL.md`
- `.github/skills/api-service/SKILL.md`
- `.github/skills/infrastructure/SKILL.md`
- `.github/skills/resilience/SKILL.md`
- `.github/skills/security/SKILL.md`
- `.github/skills/testing/SKILL.md`
- `.github/skills/csharp-async/SKILL.md`
- `.github/skills/senior-architect/SKILL.md`

Соглашения для диаграмм Mermaid описаны в `.github/instructions/mermaid.instructions.md`, соглашения для C# — в `.github/instructions/csharp.instructions.md`.
