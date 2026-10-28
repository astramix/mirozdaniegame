namespace Mirozdanie.Core.Planet;

public enum Biome : byte
{
    DeepOcean, Shelf, Lake, Ice, Tundra, Taiga, ColdDesert, TemperateRainforest, TemperateForest,
    Shrubland, Grassland, Desert, Savanna, TropicalSeasonalForest, TropicalRainforest, Count,
}

public static class Biomes
{
    /// <summary>
    /// Классификация по диаграмме Уиттекера (Whittaker 1975, «Communities and Ecosystems»),
    /// упрощённые пороги — docs/design/planet.md, таблица «Биомы».
    /// </summary>
    public static Biome Whittaker(float tempC, float precipMm)
    {
        if (tempC < -10f) return Biome.Ice;
        if (tempC < -5f) return Biome.Tundra;
        if (tempC < 5f) return precipMm >= 250f ? Biome.Taiga : Biome.ColdDesert;
        if (tempC < 20f)
        {
            if (precipMm < 250f) return Biome.Desert;
            if (precipMm < 500f) return Biome.Grassland;
            if (precipMm < 900f) return Biome.Shrubland;
            if (precipMm < 2000f) return Biome.TemperateForest;
            return Biome.TemperateRainforest;
        }
        if (precipMm < 400f) return Biome.Desert;
        if (precipMm < 1500f) return Biome.Savanna;
        if (precipMm < 2500f) return Biome.TropicalSeasonalForest;
        return Biome.TropicalRainforest;
    }

    public static bool IsWater(Biome b) => b <= Biome.Lake;

    public static string Russian(Biome b) => b switch
    {
        Biome.DeepOcean => "океан",
        Biome.Shelf => "шельф",
        Biome.Lake => "озеро",
        Biome.Ice => "ледяная пустыня",
        Biome.Tundra => "тундра",
        Biome.Taiga => "тайга",
        Biome.ColdDesert => "холодная пустыня",
        Biome.TemperateRainforest => "умеренный дождевой лес",
        Biome.TemperateForest => "умеренный лес",
        Biome.Shrubland => "лесостепь",
        Biome.Grassland => "степь",
        Biome.Desert => "пустыня",
        Biome.Savanna => "саванна",
        Biome.TropicalSeasonalForest => "сезонный тропический лес",
        Biome.TropicalRainforest => "тропический дождевой лес",
        _ => "?",
    };

    /// <summary>Может ли биом дать одомашниваемые злаки (по мотивам Diamond 1997).</summary>
    public static bool Domesticable(Biome b) =>
        b is Biome.Grassland or Biome.Shrubland or Biome.Savanna or Biome.TemperateForest or Biome.TropicalSeasonalForest;

    /// <summary>Первичная продукция по модели Майами (Lieth 1975), г/м²/год.</summary>
    public static float MiamiNpp(float tempC, float precipMm)
    {
        double nppT = 3000.0 / (1.0 + Math.Exp(1.315 - 0.119 * tempC));
        double nppP = 3000.0 * (1.0 - Math.Exp(-0.000664 * precipMm));
        return (float)Math.Min(nppT, nppP);
    }
}
