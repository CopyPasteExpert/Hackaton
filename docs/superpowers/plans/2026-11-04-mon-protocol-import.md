# Импорт на класирания от МОН: план за реализация

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Еднократно зареждане на реални резултати от 6 протокола на МОН в базата, като локалният модел разчита таблиците, а проверка не пуска измислени редове.

**Architecture:** Нов конзолен проект `Tracker.Importer` с команди `extract` (PDF → PdfPig → `qwen2.5:7b` → `RowValidator` → `Deduplicator` → CSV за преглед) и `import` (CSV → MySQL в една транзакция). Таблицата `competition_results` получава `grade`, `place`, `source_url`, които приложението показва.

**Tech Stack:** .NET 9 console, PdfPig 0.1.16, Ollama HTTP API, MySqlConnector, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-03-mon-protocol-import-design.md`

## Global Constraints

- Само локален модел: `Ollama:Url`, `Ollama:Model` (`qwen2.5:7b`) от `Tracker/appsettings*.json`; таймаут 5 минути на страница; `format: "json"`, `temperature: 0`, `num_ctx: 8192`.
- Сваляне: последователно, обикновена заявка, `User-Agent: Tracker.Importer`; без обхождане на mon.bg и без заобикаляне на защити.
- В базата се пазят само име, клас, точки, място, състезание, година, линк. Без училище, град, оценка, точки по задачи.
- `Tracker.Importer/cache/` и `Tracker.Importer/import-review.csv` са в `.gitignore`. Тестовете не съдържат реални имена от протоколите.
- `extract` не пише в базата. `import` пише само редове със `status = ok`, в една транзакция, и спира, ако няма такива.

## Review Focus

1. Ред, в който името е разделено на два текстови реда в PDF-а (дълго име или пренасяне) → отхвърлен с `name-not-in-page`, а не грешно приет ред. *(Task 2: `Validate_NameSplitAcrossLines_Rejected`)*
2. Две таблици на една страница (напр. край на 9–12 клас и начало на 7–8 клас) → правилото за място не бива да изтрива верни места на по-горната таблица. *(Task 2: `Validate_PlaceRuleIgnoresRowsWithoutScore`; поведение при смесени таблици се гледа в CSV-то в Task 8)*
3. Протокол, който не се сваля или е сканиран (0 думи на страница) → отчита се в обобщението, без срив. *(Task 5: ръчна проверка с невалиден URL в Step 4)*
4. Повторно пускане на `extract` → не тегли пак файловете от `cache/`. *(Task 5, Step 4)*
5. Имена с „ѝ“, тире или две фамилии („Мария-Елена Иванова-Петрова“) → не се отхвърлят като `name-format`. *(Task 2: `Validate_HyphenatedName_Ok`)*

---

### Task 1: Схема, проект и конфигурация

**Files:**
- Rename: `db/seed.sql` → `db/schema.sql`; съдържанието става само `CREATE DATABASE IF NOT EXISTS`, `USE tracker`, `DROP TABLE IF EXISTS competition_results`, `CREATE TABLE` от спецификацията (с `grade`, `place`, `source_url`). Без `INSERT`.
- Create: `Tracker.Importer/Tracker.Importer.csproj` (`Microsoft.NET.Sdk`, `OutputType Exe`, `net9.0`, `FrameworkReference Microsoft.AspNetCore.App`, `PackageReference PdfPig 0.1.16`, `ProjectReference ../Tracker/Tracker.csproj`); добавя се в `Tracker.sln`.
- Create: `Tracker.Importer/ImporterConfig.cs`: `static IConfiguration Load()` чете `../Tracker/appsettings.json` и `../Tracker/appsettings.Development.json` (optional) спрямо `AppContext.BaseDirectory` → коренът на репото се намира като първата родителска папка с `Tracker.sln`.
- Create: `Tracker.Importer/protocols.json` с 6-те записа (година 2026 за всички):
  - математика: `https://www.mon.bg/nfs/2026/04/rezultati-9-12kl-7-8kl-math_08042026.pdf`, „Национална олимпиада по математика“
  - информатика: `https://www.mon.bg/nfs/2026/03/rezultati26-nk-noi_30032026.pdf`, „Национална олимпиада по информатика“
  - физика: `https://www.mon.bg/nfs/2026/04/rezultati26-fizika-30042026.pdf`, „Национална олимпиада по физика“
  - химия: `https://www.mon.bg/nfs/2026/03/rezultati26-hoos_24032026.pdf`, „Национална олимпиада по химия и опазване на околната среда“
  - биология: `https://www.mon.bg/nfs/2026/03/rezultati26-nk-bio_31032026.pdf`, „Национална олимпиада по биология и здравно образование“
  - български: `https://www.mon.bg/nfs/2026/04/rezultati-nk-bel_14042026.pdf`, „Национална олимпиада по български език и литература“
- Create: `Tracker.Importer/Program.cs` с `extract` / `import`, засега само печата „not implemented“ и exit code 1 за непозната команда.
- Modify: `Tracker.Tests/Tracker.Tests.csproj`: `ProjectReference ../Tracker.Importer/Tracker.Importer.csproj`.
- Modify: `.gitignore`: `Tracker.Importer/cache/`, `Tracker.Importer/import-review.csv`.
- Modify: `README.md`: стъпка 2 става `db/schema.sql`.

**Produces:** `public record ProtocolSource(string Url, string Competition, int Year);` в `Tracker.Importer/ProtocolSource.cs`.

- [ ] `dotnet build Tracker.sln` минава; `dotnet test` → 58 passed.
- [ ] `db/schema.sql` върху локалната база → `DESCRIBE tracker.competition_results` показва `grade`, `place`, `source_url`. (Базата остава празна до Task 8; приложението дотогава просто не намира никого.)
- [ ] Commit `Add Tracker.Importer project and result detail columns`.

### Task 2: RowValidator

**Files:** Create `Tracker.Importer/RowValidator.cs`, `Tracker.Importer/Rows.cs`; Test `Tracker.Tests/RowValidatorTests.cs`.

**Produces (в `Rows.cs`):**
```csharp
public record PageText(int Number, string Text);
public record ExtractedRow(string Name, int? Grade, decimal? Score, decimal? Place);
public record ValidatedRow(string Name, int? Grade, decimal? Score, int? Place, string Status);
public static class RowStatus {
    public const string Ok = "ok", NameNotInPage = "name-not-in-page",
        NameFormat = "name-format", ScoreNotOnLine = "score-not-on-line",
        PageNotRead = "page-not-read";
}
```
`public static class RowValidator { public static ValidatedRow Validate(ExtractedRow row, PageText page, IReadOnlyList<ExtractedRow> pageRows); }`

Тестова страница (измислени имена), в `RowValidatorTests` като константа:
```
1 11 Петър Иванов Колев ПМГ Град 122 7 7 7 250 7 7 7 42 1 6
2 11 Анна Георгиева Иванова ПМГ Град 129 7 7 7 262 7 7 7 42 1 6
3 10 Мария-Елена Тодорова Иванова-Петрова СУ Град 146 7 7 7 219 7 7 2 37 3 6
4 10 Стоян Петров Василев СУ Град 153 7 7 2 247 7 0 7 22 5,75
5 7 Калина Николаева Димова СУ Град 11,5
```

- [ ] Тестове:
  - `Validate_CorrectRow_Ok`: („Петър Иванов Колев“, 11, 42, 1) → `Ok`, Place 1, Grade 11.
  - `Validate_NameNotInPage_Rejected`: „Георги Петров Колев“ → `NameNotInPage`.
  - `Validate_LatinName_NameFormat`: „Petar Ivanov“ → `NameFormat` (проверява се преди „в текста“).
  - `Validate_HyphenatedName_Ok`: „Мария-Елена Тодорова Иванова-Петрова“, 37, 3 → `Ok`.
  - `Validate_ScoreFromOtherLine_Rejected`: „Петър Иванов Колев“ със Score 37 → `ScoreNotOnLine`.
  - `Validate_DecimalCommaScore_Ok`: „Калина Николаева Димова“, Score 11.5 → `Ok`.
  - `Validate_NameSplitAcrossLines_Rejected`: страница, в която „Петър Иванов“ и „Колев … 42 1 6“ са на два реда → `NameNotInPage` (името не се среща цяло).
  - `Validate_FractionalPlace_Dropped`: „Стоян Петров Василев“, 22, Place 5.75 → `Ok`, Place null.
  - `Validate_PlaceBelowHigherScores_Dropped`: същият ред с Place 2 (на страницата има 3 реда с повече точки) → Place null.
  - `Validate_PartialLaureateList_KeepsPlace`: страница само с два реда (42 и 27), редът с 27 има Place 10 → Place 10.
  - `Validate_PlaceRuleIgnoresRowsWithoutScore`: pageRows съдържа ред с Score null → не се брои като „повече точки“.
  - `Validate_GradeOutOfRange_Null`: Grade 0 и Grade 13 → Grade null, `Ok`.
- [ ] Реализация:
  - име: `Regex ^[\p{IsCyrillic}-]+( [\p{IsCyrillic}-]+){1,3}$` → иначе `NameFormat`; после `page.Text.Contains(name)` → иначе `NameNotInPage`;
  - точки: текстовият ред = първият ред от `page.Text.Split('\n')`, който съдържа името; варианти на числото: `score.ToString("0.##", Invariant)`, същото със запетая, и с `,0`/`.0` за цели; ред е валиден, ако някоя от думите му (split по интервал) е равна на вариант; Score null → `ScoreNotOnLine`;
  - място: цяло, ≥ 1 и ≥ 1 + броя на `pageRows` със `Score > row.Score` → иначе null;
  - клас: 1–12 → иначе null.
- [ ] Тестовете минават, commit `Add RowValidator for extracted protocol rows`.

### Task 3: Deduplicator и CSV за преглед

**Files:** Create `Tracker.Importer/ReviewRow.cs`, `Tracker.Importer/Deduplicator.cs`, `Tracker.Importer/ReviewCsv.cs`; Test `Tracker.Tests/DeduplicatorTests.cs`, `Tracker.Tests/ReviewCsvTests.cs`.

**Consumes:** `RowStatus`, `NameNormalizer.Normalize` (от `Tracker.Search`).
**Produces:**
```csharp
public record ReviewRow(string Status, string Name, int? Grade, decimal? Score, int? Place,
    string Competition, int Year, string SourceUrl, int Page);
public static class Deduplicator { public static List<ReviewRow> Deduplicate(IEnumerable<ReviewRow> rows); }
public static class ReviewCsv {
    public static void Write(string path, IEnumerable<ReviewRow> rows);
    public static List<ReviewRow> Read(string path);
}
```

- [ ] Тестове:
  - `Deduplicate_LaureateAndRanked_KeepsRowWithPlace`: един ученик (имена с различен регистър) с Place 1 и с Place null → един ред, Place 1.
  - `Deduplicate_BothWithoutPlace_KeepsHigherScore`.
  - `Deduplicate_SameNameDifferentCompetition_KeepsBoth`.
  - `Deduplicate_NonOkRows_PassThroughUntouched`: отхвърлени редове не се сливат и не се губят.
  - `ReviewCsv_RoundTrip`: редове със запетая, кавички и празни полета → `Read(Write(x)) == x`.
  - `ReviewCsv_WritesUtf8Bom`: първите 3 байта са EF BB BF.
- [ ] Реализация: ключ `(Normalize(Name), Competition, Year)` само за `Ok`; CSV с `,` разделител, кавички по RFC 4180, числа в `InvariantCulture`, заглавен ред `status,name,grade,score,place,competition,year,source_url,page`.
- [ ] Тестовете минават, commit.

### Task 4: RowExtractor

**Files:** Create `Tracker.Importer/RowExtractor.cs`; Test `Tracker.Tests/RowExtractorTests.cs`.

**Consumes:** `OllamaOptions` (от `Tracker.Search`), `ExtractedRow`.
**Produces:**
```csharp
public class ExtractionFailedException(string message) : Exception(message);
public class RowExtractor(HttpClient http, OllamaOptions options) {
    public Task<IReadOnlyList<ExtractedRow>> ExtractAsync(string pageText, CancellationToken ct = default);
}
```

- [ ] Тестове с фалшив handler (по образеца на `QueryInterpreterTests`):
  - `Extract_ValidResponse_ReturnsRows`: `{"rows":[{"name":"А Б","grade":11,"score":42,"place":1},{"name":"В Г","grade":null,"score":"11,5","place":null}]}` → 2 реда, вторият Score 11.5 (числото може да дойде и като низ със запетая).
  - `Extract_InvalidJsonTwice_Throws`: два отговора без JSON → `ExtractionFailedException`, handler извикан 2 пъти.
  - `Extract_InvalidThenValid_ReturnsRows`: първо невалиден, после валиден → редове, 2 извиквания.
  - `Extract_HttpErrorTwice_Throws`.
  - `Extract_SendsModelAndJsonFormat`: тялото на заявката съдържа `"model":"test-model"`, `"format":"json"`, `"num_ctx":8192`.
- [ ] Реализация: prompt от спецификацията (същият като в пробата `probe-extract.fsx`, плюс „score е общият брой точки, не оценката; place е мястото в класирането, не оценката“); `HttpClient.Timeout = 5 min` се задава от извикващия; разбиране на `grade/score/place` като число или низ (запетая → точка), всичко друго → null.
- [ ] Тестовете минават, commit.

### Task 5: Сваляне, PDF текст и команда `extract`

**Files:** Create `Tracker.Importer/ProtocolDownloader.cs`, `Tracker.Importer/PdfTextReader.cs`, `Tracker.Importer/ExtractCommand.cs`; Modify `Tracker.Importer/Program.cs`.

**Consumes:** Task 1–4.
**Produces:**
```csharp
public class ProtocolDownloader(HttpClient http, string cacheDir) {
    public Task<string?> GetAsync(ProtocolSource source, CancellationToken ct); // локален път или null при грешка
}
public static class PdfTextReader { public static IReadOnlyList<PageText> Read(string path); } // маха U+FEFF
public static class ExtractCommand { public static Task<int> RunAsync(IConfiguration config, string root); }
```

- [ ] `ExtractCommand`: за всеки протокол → файл → страници → за страница с 0 думи или `ExtractionFailedException` се добавя `ReviewRow` със Status `RowStatus.PageNotRead` и празно име → `RowValidator` за всеки ред → `Deduplicator` → `ReviewCsv.Write`. Печата обобщение: протоколи (свалени/пропуснати), страници (разчетени/неразчетени), редове по статус, време. Exit code 0.
- [ ] Ръчно: `dotnet run --project Tracker.Importer -- extract` само с математиката (временно един запис в `protocols.json`) → в CSV-то 40+ реда `ok`, редовете от страница 2 с оценка 5,75 са с празно място, няма повторени лауреати.
- [ ] Ръчно: втори `extract` → в обобщението 0 свалени (всичко от `cache/`); запис с невалиден URL → „пропуснат“ и продължава.
- [ ] Commit `Add extract command for MON protocols`.

### Task 6: Команда `import`

**Files:** Create `Tracker.Importer/ImportCommand.cs`; Modify `Tracker.Importer/Program.cs`.

**Produces:** `public static class ImportCommand { public static Task<int> RunAsync(IConfiguration config, string root); }`

- [ ] Чете CSV-то; редове `ok` → ако 0, печата грешка и exit code 1. В една `MySqlTransaction`: `DELETE FROM saved_results`, `DELETE FROM competition_results`, параметризиран `INSERT` за всеки ред (`participant_name, competition_name, competition_year, score, grade, place, source_url`). Печата редове и различни ученици.
- [ ] Ръчно: `import` след Task 5 → броят в базата съвпада с броя `ok` в CSV-то; `import` с CSV без `ok` редове → exit code 1 и базата не е пипната.
- [ ] Commit.

### Task 7: Приложението показва място, клас и източник

**Files:** Modify `Tracker/Search/SearchService.cs` (`StudentResult` + SELECT), `Tracker/Program.cs` (`/api/results` и `/api/saved-results`), `Tracker/Data/CompetitionResult.cs`, `Tracker/wwwroot/script.js` (`resultRow`), `Tracker/wwwroot/style.css`, `Tracker/wwwroot/index.html` (`?v=3`).

- [ ] `StudentResult(int Id, string Competition, int Year, decimal? Score, int? Grade, int? Place, string? SourceUrl)`; `/api/results` и `/api/saved-results` връщат `grade`, `place`, `sourceUrl`; `CompetitionResult` получава `Grade`, `Place`, `SourceUrl`.
- [ ] `resultRow`: под името на състезанието ред „1 място · 11 клас“ (частите, които ги има); линк „Протокол (МОН)“ с `target="_blank"`, `rel="noopener"`, `href` само ако започва с `https://`; всичко чрез `textContent`.
- [ ] `dotnet test` → всички минават; ръчно в браузъра след Task 8.
- [ ] Commit.

### Task 8: Пълен импорт, примери и проверка

- [ ] `extract` с 6-те протокола → обобщение; 20 случайни реда `ok` се сравняват на ръка с PDF-ите; при над 10% отхвърлени редове в някой протокол → спиране и доклад преди `import`.
- [ ] `import`.
- [ ] Примерните бутони в `index.html` се сменят с реални ученици от базата: едно име с грешка, едно на латиница, едно с умалително от `NicknameDictionary`, едно в изречение.
- [ ] `db/model-check.ps1`: случаите се заменят с 22 заявки върху реални имена от базата по същите категории (умалително + фамилия, само собствено име, свободен текст, несъществуващ ученик); пуска се без ИИ и с `qwen2.5:7b`; резултатът влиза в README.
- [ ] README: раздел „Данни“ (източник, кои протоколи, какво се пази, команди `extract`/`import`).
- [ ] Commit `Load real MON results and update demo examples`.

---

## Поправка 2026-11-04: колони по координати

Следва ревизията в спецификацията („Ревизия 2026-11-04“). Задачи 1–5 са изпълнени (`aa3f613..cf79b17`). Задачи 5a–5d заменят разчитането с модела ред по ред; задачи 6–8 остават без промяна, но идват след 5d.

**Types (нов файл `Tracker.Importer/Tables.cs`):**
```csharp
public record PdfWord(string Text, double Left, double Right, double Bottom);
public record Column(string Label, double Left, double Right);
public record TableRow(IReadOnlyList<string> Values);            // по една стойност за всяка колона, "" ако няма
public record Table(int Page, int HeaderId, IReadOnlyList<Column> Columns, IReadOnlyList<TableRow> Rows);
public record ColumnMap(int Name, int? Grade, int Score, int? Place); // индекси в Columns
```
`HeaderId`: таблиците-продължения имат `HeaderId`-то на таблицата, от която са взели колоните си; съпоставянето се кешира по него.

### Task 5a: TableReader

**Files:** Create `Tracker.Importer/Tables.cs`, `Tracker.Importer/TableReader.cs`; Test `Tracker.Tests/TableReaderTests.cs`.

**Produces:** `public static class TableReader { public static List<Table> Read(IReadOnlyList<IReadOnlyList<PdfWord>> pages); }` (страниците по ред, страница 1 е индекс 0).

Тестовете строят думите ръчно (измислени имена), с x/y като в пробата: номер @50, клас @75, имена @90–200, училище @240, точки @700.

- [ ] Тестове:
  - `Read_HeaderAndRows`: заглавие „№ Клас Имена Училище Общ бр. точки“ (с „Общ бр.“ и „точки“ на два реда над „№“) + 3 реда → 1 таблица, 5 колони, етикет на последната `"Общ бр. точки"`, `Rows[0].Values` = `["1","11","Петър Иванов Колев","ПМГ","42"]`.
  - `Read_DocumentTitleNotInLabels`: над заглавието има центриран ред „МИНИСТЕРСТВО НА ОБРАЗОВАНИЕТО“ @240–450 → нито един етикет не съдържа „МИНИСТЕРСТВО“.
  - `Read_CenteredHeaderGoesToNearestColumn`: етикет „Име“ @211, имената @138–260 → етикетът на колоната с имената е „Име“.
  - `Read_ContinuationPageInheritsColumns`: стр. 2 без заглавие, същите x → `Columns` равни на стр. 1, `HeaderId` равен, `Page == 2`.
  - `Read_WrappedNameJoinsItsRow`: име на ред с y=500, номер и точки на y=492 → един ред с цялото име и точките.
  - `Read_PageWithoutTable_NoTable`: страница само с текст → няма таблица за нея.
- [ ] Реализация по спецификацията (редове по y с допуск 3 pt; номер в най-лявата колона на номерата; присъединяване до 9 pt; колони = слети x интервали на думите от редовете; заглавие = редовете над първия ред, чиито думи попадат в x обхвата на колоните ± 20 pt и съдържат „Им“; продължение = страница без такова заглавие, но с редове, чиито думи попадат в колоните на последното заглавие).
- [ ] Тестовете минават, commit `Add TableReader that rebuilds protocol tables from word positions`.

### Task 5b: ColumnMapper

**Files:** Create `Tracker.Importer/ColumnMapper.cs`; Test `Tracker.Tests/ColumnMapperTests.cs`. Delete `Tracker.Importer/RowExtractor.cs`, `Tracker.Tests/RowExtractorTests.cs` (заменени).

**Produces:** `public class ColumnMapper(HttpClient http, OllamaOptions options) { public Task<ColumnMap?> MapAsync(Table table, CancellationToken ct = default); }` (`null` = не може да се съпостави).

- [ ] Тестове с фалшив handler, таблица с колони `№, Клас, Имена, Фик.№2, Общ бр. точки` и редове с точки 42, 37, 30:
  - `Map_ValidAnswer_ReturnsIndexes`: `{"name":"Имена","grade":"Клас","score":"Общ бр. точки","place":null}` → `ColumnMap(2, 1, 4, null)`.
  - `Map_UnknownLabel_RetriesThenNull`: два отговора с `"score":"Точки"` → `null`, 2 заявки.
  - `Map_ScoreColumnNotRanked_RetriesWithCorrection`: първо `"score":"Фик.№2"` (стойности 250, 262, 219), после правилен → `ColumnMap` с `Score == 4`; втората заявка съдържа „Фик.№2“ като поправка.
  - `Map_ScoreColumnNotNumbers_Null`: `"score":"Имена"` два пъти → `null`.
  - `Map_PromptHasLabelsAndSampleRows`: prompt-ът съдържа всички етикети и `Общ бр. точки=42`.
- [ ] Реализация: една заявка с етикетите и първите 3 реда като `етикет=стойност`; отговор с етикети → индекси; проверки: етикетите съществуват, ≥ 90% от стойностите в колоната за точки са числа (запетая или точка), `RowValidator.IsRankingOrder`; при неуспех още един опит с поправка, която назовава грешната колона.
- [ ] Тестовете минават, commit.

### Task 5c: RowBuilder и опростен RowValidator

**Files:** Create `Tracker.Importer/RowBuilder.cs`; Modify `Tracker.Importer/RowValidator.cs`, `Tracker.Importer/Rows.cs`; Test `Tracker.Tests/RowBuilderTests.cs`, Modify `Tracker.Tests/RowValidatorTests.cs`.

**Produces:**
```csharp
public static class RowBuilder { public static List<ExtractedRow> Build(Table table, ColumnMap map); }
public static class RowValidator { public static ValidatedRow Validate(ExtractedRow row, IReadOnlyList<ExtractedRow> tableRows); /* IsRankingOrder без промяна */ }
RowStatus.ScoreNotNumber = "score-not-number"; RowStatus.TableNotMapped = "table-not-mapped";
```
Махат се `RowStatus.NameNotInPage`, `ScoreNotOnLine`, `ScoreOrder` и `PageText`.

- [ ] Тестове:
  - `Build_ReadsMappedColumns`: стойности „Петър Иванов Колев“, „11“, „18,50“, „3“ → `ExtractedRow("Петър Иванов Колев", 11, 18.50m, 3)`.
  - `Build_PlaceInWords`: „първо място“ → 1, „второ място“ → 2, „трето място“ → 3, „“ → null.
  - `Build_ScoreNotNumber_Null`: „отсъства“ → Score null.
  - `Build_NoGradeColumn_NullGrade`.
  - `Validate_ScoreNull_ScoreNotNumber`.
  - Запазват се (с новата сигнатура): `Validate_CorrectRow_Ok`, `Validate_LatinName_NameFormat`, `Validate_HyphenatedName_Ok`, `Validate_FractionalPlace_Dropped`, `Validate_PlaceBelowHigherScores_Dropped`, `Validate_PartialLaureateList_KeepsPlace`, `Validate_PlaceRuleIgnoresRowsWithoutScore`, `Validate_GradeOutOfRange_Null`, четирите `IsRankingOrder_*`. Махат се тестовете за `NameNotInPage`, `ScoreFromOtherLine`, `NameSplitAcrossLines`, `DecimalCommaScore` (покрит от `Build_ReadsMappedColumns`).
- [ ] Тестовете минават, commit.

### Task 5d: Нов поток в `extract` и проверка върху 6-те протокола

**Files:** Create `Tracker.Importer/PdfWordReader.cs` (`static IReadOnlyList<IReadOnlyList<PdfWord>> Read(string path)`, маха U+FEFF); Modify `Tracker.Importer/ExtractCommand.cs`; Delete `Tracker.Importer/PdfTextReader.cs`.

- [ ] `ExtractCommand`: PDF → `PdfWordReader` → `TableReader` → за всеки `HeaderId` един `ColumnMapper.MapAsync` (кеширан) → `RowBuilder` → `RowValidator` → `Deduplicator` → CSV. Таблица без съпоставяне → един ред `table-not-mapped` (страница, празно име). Обобщението печата и загубата по протоколи: `<състезание>: ok N, отхвърлени M (P%)`.
- [ ] Ръчно: `extract` върху 6-те протокола → всеки протокол под 10% загуба; 20 случайни реда `ok` (поне 2 от всеки протокол) съвпадат с PDF-ите. Ако някой протокол е над 10% → спиране и доклад.
- [ ] Commit `Read protocol tables by column position`.

След 5d: Task 6 (import), Task 7 (приложението), Task 8 (пълен импорт и примери) по плана по-горе. В Task 8 стъпката „при над 10% отхвърлени редове → спиране“ вече е покрита от 5d.
