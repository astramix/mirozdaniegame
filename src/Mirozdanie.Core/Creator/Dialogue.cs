using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Language;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Core.Creator;

/// <summary>Реплика народа: на его языке, с переводом и пояснением рассказчика.</summary>
public sealed record DialogueLine(string Native, string Translation, string Narration, float Mood, string Speaker = "");

public enum DialogueAction : byte { Ask, Appear, Bless, Demand, Threaten, Teach, AnswerPrayer }

/// <summary>Какая у народа нужда — от неё зависят первая реплика и молитва.</summary>
public enum Need : byte { None, Hunger, Plague, War }

/// <summary>
/// Разговор с народом (docs/design/viewer-2.md, §3). Все функции чистые и без случайности:
/// реплика зависит только от состояния мира, поэтому повтор сохранения даёт те же ответы.
/// </summary>
public static class Dialogue
{
    public static Need NeedOf(World w, Group g)
    {
        if (w.Year - g.LastPlague <= 5) return Need.Plague;
        if (g.FamineYears > 0 || g.FoodRatio < 0.8f || g.LastPrayer >= 0 && w.Year - g.LastPrayer <= 10 && g.PrayerAnswered < g.LastPrayer) return Need.Hunger;
        if (w.Year - g.LastDefeat <= 10) return Need.War;
        return Need.None;
    }

    /// <summary>Есть ли открытая молитва (ответ на неё — источник любви и энергии).</summary>
    public static bool HasOpenPrayer(World w, Group g) =>
        g.Alive && (g.LastPrayer >= 0 && w.Year - g.LastPrayer <= 10 && g.PrayerAnswered < g.LastPrayer
                    || w.Year - g.LastPlague <= 5 && g.PrayerAnswered < g.LastPlague);

    public static string CreatorName(World w, Group g)
    {
        if (g.ReligionId >= 0 && w.Religions[g.ReligionId].Form != DeityForm.None) return w.Religions[g.ReligionId].CreatorName.Native;
        if (g.Epithets.Count > 0) return g.Epithets[0].Name.Native;
        return w.LangOf(g).Cyr(Concept.Sky);
    }

    public static string CreatorGloss(World w, Group g)
    {
        if (g.ReligionId >= 0 && w.Religions[g.ReligionId].Form != DeityForm.None) return w.Religions[g.ReligionId].CreatorName.Gloss;
        if (g.Epithets.Count > 0) return g.Epithets[0].Name.Gloss;
        return "Небо";
    }

    /// <summary>Настроение народа к Создателю, 0…1 (для шкалы в окне разговора).</summary>
    public static float Mood(Group g) => Math.Clamp(0.5f + 0.45f * g.B - 0.35f * g.G - 0.2f * g.D, 0f, 1f);

    public static bool Obeys(World w, Group g)
    {
        bool rebel = g.ReligionId >= 0 && w.Religions[g.ReligionId].Rebel;
        return !rebel && g.Attitude is Attitude.Love or Attitude.Fear or Attitude.Bargain;
    }

    private static string W(Lang L, Concept c) => L.Cyr(c, false);

    private static string Say(Lang L, string creator, params Concept[] words)
    {
        var parts = words.Select(c => W(L, c)).ToArray();
        string s = string.Join(" ", parts);
        return $"{creator}! {char.ToUpperInvariant(s[0])}{s[1..]}!";
    }

    /// <summary>Первая реплика народа, когда Создатель обращает на него взор (генеративная речь, lore.md §12).</summary>
    public static DialogueLine Opening(World w, Group g, Conversation? conv = null) => Speech.Speak(w, g, null, conv);

    /// <summary>Что народ думает о Создателе (ответ на «Спросить»).</summary>
    public static DialogueLine AskAbout(World w, Group g, Conversation? conv = null)
    {
        var speech = Speech.Speak(w, g, DialogueAction.Ask, conv);
        var L = w.LangOf(g);
        string c = CreatorName(w, g);
        var reasons = new List<string>();
        for (int k = 0; k < g.MemoryCount && reasons.Count < 3; k++)
        {
            int idx = (g.MemoryHead - 1 - k + g.Memory.Length) % g.Memory.Length;
            var m = g.Memory[idx];
            string what = m.Event switch
            {
                Events.EventType.Drought or Events.EventType.NaturalDrought => "засуху",
                Events.EventType.Rain or Events.EventType.DroughtEnded => "дождь",
                Events.EventType.LightningStrike => "молнию",
                Events.EventType.MiracleHarvest or Events.EventType.Bounty => "урожай",
                Events.EventType.MeteorImpact => "огонь с неба",
                Events.EventType.Plague => "мор",
                Events.EventType.Famine => "голод",
                Events.EventType.Earthquake => "землетрясение",
                Events.EventType.Vision => "видение",
                Events.EventType.Battle => "войну",
                _ => "знак",
            };
            string how = m.Kind switch
            {
                InterpretationKind.Punishment => "карой", InterpretationKind.Wrath => "гневом", InterpretationKind.Trial => "испытанием",
                InterpretationKind.Gift => "даром", InterpretationKind.Bargain => "платой за жертвы", InterpretationKind.Omen => "знамением",
                InterpretationKind.Favoritism => "несправедливостью", _ => "делом природы",
            };
            reasons.Add($"{what} {m.Year} года они сочли {how}");
        }
        string eps = g.Epithets.Count > 0 ? string.Join(", ", g.Epithets.Select(e => $"{e.Name.Native} («{e.Name.Gloss}»)")) : "у них нет для вас имён";
        string narration = $"Они зовут вас {c} («{CreatorGloss(w, g)}»). Другие имена: {eps}. Отношение: {Chronicle.ChronicleWriter.AttitudeName(g.Attitude)}."
                           + (reasons.Count > 0 ? $" Помнят: {string.Join("; ", reasons)}." : "");
        return speech with { Narration = speech.Narration + " " + narration };
    }

    /// <summary>Ответ народа на действие Создателя (показывается сразу; эффект применяется со следующим годом).</summary>
    public static DialogueLine Reply(World w, Group g, DialogueAction a, string message = "", Conversation? conv = null) =>
        a == DialogueAction.Ask ? AskAbout(w, g, conv) : Speech.Speak(w, g, a, conv, message);

    public static string MessageRu(string m) => m switch
    {
        "peace" => "мир", "war" => "война", "love" => "любовь", "wrath" => "гнев и покаяние", "knowledge" => "знание",
        "obedience" => "покорность", "freedom" => "свобода", _ => m,
    };
}
