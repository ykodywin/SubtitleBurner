# SubtitleBurner — библиотека CapCut-субтитров (SkiaSharp + ffmpeg burn)

**Goal:** Вынести субтитровый рендер CashButton в отдельный .NET-репозиторий `D:\Subs` с публичным NuGet-пакетом `SubtitleBurner` (MIT): words → PNG-оверлеи (SkiaSharp) → ffmpeg filter_complex → готовый mp4 одним вызовом. Либа универсальная — сторонние потребители подают свои word-timings (Whisper и т.п.) и получают mp4.

**Architecture:** Три слоя в одном пакете `SubtitleBurner`:
1. `SubtitleBurner.Rendering` — раскладка слов в блоки/строки + SkiaSharp PNG-кадры (boxed karaoke, pop karaoke, regular blocks, keyword-акцент + emoji, спикер-цвета).
2. `SubtitleBurner.Ffmpeg` — построитель filter_complex (per-PNG inputs, overlay + enable-окна, Y по alignment, margin 20%).
3. `SubtitleBurner.Ffmpeg.SubtitleBurner` (static class) — `BurnAsync(video, words, options) → mp4`: probe, temp-директории, запуск ffmpeg, cleanup, cancellation/progress.

**Tech Stack:** .NET 10 (SDK 10.0.302 на машине; net10.0 либа подключается к net11.0 CashButton.Api), SkiaSharp, xunit. libass НЕ используем (решение юзера 2026-08-14). Рендер-подход = текущий SkiaSharp из CashButton, переносится почти 1-в-1.

**Решения юзера:** все 3 слоя (либа сама гоняет ffmpeg до готового mp4); отдельный репо + NuGet-пакет **SubtitleBurner** (имя свободно на nuget.org), лицензия **MIT**, ориентация на публичное использование (кроссплатформа, XML-доки, samples).

## Требования публичной либы (вшиты в P0)

- **Кроссплатформа с первого дня:** ffmpeg-дискавери = явный путь → env `FFMPEG_PATH` → `PATH` (никаких хардкодов машины); temp только `Path.GetTempPath()`; emoji-шрифты — существующая fallback-цепочка (Segoe UI Emoji → Noto Color Emoji → Apple Color Emoji); текстовый дефолт-шрифт через `SKFontManager` по платформе; Linux-зависимости (fonts-noto-color-emoji) — в README.
- **Публичная поверхность минимальна:** `SubtitleWord/Line/Overlay`, `SubtitleStyle`+`SubtitleStyles`, `SubtitleRenderer`, `FilterGraphBuilder`, `SubtitleBurner`, `BurnOptions` — остальное `internal` (+`InternalsVisibleTo` тестам).
- **csproj-метаданные:** `PackageId=SubtitleBurner`, `Description`, `PackageLicenseExpression=MIT`, `PackageReadmeFile`, `GenerateDocumentationFile` + warning-as-error на missing XML-доки публичного API, `Deterministic=true`.
- **samples/SubtitleBurner.Demo** — консольное демо: генерит тест-видео lavfi, жжёт все 5 пресетов, складывает в `out/`.

---

## Источник переноса (D:\CashButton\apps\CashButton\CashButton.Api)

| Что | Файл | Судьба |
|---|---|---|
| `SubtitleOverlayRenderer` (511 строк) | `Services/SubtitleOverlayRenderer.cs` | → `Subs.Rendering` |
| `SubtitleStylePreset`, `SubtitleStyles`, `SubtitleOptions` | `Services/SubtitleStyles.cs` | → `Subs` (модель стиля) |
| Overlay-граф + marginY/overlayYExpr + probe | `Services/ClipService.cs` (~481-620 + граф) | → `Subs.Ffmpeg` |
| `SubtitleService` (чтение words.json/transcript) | `Services/SubtitleService.cs` | **остаётся** в CashButton (формат sidecar — ответственность приложения) |
| `EmotionalKeywordList`, filler cuts, insert-режим, chat panel | — | **остаются** в CashButton; либа принимает `Func<string,bool>?` keyword-матчер, remap оверлеев делает вызывающий |

## Публичный API (черновик, фиксируется в P0-3)

```csharp
namespace SubtitleBurner;

public record SubtitleWord(double Start, double End, string Text, string? Speaker = null);
public record SubtitleLine(double Start, double End, string Text);

public record SubtitleStyle(            // = SubtitleOptions + preset resolve
    string Preset = "capcut",           // default|hormozi|minimal|neon|capcut
    string? FontName = null, double? FontScale = null, string? Color = null,
    int? Outline = null, SubtitleAlign? Align = null,   // Bottom|Center|Top
    bool? KeywordHighlight = null, string? KeywordColor = null, bool? KeywordEmoji = null);

public record RenderOptions(
    int Width, int Height,              // реальный кадр после scale
    Func<string, bool>? IsKeyword = null,
    IReadOnlyList<string>? SpeakerPalette = null);  // default: sky/amber/fuchsia/emerald

// Слой 1
public static class SubtitleRenderer {
    IReadOnlyList<SubtitleOverlay>? RenderBoxedKaraoke(words, window, RenderOptions, style, outDir, name);
    IReadOnlyList<SubtitleOverlay>? RenderPopKaraoke(...);
    IReadOnlyList<SubtitleOverlay>? RenderBlocks(lines, window, ...);
    byte[]? RenderPreviewPng(words, w, h, style);
}

// Слой 2+3
public record BurnOptions(
    string FfmpegPath, string? FfprobePath = null,
    IReadOnlyList<string>? EncoderArgs = null,   // default: libx264 fast crf 23
    double BottomMarginRatio = 0.20,
    int KaraokeWordCap = 300);

public static class SubtitleBurner {
    Task<BurnResult> BurnAsync(string inputVideo, string outputVideo,
        IReadOnlyList<SubtitleWord> words, SubtitleStyle style,
        BurnOptions opts, IProgress<double>? progress = null, CancellationToken ct = default);
}
```

`SubtitleOverlay(PngPath, Start, End)` остаётся публичным — CashButton ремапит его при filler cuts / insert-режиме.

---

## P0 — рабочая либа + интеграция в CashButton

**Метод: строгий TDD (skill test-driven-development).** Каждое поведение: RED (тест написан первым, падение проверено и объяснимо — «тип отсутствует», а не опечатка) → GREEN (минимальный код) → REFACTOR с зелёными тестами. Вертикальные tracer bullets — один цикл на поведение, не «все тесты, потом вся реализация». Переносимый код (шаг 3) не считается оправданием: сначала тесты на желаемый API либы, потом порт реализации. Каждый шаг заканчивается полным прогоном `dotnet test` и коммитом.

### Шаг 1. Скаффолд репо
- `git init` в `D:\Subs`; `.gitignore` (VS/.NET), `README.md` (quickstart + таблица пресетов), `LICENSE` (MIT), `Directory.Build.props` (net10.0, nullable, LangVersion latest).
- `SubtitleBurner.sln`, `src/SubtitleBurner/SubtitleBurner.csproj` (PackageId `SubtitleBurner`, Version `0.1.0`, метаданные из раздела «Требования публичной либы», SkiaSharp — проверить, нужен ли HarfBuzz; в CashButton только SkiaSharp), `tests/SubtitleBurner.Tests/` (xunit, coverlet), `samples/SubtitleBurner.Demo/`.
- Verify: `dotnet build D:\Subs\SubtitleBurner.sln` зелёный. Commit `chore: scaffold SubtitleBurner solution`.

### Шаг 2. Перенос моделей стиля
- Копия `SubtitleStyles.cs` → `src/SubtitleBurner/SubtitleStyle.cs` + `SubtitleStyles.cs` (namespace `SubtitleBurner`, ASS-цвета оставить как есть — парсер переезжает вместе с рендером).
- `SubtitleWord`, `SubtitleLine`, `SubtitleOverlay` → `src/SubtitleBurner/Models.cs`.
- Unit-тест: `SubtitleStyles.Resolve` (5 пресетов + fallback), `ParseAssColor` round-trip. Commit `feat: style presets and core models`.

### Шаг 3. Перенос рендера
- `SubtitleOverlayRenderer.cs` → `src/SubtitleBurner/Rendering/SubtitleRenderer.cs` почти 1-в-1. Изменения по границе:
  - `TranscriptWord` → `SubtitleWord` (speaker как строка, палитра через `RenderOptions.SpeakerPalette`, default = Tailwind-400 sky/amber/fuchsia/emerald — НЕ менять, чипы фронта завязаны).
  - Ключевые слова: сейчас `IsKeyword` решается внутри через `EmotionalKeywordList` → заменить на `RenderOptions.IsKeyword` (null = подсветки нет).
  - Emoji: fallback-цепочка шрифтов (Segoe UI Emoji → Noto Color Emoji → Apple Color Emoji) переезжает как есть; null → тихо без emoji.
  - 300-словный кап караоке: решение принимает `SubtitleBurner`/вызывающий (вернуть null при превышении, как сейчас).
- Unit-тесты (без видео): `BuildBlocks` — перенос по ширине, флеш блока на смене спикера; boxed PNG имеет тёмную плашку (пиксельный ассерт как в subtitles-rendering.md: >55% dark + >0.5% bright в полосе y∈[50%..90%]); keyword-акцент бьёт поверх спикер-цвета (регрессия реального бага). Commit `feat: SkiaSharp subtitle renderer (boxed/pop/blocks)`.

### Шаг 4. Слой ffmpeg
- `src/SubtitleBurner/Ffmpeg/VideoProbe.cs` — размер кадра через ffprobe (перенос `ProbeVideoSize`).
- `src/SubtitleBurner/Ffmpeg/FilterGraphBuilder.cs` — из ClipService: inputs на каждый PNG, `overlay=x='{overlayYExpr}':enable='between(t,s,e)'`, marginY = 20% H, alignment→Y-выражение. Чистая функция → легко тестировать строкой.
- `src/SubtitleBurner/Ffmpeg/SubtitleBurner.cs` — оркестрация: probe → (если 9:16 — scale 1080x1920 остаётся ответственностью ВЫЗЫВАЮЩЕГО, либа рендерит на переданный размер) → render PNG во temp → filter_complex_script файлом (обход 32K cmdline) → запуск ffmpeg (stdout/stderr capture, ct, progress по `-progress pipe`) → cleanup PNG.
- Unit-тест FilterGraphBuilder: 2 оверлея → точная строка графа + enable-окна + Y для align 2/5/8. Commit `feat: ffmpeg filter graph builder and burner`.

### Шаг 5. Интеграционный тест burn → mp4
- Тест: синтетическое видео 6с (`ffmpeg -f lavfi -i testsrc2=size=320x180:duration=6` — ≥320x180 под nvenc-ограничение не нужно, тест на libx264), 8 слов с таймингами → `BurnAsync` в temp mp4 → извлечь кадр в середине слова (`-ss`) → ассерт непустых субтитровых пикселей в нижней полосе.
- ffmpeg для тестов: `SUBS_TEST_FFMPEG` env → fallback `D:\CashButton\bin\`-пути не хардкодить; тест skip с понятным сообщением, если ffmpeg не найден.
- Verify: `dotnet test` зелёный. Commit `test: end-to-end burn integration test`.

### Шаг 6. Пак + интеграция в CashButton
- `dotnet pack -c Release` → `D:\Subs\artifacts\SubtitleBurner.0.1.0.nupkg`; в CashButton `nuget.config` добавить `<add key="subtitleburner-local" value="D:\Subs\artifacts" />` (закоммитить nuget.config — путь машинно-зависимый, отметить в README).
- `CashButton.Api.csproj` += `PackageReference SubtitleBurner 0.1.0`.
- `ClipService.cs`: блок субтитров (~481-554 + граф) переключить на `SubtitleBurner`/слои либы. CashButton сохраняет: filler-remap оверлеев, insert-сдвиг, keyword-матчер (`w => EmotionalKeywordList...Weight>=0.5`), words.json чтение, выбор karaoke-vs-blocks по edited lines (это приложение-логика — либа даёт оба метода, выбор снаружи). **Важно:** BurnAsync либы гоняет весь ffmpeg, а ClipService собирает многоступенчатый граф (cuts/insert/chat/overlay-media) — поэтому в CashButton используем слои 1+2 (рендер + FilterGraphBuilder) и встраиваем граф в существующий pipeline, а `SubtitleBurner` — для простых случаев/других приложений. Это надёжнее, чем натягивать весь монтаж на BurnAsync.
- `SubtitlePreviewEndpoint` → `SubtitleRenderer.RenderPreviewPng`.
- Удалить `Services/SubtitleOverlayRenderer.cs`, `SubtitleStyles.cs` из CashButton.
- Verify через живое API (правило юзера): пересобрать backend (dotnet build с Windows-путём csproj, сверить timestamp DLL), рестарт, рендер короткого клипа (~10с, grep `.words.json` на keyword-офсет) → извлечь кадры ±1-2с от границ линий → vision_analyze (boxed karaoke, акцент, emoji) → удалить тест-клип. Обновить `PROJECT_STRUCTURE.md`.
- Commit в CashButton: `refactor: extract subtitle rendering to Subs package`.

## P1 — после P0
7. Pop-анимация (scale/fade текущего слова между состояниями — плавный CapCut-pop, сейчас мгновенный).
8. Ещё пресеты (OpusClip per-word boxes, градиентная заливка).
9. GitHub-репо + Actions (build+test матрица windows/ubuntu/macos, pack по тегу), README: галерея стилей (скриншоты кадров каждого пресета из Demo), badges.
10. Публикация на nuget.org после стабилизации API (0.x → 1.0): `dotnet nuget push`, trusted publishing или API key в секретах.

## Риски / открытые вопросы
- **Namespace-модель:** `SubtitleOptions` в CashButton — DTO с автобиндингом minimal API (эндпоинты). Либовый `SubtitleStyle` — отдельный record; маппинг в ClipService (5 строк). НЕ тащить ASP.NET-зависимости в либу.
- **Версионирование локального пакета:** при каждой правке либы бампать Version и `dotnet pack`, иначе NuGet закэширует. Рецепт в README (или `nuget locals global-packages --clear` при отладке).
- **Шрифты на других машинах:** либа ищет emoji/текст-шрифты в рантайме; на Linux-деплое потребуется fonts-noto-color-emoji — задокументировать.
- PROJECT_STRUCTURE.md CashButton и skill `cashbutton-app/references/subtitles-rendering.md` обновить после миграции (рендер живёт в D:\Subs).

## Команды верификации (шпаргалка)
```bash
dotnet build D:\Subs\SubtitleBurner.sln
dotnet test D:\Subs\SubtitleBurner.sln
dotnet pack D:\Subs\src\SubtitleBurner\SubtitleBurner.csproj -c Release -o D:\Subs\artifacts
dotnet build D:\CashButton\apps\CashButton\CashButton.Api\CashButton.Api.csproj   # Windows-путь!
```
