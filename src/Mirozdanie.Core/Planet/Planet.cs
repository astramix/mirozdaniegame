namespace Mirozdanie.Core.Planet;

/// <summary>
/// Слои планеты. Карта W×H и эко-сетка (W/s)×(H/s). См. docs/design/planet.md.
/// Правки Создателя хранятся отдельными слоями поверх сгенерированного рельефа, производные поля
/// (климат, реки, биомы, эко-сетка) пересчитывает <see cref="PlanetGenerator.Recompute"/> (docs/design/terraforming.md, §2).
/// </summary>
public sealed class PlanetData
{
    public readonly int W, H, EW, EH, Scale;

    // Карта
    public readonly float[] Elevation;     // км, < 0 — под водой
    public readonly float[] Filled;        // высота после заполнения впадин (для стока)
    public readonly float[] Temperature;   // средняя годовая, °C
    public readonly float[] SeasonAmp;     // амплитуда сезонов, °C
    public readonly float[] Precip;        // мм/год
    public readonly Biome[] Biome;
    public readonly byte[] Plate;
    public readonly float[] BoundaryDist;  // до границы плит, клеток
    public readonly float[] Flow;          // накопленный сток
    public readonly int[] Downstream;      // индекс клетки ниже по течению, −1 у моря
    public readonly bool[] River;
    public readonly float[] Npp;
    public readonly float[] Fertility;
    public readonly float[] Ore;
    public readonly float[] Fish;

    // Эко-сетка
    public readonly float[] ELand;         // доля суши
    public readonly float[] ETemp;
    public readonly float[] EPrecip;
    public readonly float[] ENpp;
    public readonly float[] EFertility;
    public readonly float[] ERiver;
    public readonly float[] EOre;
    public readonly float[] EFish;
    public readonly float[] EElev;
    public readonly float[] ESeason;
    public readonly float[] EBoundary;     // близость к границе плит 0…1
    public readonly Biome[] EBiome;
    public readonly bool[] EDomesticable;
    public readonly bool[] ECoast;

    // Правки Создателя (terraforming.md, §2)
    public readonly float[] BaseElevation; // рельеф генератора, км
    public readonly float[] ElevDelta;     // добавка к высоте, км
    public readonly float[] PrecipMul;     // множитель осадков
    public readonly float[] TempDelta;     // добавка к температуре, °C
    public readonly float[] FertDelta;     // добавка к плодородию
    public readonly float[] OreField;      // рудоносность (едет с сушей при дрейфе)
    public readonly float[] ExtraFlow;     // родники: добавка к стоку (сила «Река»)
    public readonly float[] IceNoise;      // шум края паковых льдов (вычислен при генерации)
    // Ископаемые (kardashev.md, §4): запас на клетку карты, ЭДж. Едут с сушей при дрейфе.
    public readonly float[] Coal, Oil, Uranium;
    // Эко-сетка: запасы (ЭДж) и возобновляемые источники 0…1.
    public readonly float[] ECoal, EOil, EUranium, ESolar, EWind, EHydro;
    public float SeaLevel;                 // км; рост — море наступает
    public float GlobalTempDelta;          // °C
    public float RiverThreshold;           // сток, выше которого клетка — река (последний пересчёт)

    public int ContinentCount;
    public int HighestCell;
    public int[] GreatRivers = Array.Empty<int>(); // устья крупнейших рек (клетки карты)

    public PlanetData(int w, int h, int scale)
    {
        W = w; H = h; Scale = scale; EW = w / scale; EH = h / scale;
        int n = w * h, en = EW * EH;
        Elevation = new float[n]; Filled = new float[n]; Temperature = new float[n]; SeasonAmp = new float[n];
        Precip = new float[n]; Biome = new Biome[n]; Plate = new byte[n]; BoundaryDist = new float[n];
        Flow = new float[n]; Downstream = new int[n]; River = new bool[n]; Npp = new float[n];
        Fertility = new float[n]; Ore = new float[n]; Fish = new float[n];
        ELand = new float[en]; ETemp = new float[en]; EPrecip = new float[en]; ENpp = new float[en];
        EFertility = new float[en]; ERiver = new float[en]; EOre = new float[en]; EFish = new float[en];
        EElev = new float[en]; ESeason = new float[en]; EBoundary = new float[en]; EBiome = new Biome[en];
        EDomesticable = new bool[en]; ECoast = new bool[en];
        BaseElevation = new float[n]; ElevDelta = new float[n]; PrecipMul = new float[n]; TempDelta = new float[n];
        FertDelta = new float[n]; OreField = new float[n]; ExtraFlow = new float[n]; IceNoise = new float[n];
        Coal = new float[n]; Oil = new float[n]; Uranium = new float[n];
        ECoal = new float[en]; EOil = new float[en]; EUranium = new float[en]; ESolar = new float[en]; EWind = new float[en]; EHydro = new float[en];
        Array.Fill(PrecipMul, 1f);
    }

    /// <summary>Высота над исходным нулём (без уровня моря): основа + правки.</summary>
    public float Total(int i) => BaseElevation[i] + ElevDelta[i];

    /// <summary>Площадь доли суши клетки карты пропорциональна cos широты (для «объёма» правок).</summary>
    public float CosLat(int y) => MathF.Max(0.05f, MathF.Cos(Latitude(y) * MathF.PI / 180f));

    public float Latitude(int y) => 90f - (y + 0.5f) * 180f / H;
    public float EcoLatitude(int ey) => 90f - (ey + 0.5f) * 180f / EH;

    public int WrapX(int x) => ((x % W) + W) % W;
    public int EWrapX(int x) => ((x % EW) + EW) % EW;

    public int EcoIndex(int ex, int ey) => ey * EW + EWrapX(ex);

    /// <summary>Расстояние на эко-сетке с замкнутой долготой (евклидово, в клетках).</summary>
    public float EcoDistance(int a, int b)
    {
        int ax = a % EW, ay = a / EW, bx = b % EW, by = b / EW;
        int dx = Math.Abs(ax - bx);
        if (dx > EW / 2) dx = EW - dx;
        int dy = ay - by;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public bool EIsLand(int e) => ELand[e] >= 0.5f;
}
