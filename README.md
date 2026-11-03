# Постижения (Tracker)

Уеб приложение за хакатон. Ученик пише името си и вижда резултатите си от състезания.

Търсенето разбира имена, написани:
- с правописна грешка: „иван петорв“;
- на латиница: „Ivan Petrov“;
- в обратен ред или само с фамилия: „Петров Иван“, „Петров“;
- с умалително име: „Ваньо Петров“ → Иван Петров *(ИИ)*;
- в изречение: „аз съм Мария Иванова“ *(ИИ)*.

## Как работи

```
текст → QueryInterpreter (локален LLM в Ollama) → NameNormalizer → NameMatcher (Jaro-Winkler) → MySQL
```

- **`QueryInterpreter`** изпраща текста на малък локален модел, който извлича името и превръща умалителните в официални. Ако Ollama не работи или се бави повече от 3 секунди, стъпката се прескача и търсенето продължава без нея.
- **`NameNormalizer`** превръща текста в малки букви, маха пунктуацията и транслитерира латиница → кирилица.
- **`NameMatcher`** оценява сходството с всяко име в базата и предлага „Имахте предвид…?“.

Кодът е в `Tracker/Search/`, а дизайнът е в `docs/superpowers/specs/`.

## Пускане

Нужни са .NET 9 SDK, MySQL или MariaDB и (по желание) [Ollama](https://ollama.com).

1. Connection string в `Tracker/appsettings.Development.json` (файлът не е в git):
   ```json
   { "ConnectionStrings": { "Tracker": "Server=localhost;Port=3306;Database=tracker;User=root;Password=...;CharSet=utf8mb4" } }
   ```
2. Създаване на таблиците и демо данните:
   ```bash
   dotnet tool restore
   dotnet ef database update --project Tracker
   mariadb -uroot -p --default-character-set=utf8mb4 < db/seed.sql
   ```
3. Моделът (размерът е в `appsettings.json` → `Ollama:Model`):
   ```bash
   ollama pull qwen2.5:3b
   ```
4. Стартиране:
   ```bash
   dotnet run --project Tracker
   ```
   и отворете http://localhost:5011

Без Ollama: задайте `"Ollama": { "Enabled": false }`. Всичко освен умалителните имена и свободния текст продължава да работи.

## Тестове

```bash
dotnet test
```

`db/model-check.ps1` сравнява локалните модели по 12 умалителни имена през истинския `/api/search`.
