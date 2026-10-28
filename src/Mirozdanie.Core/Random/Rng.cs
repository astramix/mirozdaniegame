namespace Mirozdanie.Core.Random;

/// <summary>
/// xoshiro256** (Blackman &amp; Vigna 2018, https://prng.di.unimi.it/xoshiro256starstar.c).
/// Единственный источник случайности в Core. См. docs/design/e0-skeleton.md.
/// </summary>
public sealed class Rng
{
    private ulong _s0, _s1, _s2, _s3;

    public Rng(ulong seed)
    {
        // Состояние заполняется SplitMix64, как рекомендуют авторы xoshiro.
        ulong x = seed;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
        if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1;
    }

    public static ulong SplitMix64(ref ulong x)
    {
        ulong z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Смешивание нескольких чисел в один сид (для «случайности по запросу»).</summary>
    public static ulong Hash(ulong a, ulong b = 0, ulong c = 0, ulong d = 0)
    {
        ulong x = a;
        ulong h = SplitMix64(ref x);
        x = h ^ b; h = SplitMix64(ref x);
        x = h ^ c; h = SplitMix64(ref x);
        x = h ^ d; h = SplitMix64(ref x);
        return h;
    }

    public static Rng Derive(ulong seed, ulong a, ulong b = 0, ulong c = 0) => new(Hash(seed, a, b, c));

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextULong()
    {
        ulong result = Rotl(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>[0, 1) с 24 битами точности.</summary>
    public float NextFloat() => (NextULong() >> 40) * (1.0f / (1 << 24));

    /// <summary>[0, 1) с 53 битами точности.</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    public float Range(float min, float max) => min + (max - min) * NextFloat();

    /// <summary>[0, n) без смещения (Lemire 2019, «Fast Random Integer Generation in an Interval»).</summary>
    public int NextInt(int n)
    {
        if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
        ulong un = (ulong)n;
        ulong x = NextULong() >> 32;
        ulong m = x * un;
        ulong l = m & 0xFFFFFFFFUL;
        if (l < un)
        {
            ulong t = (0x1_0000_0000UL - un) % un;
            while (l < t)
            {
                x = NextULong() >> 32;
                m = x * un;
                l = m & 0xFFFFFFFFUL;
            }
        }
        return (int)(m >> 32);
    }

    public int Range(int minInclusive, int maxExclusive) => minInclusive + NextInt(maxExclusive - minInclusive);

    public bool Chance(double p) => NextDouble() < p;

    /// <summary>Нормальное распределение (Бокс–Мюллер, без кеша второго значения — нет скрытого состояния).</summary>
    public float NextGaussian()
    {
        double u1 = 1.0 - NextDouble();
        double u2 = NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }

    /// <summary>Шум Гумбеля для выбора по максимуму (эквивалент выборки softmax).</summary>
    public float NextGumbel()
    {
        double u = NextDouble();
        if (u < 1e-12) u = 1e-12;
        return (float)-Math.Log(-Math.Log(u));
    }

    public T Pick<T>(IReadOnlyList<T> list) => list[NextInt(list.Count)];

    /// <summary>Детерминированная перетасовка Фишера–Йетса.</summary>
    public void Shuffle<T>(Span<T> span)
    {
        for (int i = span.Length - 1; i > 0; i--)
        {
            int j = NextInt(i + 1);
            (span[i], span[j]) = (span[j], span[i]);
        }
    }

    public (ulong, ulong, ulong, ulong) State => (_s0, _s1, _s2, _s3);
}

public enum StreamId : ulong
{
    Planet = 1, Climate, Life, Agents, Evolution, Awakening, Language, Culture,
    Faith, Economy, Politics, Creator, Chronicle, Natural, Naming, GodLoop,
}

/// <summary>Отдельный поток RNG на подсистему: изменение одной системы не сдвигает случайность других.</summary>
public sealed class RngStreams
{
    private readonly ulong _seed;
    private readonly Rng[] _streams;

    public RngStreams(ulong seed)
    {
        _seed = seed;
        var values = Enum.GetValues<StreamId>();
        ulong max = 0;
        foreach (var v in values) max = Math.Max(max, (ulong)v);
        _streams = new Rng[max + 1];
        foreach (var v in values) _streams[(ulong)v] = new Rng(Rng.Hash(seed, (ulong)v * 0x632BE59BD9B4E019UL));
    }

    public ulong Seed => _seed;
    public Rng this[StreamId id] => _streams[(ulong)id];
}
