# Э0. Каркас

## Проекты

- `src/Mirozdanie.Core` — net8.0, nullable, `TreatWarningsAsErrors`, без внешних зависимостей (System.Text.Json и System.IO.Compression входят в BCL).
- `src/Mirozdanie.Core.Tests` — xUnit.
- `src/Mirozdanie.Bench` — BenchmarkDotNet; плюс режим `--budget` (свой секундомер, без BenchmarkDotNet), который сверяет медианы с бюджетами ARCHITECTURE.md и возвращает код 1 при превышении более чем на 20 %. Этот режим запускается в CI, потому что полный BenchmarkDotNet долог.
- `src/Mirozdanie.Cli` — команды `run`, `maps`, `diversity`.
- `game/` — Godot 4.4 .NET, ссылается на Core через `ProjectReference`.

## RNG

`Rng` — xoshiro256** (Blackman & Vigna 2018, https://prng.di.unimi.it/). Состояние — 4×ulong, `struct` с методами `NextULong`, `NextFloat` [0,1), `NextInt(n)` (метод Лемира без смещения), `NextGaussian` (Бокс–Мюллер без кеша, чтобы не было скрытого состояния), `Chance(p)`.

Потоки подсистем: `RngStreams.Get(StreamId id)` — сид потока = SplitMix64(seed ⊕ hash(id)). Перечисление `StreamId`: Planet, Climate, Life, Agents, Evolution, Awakening, Language, Culture, Faith, Economy, Politics, Creator, Chronicle, Natural. Изменение одной системы не сдвигает случайность других. Для «случайности по запросу» (жители города, имена) — `Rng.Derive(seed, a, b, c)`.

Тест: известная последовательность xoshiro256** для сида SplitMix64(0); одинаковые потоки при одном сиде; независимость: вызовы в потоке Faith не меняют последовательность Planet.

## Event bus

```csharp
public readonly record struct WorldEvent(int Id, long Year, EventType Type, int Cell,
    int A, int B, int C, float Magnitude, int Flags, int Cause);
```

- `A,B,C` — участники (id групп, религий, видов, персонажей — по смыслу типа, описано в `EventType`).
- `Cause` — id события-причины (цепочки причинности для Хроники: «из-за засухи 200 года…»).
- `EventBus.Publish(in WorldEvent)` пишет в журнал (`List<WorldEvent>`, заранее ёмкий) и в очередь текущего тика. `Subscribe(EventType, handler)` — подписчики в порядке регистрации. `Dispatch()` раздаёт очередь, пока она не опустеет (с защитой от бесконечного каскада: глубина ≤ 8).
- Значимость `Magnitude` используется Хроникой для отбора.
- Тексты не хранятся в событиях: Хроника строит их в конце из состояния мира (имена берутся из языков).

## Снимки состояния

`WorldSnapshot` — неизменяемый объект: год, эпоха, ширина и высота, ссылки на статические слои планеты (общие, неизменяемые после генерации), копии динамических слоёв: владелец клетки (группа), язык клетки, религия клетки, полития клетки, отношение к Создателю по клеткам, список групп (позиция, население, имя), энергия Создателя, последние 50 событий. Создаётся `World.CreateSnapshot()` в заранее выделенные буферы (двойная буферизация: два снимка, чередуются). Бюджет ≤ 1 мс.

## CI

`.github/workflows/ci.yml`: ubuntu-latest, setup-dotnet 8, `dotnet build -warnaserror`, `dotnet test`, `dotnet run --project src/Mirozdanie.Bench -c Release -- --budget`, прогон CLI на 300 лет (дымовой). Godot-экспорт под три ОС — отдельный workflow `release.yml` по тегу (черновик; ключевой шаг — `godot --headless --export-release`).
