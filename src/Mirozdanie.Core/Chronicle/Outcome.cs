using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Sim;
using static Mirozdanie.Core.Chronicle.Russian;

namespace Mirozdanie.Core.Chronicle;

public enum OutcomeKind : byte { Ongoing, Ascension, Freedom, Orphans, Ashes, Decline }

/// <summary>Итог мира (docs/design/god-loop.md, §5): название, эпилог и короткие итоги правления Создателя.</summary>
public sealed record WorldOutcome(OutcomeKind Kind, string Title, string Epilogue, IReadOnlyList<string> Facts);

public static class Outcome
{
    /// <summary>Пик населения по сводкам веков (и текущему году).</summary>
    public static double PeakPopulation(World w)
    {
        double peak = w.WorldPopulation;
        foreach (var s in w.Annals) peak = Math.Max(peak, s.Population);
        return peak;
    }

    public static bool IsFinal(World w)
    {
        var k = Evaluate(w).Kind;
        return k != OutcomeKind.Ongoing;
    }

    public static WorldOutcome Evaluate(World w)
    {
        double pop = 0; int alive = 0, love = 0, denial = 0, rebel = 0;
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            alive++; pop += g.Pop;
            if (g.Attitude == Attitude.Love) love++;
            if (g.Attitude is Attitude.Denial or Attitude.Nihilism) denial++;
            if (g.Attitude == Attitude.Rebellion) rebel++;
        }
        double peak = Math.Max(1, PeakPopulation(w));
        int acts = 0;
        foreach (var it in w.AppliedLog) if (it.Year >= 0) acts++;
        int kept = 0, broken = 0;
        foreach (var o in w.Oaths) { if (o.Status == 1) kept++; if (o.Status == -1) broken++; }
        bool contact = false, freedom = false, nuclear = false;
        foreach (var e in w.Events.Log)
        {
            if (e.Type == EventType.ContactAnswered && e.B == 0) contact = true;
            if (e.Type == EventType.FreedomDemanded || e.Type == EventType.ContactAnswered && e.B == 1) freedom = true;
            if (e.Type == EventType.NuclearWar) nuclear = true;
        }
        string style = Simulation.StyleTitle(w.Style == CreatorStyle.Unknown ? CreatorStyle.Silent : w.Style);
        var facts = new List<string>
        {
            $"Лет истории: {w.Year}; людей сейчас {Number(pop)} (на пике — {Number(peak)}).",
            $"Кардашёв {Math.Max(0, w.Kardashev):0.00}; эпоха — {Technology.EraNames[Math.Clamp(w.WorldEra, 1, 9)]}.",
            $"Ваш стиль: {style}. Деяний после Пробуждения: {acts}. Обетов сдержано {kept}, нарушено {broken}.",
            $"Народов {alive}: любят вас {love}, отрицают {denial}, восстали {rebel}.",
        };
        OutcomeKind kind;
        if (alive == 0 || pop < 0.01 * peak) kind = OutcomeKind.Decline;
        else if (nuclear && pop < 0.3 * peak) kind = OutcomeKind.Ashes;
        else if (w.Kardashev >= 0.999f)
            kind = freedom && contact || freedom && rebel + denial > love ? OutcomeKind.Freedom
                : acts == 0 || denial * 2 > alive ? OutcomeKind.Orphans
                : love * 2 >= alive || love >= denial + rebel ? OutcomeKind.Ascension : OutcomeKind.Orphans;
        else kind = OutcomeKind.Ongoing;
        string w0 = w.WorldName.Native;
        (string title, string epi) = kind switch
        {
            OutcomeKind.Ascension => ("Восхождение",
                $"Мир {w0} достиг первой ступени Кардашёва — и не забыл того, кто зажёг в нём жизнь. Зеркала над планетой ловят свет звезды, а в храмах всё ещё поют Создателю, которого зовут {style}. Дети, взявшие в руки солнце, остались детьми неба."),
            OutcomeKind.Freedom => ("Свобода",
                $"Мир {w0} взял себе всю энергию своей звезды — и потребовал права жить без вас. Их учёные знают, что мир сделан, и спрашивают не «кто ты», а «по какому праву». Отпустить их — последнее, что вы можете для них сделать."),
            OutcomeKind.Orphans => ("Сироты",
                $"Мир {w0} поднялся к звёздам сам. Они объяснили всё — и молнии, и засухи, и дожди — законами природы. Ваше имя осталось в старых сказках, которые читают детям перед сном."),
            OutcomeKind.Ashes => ("Пепел",
                $"Мир {w0} сжёг себя сам. Ядерная зима легла на поля, огни городов погасли. Где-то в горах уцелевшие снова учатся разводить огонь — и снова смотрят в небо."),
            OutcomeKind.Decline => ("Упадок",
                $"От цивилизации мира {w0} почти ничего не осталось. Ветер заметает дороги, по которым когда-то шли войска и караваны."),
            _ => ("История продолжается", $"Мир {w0} ещё в пути. Каким он придёт к звёздам — решат его народы и вы."),
        };
        return new WorldOutcome(kind, title, epi, facts);
    }
}
