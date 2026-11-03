# Умно търсене по име: план за реализация

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ученик пише името си по какъвто и да е начин (с грешка, на латиница, с умалително, в изречение) и вижда постиженията си.

**Architecture:** Нов endpoint `GET /api/search` → `SearchService`, който свързва `QueryInterpreter` (локален LLM в Ollama, по избор), `NameNormalizer` + `NameMatcher` (Jaro-Winkler в C#) и MySQL. Нов фронтенд в `wwwroot/`.

**Tech Stack:** ASP.NET Core 9 minimal API, MySqlConnector, Ollama HTTP API, xUnit, чист HTML/CSS/JS.

**Spec:** `docs/superpowers/specs/2026-11-03-smart-name-search-design.md`

## Global Constraints

- Без външни ИИ API. Моделът е само локален, през `Ollama:Url`.
- Търсенето работи и без Ollama (`aiUsed: false`, без грешка към потребителя).
- `MatchThreshold = 0.85`, `SuggestThreshold = 0.70`, до 5 ученика в отговора.
- `Ollama` конфигурация: `Enabled`, `Url`, `Model`, `TimeoutSeconds` (по подразбиране `true`, `http://localhost:11434`, избраният модел, `3`).
- Данни от потребители и базата се вмъкват в DOM само чрез `textContent`.
- `/api/results`, регистрацията, входът и библиотеката не се променят по поведение.

## Review Focus

1. Заявка само от интервали или пунктуация → `400`, без изключение. *(Task 5: `Search_BlankQuery_Returns400` се проверява ръчно с curl в Task 7.)*
2. Главни и малки букви, смесени латиница и кирилица („Ivan Петров“) → намира. *(Task 2: `Normalize_MixedScripts`)*
3. Моделът върне JSON с празни низове или с поле, различно от `names` → fallback. *(Task 4: `Interpret_EmptyNames_FallsBack`)*
4. Едно и също име в много състезания → един ученик с всички резултати, а не повторения. *(Task 5, групиране; проверява се в Task 7.)*
5. Заявка с една буква („и“) → да не връща половината база. *(Task 3: `Match_SingleLetter_ReturnsNothing`; думите на заявката под 2 символа се игнорират.)*

---

### Task 1: Структура на проекта, база и тестов проект

**Files:**
- Move: `*.cs`, `Tracker.csproj`, `appsettings.json` → `Tracker/`; `Data/` за `ApplicationDbContext.cs`, `CompetitionResult.cs`, `SavedResult.cs`; `Migrations/` за миграциите; `Properties/launchSettings.json`; `wwwroot/` за `index.html`, `script.js`, `style.css`.
- Create: `Tracker.Tests/Tracker.Tests.csproj` (xUnit, net9.0, ProjectReference `../Tracker/Tracker.csproj`), добавен в `Tracker.sln`.
- Create: `db/seed.sql`: `CREATE DATABASE IF NOT EXISTS tracker`; таблица `competition_results(id INT AUTO_INCREMENT PK, participant_name VARCHAR(200), competition_name VARCHAR(200), competition_year INT, score DECIMAL(6,2) NULL)` с utf8mb4; ~25 ученика × 1–4 резултата, български имена, сред тях Иван Петров, Мария Иванова, Георги Димитров, Димитър Стоянов, Петър Николов, Тодор Василев.
- Create: миграция `AddSavedResults` (`dotnet ef migrations add`), понеже `saved_results` не е в нито една миграция.
- Connection string `ConnectionStrings:Tracker` в user secrets.

- [ ] `dotnet build Tracker.sln` минава; `dotnet test` минава с 0 теста.
- [ ] `dotnet ef database update` + `seed.sql` → таблиците съществуват.
- [ ] Commit `chore: restore project layout, add test project and seed data`.

### Task 2: NameNormalizer

**Files:** Create `Tracker/Search/NameNormalizer.cs`; Test `Tracker.Tests/NameNormalizerTests.cs`.

**Produces:** `public static class NameNormalizer { public static string Normalize(string input); }`

- [ ] Тестове (`[Theory]`): `"Ivan Petrov"→"иван петров"`, `"Zhivko"→"живко"`, `"Shtilianov"→"щилианов"`, `"Yana Tsoneva"→"яна цонева"`, `"Chavdar Shishkov"→"чавдар шишков"`, `"  Иван,   Петров! "→"иван петров"`, `"Мария-Елена"→"мария-елена"`, `Normalize_MixedScripts: "Ivan Петров"→"иван петров"`, `"" → ""`.
- [ ] Реализация: правилата от спецификацията; многобуквените съчетания (`sht, zh, ts, ch, sh, yu, ya`) преди единичните.
- [ ] Тестовете минават, commit.

### Task 3: NameMatcher

**Files:** Create `Tracker/Search/NameMatcher.cs`; Test `Tracker.Tests/NameMatcherTests.cs`.

**Consumes:** `NameNormalizer.Normalize`.
**Produces:**
```csharp
public record NameMatch(string Name, double Score);
public record MatchResult(IReadOnlyList<NameMatch> Matches, string? DidYouMean);
public static class NameMatcher {
    public const double MatchThreshold = 0.85, SuggestThreshold = 0.70;
    public static double JaroWinkler(string a, string b);
    public static MatchResult Match(string query, IEnumerable<string> candidates);
}
```

Кандидати в тестовете: Иван Петров, Иван Петков, Мария Иванова, Георги Димитров, Димитър Стоянов, Петър Николов, Тодор Василев, Анна Георгиева.

- [ ] Тестове: `JaroWinkler("марта","марат")≈0.953` (±0.001), еднакви → 1, празен → 0; `Match("Иван Петров")` → първо „Иван Петров“ със Score 1; `"иван петорв"` → първо „Иван Петров“; `"Ivan Petrov"` → първо „Иван Петров“; `"Петров Иван"` → първо „Иван Петров“; `"Петров"` → съдържа „Иван Петров“; `"Зорница Кирова"` → празно, `DidYouMean == null`; `"Тодорр Васил"` → `Matches` празно или първо „Тодор Василев“; `Match_SingleLetter_ReturnsNothing: "и"` → празно.
- [ ] Реализация: Jaro-Winkler (prefix scale 0.1, max prefix 4); оценката е средна стойност по думите на заявката (думи с дължина ≥ 2) от максималното сходство с дума на кандидата; праговете са от Interfaces.
- [ ] Тестовете минават, commit.

### Task 4: QueryInterpreter + избор на модел

**Files:** Create `Tracker/Search/OllamaOptions.cs`, `Tracker/Search/QueryInterpreter.cs`; Test `Tracker.Tests/QueryInterpreterTests.cs`.

**Produces:**
```csharp
public class OllamaOptions { bool Enabled = true; string Url = "http://localhost:11434"; string Model; int TimeoutSeconds = 3; }
public record InterpretedQuery(IReadOnlyList<string> Names, bool AiUsed, string? Note);
public class QueryInterpreter(HttpClient http, IOptions<OllamaOptions> options) {
    public Task<InterpretedQuery> InterpretAsync(string text, CancellationToken ct = default);
}
```

- [ ] Тестове с фалшив `HttpMessageHandler`, който връща `{"response":"{\"names\":[\"Иван Петров\"]}"}`: вход „Ваньо Петров“ → `Names=["Иван Петров"]`, `AiUsed=true`, `Note=="Разбрах „Ваньо Петров“ като „Иван Петров“"`; `Interpret_SameName_NotAiUsed`; `Interpret_InvalidJson_FallsBack`; `Interpret_EmptyNames_FallsBack` (`{"names":["", " "]}`); `Interpret_HttpError_FallsBack` (500); `Interpret_Timeout_FallsBack` (handler с `Task.Delay` над timeout, `TimeoutSeconds=1`); `Interpret_Disabled_NoRequest` (броячът на заявки е 0). Fallback = `Names=[text]`, `AiUsed=false`, `Note=null`.
- [ ] Реализация: `POST {Url}/api/generate`, `{model, prompt, format:"json", stream:false, options:{temperature:0}}`; prompt на български с правилата от спецификацията и 3–4 примера; timeout с `CancellationTokenSource.CancelAfter`.
- [ ] Избор на модел: скрипт `db/model-check.ps1` пуска 12 умалителни имена срещу всеки модел; най-малкият с ≥ 10/12 верни става стойност по подразбиране в `appsettings.json`.
- [ ] Тестовете минават, commit.

### Task 5: SearchService и endpoint

**Files:** Create `Tracker/Search/SearchService.cs`; Modify `Tracker/Program.cs` (регистрация + `MapGet("/api/search")`; махане на примерния `/weatherforecast`); Modify `Tracker/appsettings.json` (секция `Ollama`); Modify `Tracker.http`.

**Consumes:** Task 3 и Task 4.
**Produces:** JSON по спецификацията: `{ students: [{ name, matchScore, results: [{ id, competition, year, score }] }], didYouMean, aiUsed, aiNote }`.

- [ ] `SearchService(QueryInterpreter, IConfiguration)`, `Task<SearchResponse> SearchAsync(string q, CancellationToken ct)`: стъпките 1–5 от спецификацията; при няколко имена от модела съвпаденията се обединяват (максималната оценка на име); резервно съвпадение с оригиналния текст.
- [ ] Endpoint: `q` празно или само интервали/пунктуация (празно след `Normalize`) → `400`; `MySqlException` → `Results.Problem`.
- [ ] `dotnet build` минава, commit.

### Task 6: Фронтенд

**Files:** Rewrite `Tracker/wwwroot/index.html`, `style.css`, `script.js`.

- [ ] Страница на български: централно търсене („Напиши името си…“) като главен елемент; карти по ученик с резултатите и бутон „Запази“; ИИ надпис 🤖 с `aiNote`; бутон „Имахте предвид…?“; състояния за зареждане, празен резултат и грешка; светла и тъмна тема; работи на телефон.
- [ ] Вход, регистрация и библиотека се запазват (вход/регистрация в отделен панел), старите филтри са в секция „Разширено търсене“.
- [ ] Никакъв `innerHTML` с данни; `onclick` атрибутите се заменят с `addEventListener`.
- [ ] Commit.

### Task 7: Проверка от край до край

- [ ] `dotnet test` → всички минават.
- [ ] Приложението работи; 8-те заявки от „Ръчна проверка“ в спецификацията дават очакваното, включително с `Ollama:Enabled=false`; `q=   ` → 400.
- [ ] Регистрация → вход → запазване → библиотека работят в браузъра.
- [ ] README с инструкции за пускане; commit.
