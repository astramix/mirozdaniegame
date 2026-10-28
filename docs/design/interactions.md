# Карта связей систем через события

Стрелка «A → событие → B» означает: A публикует событие, B на него подписан и меняет своё состояние.

```
            ┌────────────── Создатель (сценарий / зритель) ───────────────┐
            │ Intervention: drought rain lightning harvest vision meteor spark
            ▼                                                             │
 ┌──────── Планета ────────┐   NaturalDrought, Earthquake, Comet           │
 │ климат, осадки, рельеф  │──────────────────────────────┐                │
 └─────────┬───────────────┘                              │                │
           │ NPP, урожайность                             ▼                │
 ┌─────────▼───────────────┐  Famine, Plague,     ┌───────────────┐        │
 │ Жизнь / Хозяйство       │  MegafaunaGone       │   ВЕРА        │ Energy │
 │ фауна, пища, торговля   │─────────────────────▶│ толкование,   │────────┘
 └─────────┬───────────────┘  TradeRoute (каналы) │ доктрины,     │
           │                                       │ культы,       │
           │ Pop, излишек                          │ расколы,      │
 ┌─────────▼───────────────┐  GroupFissioned       │ отношение     │
 │ Группы / Демография     │──────────────────────▶│               │
 └─────────┬───────────────┘                       └──┬─────┬──────┘
           │ изоляция                                 │     │ Schism, CultFounded,
 ┌─────────▼───────────────┐  LanguageSplit           │     │ HolyWar-повод, Martyrdom
 │ Языки и имена           │──── intelligibility ────▶│     ▼
 └─────────────────────────┘  (контакт ×)             │  ┌───────────────┐
           ▲                                          │  │ Политика      │ War, Revolt,
           └──── PolitySplit / Annexation ────────────┼──│ войны, Турчин │ LeaderStruck
                                                      │  └──────┬────────┘
                                                      └─────────┘ Battle, Collapse → толкуются
```

## Таблица подписок

| Событие | Кто порождает | Кто слушает и что делает |
|---|---|---|
| `Intervention` | Creator | Planet (меняет климат/рельеф), Life (вымирание при метеорите), Faith (anomaly-флаг), Chronicle |
| `NaturalDrought`, `Drought` | Planet | Economy (урожай), Faith (толкование) |
| `Rain` | Creator | Planet (осадки), Faith (дар/сделка) |
| `LightningStrike` | Creator, Planet (редко) | Politics (`LeaderStruck`), Faith (кара/гнев/знамение), Faith (святыня) |
| `Harvest` (чудо) | Creator | Economy (×урожай), Faith (дар/сделка/избирательность у соседей) |
| `Vision` | Creator | Faith (пророк, культ) |
| `MeteorImpact` | Creator | Planet (кратер, похолодание), Life (вымирание), Groups (гибель), Faith (знамение конца) |
| `Famine` | Economy | Faith (толкование вреда, D↑), Politics (MMP, tension), Groups (смертность) |
| `Plague` | Life | Faith, Groups |
| `GroupFissioned` | Groups | Language (будущий раскол), Faith (вера наследуется) |
| `LanguageSplit` | Language | Faith (контакт × intelligibility), Chronicle |
| `TradeRoute` | Economy | Faith (канал SIR), Language (заимствования), Knowledge |
| `Interpretation` | Faith | Faith (эпитеты, вера, культы), Politics (кара на династию → легитимность), Chronicle |
| `CultFounded`, `ProphetBorn` | Faith | Politics (напряжённость, преследование → `Martyrdom`) |
| `Schism` | Faith | Politics (религиозная дистанция → войны), Chronicle |
| `Conversion` | Faith | Politics (легитимность), Chronicle |
| `AttitudeShift` | Faith | Creator (энергия), Chronicle |
| `WarDeclared`, `HolyWar`, `Battle` | Politics | Faith (вина → кара; победа → дар), Economy (разорение), Groups (потери) |
| `Revolt`, `PolitySplit` | Politics | Language (раскол), Faith (новая вера мятежников) |
| `LeaderDied`, `LeaderStruck` | Politics | Faith (толкование), Chronicle |
| `AnomalyNoticed` | Faith | Chronicle (семечко Э7) |
| `Comet` | Planet | Faith (знамение; натурализм при знании) |

## Обратные связи (ради которых всё и затевается)

1. **Вера ↔ Создатель**: толкования → отношение → энергия → сила следующих вмешательств.
2. **Вера ↔ Политика**: раскол → религиозная дистанция → война → вина/победа → новые толкования.
3. **Язык ↔ Вера**: изоляция → новый язык → хуже понимание → культы не переходят → дивергенция доктрин → раскол → отделение политии → ещё один язык.
4. **Хозяйство ↔ Вера**: голод → кара → жертвы (траты пищи) → глубже голод; излишек → храм → CRED → рост веры.
5. **Планета ↔ Вера**: место чуда → святыня → паломничество/престиж → торговля → распространение веры.
