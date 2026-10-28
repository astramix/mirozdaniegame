using System.Text;
using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Language;
using Mirozdanie.Core.Life;
using Mirozdanie.Core.Random;
using Mirozdanie.Core.Sim;
using static Mirozdanie.Core.Chronicle.Russian;

namespace Mirozdanie.Core.Chronicle;

/// <summary>
/// Хроника мира: читаемая летопись на русском из журнала событий и итогового состояния.
/// Отбор и шаблоны — docs/design/interventions-chronicle.md.
/// </summary>
public sealed class ChronicleWriter
{
    private readonly World _w;
    private readonly Rng _rng;
    private readonly StringBuilder _sb = new(200_000);
    private readonly bool[] _groupGlossed, _religionGlossed, _creatorGlossed, _polityGlossed;
    private readonly List<(string native, string gloss, string kind)> _glossary = new();
    public int MaxEntriesPerCentury = 45;
    private int _anomaliesShown;

    public ChronicleWriter(World world, int capacity = 0)
    {
        _w = world;
        _rng = Rng.Derive(world.Seed, (ulong)StreamId.Chronicle);
        // capacity > 0 — запас для ленты живого мира, где сущности ещё появляются.
        _groupGlossed = new bool[Math.Max(capacity, world.Groups.Count)];
        _religionGlossed = new bool[Math.Max(capacity, world.Religions.Count)];
        _creatorGlossed = new bool[Math.Max(capacity * 2, world.Religions.Count + world.Groups.Count)];
        _polityGlossed = new bool[Math.Max(capacity, world.Polities.Count)];
    }

    // ---------- Подписи сущностей ----------

    private void Gloss(Name n, string kind)
    {
        if (string.IsNullOrEmpty(n.Native)) return;
        foreach (var e in _glossary) if (e.native == n.Native && e.kind == kind) return;
        _glossary.Add((n.Native, n.Gloss, kind));
    }

    private string G(int id)
    {
        if (id < 0 || id >= _w.Groups.Count) return "безымянный народ";
        var g = _w.Groups[id];
        Gloss(g.Name, "народ");
        if (_groupGlossed[id]) return $"народ {g.Name.Native}";
        _groupGlossed[id] = true;
        return $"народ {g.Name.Native} («{g.Name.Gloss}»)";
    }

    private string GName(int id) => id >= 0 && id < _w.Groups.Count ? _w.Groups[id].Name.Native : "?";

    private string R(int id)
    {
        if (id < 0 || id >= _w.Religions.Count) return "без веры";
        var r = _w.Religions[id];
        Gloss(r.Name, r.IsCult && !r.Converted ? "культ" : "вера");
        string head = r.Form == DeityForm.None ? "учение" : r.IsCult && !r.Converted ? "культ" : "вера";
        if (_religionGlossed[id]) return $"{head} {r.Name.Native}";
        _religionGlossed[id] = true;
        return $"{head} {r.Name.Native} («{r.Name.Gloss}»)";
    }

    /// <summary>Вера в винительном падеже: «принял веру X», «ушли в культ Y».</summary>
    private string RAcc(int id)
    {
        var r = R(id);
        return r.StartsWith("вера ") ? "веру " + r[5..] : r;
    }

    private string RGen(int id)
    {
        var r = R(id);
        return r.StartsWith("вера ") ? "веры " + r[5..] : r.StartsWith("культ ") ? "культа " + r[6..] : r.StartsWith("учение ") ? "учения " + r[7..] : r;
    }

    private string CreatorOfReligion(int rid)
    {
        if (rid < 0 || rid >= _w.Religions.Count) return "Создатель";
        var r = _w.Religions[rid];
        Gloss(r.CreatorName, "имя Создателя");
        if (_creatorGlossed[rid]) return r.CreatorName.Native;
        _creatorGlossed[rid] = true;
        return $"{r.CreatorName.Native} («{r.CreatorName.Gloss}»)";
    }

    /// <summary>Как эта группа называет Создателя (по её вере или последнему эпитету).</summary>
    private string CreatorFor(Group g)
    {
        if (g.ReligionId >= 0 && _w.Religions[g.ReligionId].Form != DeityForm.None) return CreatorOfReligion(g.ReligionId);
        if (g.Epithets.Count > 0)
        {
            var ep = g.Epithets[0].Name;
            Gloss(ep, "имя Создателя");
            int slot = _w.Religions.Count + g.Id;
            if (_creatorGlossed[slot]) return ep.Native;
            _creatorGlossed[slot] = true;
            return $"{ep.Native} («{ep.Gloss}»)";
        }
        return "неведомая сила";
    }

    private string P(int id)
    {
        if (id < 0 || id >= _w.Polities.Count) return "держава";
        var p = _w.Polities[id];
        Gloss(p.Name, "держава");
        if (_polityGlossed[id]) return $"держава {p.Name.Native}";
        _polityGlossed[id] = true;
        return $"держава {p.Name.Native} («{p.Name.Gloss}»)";
    }

    private string PAcc(int id)
    {
        var p = P(id);
        return p.StartsWith("держава ") ? "державу " + p[8..] : p;
    }

    private string City(int id)
    {
        if (id < 0 || id >= _w.Cities.Count) return "город";
        var c = _w.Cities[id];
        Gloss(c.Name, "город");
        return c.Name.Native;
    }

    private static string FormInstr(GovForm f) => f switch
    {
        GovForm.Chiefdom => "вождеством", GovForm.Kingdom => "царством", GovForm.Theocracy => "теократией", GovForm.Empire => "империей",
        GovForm.Republic => "республикой", GovForm.Federation => "федерацией", GovForm.Ideocracy => "идеократией",
        GovForm.Democracy => "народовластием", _ => "технократией",
    };

    private static string FormPhrase(GovForm f) => f switch
    {
        GovForm.Chiefdom => "власть снова у вождей родов",
        GovForm.Kingdom => "власть перешла к одному дому и передаётся от отца к сыну",
        GovForm.Theocracy => "правит тот, кто ближе к небу, и закон теперь — слово веры",
        GovForm.Empire => "под одной рукой собраны народы разных языков",
        GovForm.Republic => "власть у собрания богатых домов, и правителя выбирают на срок",
        GovForm.Federation => "земли договорились жить по общему закону",
        GovForm.Ideocracy => "правит учение, и несогласных не терпят",
        GovForm.Democracy => "правителя выбирает весь народ",
        _ => "решения принимают учёные и машины, что считают быстрее людей",
    };

    private string PolityShort(int id) => id >= 0 && id < _w.Polities.Count ? _w.Polities[id].Name.Native : "?";

    private string Person(int id)
    {
        if (id < 0 || id >= _w.People.Count) return "некто";
        var p = _w.People[id];
        Gloss(p.Name, "имя");
        return p.Name.Native;
    }

    private string SpeciesName(int id)
    {
        var n = _w.SpeciesName(_w.Life.Species[id]);
        Gloss(n, "вид");
        return $"{n.Native} («{n.Gloss}»)";
    }

    private string LangName(int id)
    {
        var L = _w.Languages[id];
        if (L.FounderGroup >= 0) return $"язык народа {GName(L.FounderGroup)}";
        return "праязык";
    }

    // ---------- Существительные событий ----------

    private readonly record struct Noun(string Nom, string Acc, string Prep, string Gen, string Where = "явился знак");

    private static Noun EventNoun(EventType t, bool won = true) => t switch
    {
        EventType.NaturalDrought or EventType.Drought => new("засуха", "засуху", "засухе", "засухи", "началась засуха"),
        EventType.Rain => new("дождь", "дождь", "дожде", "дождя", "пролился нежданный дождь"),
        EventType.Bounty => new("обильный год", "обильный год", "обильном годе", "обильного года", "выдался обильный год"),
        EventType.DroughtEnded => new("возвращение дождей", "возвращение дождей", "возвращении дождей", "возвращения дождей", "вернулись дожди"),
        EventType.LightningStrike => new("молния", "молнию", "молнии", "молнии", "ударила молния"),
        EventType.MiracleHarvest => new("небывалый урожай", "небывалый урожай", "небывалом урожае", "небывалого урожая", "вырос небывалый урожай"),
        EventType.MeteorImpact => new("огонь с неба", "огонь с неба", "огне с неба", "огня с неба", "упал огонь с неба"),
        EventType.Earthquake => new("землетрясение", "землетрясение", "землетрясении", "землетрясения", "дрогнула земля"),
        EventType.Comet => new("хвостатая звезда", "хвостатую звезду", "хвостатой звезде", "хвостатой звезды"),
        EventType.Famine => new("голод", "голод", "голоде", "голода"),
        EventType.Plague => new("поветрие", "поветрие", "поветрии", "поветрия"),
        EventType.Battle => won ? new("победа", "победу", "победе", "победы") : new("поражение", "поражение", "поражении", "поражения"),
        EventType.MegafaunaGone => new("исчезновение великих зверей", "исчезновение великих зверей", "исчезновении великих зверей", "исчезновения великих зверей"),
        EventType.Vision => new("видение", "видение", "видении", "видения", "пророку было видение"),
        EventType.Healing => new("исцеление", "исцеление", "исцелении", "исцеления", "больные встали"),
        EventType.Inspiration => new("озарение", "озарение", "озарении", "озарения", "мудрецов осенило"),
        EventType.Blessing => new("благословение", "благословение", "благословении", "благословения", "сошло благословение"),
        EventType.ColdSnap => new("долгая зима", "долгую зиму", "долгой зиме", "долгой зимы", "пришла долгая зима"),
        EventType.Flood => new("потоп", "потоп", "потопе", "потопа", "реки вышли из берегов"),
        EventType.HerdsReturn => new("возвращение стад", "возвращение стад", "возвращении стад", "возвращения стад", "вернулись стада"),
        EventType.RelicAppeared => new("сияющий камень", "сияющий камень", "сияющем камне", "сияющего камня", "явился сияющий камень"),
        EventType.CursedGround => new("проклятие земли", "проклятие земли", "проклятии земли", "проклятия земли", "земля зачахла"),
        EventType.DynastyBlessed => new("благословение династии", "благословение династии", "благословении династии", "благословения династии", "правителей осенило небо"),
        EventType.DynastyCursed => new("проклятие династии", "проклятие династии", "проклятии династии", "проклятия династии", "небо отвернулось от правителей"),
        EventType.PeaceImposed => new("нежданный мир", "нежданный мир", "нежданном мире", "нежданного мира", "оружие легло на землю"),
        EventType.CreatorAppeared => new("явление Создателя", "явление Создателя", "явлении Создателя", "явления Создателя", "явился Создатель"),
        EventType.Threat => new("грозный знак", "грозный знак", "грозном знаке", "грозного знака", "был грозный знак"),
        EventType.PrayerAnswered => new("ответ на молитву", "ответ на молитву", "ответе на молитву", "ответа на молитву", "молитва была услышана"),
        EventType.Terraformed => new("перемена земли", "перемену земли", "перемене земли", "перемены земли", "земля переменила облик"),
        EventType.RiverBorn => new("новая река", "новую реку", "новой реке", "новой реки", "забил родник и потекла река"),
        EventType.Eruption => new("огненная гора", "огненную гору", "огненной горе", "огненной горы", "разверзлась огненная гора"),
        EventType.SeaLevelChanged => new("великая вода", "великую воду", "великой воде", "великой воды", "море пришло в движение"),
        EventType.ClimateShifted => new("перемена неба", "перемену неба", "перемене неба", "перемены неба", "небо переменилось"),
        EventType.ContinentDrifted => new("движение земли", "движение земли", "движении земли", "движения земли", "сдвинулась земля"),
        EventType.Refugees => new("бегство от воды", "бегство от воды", "бегстве от воды", "бегства от воды", "народ бежал от воды"),
        _ => new("знак", "знак", "знаке", "знака"),
    };

    private static string AttitudePhrase(Attitude a) => a switch
    {
        Attitude.Love => "любит Создателя",
        Attitude.Fear => "боится Создателя и умилостивляет его",
        Attitude.Bargain => "торгуется с Создателем: молитва за урожай",
        Attitude.Indifference => "равнодушен к Создателю",
        Attitude.Denial => "отрицает, что Создатель существует",
        Attitude.Rebellion => "восстал против Создателя и зовёт его тираном",
        _ => "отверг всё — и богов, и сам смысл мира",
    };

    public static string AttitudeName(Attitude a) => a switch
    {
        Attitude.Love => "любовь", Attitude.Fear => "страх", Attitude.Bargain => "сделка", Attitude.Indifference => "равнодушие",
        Attitude.Denial => "отрицание", Attitude.Rebellion => "мятеж", _ => "нигилизм",
    };

    public static string FormName(DeityForm f) => f switch
    {
        DeityForm.OneCreator => "единый Создатель", DeityForm.Pantheon => "пантеон духов", DeityForm.Ancestors => "духи предков",
        DeityForm.Nature => "обожествлённая природа", _ => "без богов",
    };

    private static string MessagePhrase(string m) => m switch
    {
        "peace" => "что Создатель запрещает проливать кровь",
        "war" => "что Создатель требует меча и победы",
        "love" => "что Создатель любит всех живущих",
        "wrath" => "что Создатель гневен и ждёт покаяния",
        "knowledge" => "что Создатель велит познавать устройство мира",
        "obedience" => "что Создатель требует покорности и обрядов",
        "freedom" => "что никто не должен служить ни царю, ни богу",
        _ => "нечто, чего никто не смог пересказать",
    };

    private string GuiltReason(Group g, Rng rng)
    {
        string[] reasons = g.Culture[CultureTrait.Aggression] > 0.55f
            ? new[] { "пролитую кровь", "набеги на соседей", "гордыню своих вождей" }
            : g.Belief[Doctrine.Ritual] > 0.55f
                ? new[] { "забытые обряды", "скудные жертвы", "нечистоту святилищ" }
                : new[] { "свои раздоры", "ложь старейшин", "нарушенные клятвы" };
        return reasons[rng.NextInt(reasons.Length)];
    }

    // ---------- Значимость ----------

    private float Weight(WorldEvent e)
    {
        float m = Math.Max(0.1f, e.Magnitude);
        float wgt = e.Type switch
        {
            EventType.Intervention => 100f,
            EventType.AnomalyNoticed => 90f,
            EventType.ReligionFounded => 30f,
            EventType.Schism => 35f,
            EventType.Martyrdom => 30f,
            EventType.ProphetBorn => e.ByCreator ? 40f : 14f,
            EventType.CultFounded => 12f,
            EventType.PolityFounded => 22f,
            EventType.HolyWar => 26f,
            EventType.WarDeclared => 0f, // битва несёт всю информацию
            EventType.Battle => 10f + 10f * m,
            EventType.PolitySplit => 18f,
            EventType.Collapse => 18f,
            EventType.Revolt => 10f,
            EventType.LeaderStruck => 25f,
            EventType.MeteorImpact => 50f,
            EventType.Interpretation => (e.ByCreator ? 20f : 4f) * m,
            EventType.EpithetGained => e.B == (int)EpithetKind.Silent ? 22f : 6f,
            EventType.SacredSiteFounded => 9f,
            EventType.AttitudeShift => 7f * m + ((Attitude)e.B is Attitude.Rebellion or Attitude.Nihilism or Attitude.Denial ? 8f : 0f),
            EventType.Conversion => 6f * m,
            EventType.LanguageSplit => 10f,
            EventType.Discovery => e.C >= 0 ? 1.5f : 6f + 2.5f * Technology.Get((Tech)e.B).Era,
            EventType.EraBegan => 60f,
            EventType.EnergyCrisis => 14f,
            EventType.ClimateWarming => 30f,
            EventType.NuclearWar => 120f,
            EventType.MegaprojectStarted => 40f,
            EventType.MegaprojectCompleted => 60f,
            EventType.AgricultureAdopted => 5f,
            EventType.MegafaunaGone => 10f,
            EventType.Plague => e.Cause >= 0 ? 2f + 4f * m : 6f + 10f * m, // занесённое по торговому пути — мельче
            EventType.PrayerUnanswered => 6f * m + 2f,
            EventType.Earthquake => 3f,
            EventType.Comet => 3f,
            EventType.TradeRoute => 3f,
            EventType.WordBorrowed => 1.5f,
            EventType.Famine => 2f * m,
            EventType.NaturalDrought => 1.5f * m,
            EventType.GroupDestroyed => 6f,
            EventType.CultFaded => 3f * m,
            EventType.KardashevMilestone => e.Magnitude >= 0.99f ? 150f : 8f + 20f * e.Magnitude,
            EventType.ProphecyMade => 14f,
            EventType.ProphecyFulfilled => 28f,
            EventType.ProphecyFailed => 22f,
            EventType.MandateLost => 16f,
            EventType.PrayerAnswered => 30f,
            EventType.CreatorAppeared => 40f,
            EventType.DemandMet => 12f,
            EventType.DemandRefused => 18f,
            EventType.Teaching => 14f,
            EventType.Forgetting => 16f,
            EventType.Healing or EventType.Inspiration or EventType.Blessing or EventType.ColdSnap or EventType.Flood or EventType.HerdsReturn
                or EventType.RelicAppeared or EventType.CursedGround or EventType.DynastyBlessed or EventType.DynastyCursed or EventType.PeaceImposed or EventType.Threat => 10f,
            EventType.Terraformed => 6f + 12f * m,
            EventType.RiverBorn => 12f,
            EventType.Eruption => 25f,
            EventType.SeaLevelChanged => 35f,
            EventType.ClimateShifted => 30f,
            EventType.ContinentDrifted => 40f,
            EventType.GroupDrowned => 30f,
            EventType.Refugees => 10f + 10f * m,
            EventType.LandSank => 4f,
            EventType.LeaderRose => e.C == 1 ? 4f : 0f,
            EventType.CityFounded => 3f,
            EventType.CityGrew => e.B >= 3 ? (e.C <= 3 ? 25f : 5f) : 4f, // первые мегаполисы мира — веха, дальше — обыденность
            EventType.GovernmentChanged => (GovForm)e.B is GovForm.Kingdom or GovForm.Chiefdom ? 4f : 12f,
            EventType.Assimilated => 2f,
            EventType.ArmyMarched => 0f, // битва несёт всю информацию
            EventType.PeaceMade => 4f,
            EventType.TreatyBroken => 18f,
            EventType.AllianceFormed => e.C == 1 ? 45f : 10f,
            EventType.PetitionMade => 5f,
            EventType.PetitionGranted => 10f,
            EventType.PetitionIgnored => 8f,
            EventType.OathSworn => 12f,
            EventType.OathKept => 25f,
            EventType.OathBroken => 35f,
            EventType.StyleNamed => 40f,
            EventType.SimulationSuspected => 45f,
            EventType.ProbeLaunched => e.B == 1 ? 80f : e.B == 2 ? 60f : 50f,
            EventType.GeoglyphMade => 40f,
            EventType.LightMessage => 35f,
            EventType.ShieldRaised => 45f,
            EventType.FreedomDemanded => 70f,
            EventType.ContactAnswered => 60f,
            EventType.CreatorWounded => 70f,
            EventType.Annexation => e.C == 1 ? 3f : 0f,
            _ => 0f,
        };
        if (e.ByCreator && e.Type != EventType.Interpretation) wgt *= 3f;
        return wgt;
    }

    // ---------- Основной вывод ----------

    public string Write()
    {
        var w = _w;
        var sp = w.SapientSpecies >= 0 ? w.Life.Species[w.SapientSpecies] : null;
        Gloss(w.WorldName, "мир");
        _sb.AppendLine($"# Хроника мира {w.WorldName.Native}");
        _sb.AppendLine();
        _sb.AppendLine($"*«{w.WorldName.Gloss}» — так назвали свою землю первые разумные. Сид {w.Seed}, законы мира: «{w.Params.PresetTitle}»" +
                       (w.ScenarioName != null ? $", сценарий Создателя: «{w.ScenarioName}»" : ", Создатель не вмешивался по сценарию") +
                       $". Летопись охватывает {Years(w.Year)} после Пробуждения.*");
        _sb.AppendLine();
        Creation();
        LifeEra();
        AwakeningSection(sp);
        History();
        CreatorNames();
        Faiths();
        Languages();
        Polities();
        Summary();
        Glossary();
        return _sb.ToString();
    }

    private void Creation()
    {
        var w = _w; var p = w.Planet;
        var L = w.ProtoLanguage >= 0 ? w.Languages[w.ProtoLanguage] : null;
        _sb.AppendLine("## Сотворение");
        _sb.AppendLine();
        int land = 0; foreach (var e in p.Elevation) if (e >= 0) land++;
        float ocean = 1f - land / (float)p.Elevation.Length;
        string continents = p.ContinentCount switch { 0 => "без больших материков, россыпью островов", 1 => "с одним великим материком", _ => $"с {p.ContinentCount} {Plural(p.ContinentCount, "материком", "материками", "материками")}" };
        _sb.Append($"Мир родился {continents}; океан покрыл {Percent(ocean)} его лица. ");
        float hi = p.Elevation[p.HighestCell];
        if (L != null)
        {
            var mount = L.MakeName(L.Compound(Concept.Great, Concept.Mountain), "великая гора");
            Gloss(mount, "место");
            _sb.Append($"Высочайшая вершина, которую потом назовут {mount.Native} («{mount.Gloss}»), поднялась на {hi:0.0} км. ".Replace('.', ',').Replace(", км", " км").Replace(" км, ", " км. "));
            Concept[] riverWords = { Concept.Mother, Concept.Old, Concept.Great };
            string[] riverGloss = { "мать-река", "старая река", "великая река" };
            var names = new List<string>();
            for (int i = 0; i < p.GreatRivers.Length; i++)
            {
                var rn = L.MakeName(L.Compound(riverWords[i % 3], Concept.River), riverGloss[i % 3]);
                Gloss(rn, "река");
                names.Add($"{rn.Native} («{rn.Gloss}»)");
            }
            if (names.Count > 0) _sb.Append($"Великие реки: {List(names)}. ");
        }
        _sb.AppendLine($"Средняя температура мира — {w.Params.MeanTemperature:0} °C, наклон оси — {w.Params.AxialTilt:0}°.");
        _sb.AppendLine();
        // Лепка мира до жизни (terraforming.md, §1).
        int strokes = 0, spells = 0;
        var forms = new List<string>();
        foreach (var e in w.Events.Log)
        {
            if (e.Type == EventType.LifeBegan) break;
            if (e.Type != EventType.Intervention || !Intervention.IsTerraform((InterventionType)e.A)) continue;
            if ((InterventionType)e.A == InterventionType.Sculpt) strokes++;
            else { spells++; string t = Creator.Spellbook.Get((InterventionType)e.A).Title.ToLowerInvariant(); if (!forms.Contains(t)) forms.Add(t); }
        }
        if (strokes + spells > 0)
        {
            _sb.AppendLine($"*Прежде чем зародилась жизнь, Создатель лепил этот мир своими руками: {strokes} {Plural(strokes, "движение", "движения", "движений")} кисти" +
                           (forms.Count > 0 ? $"; {List(forms.Take(6).ToList())}" : "") + ". Таким его и застала первая жизнь.*");
            _sb.AppendLine();
        }
    }

    private string LifeDate(long tick) => _w.AwakeningTick - tick <= 0 ? "Накануне Пробуждения" : $"За {(_w.AwakeningTick - tick)} тыс. лет до Пробуждения";

    private void LifeEra()
    {
        var w = _w;
        _sb.AppendLine("## Эпоха жизни");
        _sb.AppendLine();
        int lines = 0;
        foreach (var e in w.Events.Log)
        {
            if (e.Type == EventType.Awakening) break;
            switch (e.Type)
            {
                case EventType.LifeBegan:
                    _sb.AppendLine($"- **{LifeDate(0)}.** В тёплых водах и на влажных берегах зашевелилась жизнь: {Plural(e.A, "первый вид", "первых вида", "первых видов")} — " +
                                   $"{List(Enumerable.Range(0, Math.Min(3, w.Life.Species.Count)).Select(SpeciesName).ToList())}.");
                    lines++;
                    break;
                case EventType.SpeciesBorn when lines < 28:
                {
                    var s = w.Life.Species[e.A];
                    _sb.AppendLine($"- **{LifeDate(e.Year)}.** От вида {SpeciesName(e.B)} отделился новый — {SpeciesName(e.A)}: {Describe(s)}.");
                    lines++;
                    break;
                }
                case EventType.SpeciesExtinct when lines < 28 && e.Year < w.AwakeningTick:
                    _sb.AppendLine($"- **{LifeDate(e.Year)}.** Вымер вид {SpeciesName(e.A)}.");
                    lines++;
                    break;
                case EventType.MeteorImpact:
                    _sb.AppendLine($"- **{LifeDate(e.Year)}.** *Создатель обрушил на мир огненный камень.* Жизнь вокруг места удара сгорела; холод держался долгие века.");
                    lines++;
                    break;
                case EventType.Spark:
                    _sb.AppendLine($"- **{LifeDate(e.Year)}.** *Создатель коснулся разума вида {SpeciesName(e.A)}.*");
                    lines++;
                    break;
                case EventType.Terraformed or EventType.RiverBorn or EventType.Eruption or EventType.SeaLevelChanged or EventType.ClimateShifted
                    or EventType.ContinentDrifted when e.ByCreator && e.Year > 0:
                    _sb.AppendLine($"- **{LifeDate(e.Year)}.** *Создатель изменил облик мира:* {LifeTerraform(e)}");
                    lines++;
                    break;
                case EventType.LandSank when e.Year > 0:
                    _sb.AppendLine($"- **{LifeDate(e.Year)}.** Море поглотило землю, а с ней — {Russian.Number(e.A)} зверей.");
                    lines++;
                    break;
            }
        }
        _sb.AppendLine();
    }

    private static string LifeTerraform(WorldEvent e) => e.Type switch
    {
        EventType.RiverBorn => "потекла новая река.",
        EventType.Eruption => "разверзлась огненная гора, и пепел на века закрыл солнце.",
        EventType.SeaLevelChanged => e.A > 0 ? "море поднялось и залило низины." : "море отступило, обнажив шельфы.",
        EventType.ClimateShifted => e.A > 0 ? "мир потеплел." : "мир остыл.",
        EventType.ContinentDrifted => "материк сдвинулся.",
        _ => (TerraformKind)e.A switch
        {
            TerraformKind.Mountains => "выросли горы.",
            TerraformKind.Island => "из моря поднялся остров.",
            TerraformKind.Bridge => "встал перешеек, соединив берега.",
            TerraformKind.Strait => "море рассекло сушу.",
            TerraformKind.Sink => "земля ушла под воду.",
            _ => "земля переменила облик.",
        },
    };

    private static string Describe(Species s)
    {
        var g = s.Genome;
        float kg = Genome.MassKg(g[Trait.Mass]);
        string mass = kg < 1 ? $"{kg * 1000:0} г" : kg < 1000 ? $"{kg:0} кг" : $"{kg / 1000:0.#} т";
        string diet = g[Trait.Diet] < 0.33f ? "травоядный" : g[Trait.Diet] < 0.66f ? "всеядный" : "хищник";
        string soc = g[Trait.Sociality] > 0.6f ? ", живёт стаями" : g[Trait.Sociality] < 0.3f ? ", одиночка" : "";
        string cog = g[Trait.Cognition] > 0.5f ? ", смышлёный" : "";
        string climate = g[Trait.ThermalOpt] < 5 ? ", любит холод" : g[Trait.ThermalOpt] > 22 ? ", любит зной" : "";
        return $"{diet} весом около {mass.Replace('.', ',')}{soc}{cog}{climate}";
    }

    private void AwakeningSection(Species? sp)
    {
        var w = _w;
        _sb.AppendLine("## Пробуждение");
        _sb.AppendLine();
        if (sp == null || w.ProtoLanguage < 0) { _sb.AppendLine("Разум так и не пробудился."); _sb.AppendLine(); return; }
        var L = w.ProtoSnapshot ?? w.Languages[w.ProtoLanguage];
        bool forced = w.Events.Log.Any(e => e.Type == EventType.Awakening && (e.Flags & EventFlags.Crisis) != 0);
        _sb.Append($"Через {w.AwakeningTick} тыс. лет после зарождения жизни вид {SpeciesName(sp.Id)} ");
        _sb.AppendLine(forced
            ? "пробудился поздно и трудно: разум пришёл к нему не от лёгкой жизни, а от долгого отбора в суровых краях."
            : "пересёк порог разума: стая стала общиной, а крик — словом.");
        _sb.AppendLine();
        var groups = w.Groups.Where(g => g.Founded == 0 && g.Parent < 0).Select(g => G(g.Id)).ToList();
        _sb.AppendLine($"Первые общины: {List(groups)}.");
        _sb.AppendLine();
        var cons = L.Consonants().Select(c => Phonemes.All[c].Cyr).ToList();
        var vows = L.Vowels().Select(c => Phonemes.All[c].Cyr).ToList();
        var gn = sp.Genome;
        var anatomy = new List<string>
        {
            gn[Trait.Lips] < 0.3f ? "без подвижных губ — в речи нет губных звуков" : gn[Trait.Lips] > 0.7f ? "с подвижными губами" : "с обычными губами",
            gn[Trait.TongueAgility] < 0.4f ? "с неповоротливым языком" : gn[Trait.TongueAgility] > 0.7f ? "с гибким языком" : "с умеренно гибким языком",
            gn[Trait.LarynxDepth] < 0.4f ? "с высокой гортанью (всего три гласных)" : "с глубокой гортанью",
            gn[Trait.Nasality] > 0.6f ? "с сильным носовым резонансом" : gn[Trait.Nasality] < 0.2f ? "почти без носовых звуков" : "",
        };
        anatomy.RemoveAll(string.IsNullOrEmpty);
        _sb.AppendLine($"Пробудившийся вид — {Describe(sp)}; речевой аппарат: {string.Join(", ", anatomy)}.");
        _sb.AppendLine();
        _sb.AppendLine($"Праязык: {cons.Count} {Plural(cons.Count, "согласный", "согласных", "согласных")} ({string.Join(" ", cons)}) и {vows.Count} {Plural(vows.Count, "гласный", "гласных", "гласных")} ({string.Join(" ", vows)}); " +
                       $"порядок слов {L.Order}, ударение {(L.Stress switch { Stress.Initial => "на первом слоге", Stress.Final => "на последнем слоге", _ => "на предпоследнем слоге" })}" +
                       $", {(L.ModifierFirst ? "определение перед определяемым" : "определение после определяемого")}.");
        _sb.AppendLine();
        _sb.AppendLine("| Понятие | Слово праязыка |");
        _sb.AppendLine("|---|---|");
        foreach (var c in new[] { Concept.Water, Concept.Fire, Concept.Sun, Concept.People, Concept.God, Concept.Death, Concept.Mother, Concept.Star })
            _sb.AppendLine($"| {Concepts.Russian[(int)c]} | {L.Cyr(c, false)} |");
        _sb.AppendLine();
    }

    // ---------- Века истории ----------

    private void History()
    {
        var w = _w;
        var log = w.Events.Log;
        int start = 0;
        for (int i = 0; i < log.Count; i++) if (log[i].Type == EventType.Awakening) { start = i + 1; break; }
        long lastYear = w.Year;
        int centuries = (int)(lastYear / 100) + 1;
        // Индекс событий по причинам: для цепочек «вмешательство → толкования».
        var byCause = new List<int>[log.Count];
        for (int i = start; i < log.Count; i++)
        {
            int c = log[i].Cause;
            if (c >= 0) (byCause[c] ??= new List<int>()).Add(i);
        }
        int idx = start;
        for (int cen = 0; cen < centuries; cen++)
        {
            long from = cen * 100 + (cen == 0 ? 0 : 1), to = (cen + 1) * 100;
            var entries = new List<int>();
            int first = idx;
            while (idx < log.Count && log[idx].Year <= to) idx++;
            if (idx == first) continue;
            var mandatory = new bool[idx - first];
            // Вмешательства и их прямые последствия — всегда.
            for (int i = first; i < idx; i++)
            {
                var e = log[i];
                if (e.Type is EventType.Intervention or EventType.AnomalyNoticed or EventType.Schism or EventType.ReligionFounded or EventType.Martyrdom
                    || e.Type == EventType.ProphetBorn && e.ByCreator || e.Type == EventType.EpithetGained && e.B == (int)EpithetKind.Silent)
                    mandatory[i - first] = true;
            }
            var scored = new List<(float score, int i)>();
            for (int i = first; i < idx; i++)
            {
                var e = log[i];
                if (mandatory[i - first]) continue;
                float s = Weight(e);
                if (s <= 0) continue;
                // Толкования вмешательств показываем в блоке вмешательства.
                if (e.Type == EventType.Interpretation && e.ByCreator) continue;
                if (e.Type is EventType.Drought or EventType.Rain or EventType.MiracleHarvest or EventType.Vision or EventType.Spark or EventType.CreatorEnergyLow
                    || e.Type == EventType.LightningStrike && e.ByCreator || e.Type == EventType.MeteorImpact && e.ByCreator
                    || e.ByCreator && e.Type is EventType.Terraformed or EventType.RiverBorn or EventType.SeaLevelChanged or EventType.ClimateShifted
                        or EventType.ContinentDrifted or EventType.Eruption) continue;
                s *= 0.8f + 0.4f * _rng.NextFloat();
                scored.Add((s, i));
            }
            scored.Sort((a, b) => { int c = b.score.CompareTo(a.score); return c != 0 ? c : a.i.CompareTo(b.i); });
            int mandatoryCount = mandatory.Count(x => x);
            int budget = Math.Max(8, MaxEntriesPerCentury - mandatoryCount);
            for (int i = first; i < idx; i++) if (mandatory[i - first]) entries.Add(i);
            var used = new int[(int)EventType.Count];
            foreach (var (score, i) in scored)
            {
                if (budget <= 0) break;
                var t = log[i].Type;
                if (used[(int)t] >= Cap(t)) continue;
                used[(int)t]++;
                entries.Add(i);
                budget--;
            }
            entries.Sort();
            if (entries.Count == 0) continue;
            _sb.AppendLine($"## {RomanCentury(cen + 1)} век после Пробуждения (годы {from}–{Math.Min(to, lastYear)})");
            _sb.AppendLine();
            var shown = new HashSet<int>(); // только проверка принадлежности
            for (int k = 0; k < entries.Count; k++)
            {
                int i = entries[k];
                if (shown.Contains(i)) continue;
                shown.Add(i);
                string? line = Render(log[i], byCause, shown);
                if (line == null) continue;
                // Свёртка однотипных строк года (lore.md §7.2): «так же — ещё у народов …».
                if (Foldable(log[i].Type))
                {
                    var same = new List<string>();
                    for (int k2 = k + 1; k2 < entries.Count; k2++)
                    {
                        var o = log[entries[k2]];
                        if (o.Year != log[i].Year) break;
                        if (shown.Contains(entries[k2]) || !SameShape(log[i], o)) continue;
                        shown.Add(entries[k2]);
                        if (o.Type == EventType.CityGrew) { if (o.A >= 0 && o.A < w.Cities.Count) same.Add(w.Cities[o.A].Name.Native); }
                        else if (o.Type == EventType.GovernmentChanged) { if (o.A >= 0 && o.A < w.Polities.Count) same.Add(w.Polities[o.A].Name.Native); }
                        else if (o.A >= 0 && o.A < w.Groups.Count) same.Add(w.Groups[o.A].Name.Native);
                    }
                    if (same.Count > 0)
                        line += log[i].Type is EventType.CityGrew or EventType.GovernmentChanged
                            ? $" *Так же — ещё {same.Count} {(log[i].Type == EventType.CityGrew ? Plural(same.Count, "город", "города", "городов") : Plural(same.Count, "держава", "державы", "держав"))}: {List(same.Take(6).ToList())}{(same.Count > 6 ? " и другие" : "")}.*"
                            : $" *Так же — ещё {same.Count} {Plural(same.Count, "народ", "народа", "народов")}: {List(same.Take(6).ToList())}{(same.Count > 6 ? " и другие" : "")}.*";
                }
                _sb.AppendLine(line);
            }
            _sb.AppendLine();
        }
    }

    /// <summary>Сколько событий одного типа показывать за век (иначе Хроника тонет в повторах).</summary>
    private static int Cap(EventType t) => t switch
    {
        EventType.Interpretation => 6,
        EventType.AttitudeShift => 4,
        EventType.EpithetGained => 3,
        EventType.NaturalDrought => 2,
        EventType.Famine => 3,
        EventType.Earthquake => 2,
        EventType.LanguageSplit => 3,
        EventType.Discovery => 4,
        EventType.AgricultureAdopted => 3,
        EventType.Conversion => 4,
        EventType.CultFaded => 2,
        EventType.Comet => 1,
        EventType.SacredSiteFounded => 3,
        EventType.TradeRoute => 2,
        EventType.WordBorrowed => 1,
        EventType.PrayerUnanswered => 3,
        EventType.Plague => 3,
        EventType.LeaderRose => 3,
        EventType.GroupDestroyed => 3,
        EventType.PolitySplit => 4,
        EventType.Revolt => 2,
        EventType.Collapse => 3,
        EventType.Battle => 5,
        EventType.HolyWar => 3,
        EventType.PolityFounded => 5,
        _ => 8,
    };

    private static bool Foldable(EventType t) => t is EventType.Interpretation or EventType.AttitudeShift or EventType.Conversion or EventType.Plague
        or EventType.Famine or EventType.Discovery or EventType.AgricultureAdopted or EventType.EpithetGained or EventType.CityGrew or EventType.Assimilated
        or EventType.PrayerUnanswered or EventType.GovernmentChanged;

    /// <summary>Одинаково построенные строки: тот же тип и те же «смысловые» поля (вид толкования, новая позиция, открытие).</summary>
    private static bool SameShape(WorldEvent a, WorldEvent b) => a.Type == b.Type && a.Type switch
    {
        EventType.Interpretation => a.B == b.B && a.C == b.C,
        EventType.AttitudeShift or EventType.Conversion or EventType.EpithetGained or EventType.GovernmentChanged => a.B == b.B,
        EventType.Discovery => a.B == b.B && (a.C < 0) == (b.C < 0),
        EventType.CityGrew => a.B == b.B,
        _ => true,
    };

    private static string RomanCentury(int n)
    {
        string[] r = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "XIII", "XIV", "XV", "XVI", "XVII", "XVIII", "XIX", "XX" };
        return n < r.Length ? r[n] : n.ToString();
    }

    private string Date(WorldEvent e) => $"- **Год {e.Year}.**";

    private string? CausePrefix(WorldEvent e)
    {
        if (e.Cause < 0) return null;
        var c = _w.Events.Get(e.Cause);
        if (c.Year >= e.Year) return null;
        if (c.Type is EventType.NaturalDrought or EventType.Drought or EventType.MeteorImpact or EventType.Plague or EventType.Battle or EventType.Famine)
            return $"Из-за {EventNoun(c.Type).Gen} {c.Year} года ";
        return null;
    }

    /// <summary>Одна строка Хроники для ленты событий (без цепочек последствий).</summary>
    public string? Line(WorldEvent e)
    {
        if (Weight(e) <= 0 && e.Type != EventType.Intervention) return null;
        return Render(e, Array.Empty<List<int>>(), new HashSet<int>()).Replace("**", "");
    }

    /// <summary>Значимость события для отбора в ленту.</summary>
    public float Significance(WorldEvent e) => Weight(e);

    private string Render(WorldEvent e, List<int>[] byCause, HashSet<int> shown)
    {
        var w = _w;
        string d = Date(e);
        switch (e.Type)
        {
            case EventType.Intervention:
                return RenderIntervention(e, byCause, shown);
            case EventType.Interpretation:
                return $"{d} {Interpretation(e)}";
            case EventType.ReligionFounded:
            {
                var r = w.Religions[e.A];
                var g = w.Groups[e.B];
                string creator = CreatorOfReligion(r.Id);
                string intro = r.Prophet >= 0 ? $"Вокруг пророка {Person(r.Prophet)} {G(g.Id)} сложил свою первую веру" : $"{Capitalize(G(g.Id))} сложил свою первую веру";
                return $"{d} {intro} — {R(r.Id)}. " +
                       $"Создателя они назвали {creator}; образ божества — {FormName(r.Form)}. {DoctrineSentence(r)}";
            }
            case EventType.Schism:
            {
                var nr = w.Religions[e.A];
                string crisis = (e.Flags & EventFlags.Crisis) != 0 ? " Раскол случился в тяжёлые годы, когда голод и войны обострили споры." : "";
                return $"{d} **Раскол.** {Capitalize(R(e.B))} раскололась: {G(e.C)} и единоверцы, чьи убеждения разошлись слишком далеко, ушли в {RAcc(nr.Id)}. " +
                       $"Создателя они зовут {CreatorOfReligion(nr.Id)}. {DoctrineDiff(nr, w.Religions[e.B])}{crisis}";
            }
            case EventType.CultFounded:
            {
                var r = w.Religions[e.A];
                if (r.Rebel) return $"{d} **Мятеж против неба.** В {GPrep(e.B)} мудрец {Person(e.C)} объявил Создателя тираном — {CreatorOfReligion(r.Id)}. Его последователи назвали себя {r.Name.Native} («{r.Name.Gloss}»).";
                if (r.Form == DeityForm.None && r.CreatorName.Gloss == "Пустое небо") return $"{d} В {GPrep(e.B)} мудрец {Person(e.C)} учит, что небо пусто, а засухи и молнии — дело природы. Так родилось {R(r.Id)}.";
                if (r.Form == DeityForm.None) return $"{d} В {GPrep(e.B)} {Person(e.C)} проповедует конец всего: {R(r.Id)} отвергает и богов, и царей, и саму жизнь.";
            {
                string label = R(r.Id);
                string verb = label.StartsWith("вера") ? "возникла" : label.StartsWith("учение") ? "возникло" : "возник";
                return $"{d} В {GPrep(e.B)} пророк {Person(e.C)} собрал последователей: {verb} {label}. {DoctrineDiff(r, r.Parent >= 0 ? w.Religions[r.Parent] : null)}";
            }
            }
            case EventType.ProphetBorn:
                return $"{d} В {GPrep(e.B)} появился пророк {Person(e.A)}.";
            case EventType.Martyrdom:
                return $"{d} **Мученик.** Вожди {GGen(e.B)} казнили пророка {Person(e.A)}. Его смерть стала знаменем: {R(e.C)} стал расти быстрее.";
            case EventType.Conversion:
                return $"{d} {Capitalize(G(e.A))} принял {RAcc(e.B)}" + (e.C >= 0 ? $", оставив {RAcc(e.C)}." : ".");
            case EventType.AttitudeShift:
            {
                var g = w.Groups[e.A];
                string why = (Attitude)e.B switch
                {
                    Attitude.Rebellion => " Обида копилась годами: беды падали на них, а милости — на других.",
                    Attitude.Denial => " Мудрецы всё чаще объясняют знаки неба природой.",
                    Attitude.Nihilism => " Слишком много горя без ответа.",
                    Attitude.Love => " Милости Создателя запомнились крепче бед.",
                    Attitude.Indifference => " Создатель давно не подавал знаков.",
                    _ => "",
                };
                return $"{d} {Capitalize(G(e.A))} теперь {AttitudePhrase((Attitude)e.B)} (прежде — {AttitudeName((Attitude)e.C)}).{why}";
            }
            case EventType.EpithetGained:
            {
                var g = w.Groups[e.A];
                var ep = g.Epithets.FirstOrDefault(x => (int)x.Kind == e.B);
                if (ep.Name.Native == null) return $"{d} {Capitalize(G(e.A))} дал Создателю новое имя.";
                Gloss(ep.Name, "имя Создателя");
                if ((EpithetKind)e.B == EpithetKind.Silent)
                    return $"{d} {Capitalize(G(e.A))} три века не видел знаков неба и стал звать Создателя {ep.Name.Native} — «{ep.Name.Gloss}».";
                return $"{d} {Capitalize(G(e.A))} стал звать Создателя ещё и {ep.Name.Native} («{ep.Name.Gloss}»).";
            }
            case EventType.SacredSiteFounded:
            {
                var s = w.Sites[e.A];
                Gloss(s.Name, "святыня");
                return $"{d} Место, где {EventNoun(s.Origin).Where}, стало святыней {s.Name.Native} («{s.Name.Gloss}»); её чтит {R(e.B)}.";
            }
            case EventType.AnomalyNoticed:
            {
                var g = w.Groups[e.A];
                if (_anomaliesShown++ > 0)
                    return $"{d} Ещё одни мудрецы — {GGen(e.A)} — пришли к той же мысли: знаки неба приходят не по законам природы.";
                string detail = e.C == 1 ? "знаки неба снова и снова били в одни и те же места" : $"{e.B} раз знаки приходили не по законам природы — не в сезон и с ясного неба";
                return $"{d} **Аномалия замечена.** Мудрецы {GGen(e.A)} сопоставили свои записи: {detail}. Впервые прозвучала мысль, что мир не только живёт своими законами, — что его кто-то *делает*.";
            }
            case EventType.PrayerUnanswered:
                return $"{d} {Capitalize(G(e.A))}, голодавший уже {Years(e.B)}, взмолился к {CreatorFor(w.Groups[e.A])}. Ответа не было.";
            case EventType.CultFaded:
            {
                string label = R(e.A);
                string verb = label.StartsWith("вера") ? "угасла" : label.StartsWith("учение") ? "угасло" : "угас";
                return $"{d} {Capitalize(label)} {verb}" + (w.Religions[e.A].IsCult ? ", не пережив своего основателя." : ": не осталось ни одного верного.");
            }
            case EventType.NaturalDrought:
            {
                var names = w.Groups.Where(g => g.Alive && w.RegionOf(g.Cell) == e.A).OrderByDescending(g => g.Pop).ThenBy(g => g.Id).Take(3).Select(g => g.Name.Native).ToList();
                return $"{d} Засуха пришла в земли {(names.Count > 1 ? "народов" : "народа")} {List(names)}.";
            }
            case EventType.Famine:
                return $"{d} {CausePrefix(e) ?? ""}{(CausePrefix(e) != null ? G(e.A) : Capitalize(G(e.A)))} голодает{(e.B > 1 ? $" уже {Years(e.B)}" : "")}.";
            case EventType.Plague:
                return $"{d} {(CausePrefix(e) is { } cp2 ? cp2 + "п" : "П")}оветрие унесло {Percent(e.Magnitude)} людей {GGen(e.A)}.";
            case EventType.Earthquake:
            {
                int owner = w.CellOwner.Length > e.Cell && e.Cell >= 0 ? w.CellOwner[e.Cell] : -1;
                return $"{d} Земля содрогнулась{(owner >= 0 ? $" в землях {GGen(owner)}" : "")}.";
            }
            case EventType.Comet:
                return e.C <= 1 ? $"{d} В небе впервые показалась хвостатая звезда." : $"{d} Хвостатая звезда вернулась — в {e.C}-й раз, через {Years(e.B)}.";
            case EventType.MegafaunaGone:
                return $"{d} Охотники {(e.B >= 0 ? GGen(e.B) : "мира")} истребили последних зверей вида {SpeciesName(e.A)}. Великая охота кончилась.";
            case EventType.GroupDestroyed:
                return $"{d} {Capitalize(G(e.A))} исчез.";
            case EventType.AgricultureAdopted:
                return e.B == 1 ? $"{d} {Capitalize(G(e.A))} перенял у соседей земледелие." : $"{d} {Capitalize(G(e.A))} первым в своих краях стал сеять зерно.";
            case EventType.TradeRoute:
                return $"{d} Между {GIns(e.A)} и {GIns(e.B)} пролёг торговый путь.";
            case EventType.Discovery:
                return $"{d} {Discovery(e)}";
            case EventType.PolityFounded:
            {
                var pol = w.Polities[e.A];
                return $"{d} {Capitalize(G(e.B))} основал державу — {P(pol.Id)}.";
            }
            case EventType.LeaderRose:
            {
                var pol = w.Polities[e.A];
                if (e.C == 1 && pol.Dynasty.Native.Length > 0) Gloss(pol.Dynasty, "династия");
                return $"{d} Власть в державе {PolityShort(e.A)} взял {Person(e.B)}" + (e.C == 1 && pol.Dynasty.Native.Length > 0 ? ", основатель новой династии." : ".");
            }
            case EventType.LeaderStruck:
            {
                var src = e.Cause >= 0 ? w.Events.Get(e.Cause) : default;
                string hand = src.ByCreator ? " *(рука Создателя)*" : "";
                return $"{d} **Молния убила правителя** {Person(e.B)} — владыку державы {PolityShort(e.A)}.{hand}";
            }
            case EventType.WarDeclared:
                return $"{d} {Capitalize(PolityOfGroup(e.A))} пошла войной на {G(e.B)}.";
            case EventType.HolyWar:
                return $"{d} **Священная война.** {Capitalize(PolityOfGroup(e.A))} обрушилась на {G(e.B)}: их веры разошлись слишком далеко.";
            case EventType.Battle:
            {
                bool holy = (e.Flags & EventFlags.Crisis) != 0;
                var decl = e.Cause >= 0 ? w.Events.Get(e.Cause) : default;
                string army = e.C >= 0 ? $"войско державы {PolityShort(e.C)}" : "войско пришельцев";
                string dead = $"полегло около {Number(e.Magnitude * 1500)} воинов";
                return decl.A == e.A
                    ? $"{d} {Capitalize(army)} разбило {GAcc(e.B)}{(holy ? " в битве за веру" : "")}; {dead}."
                    : $"{d} {Capitalize(G(e.A))} отбился от {(e.C >= 0 ? $"войска державы {PolityShort(e.C)}" : "пришельцев")}{(holy ? " в битве за веру" : "")}; {dead}.";
            }
            case EventType.Annexation:
                return $"{d} {Capitalize(G(e.B))} мирно вошёл в {PAcc(e.A)}.";
            case EventType.Revolt:
                return $"{d} В державе {PolityShort(e.A)} вспыхнул мятеж: поднялся {G(e.B)}.";
            case EventType.PolitySplit:
                return $"{d} Мятежники отложились от державы {PolityShort(e.B)} и основали свою — {P(e.A)}.";
            case EventType.Collapse:
                return $"{d} {Capitalize(P(e.A))} пала.";
            case EventType.LanguageSplit:
            {
                var L = w.Languages[e.A]; var parent = w.Languages[e.B];
                // Пример — первое базовое слово, которое уже различается.
                var c = Concept.Water;
                foreach (var k in Concepts.Core) if (!L.Word(k).AsSpan().SequenceEqual(parent.Word(k))) { c = k; break; }
                return $"{d} Речь {GGen(e.C)} так отдалилась от {(parent.FounderGroup >= 0 ? $"речи {GGen(parent.FounderGroup)}" : "праязыка")}, что стала отдельным языком: «{Concepts.Russian[(int)c]}» у них — {L.Cyr(c, false)}, а у предков — {parent.Cyr(c, false)}.";
            }
            case EventType.WordBorrowed:
            {
                var to = w.Languages[e.A]; var from = w.Languages[e.B];
                return $"{d} Носители языка {GName(to.FounderGroup >= 0 ? to.FounderGroup : 0)} переняли у соседей слово «{Concepts.Russian[e.C]}» — {to.Cyr((Concept)e.C, false)}.";
            }
            case EventType.KardashevMilestone:
                return $"{d} Все народы мира вместе впервые тратят столько энергии, что их уровень по шкале Кардашёва достиг {e.Magnitude.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',')}.";
            case EventType.MeteorImpact:
                return $"{d} С неба упал огненный камень.";
            case EventType.ProphecyMade:
                return $"{d} Проповедники {RGen(e.A)} возвестили: мир кончится в {e.B} году.";
            case EventType.ProphecyFulfilled:
            {
                var src = w.Events.Get(e.C);
                return $"{d} **Пророчество сбылось.** В назначенный срок пришла {EventNoun(src.Type).Nom}, и {R(e.A)} уверилась в своей правоте: верных стало больше.";
            }
            case EventType.ProphecyFailed:
                return e.B == 1
                    ? $"{d} **Конец света не наступил.** Но верные {RGen(e.A)} не отреклись: «Наша молитва отсрочила гибель», — и стали проповедовать вдвое усерднее."
                    : $"{d} **Конец света не наступил**, и {R(e.A)} рассыпалась: люди разошлись по домам.";
            case EventType.PrayerAnswered:
                return $"{d} **Молитва услышана.** {Capitalize(G(e.A))} дождался ответа неба и зовёт Создателя Внимающим.";
            case EventType.CreatorAppeared:
                return (Attitude)e.B switch
                {
                    Attitude.Love => $"{d} **Явление.** Создатель явился народу {GNameGl(e.A)}. Люди плакали от счастья.",
                    Attitude.Fear => $"{d} **Явление.** Создатель явился народу {GNameGl(e.A)}. Ужас гнал людей в горы.",
                    Attitude.Denial => $"{d} **Явление.** Создатель явился народу {GNameGl(e.A)}, где учили, что небо пусто. Мудрецы онемели.",
                    Attitude.Rebellion => $"{d} **Явление.** Создатель явился мятежному народу {GNameGl(e.A)}. Они не склонили головы.",
                    _ => $"{d} **Явление.** Создатель явился народу {GNameGl(e.A)}.",
                };
            case EventType.DemandMet:
                return $"{d} {Capitalize(G(e.A))} принёс Создателю требуемые жертвы.";
            case EventType.DemandRefused:
                return $"{d} {Capitalize(G(e.A))} отказался платить Создателю дань: «Мы свободный народ».";
            case EventType.Threat:
                return $"{d} Небо пригрозило народу {GNameGl(e.A)}.";
            case EventType.Teaching:
                return $"{d} Создатель наставил народ {GNameGl(e.A)}: {Creator.Dialogue.MessageRu(e.B >= 0 && e.B < Intervention.Messages.Length ? Intervention.Messages[e.B] : "")}.";
            case EventType.Forgetting:
                return $"{d} {Capitalize(G(e.A))} забыл все знаки неба и все имена Создателя.";
            case EventType.Healing:
                return $"{d} Мор отступил от народа {GNameGl(e.A)}.";
            case EventType.Inspiration:
                return $"{d} Мудрецов народа {GNameGl(e.A)} осенило.";
            case EventType.Blessing:
                return $"{d} На народ {GNameGl(e.A)} сошло благословение.";
            case EventType.ColdSnap:
                return $"{d} Пришла долгая зима.";
            case EventType.Flood:
                return $"{d} Реки вышли из берегов.";
            case EventType.HerdsReturn:
                return $"{d} В опустевшие земли вернулись стада.";
            case EventType.RelicAppeared:
                return $"{d} С неба упал сияющий камень{(e.A >= 0 ? $" в землях народа {GNameGl(e.A)}" : "")}.";
            case EventType.CursedGround:
                return $"{d} Земля зачахла, будто проклятая.";
            case EventType.DynastyBlessed:
                return $"{d} Небо осенило правителей державы {PolityShort(e.B)}.";
            case EventType.DynastyCursed:
                return $"{d} Небо отвернулось от правителей державы {PolityShort(e.B)}.";
            case EventType.PeaceImposed:
                return $"{d} В землях народа {GNameGl(e.A)} оружие легло на землю: настал нежданный мир.";
            case EventType.MandateLost:
                return $"{d} Беды одна за другой: в державе {PolityShort(e.A)} говорят, что небо отвернулось от правителей.";
            case EventType.LightningStrike:
                return $"{d} Молния ударила в землю {GGen(e.A)}.";
            case EventType.Terraformed:
                return $"{d} {TerraformSentence((TerraformKind)e.A, e.C == 1, e.Cell)}";
            case EventType.EraBegan:
                return e.A >= 9
                    ? $"{d} **Кардашёв I.** Цивилизация мира подчинила себе всю энергию планеты."
                    : $"{d} **Новая эпоха — {Technology.EraNames[Math.Clamp(e.A, 1, 9)]}.**" + (e.B >= 0 ? $" Первым в неё вступил {G(e.B)}." : "");
            case EventType.EnergyCrisis:
                return $"{d} {Capitalize(G(e.A))} исчерпал {(e.B == 0 ? "угольные копи" : e.B == 1 ? "нефтяные поля" : "урановые рудники")}: огни гаснут, заводы встают.";
            case EventType.ClimateWarming:
                return $"{d} **Мир теплеет:** уже на {e.A / 10f:0.0} °C теплее, чем до огня машин. Льды тают, море подступает к городам.".Replace(".0 °C", " °C");
            case EventType.NuclearWar:
                return $"{d} **Ядерная война.** {Capitalize(P(e.A))} и {P(e.B)} обменялись ударами; погибло {Percent(e.Magnitude)} их людей. Пепел закрыл солнце.";
            case EventType.MegaprojectStarted:
                return $"{d} Державы мира начали стройку века: {MegaprojectName((MegaprojectKind)e.A)}.";
            case EventType.MegaprojectCompleted:
                return (MegaprojectKind)e.A == MegaprojectKind.OrbitalCollectors && e.B > 1
                    ? $"{d} Над планетой раскрылся {e.B}-й ярус зеркал, ловящих свет звезды."
                    : $"{d} **Стройка века завершена:** {MegaprojectName((MegaprojectKind)e.A)}.";
            case EventType.CityFounded:
                return $"{d} {Capitalize(G(e.B))} заложил город {City(e.A)}.";
            case EventType.CityGrew:
                return e.B >= 3
                    ? e.C == 1
                        ? $"{d} **{City(e.A)} — первый мегаполис мира:** больше десяти миллионов людей, и ночью его огни видны с неба."
                        : $"{d} {City(e.A)} стал мегаполисом: больше десяти миллионов людей."
                    : $"{d} {City(e.A)} разросся в большой город: рынки, мастерские, стены в два кольца.";
            case EventType.GovernmentChanged:
                return $"{d} {Capitalize(P(e.A))} стала {FormInstr((GovForm)e.B)}: {FormPhrase((GovForm)e.B)}.";
            case EventType.Assimilated:
                return $"{d} {Capitalize(G(e.A))} растворился среди соседей — {GGen(e.B)}: язык и вера у них давно были одни.";
            case EventType.ArmyMarched:
                return $"{d} Войско державы {PolityShort(e.B)} выступило в поход на {G(e.C)}.";
            case EventType.PeaceMade:
                return $"{d} {Capitalize(P(e.A))} и {P(e.B)} заключили мир на {Russian.Years(e.C)}.";
            case EventType.TreatyBroken:
                return $"{d} **Договор нарушен.** {Capitalize(P(e.A))} напала на {PAcc(e.B)}, не дождавшись конца мира. Этого не забудут.";
            case EventType.AllianceFormed:
                return e.C == 1
                    ? $"{d} **Федерация.** {Capitalize(P(e.A))} и {P(e.B)} после векового союза слились в одну державу по доброй воле."
                    : $"{d} {Capitalize(P(e.A))} и {P(e.B)} заключили союз: враг одной — враг другой.";
            case EventType.PetitionMade:
                return $"{d} {Capitalize(G(e.A))} просит Создателя {Simulation.PetitionTitle((PetitionKind)e.B)}{(e.C >= 0 ? $" — над державой {PolityShort(e.C)}" : "")}.";
            case EventType.PetitionGranted:
                return $"{d} Просьба народа {GNameGl(e.A)} исполнилась: они получили {Simulation.PetitionTitle((PetitionKind)e.B).Replace("победы", "победу").Replace("кары", "кару").Replace("исцеления", "исцеление").Replace("знака", "знак").Replace("ответа", "ответ")}.";
            case EventType.PetitionIgnored:
                return $"{d} {Capitalize(G(e.A))} не дождался {Simulation.PetitionTitle((PetitionKind)e.B)} — и запомнил это.";
            case EventType.OathSworn:
                return $"{d} **Обет.** Создатель пообещал народу {GNameGl(e.A)}: «{Capitalize(Simulation.OathTitle((OathKind)e.B))}{(e.C >= 0 && (OathKind)e.B == OathKind.Victory ? $" над державой {PolityShort(e.C)}" : "")}».";
            case EventType.OathKept:
                return $"{d} **Слово сдержано.** Обет «{Simulation.OathTitle((OathKind)e.B)}» исполнился; {G(e.A)} зовёт Создателя Верным слову.";
            case EventType.OathBroken:
                return $"{d} **Слово нарушено.** Обет «{Simulation.OathTitle((OathKind)e.B)}» не исполнен; {G(e.A)} зовёт Создателя Обманщиком.";
            case EventType.StyleNamed:
                return $"{d} **Народы мира зовут вас {Simulation.StyleTitleIns((CreatorStyle)e.A)}.** Так о вас теперь рассказывают от моря до моря.";
            case EventType.SimulationSuspected:
                return $"{d} **Мир сделан.** Учёные народа {GNameGl(e.A)} сложили вместе чудеса, странную геологию и слишком удобные законы природы и решили: мир кем-то устроен.";
            case EventType.ProbeLaunched:
                return e.B switch
                {
                    1 => $"{d} **Ракеты в небо.** {Capitalize(G(e.A))} ударил ракетами туда, где, по их расчётам, прячется Создатель. Небо дрогнуло.",
                    2 => $"{d} **Посольство к небу.** {Capitalize(G(e.A))} отправил корабль с дарами и посланием Создателю.",
                    _ => $"{d} **Зонд к небу.** {Capitalize(G(e.A))} запустил зонд с посланием: «Мы видим тебя».",
                };
            case EventType.GeoglyphMade:
                return $"{d} **Знак на земле.** {Capitalize(G(e.A))} выложил на равнине огромный знак — его видно только с неба.";
            case EventType.LightMessage:
                return $"{d} **Огни говорят.** Ночью города народа {GNameGl(e.A)} мигают: простые числа, потом слова.";
            case EventType.ShieldRaised:
                return $"{d} **Щит.** {Capitalize(P(e.A))} построила купол, гасящий чудеса: в её землях воля Создателя слабеет.";
            case EventType.FreedomDemanded:
                return $"{d} **Требование свободы.** {Capitalize(G(e.A))} обратился к Создателю: «Мы знаем, что ты есть. Отпусти нас».";
            case EventType.ContactAnswered:
                return e.B switch
                {
                    1 => $"{d} **Создатель отпустил народ {GNameGl(e.A)}.** Отныне их земля — их собственная.",
                    2 => $"{d} Создатель промолчал в ответ на послание народа {GNameGl(e.A)}.",
                    _ => $"{d} **Контакт.** Создатель ответил народу {GNameGl(e.A)}. Учёные и жрецы впервые говорят об одном и том же.",
                };
            case EventType.CreatorWounded:
                return $"{d} **Создатель ранен.** Сила его убыла; десять лет чудеса будут даваться ему тяжелее.";
            case EventType.RiverBorn:
                return e.C == 1 ? $"{d} {Capitalize(Near(e.Cell, "в краях"))} ручьи год от года полнеют: рождается новая река." : $"{d} {Capitalize(Near(e.Cell, "в землях"))} забил родник, и потекла новая река.";
            case EventType.Eruption:
                return $"{d} **Извержение.** {Capitalize(Near(e.Cell, "в землях"))} разверзлась огненная гора" + (e.A > 0 ? $"; погибло около {Russian.Number(e.A * 100)} человек" : "") + ". Пепел закрыл солнце.";
            case EventType.SeaLevelChanged:
                return e.C == 1
                    ? $"{d} {(e.A > 0 ? "Море из года в год подступает к берегам." : "Море год за годом отступает, обнажая отмели.")}"
                    : $"{d} **{(e.A > 0 ? $"Великий потоп. Море поднялось на {e.B} м" : $"Море отступило на {e.B} м")}**{(e.A > 0 ? " и затопило берега." : ": обнажились шельфы и сухопутные мосты.")}";
            case EventType.ClimateShifted:
                return e.C == 1 ? $"{d} {(e.A > 0 ? "Зимы год от года мягче, льды отступают." : "Зимы год от года злее, льды наступают.")}"
                    : $"{d} {(e.A > 0 ? "Весь мир потеплел; льды начали таять." : "Весь мир остыл; льды поползли от полюсов.")}";
            case EventType.ContinentDrifted:
                return e.C == 1 ? $"{d} Земля {Near(e.Cell, "в краях")} медленно поползла: материк пустился в путь по морю."
                    : $"{d} **Материк сдвинулся.** {Capitalize(Near(e.Cell, "земли"))} за одну ночь ушли по морю на сотни вёрст.";
            case EventType.GroupDrowned:
                return $"{d} **{Capitalize(G(e.A))} утонул.** Бежать было некуда: вода сомкнулась над их землёй.";
            case EventType.Refugees:
                return $"{d} {Capitalize(G(e.A))} бежал от воды на новые земли; в пути погибло {Percent(e.Magnitude)} людей.";
            case EventType.LandSank:
                return $"{d} Земля ушла под воду; погибло {Russian.Number(e.A)} зверей.";
            // Мелкие события: в Хронику не попадают, но нужны Летописи («почему»).
            case EventType.Awakening:
                return $"{d} Вид {SpeciesName(e.A)} пробудился к разуму: родилось {e.B} {Plural(e.B, "первый народ", "первых народа", "первых народов")}.";
            case EventType.DroughtEnded:
                return $"{d} Засуха {Near(e.Cell, "в краях")} кончилась: пошли дожди.";
            case EventType.Bounty:
                return $"{d} Обильный год {Near(e.Cell, "в краях")}: зерна вдоволь.";
            case EventType.GroupFormed:
                return $"{d} Появился {G(e.A)}.";
            case EventType.GroupFissioned:
                return $"{d} От народа {GNameGl(e.B)} отделился {G(e.A)} и ушёл на новые земли.";
            case EventType.SoundLawApplied:
                return $"{d} В языке {GName(Math.Max(0, w.Languages[e.A].FounderGroup))} поменялось произношение.";
        }
        return $"{d} {e.Type}.";
    }

    /// <summary>Строка любого события, даже незначительного (для Летописи и дерева причин).</summary>
    public string Describe(WorldEvent e) => Render(e, Array.Empty<List<int>>(), new HashSet<int>()).Replace("**", "");

    private string GPrep(int id) => id >= 0 ? $"народе {GNameGl(id)}" : "народе";
    private string GGen(int id) => id >= 0 ? $"народа {GNameGl(id)}" : "народа";
    private string GIns(int id) => id >= 0 ? $"народом {GNameGl(id)}" : "народом";
    private string GAcc(int id) => id >= 0 ? $"народ {GNameGl(id)}" : "народ";

    private string GNameGl(int id)
    {
        var g = _w.Groups[id];
        Gloss(g.Name, "народ");
        if (_groupGlossed[id]) return g.Name.Native;
        _groupGlossed[id] = true;
        return $"{g.Name.Native} («{g.Name.Gloss}»)";
    }

    private string PolityOfGroup(int gid)
    {
        var g = _w.Groups[gid];
        return g.PolityId >= 0 ? P(g.PolityId) : G(gid);
    }

    private string Discovery(WorldEvent e)
    {
        var who = Capitalize(G(e.A));
        var t = (Tech)e.B;
        var info = Technology.Get(t);
        if (e.C >= 0) return $"{who} перенял у {GGen(e.C)} {info.Title}.";
        if (t == Tech.Writing)
        {
            string script = (ScriptKind)(int)e.Magnitude switch
            {
                ScriptKind.Syllabic => "слоговое: каждый знак — целый слог, ведь слоги их речи просты",
                ScriptKind.Alphabetic => "буквенное: их слоги слишком сложны для слоговых знаков",
                _ => "из знаков-рисунков, каждый из которых — целое слово",
            };
            return $"{who} изобрёл письмо — {script}.";
        }
        return $"{who} {info.Phrase}.";
    }

    private string Interpretation(WorldEvent e)
    {
        var w = _w;
        var g = w.Groups[e.A];
        var kind = (InterpretationKind)e.B;
        var src = e.Cause >= 0 ? w.Events.Get(e.Cause) : default;
        var srcType = (EventType)e.C;
        bool won = srcType != EventType.Battle || src.A == g.Id;
        var noun = EventNoun(srcType, won);
        if (srcType == EventType.LightningStrike && src.B >= 0) noun = new("смертоносная молния", "смертоносную молнию", "смертоносной молнии", "смертоносной молнии");
        string who = Capitalize(G(g.Id));
        string creator = CreatorFor(g);
        var rng = Rng.Derive(w.Seed, (ulong)e.Id, 77);
        bool prophetic = g.ReligionId >= 0 && w.Religions[g.ReligionId].Doctrine[Doctrine.Wrathfulness] > 0.55f;
        if (srcType == EventType.Vision)
            return kind switch
            {
                InterpretationKind.Natural => $"Мудрецы {GGenShort(g.Id)} сочли сон пророка обычным сном.",
                InterpretationKind.Gift => $"{who} принял видение как милость: {creator} заговорил с ними.",
                InterpretationKind.Wrath or InterpretationKind.Punishment => $"{who} испугался: если {creator} заговорил, значит, он недоволен.",
                InterpretationKind.Bargain => $"{who} решил, что видение — ответ на их жертвы.",
                _ => $"{who} внимал пророку и спорил, что значит его сон.",
            };
        switch (kind)
        {
            case InterpretationKind.Punishment:
                return prophetic
                    ? $"{who} возгласил: «{Capitalize(noun.Nom)} — это кара {creator}. Мы забыли закон — и закон вспомнил о нас». Каялись за {GuiltReason(g, rng)}."
                    : $"{who} увидел в {noun.Prep} кару {creator} за {GuiltReason(g, rng)}.";
            case InterpretationKind.Wrath:
                return rng.Chance(0.5)
                    ? $"{who} решил, что {creator} гневается без всякой причины, и стал бояться неба."
                    : $"«{creator} зол», — сказали старейшины {GGenShort(g.Id)}, пережив {noun.Acc}.";
            case InterpretationKind.Trial:
                return $"{who} принял {noun.Acc} как испытание веры и молился усерднее прежнего.";
            case InterpretationKind.Gift:
                return rng.Chance(0.5)
                    ? $"{who} восславил {creator} за {noun.Acc}: «Он помнит о нас»."
                    : $"{who} принял {noun.Acc} как дар {creator} и принёс благодарственные жертвы.";
            case InterpretationKind.Bargain:
                return g.LastPrayer >= 0 && src.Year - g.LastPrayer <= 6
                    ? $"{who} счёл {noun.Acc} ответом на свои молитвы: «Мы просили — {creator} дал». С тех пор молитва у них — договор."
                    : $"{who} счёл {noun.Acc} платой за свои жертвы: «Мы даём — {creator} даёт».";
            case InterpretationKind.Omen:
                return $"{who} прочёл в {noun.Prep} знамение и долго спорил, что оно предвещает.";
            case InterpretationKind.Favoritism:
                return srcType is EventType.MiracleHarvest or EventType.Rain
                    ? $"{who}, голодая, смотрел, как {noun.Nom} достался другим: «{creator} выбирает любимцев»."
                    : $"{who} с горечью заметил, что милость неба досталась соседям, а им — {noun.Nom}: «{creator} выбирает любимцев».";
            default:
                return $"Мудрецы {GGenShort(g.Id)} объяснили {noun.Acc} естественными причинами — без воли богов.";
        }
    }

    private string GGenShort(int id) => $"народа {GNameGl(id)}";

    private string RenderIntervention(WorldEvent e, List<int>[] byCause, HashSet<int> shown)
    {
        var w = _w;
        var type = (InterventionType)e.A;
        // У сил Земли в B — режим кисти, а не народ.
        string target = e.B >= 0 && !Intervention.IsTerraform(type) ? GNameGl(e.B) : "";
        string weak = (e.Flags & EventFlags.Weakened) != 0 ? " Но сила Создателя была на исходе, и знак вышел слабым." : "";
        string head = type switch
        {
            InterventionType.Drought => $"*Создатель наслал засуху на земли народа {target}.*",
            InterventionType.Rain => $"*Создатель пролил дождь над землями народа {target}.*",
            InterventionType.Lightning => $"*Создатель ударил молнией {(e.B >= 0 ? $"в землю народа {target}" : "в землю")}.*",
            InterventionType.Harvest => $"*Создатель дал народу {target} небывалый урожай.*",
            InterventionType.Vision => $"*Создатель послал видение в народ {target}.*",
            InterventionType.Meteor => "*Создатель обрушил с неба огненный камень.*",
            InterventionType.Spark => $"*Создатель коснулся разума народа {target}.*",
            _ when Intervention.IsTerraform(type) => TerraformHead(type, e),
            _ => $"*Создатель: {Creator.Spellbook.Get(type).Title.ToLowerInvariant()}{(e.B >= 0 ? $" — народу {target}" : "")}.*",
        };
        var sb = new StringBuilder();
        sb.Append($"{Date(e)} {head}{weak}");
        // Прямые последствия и их толкования.
        var consequences = new List<int>();
        Collect(e.Id, byCause, consequences, 0);
        var interps = new List<int>();
        var other = new List<int>();
        foreach (var c in consequences)
        {
            var ce = w.Events.Get(c);
            if (ce.Type == EventType.Interpretation) interps.Add(c);
            else if (ce.Type is EventType.LeaderStruck or EventType.ProphetBorn or EventType.CultFounded or EventType.Martyrdom or EventType.SacredSiteFounded
                     or EventType.ReligionFounded or EventType.GroupDestroyed or EventType.EpithetGained or EventType.Famine
                     or EventType.PrayerAnswered or EventType.CreatorAppeared or EventType.DemandMet or EventType.DemandRefused or EventType.Teaching
                     or EventType.Forgetting or EventType.DynastyBlessed or EventType.DynastyCursed or EventType.PeaceImposed or EventType.Battle
                     or EventType.GroupDrowned or EventType.Refugees or EventType.Eruption or EventType.LandSank) other.Add(c);
            else if (ce.Type is EventType.Terraformed or EventType.RiverBorn or EventType.SeaLevelChanged or EventType.ClimateShifted or EventType.ContinentDrifted)
                shown.Add(c); // описано в заголовке вмешательства
        }
        // Метеорит: кто погиб.
        if (type == InterventionType.Meteor)
        {
            var dead = w.Events.Log.Where(x => x.Year == e.Year && x.Type == EventType.GroupDestroyed && x.Id > e.Id && x.Id < e.Id + 60).Select(x => GName(x.A)).ToList();
            if (dead.Count > 0) sb.Append(dead.Count > 1 ? $" В огне погибли народы {List(dead)}." : $" В огне погиб народ {dead[0]}.");
            else sb.Append(" Камень упал вдали от людей, но зарево видели за сотни вёрст.");
        }
        if (type == InterventionType.Vision)
        {
            var pe = consequences.Select(c => w.Events.Get(c)).FirstOrDefault(x => x.Type == EventType.ProphetBorn);
            if (pe.Type == EventType.ProphetBorn)
            {
                var prophet = w.People[pe.A];
                sb.Append($" Пророк {Person(prophet.Id)} увидел во сне, {MessagePhrase(prophet.Message)}.");
                if (pe.C == 1) sb.Append(" Но учёные мужи его народа сочли пророка безумцем.");
                shown.Add(pe.Id);
            }
        }
        // Толкования: самые сильные, по одному на вид толкования, до 5.
        interps.Sort((a, b) =>
        {
            var ea = w.Events.Get(a); var eb = w.Events.Get(b);
            int c = eb.Magnitude.CompareTo(ea.Magnitude);
            return c != 0 ? c : a.CompareTo(b);
        });
        var kinds = new bool[8];
        var groupsShown = new List<int>();
        int shownInterp = 0;
        foreach (var i in interps)
        {
            var ie = w.Events.Get(i);
            if (groupsShown.Contains(ie.A)) { shown.Add(i); continue; }
            if (kinds[ie.B] && shownInterp >= 2) continue;
            groupsShown.Add(ie.A);
            if (shownInterp >= 5) break;
            kinds[ie.B] = true;
            shownInterp++;
            sb.Append($"\n  - {Interpretation(ie)}");
            shown.Add(i);
        }
        foreach (var i in other)
        {
            if (shown.Contains(i)) continue;
            var oe = w.Events.Get(i);
            if (oe.Type == EventType.ProphetBorn) { shown.Add(i); continue; }
            var line = Render(oe, byCause, shown);
            shown.Add(i);
            sb.Append($"\n  {line}");
        }
        if (interps.Count > shownInterp) sb.Append($"\n  - Всего этот знак по-своему истолковали {interps.Count} {Plural(interps.Count, "народ", "народа", "народов")}.");
        return sb.ToString();
    }

    private static string MegaprojectName(MegaprojectKind k) => k switch
    {
        MegaprojectKind.GlobalGrid => "единая энергосеть мира",
        MegaprojectKind.OrbitalCollectors => "орбитальные коллекторы света",
        _ => "управление климатом",
    };

    /// <summary>«в землях народа X» / «близ народа X» / пусто — по клетке эко-сетки.</summary>
    private string Near(int cell, string prefix)
    {
        var w = _w;
        if (cell < 0 || w.Groups.Count == 0) return "в мире";
        int owner = cell < w.CellOwner.Length ? w.CellOwner[cell] : -1;
        if (owner >= 0 && w.Groups[owner].Alive) return $"{prefix} {GGen(owner)}";
        int best = -1; float bd = 6.5f;
        foreach (var g in w.Groups)
        {
            if (!g.Alive) continue;
            float d = w.Planet.EcoDistance(g.Cell, cell);
            if (d < bd) { bd = d; best = g.Id; }
        }
        return best >= 0 ? $"близ {GGen(best)}" : "в безлюдных краях";
    }

    private string TerraformSentence(TerraformKind k, bool slow, int cell)
    {
        string where = Near(cell, "в землях");
        if (slow)
            return k switch
            {
                TerraformKind.Mountains or TerraformKind.Brush => $"{Capitalize(where)} земля медленно меняет облик: горы растут, долины оседают.",
                TerraformKind.Island => $"Близ берегов {where.Replace("в землях ", "")} со дна моря медленно поднимается остров.",
                TerraformKind.Bridge => $"{Capitalize(where)} год за годом мелеет море: из воды встаёт перешеек.",
                TerraformKind.Sink or TerraformKind.Strait => $"{Capitalize(where)} земля медленно оседает, и море подступает.",
                TerraformKind.Desert => $"{Capitalize(where)} дожди год от года всё скуднее.",
                TerraformKind.Oasis => $"{Capitalize(where)} дожди год от года всё щедрее.",
                TerraformKind.Glacier => $"{Capitalize(where)} лёд год от года спускается ниже.",
                TerraformKind.Thaw => $"{Capitalize(where)} льды год от года отступают.",
                TerraformKind.Lake => $"{Capitalize(where)} в низине копится вода — будет озеро.",
                _ => $"{Capitalize(where)} земля медленно меняется.",
            };
        return k switch
        {
            TerraformKind.Mountains => $"{Capitalize(where)} за одну ночь выросли горы.",
            TerraformKind.Island => "Из моря поднялся остров.",
            TerraformKind.Bridge => $"{Capitalize(where)} из моря поднялся перешеек: берега соединились.",
            TerraformKind.Strait => $"{Capitalize(where)} море прорезало сушу.",
            TerraformKind.Sink => $"{Capitalize(where)} земля ушла под воду.",
            TerraformKind.Lake => $"{Capitalize(where)} разлилось новое озеро.",
            TerraformKind.Oasis => $"{Capitalize(where)} пошли обильные дожди.",
            TerraformKind.Desert => $"{Capitalize(where)} дожди ушли, земля сохнет.",
            TerraformKind.Glacier => $"{Capitalize(where)} землю сковал вечный лёд.",
            TerraformKind.Thaw => $"{Capitalize(where)} растаяли вековые льды.",
            _ => $"{Capitalize(where)} земля переменила облик.",
        };
    }

    private string TerraformHead(InterventionType type, WorldEvent e)
    {
        string where = Near(e.Cell, "в землях");
        string slowTail = e.C == 1 ? " Медленно, век за веком — так, что смертные примут это за дело природы." : "";
        string head = type switch
        {
            InterventionType.Sculpt => (Planet.BrushMode)Math.Max(0, e.B) switch
            {
                Planet.BrushMode.Raise => $"Создатель поднял землю {where}.",
                Planet.BrushMode.Lower => $"Создатель опустил землю {where}.",
                Planet.BrushMode.Flatten => $"Создатель выровнял землю {where}.",
                Planet.BrushMode.Smooth => $"Создатель сгладил горы {where}.",
                Planet.BrushMode.Roughen => $"Создатель изрезал землю грядами {where}.",
                Planet.BrushMode.Wet => $"Создатель напоил дождями землю {where}.",
                Planet.BrushMode.Dry => $"Создатель иссушил землю {where}.",
                Planet.BrushMode.Warm => $"Создатель согрел землю {where}.",
                Planet.BrushMode.Cool => $"Создатель остудил землю {where}.",
                Planet.BrushMode.Fertile => $"Создатель сделал почвы жирнее {where}.",
                _ => $"Создатель истощил почвы {where}.",
            },
            InterventionType.Ridge => $"Создатель воздвиг горный хребет {where}.",
            InterventionType.Bridge => $"Создатель поднял из моря перешеек {where}: суша соединила берега.",
            InterventionType.Strait => $"Создатель рассёк сушу проливом {where}.",
            InterventionType.Island => $"По воле Создателя из моря поднялся остров {where.Replace("в землях", "у берегов")}.",
            InterventionType.Sink => $"Создатель погрузил землю в море {where}.",
            InterventionType.River => $"По воле Создателя забил родник и потекла новая река {where}.",
            InterventionType.Lake => $"Создатель выкопал чашу нового озера {where}.",
            InterventionType.Volcano => $"Создатель разверз огненную гору {where}.",
            InterventionType.Oasis => $"Создатель дал дожди сухой земле {where}.",
            InterventionType.Desert => $"Создатель отнял дожди у земли {where}.",
            InterventionType.Glacier => $"Создатель сковал землю вечным льдом {where}.",
            InterventionType.Thaw => $"Создатель растопил льды {where}.",
            InterventionType.Warmer => "Создатель вдохнул в мир тепло: льды тают, море поднимается.",
            InterventionType.Colder => "Создатель выдохнул холод: льды растут, море отступает.",
            InterventionType.SeaRise => "Создатель поднял море.",
            InterventionType.SeaFall => "Создатель опустил море: обнажились шельфы.",
            InterventionType.Drift => $"Создатель сдвинул материк {where}.",
            _ => "Создатель изменил облик мира.",
        };
        return $"*{head.Replace("  ", " ").Replace(" .", ".")}*{slowTail}";
    }

    private void Collect(int id, List<int>[] byCause, List<int> outList, int depth)
    {
        if (depth > 3 || id >= byCause.Length || byCause[id] == null) return;
        foreach (var c in byCause[id])
        {
            outList.Add(c);
            Collect(c, byCause, outList, depth + 1);
        }
    }

    private string DoctrineSentence(Religion r)
    {
        var d = r.Doctrine;
        var parts = new List<string>();
        if (d[Doctrine.Moralism] > 0.45f) parts.Add("бог видит всё и карает за ложь и раздор");
        if (d[Doctrine.Ritual] > 0.6f) parts.Add("обряды и жертвы — основа жизни");
        if (d[Doctrine.Wrathfulness] > 0.5f) parts.Add("небо гневно, его надо умилостивлять");
        if (d[Doctrine.Benevolence] > 0.62f) parts.Add("Создатель милостив");
        if (d[Doctrine.Violence] > 0.6f) parts.Add("кровь врага угодна богу");
        else if (d[Doctrine.Violence] < 0.25f) parts.Add("кровопролитие — грех");
        if (d[Doctrine.Knowledge] > 0.6f) parts.Add("познание — служение");
        else if (d[Doctrine.Knowledge] < 0.25f) parts.Add("лишнее знание опасно");
        if (d[Doctrine.Proselytism] > 0.6f) parts.Add("истину надо нести чужакам");
        if (parts.Count == 0) parts.Add("живи как предки");
        return $"Учат: {string.Join("; ", parts.Take(3))}.";
    }

    private string DoctrineDiff(Religion nr, Religion? parent)
    {
        if (parent == null) return DoctrineSentence(nr);
        string[] names = { "о милости Создателя", "о его гневе", "о каре за грехи", "об обрядах", "о единстве бога", "о знании", "о насилии", "об обращении чужаков" };
        string[] more = { "верят в его милость сильнее", "считают его гневнее", "строже судят грехи", "умножили обряды", "чтут его как единого", "ставят знание выше", "оправдывают меч", "проповедуют чужакам" };
        string[] less = { "сомневаются в его милости", "не верят в его гнев", "мягче к грешникам", "отвергли обряды", "видят в нём одного из многих", "боятся лишнего знания", "отвергают меч", "замкнулись в себе" };
        int best = 0; float bv = 0;
        for (int i = 0; i < Doctrine.Count; i++)
        {
            float diff = nr.Doctrine[i] - parent.Doctrine[i];
            if (MathF.Abs(diff) > MathF.Abs(bv)) { bv = diff; best = i; }
        }
        _ = names;
        if (MathF.Abs(bv) < 0.04f) return DoctrineSentence(nr);
        return $"Главный спор — {names[best]}: новые {(bv > 0 ? more[best] : less[best])}.";
    }

    // ---------- Итоговые разделы ----------

    private void CreatorNames()
    {
        var w = _w;
        _sb.AppendLine("## Имена Создателя");
        _sb.AppendLine();
        _sb.AppendLine("Каждый народ видел только следы вмешательств — и назвал их автора по-своему.");
        _sb.AppendLine();
        _sb.AppendLine("| Народ | Вера | Имя Создателя | Эпитеты | Отношение |");
        _sb.AppendLine("|---|---|---|---|---|");
        var groups = w.Groups.Where(g => g.Alive).OrderByDescending(g => g.Pop).ThenBy(g => g.Id).Take(30).ToList();
        foreach (var g in groups)
        {
            string rel = g.ReligionId >= 0 ? w.Religions[g.ReligionId].Name.Native : "—";
            string name = g.ReligionId >= 0 ? $"{w.Religions[g.ReligionId].CreatorName.Native} («{w.Religions[g.ReligionId].CreatorName.Gloss}»)" : "—";
            string eps = g.Epithets.Count > 0 ? string.Join(", ", g.Epithets.Select(x => $"{x.Name.Native} («{x.Name.Gloss}»)")) : "—";
            _sb.AppendLine($"| {g.Name.Native} ({Number(g.Pop)} чел.) | {rel} | {name} | {eps} | {AttitudeName(g.Attitude)} |");
        }
        _sb.AppendLine();
    }

    private void Faiths()
    {
        var w = _w;
        _sb.AppendLine("## Веры мира");
        _sb.AppendLine();
        var shownRel = w.Religions.Where(r => r.MaxAdherents > 0 || r.Converted || !r.IsCult).OrderBy(r => r.Id).Take(40).ToList();
        foreach (var r in shownRel)
        {
            Gloss(r.Name, "вера");
            Gloss(r.CreatorName, "имя Создателя");
            string status = r.Alive ? (r.Adherents > 0 ? $"жива, {r.Adherents} {Plural(r.Adherents, "народ", "народа", "народов")}, {Number(r.AdherentPop)} верных" : "живёт как культ меньшинства") : $"угасла в {r.Faded} году";
            string parent = r.Parent >= 0 ? $", ответвление веры {w.Religions[r.Parent].Name.Native}" : "";
            _sb.AppendLine($"- **{r.Name.Native}** («{r.Name.Gloss}»), с {r.Founded} года{parent}; основатели — народ {GName(r.FounderGroup)}" +
                           (r.Prophet >= 0 ? $", пророк {w.People[r.Prophet].Name.Native}" : "") + $". {Capitalize(FormName(r.Form))}; Создатель — {r.CreatorName.Native} («{r.CreatorName.Gloss}»). " +
                           $"{DoctrineSentence(r)} {Taboo(r)}{Sites(r)}Статус: {status}.");
        }
        _sb.AppendLine();
    }

    private string Taboo(Religion r)
    {
        var g = _w.Groups[r.FounderGroup];
        if (g.Taboo < 0) return "";
        var L = _w.LangOf(g);
        return $"Табу: нельзя вслух произносить слово «{Concepts.Russian[g.Taboo]}» ({L.Cyr((Concept)g.Taboo, false)}) — так сильно было горе. ";
    }

    private string Sites(Religion r)
    {
        if (r.Sites.Count == 0) return "";
        var names = r.Sites.Take(4).Select(s => _w.Sites[s].Name.Native).ToList();
        return $"Святыни: {List(names)}. ";
    }

    private void Languages()
    {
        var w = _w;
        _sb.AppendLine("## Языки");
        _sb.AppendLine();
        if (w.Languages.Count == 0) return;
        _sb.AppendLine("Дерево языков (потомок — отступом):");
        _sb.AppendLine();
        void Tree(int id, int depth)
        {
            var L = w.Languages[id];
            string laws = L.History.Count > 0 ? $" — звуковые законы: {string.Join("; ", L.History.Take(4).Select(h => h[(h.IndexOf(':') + 2)..]))}" : "";
            _sb.AppendLine($"{new string(' ', depth * 2)}- {LangName(id)} (с {L.BornYear} г.{(L.Alive ? "" : ", вымер")}){laws}");
            foreach (var c in w.Languages.Where(x => x.Parent == id)) Tree(c.Id, depth + 1);
        }
        Tree(0, 0);
        _sb.AppendLine();
        var live = w.Languages.Where(l => l.Alive && l.Id != 0).Take(6).ToList();
        live.Insert(0, w.ProtoSnapshot ?? w.Languages[0]);
        _sb.AppendLine("Как менялись слова:");
        _sb.AppendLine();
        _sb.AppendLine("| Понятие | " + string.Join(" | ", live.Select((l, k) => k == 0 ? "праязык (год 0)" : l.Id == 0 ? "праязык сейчас" : GName(l.FounderGroup))) + " |");
        _sb.AppendLine("|---|" + string.Join("", live.Select(_ => "---|")));
        foreach (var c in new[] { Concept.Water, Concept.Fire, Concept.God, Concept.Mother, Concept.Star, Concept.Death })
            _sb.AppendLine($"| {Concepts.Russian[(int)c]} | " + string.Join(" | ", live.Select(l => l.Cyr(c, false))) + " |");
        _sb.AppendLine();
    }

    private void Polities()
    {
        var w = _w;
        if (w.Polities.Count == 0) return;
        _sb.AppendLine("## Державы");
        _sb.AppendLine();
        foreach (var p in w.Polities.Take(30))
        {
            Gloss(p.Name, "держава");
            string fate = p.Alive ? $"стоит, {p.Members.Count} {Plural(p.Members.Count, "народ", "народа", "народов")}" : $"пала в {p.Ended} году";
            string ruler = p.Alive && p.Leader >= 0 ? $"; правит {w.People[p.Leader].Name.Native}" : "";
            _sb.AppendLine($"- **{p.Name.Native}** («{p.Name.Gloss}»), основана в {p.Founded} году народом {GName(p.Capital)}; {fate}{ruler}.");
        }
        _sb.AppendLine();
    }

    private void Summary()
    {
        var w = _w;
        _sb.AppendLine("## Итог");
        _sb.AppendLine();
        var alive = w.Groups.Where(g => g.Alive).ToList();
        float pop = alive.Sum(g => g.Pop);
        var att = new int[7];
        float[] attPop = new float[7];
        foreach (var g in alive) { att[(int)g.Attitude]++; attPop[(int)g.Attitude] += g.Pop; }
        var attParts = new List<string>();
        for (int i = 0; i < 7; i++) if (attPop[i] > 0) attParts.Add($"{AttitudeName((Attitude)i)} — {Percent(attPop[i] / Math.Max(1, pop))}");
        _sb.AppendLine($"- Население мира: {Number(pop)} в {alive.Count} {Plural(alive.Count, "народе", "народах", "народах")}; держав: {w.Polities.Count(p => p.Alive)}.");
        _sb.AppendLine($"- Живых вер и учений: {w.Religions.Count(r => r.Alive && r.Adherents > 0)}; всего возникло: {w.Religions.Count}. Языков: {w.Languages.Count(l => l.Alive)} живых из {w.Languages.Count}.");
        _sb.AppendLine($"- Отношение к Создателю (доля людей): {string.Join(", ", attParts)}.");
        _sb.AppendLine($"- Божественная энергия: {w.CreatorEnergy:0} (приток {w.EnergyIncome:0.00} в год).".Replace('.', ','));
        _sb.AppendLine($"- Мощность цивилизации: {w.PowerWatts:0.0e0} Вт; уровень по Кардашёву (формула Сагана): {w.Kardashev:0.00}.".Replace('.', ','));
        _sb.AppendLine($"- Эпоха мира: {Technology.EraNames[Math.Clamp(w.WorldEra, 1, 9)]}; CO₂ {w.Co2:0} ppm, потепление {w.Warming:0.0} °C.".Replace(".0 °C", " °C").Replace('.', ',').Replace("ppm,", "ppm,"));
        if (w.FirstAnomalyGroup >= 0) _sb.AppendLine($"- Первыми усомнились в естественности мира мудрецы народа {GName(w.FirstAnomalyGroup)}.");
        _sb.AppendLine();
    }

    private void Glossary()
    {
        _sb.AppendLine("## Глоссарий");
        _sb.AppendLine();
        var items = _glossary.Where(g => !string.IsNullOrEmpty(g.gloss)).OrderBy(g => g.native, StringComparer.Ordinal).ThenBy(g => g.kind, StringComparer.Ordinal).ToList();
        foreach (var (native, gloss, kind) in items) _sb.AppendLine($"- **{native}** — {kind}, «{gloss}»");
        _sb.AppendLine();
    }
}
