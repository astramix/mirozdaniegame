using System;
using System.Collections.Generic;
using Godot;

namespace Mirozdanie.Game;

/// <summary>
/// Процедурный звук: все эффекты и фоновая музыка синтезируются кодом (без внешних файлов).
/// Синтез — простые осцилляторы, шум, огибающие и однополюсные фильтры.
/// </summary>
public partial class Sound : Node
{
    private const int Rate = 22050;
    private readonly Dictionary<string, AudioStreamWav> _bank = new();
    private readonly List<AudioStreamPlayer> _players = new();
    private AudioStreamPlayer _music = null!;
    private AudioStreamPlayer _loop = null!;
    private string _loopName = "";
    private double _lastPlay;
    private int _next;
    public float EffectsVolume = 0.8f;
    public float MusicVolume = 0.45f;
    public bool Muted;

    public override void _Ready()
    {
        for (int i = 0; i < 8; i++)
        {
            var p = new AudioStreamPlayer();
            AddChild(p);
            _players.Add(p);
        }
        _music = new AudioStreamPlayer();
        AddChild(_music);
        _loop = new AudioStreamPlayer();
        AddChild(_loop);
        // Генерация звуков — несколько сотен миллисекунд; музыка — отдельно, при первом запросе.
        _bank["click"] = Make(0.06, (t, r) => Sine(1800, t) * Env(t, 0.002, 0.05) * 0.4);
        _bank["hover"] = Make(0.04, (t, r) => Sine(2600, t) * Env(t, 0.002, 0.03) * 0.15);
        _bank["thunder"] = Make(2.2, Thunder);
        _bank["rain"] = Make(2.5, Rain);
        _bank["chime"] = Make(2.0, (t, r) => Bell(t, 660) + 0.6 * Bell(t - 0.12, 990) + 0.4 * Bell(t - 0.24, 1320));
        _bank["choir"] = Make(3.0, Choir);
        _bank["rumble"] = Make(2.5, Rumble);
        _bank["boom"] = Make(3.0, Boom);
        _bank["drum"] = Make(1.6, Drums);
        _bank["bell"] = Make(2.5, (t, r) => Bell(t, 440) * 1.2);
        _bank["gong"] = Make(3.0, (t, r) => (Sine(110, t) + 0.6 * Sine(173, t) + 0.4 * Sine(247, t)) * Math.Exp(-t * 1.1) * Env(t, 0.01, 3.0) * 0.5);
        _bank["wind"] = Make(3.0, Wind);
        _bank["whoosh"] = Make(1.2, (t, r) => Noise(r) * Math.Sin(Math.PI * t / 1.2) * 0.5);
        _bank["harp"] = Make(1.8, (t, r) => Pluck(t, 523) + 0.7 * Pluck(t - 0.09, 659) + 0.6 * Pluck(t - 0.18, 784));
        _bank["dark"] = Make(2.5, (t, r) => (Sine(73, t) + 0.5 * Sine(77.8, t)) * Env(t, 0.3, 2.5) * 0.6);
        _bank["create"] = Make(4.0, (t, r) => Choir(t, r) * 0.7 + 0.5 * (Bell(t - 0.4, 392) + Bell(t - 0.9, 587) + Bell(t - 1.4, 784)));
        // Земля (docs/design/terraforming.md, §6)
        _bank["mountain"] = Make(3.5, Mountain);
        _bank["wave"] = Make(4.5, Wave);
        _bank["volcano"] = Make(4.5, Volcano);
        _bank["drift"] = Make(4.5, Drift);
        _bank["frost"] = Make(2.5, (t, r) => Bell(t, 1568) * 0.6 + Bell(t - 0.07, 2093) * 0.5 + Bell(t - 0.15, 2637) * 0.4 + Bell(t - 0.3, 3136) * 0.3);
        _bank["growth"] = Make(2.5, (t, r) => Pluck(t, 392) + 0.8 * Pluck(t - 0.15, 494) + 0.7 * Pluck(t - 0.3, 587) + 0.6 * Pluck(t - 0.45, 784) + 0.3 * Choir(t, r));
        _bank["heat"] = Make(2.5, Crackle);
        _bank["splash"] = Make(1.5, Splash);
        _loops["earth"] = MakeLoop(4.0, EarthLoop);
        _loops["air"] = MakeLoop(4.0, AirLoop);
    }

    private readonly Dictionary<string, AudioStreamWav> _loops = new();

    /// <summary>Бесшовная петля: синтез на 0,25 с длиннее, хвост плавно вмешивается в начало (без щелчка на стыке).</summary>
    private static AudioStreamWav MakeLoop(double seconds, Synth f)
    {
        int n = (int)(seconds * Rate), x = Rate / 4;
        var rng = new System.Random(4321);
        var buf = new double[n + x];
        for (int i = 0; i < buf.Length; i++) buf[i] = f(i / (double)Rate, rng);
        double peak = 1e-6;
        var outp = new double[n];
        for (int i = 0; i < n; i++)
        {
            double v = i < x ? buf[i] * (i / (double)x) + buf[n + i] * (1 - i / (double)x) : buf[i];
            outp[i] = v;
            peak = Math.Max(peak, Math.Abs(v));
        }
        var data = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            short sv = (short)Math.Clamp(outp[i] * 0.85 / peak * 32767, -32767, 32767);
            data[2 * i] = (byte)(sv & 0xFF);
            data[2 * i + 1] = (byte)((sv >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Data = data, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopBegin = 0, LoopEnd = n,
        };
    }

    /// <summary>Петля, пока ведётся кисть: гул земли или шорох воздуха. Громкость и высота — по силе мазка.</summary>
    public void Loop(string name, float volume, float pitch = 1f)
    {
        if (Muted || volume <= 0.001f || !_loops.TryGetValue(name, out var s)) { StopLoop(); return; }
        if (_loopName != name) { _loop.Stream = s; _loopName = name; _loop.Play(); }
        else if (!_loop.Playing) _loop.Play();
        _loop.VolumeDb = Mathf.LinearToDb(Math.Max(0.001f, EffectsVolume * volume));
        _loop.PitchScale = pitch;
    }

    public void StopLoop()
    {
        if (_loop.Playing) _loop.Stop();
        _loopName = "";
    }

    // ---------- Звуки земли ----------

    private static double _lp2, _lp3;

    /// <summary>Низкий гул с перекатами: шум через два однополюсных фильтра, медленная модуляция (петля без щелчка: синус по длине).</summary>
    private static double EarthLoop(double t, System.Random r)
    {
        if (t == 0) { _lp1 = 0; _lp2 = 0; }
        _lp1 += 0.02 * (Noise(r) - _lp1);
        _lp2 += 0.08 * (Noise(r) - _lp2);
        double mod = 0.7 + 0.3 * Math.Sin(2 * Math.PI * t / 4.0 * 3) * Math.Sin(2 * Math.PI * t / 4.0 * 5);
        double grind = r.NextDouble() < 0.004 ? Noise(r) * 0.6 : 0;
        return (_lp1 * 9 + _lp2 * 1.2 + grind) * mod + 0.25 * Sine(41, t) * mod;
    }

    private static double AirLoop(double t, System.Random r)
    {
        if (t == 0) _lp1 = 0;
        _lp1 += 0.12 * (Noise(r) - _lp1);
        double swell = 0.6 + 0.4 * Math.Sin(2 * Math.PI * t / 4.0 * 2);
        return _lp1 * swell * 0.8 + 0.05 * Sine(880, t) * Math.Sin(2 * Math.PI * t / 4.0);
    }

    /// <summary>Рост гор: треск камня и нарастающий низкий стон.</summary>
    private static double Mountain(double t, System.Random r)
    {
        if (t == 0) { _lp1 = 0; _lp2 = 0; }
        _lp1 += 0.015 * (Noise(r) - _lp1);
        double crack = 0;
        for (int k = 0; k < 4; k++)
        {
            double at = 0.05 + k * 0.45;
            if (t > at && t < at + 0.12) crack += Noise(r) * (1 - (t - at) / 0.12) * 0.6;
        }
        double groan = Sine(38 + 25 * Math.Min(1, t / 2.5), t) * Env(t, 1.2, 3.5) * 0.6;
        return _lp1 * 10 * Env(t, 0.8, 3.5) + crack + groan;
    }

    /// <summary>Большая волна: нарастающий шум воды и обрушение.</summary>
    private static double Wave(double t, System.Random r)
    {
        if (t == 0) { _lp1 = 0; _lp2 = 0; }
        _lp1 += 0.05 * (Noise(r) - _lp1);
        _lp2 += 0.4 * (Noise(r) - _lp2);
        double swell = Math.Pow(Math.Sin(Math.PI * Math.Min(1, t / 3.2)), 2);
        double crash = t > 2.2 ? Math.Exp(-(t - 2.2) * 1.6) : 0;
        return _lp1 * 5 * swell + _lp2 * crash * 0.9 + Sine(55, t) * swell * 0.3;
    }

    /// <summary>Извержение: удар, треск огня, шипение пара.</summary>
    private static double Volcano(double t, System.Random r)
    {
        if (t == 0) { _lp1 = 0; _lp3 = 0; }
        _lp1 += 0.02 * (Noise(r) - _lp1);
        _lp3 += 0.6 * (Noise(r) - _lp3);
        double boom = Sine(48 * Math.Exp(-t * 0.8), t) * Math.Exp(-t * 1.2) * 1.3 + _lp1 * 12 * Math.Exp(-t * 0.9);
        double crackle = r.NextDouble() < 0.01 * Math.Exp(-t * 0.3) ? Noise(r) * 0.9 : 0;
        double hiss = t > 0.8 ? _lp3 * 0.25 * Env(t - 0.8, 0.5, 3.7) : 0;
        return boom + crackle + hiss;
    }

    /// <summary>Дрейф материка: долгий скрежет и низкие биения.</summary>
    private static double Drift(double t, System.Random r)
    {
        if (t == 0) { _lp1 = 0; _lp2 = 0; }
        _lp1 += 0.01 * (Noise(r) - _lp1);
        _lp2 += 0.2 * (Noise(r) - _lp2);
        double scrape = _lp2 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 7 * t)) * 0.4;
        double beat = (Sine(36, t) + Sine(37.5, t)) * 0.4;
        return (_lp1 * 10 + scrape + beat) * Env(t, 1.0, 4.5);
    }

    private static double Crackle(double t, System.Random r)
    {
        if (t == 0) _lp1 = 0;
        _lp1 += 0.3 * (Noise(r) - _lp1);
        double pop = r.NextDouble() < 0.006 ? Noise(r) : 0;
        return (_lp1 * 0.3 + pop) * Env(t, 0.2, 2.5);
    }

    private static double Splash(double t, System.Random r)
    {
        if (t == 0) _lp1 = 0;
        _lp1 += 0.5 * (Noise(r) - _lp1);
        return _lp1 * Math.Exp(-t * 3) * Math.Min(1, t * 80) + Sine(180 * Math.Exp(-t * 3), t) * Math.Exp(-t * 5) * 0.4;
    }

    // ---------- Синтез ----------

    private delegate double Synth(double t, System.Random rng);

    private static AudioStreamWav Make(double seconds, Synth f)
    {
        int n = (int)(seconds * Rate);
        var data = new byte[n * 2];
        var rng = new System.Random(12345);
        double peak = 1e-6;
        var buf = new double[n];
        for (int i = 0; i < n; i++)
        {
            double v = f(i / (double)Rate, rng);
            buf[i] = v;
            peak = Math.Max(peak, Math.Abs(v));
        }
        double norm = peak > 0.9 ? 0.9 / peak : 1.0;
        for (int i = 0; i < n; i++)
        {
            short s = (short)Math.Clamp(buf[i] * norm * 32767, -32767, 32767);
            data[2 * i] = (byte)(s & 0xFF);
            data[2 * i + 1] = (byte)((s >> 8) & 0xFF);
        }
        return new AudioStreamWav { Data = data, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false };
    }

    private static double Sine(double f, double t) => Math.Sin(2 * Math.PI * f * t);
    private static double Noise(System.Random r) => r.NextDouble() * 2 - 1;

    /// <summary>Огибающая: атака a, затухание до длины len.</summary>
    private static double Env(double t, double a, double len)
    {
        if (t < 0) return 0;
        if (t < a) return t / a;
        return Math.Max(0, 1 - (t - a) / Math.Max(1e-6, len - a));
    }

    private static double Bell(double t, double f)
    {
        if (t < 0) return 0;
        // Неровные обертоны колокола.
        return (Sine(f, t) + 0.5 * Sine(f * 2.76, t) + 0.25 * Sine(f * 5.4, t)) * Math.Exp(-t * 2.2) * Math.Min(1, t * 400) * 0.35;
    }

    private static double Pluck(double t, double f)
    {
        if (t < 0) return 0;
        return (Sine(f, t) + 0.3 * Sine(2 * f, t)) * Math.Exp(-t * 4) * Math.Min(1, t * 600) * 0.4;
    }

    private static double _lp1;

    private static double Thunder(double t, System.Random r)
    {
        if (t == 0) _lp1 = 0;
        // Треск в начале и раскатистый низкий шум.
        double crack = t < 0.15 ? Noise(r) * (1 - t / 0.15) : 0;
        _lp1 += 0.03 * (Noise(r) - _lp1);
        double roll = _lp1 * 6 * Math.Exp(-t * 1.3) * (1 + 0.5 * Math.Sin(t * 9));
        return crack * 0.8 + roll;
    }

    private static double Rain(double t, System.Random r)
    {
        if (t == 0) _lp1 = 0;
        _lp1 += 0.35 * (Noise(r) - _lp1);
        double drops = r.NextDouble() < 0.002 ? 0.8 : 0;
        return (_lp1 * 0.5 + drops * Noise(r)) * Math.Sin(Math.PI * t / 2.5);
    }

    private static double Choir(double t, System.Random r)
    {
        // Аккорд ре-минор с медленным вибрато и мягкой атакой — «хор».
        double v = 0;
        double[] f = { 146.8, 220.0, 293.7, 349.2, 440.0 };
        foreach (var x in f)
        {
            double vib = 1 + 0.004 * Math.Sin(2 * Math.PI * 5 * t + x);
            v += Sine(x * vib, t) + 0.3 * Sine(2 * x * vib, t);
        }
        return v * Env(t, 0.6, 3.0) * 0.12;
    }

    private static double Rumble(double t, System.Random r)
    {
        if (t == 0) _lp1 = 0;
        _lp1 += 0.01 * (Noise(r) - _lp1);
        return _lp1 * 14 * Env(t, 0.3, 2.5) * (1 + 0.6 * Math.Sin(t * 23));
    }

    private static double Boom(double t, System.Random r)
    {
        if (t == 0) _lp1 = 0;
        _lp1 += 0.02 * (Noise(r) - _lp1);
        double whoosh = t < 0.8 ? Noise(r) * 0.3 * (t / 0.8) : 0;
        double hit = t >= 0.8 ? (Sine(45 * Math.Exp(-(t - 0.8) * 2), t) * 1.2 + _lp1 * 10) * Math.Exp(-(t - 0.8) * 1.5) : 0;
        return whoosh + hit;
    }

    private static double Drums(double t, System.Random r)
    {
        double v = 0;
        double[] hits = { 0, 0.35, 0.7, 0.9 };
        foreach (var h in hits)
        {
            double dt = t - h;
            if (dt < 0) continue;
            v += Sine(70 * Math.Exp(-dt * 6), dt) * Math.Exp(-dt * 7);
        }
        return v * 0.8;
    }

    private static double Wind(double t, System.Random r)
    {
        if (t == 0) _lp1 = 0;
        double cutoff = 0.02 + 0.03 * (1 + Math.Sin(t * 2.1));
        _lp1 += cutoff * (Noise(r) - _lp1);
        return _lp1 * 3 * Math.Sin(Math.PI * t / 3.0);
    }

    // ---------- Музыка ----------

    /// <summary>Настроение музыки — по состоянию мира (реактивная музыка, этап F).</summary>
    public enum Mood { Calm, War, Wonder, Dread }

    private readonly Dictionary<Mood, AudioStreamWav> _moods = new();
    private AudioStreamPlayer _music2 = null!;
    private Mood _mood = Mood.Calm;
    private double _fade = 1; // 1 — слышен _music, 0 — _music2
    private bool _fadeToSecond;

    /// <summary>Фоновая музыка: начинаем с «покоя»; смена настроения — плавным перекрёстным затуханием за 4 с.</summary>
    public void StartMusic()
    {
        if (_music.Stream != null) { if (!_music.Playing) _music.Play(); return; }
        if (_music2 == null) { _music2 = new AudioStreamPlayer(); AddChild(_music2); }
        _music.Stream = MoodStream(Mood.Calm);
        ApplyVolumes();
        _music.Play();
    }

    public void SetMood(Mood m)
    {
        if (m == _mood || _music.Stream == null || Muted) return;
        _mood = m;
        // Новое настроение заходит на свободный плеер, старое уходит.
        var incoming = _fade >= 0.5 ? _music2 : _music;
        incoming.Stream = MoodStream(m);
        incoming.VolumeDb = -60;
        incoming.Play();
        _fadeToSecond = incoming == _music2;
    }

    public override void _Process(double delta)
    {
        if (_music2 == null || _music.Stream == null) return;
        double target = _fadeToSecond ? 0 : 1;
        _fade += Math.Sign(target - _fade) * Math.Min(Math.Abs(target - _fade), delta / 4.0);
        float vol = Muted || MusicVolume <= 0.001f ? 0f : MusicVolume;
        _music.VolumeDb = Mathf.LinearToDb(Math.Max(0.0001f, vol * (float)_fade));
        _music2.VolumeDb = Mathf.LinearToDb(Math.Max(0.0001f, vol * (float)(1 - _fade)));
        if (_fade <= 0.001 && _music.Playing) _music.Stop();
        if (_fade >= 0.999 && _music2.Playing) _music2.Stop();
    }

    /// <summary>
    /// Петля настроения ≈ 48 с, всё синтезом: покой — дорийские аккорды и колокольчики пентатоники;
    /// война — минор, низкий барабан на каждую долю и квинты; чудо — мажор с высокими колокольчиками;
    /// страх — гудящая кластерная педаль и редкие удары.
    /// </summary>
    private AudioStreamWav MoodStream(Mood m)
    {
        if (_moods.TryGetValue(m, out var cached)) return cached;
        double len = 48;
        int n = (int)(len * Rate);
        var buf = new double[n];
        double[][] chords = m switch
        {
            Mood.War => new[] { new[] { 110.0, 164.8, 220.0, 261.6 }, new[] { 98.0, 146.8, 196.0, 233.1 }, new[] { 87.3, 130.8, 174.6, 220.0 }, new[] { 82.4, 123.5, 164.8, 207.7 } },
            Mood.Wonder => new[] { new[] { 130.8, 196.0, 261.6, 329.6 }, new[] { 174.6, 220.0, 261.6, 349.2 }, new[] { 146.8, 220.0, 293.7, 370.0 }, new[] { 196.0, 246.9, 293.7, 392.0 } },
            Mood.Dread => new[] { new[] { 61.7, 65.4, 92.5, 98.0 }, new[] { 58.3, 65.4, 87.3, 92.5 } },
            _ => new[] { new[] { 146.8, 220.0, 293.7, 349.2 }, new[] { 130.8, 196.0, 261.6, 329.6 }, new[] { 116.5, 174.6, 233.1, 293.7 }, new[] { 130.8, 196.0, 246.9, 329.6 } },
        };
        double chordLen = len / (m == Mood.War ? 12 : m == Mood.Dread ? 4 : 8);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            int ci = (int)(t / chordLen) % chords.Length;
            double ct = t % chordLen;
            double env = Math.Sin(Math.PI * ct / chordLen);
            double v = 0;
            foreach (var f in chords[ci]) v += Sine(f, t) * 0.5 + Sine(f * 1.003, t) * 0.5;
            if (m == Mood.Dread) v *= 0.8 + 0.2 * Math.Sin(2 * Math.PI * 0.25 * t);
            buf[i] = v * env * (m == Mood.Dread ? 0.06 : 0.05);
        }
        var rng = new System.Random(7 + (int)m);
        if (m == Mood.War)
        {
            // Барабан: низкий удар каждые 0,6 с, на четвёртую долю — двойной.
            for (double at = 0; at < len; at += 0.6)
            {
                int s0 = (int)(at * Rate);
                bool dbl = ((int)(at / 0.6)) % 4 == 3;
                for (int rep = 0; rep < (dbl ? 2 : 1); rep++)
                    for (int k = 0; k < Rate * 0.35 && s0 + k + rep * Rate / 8 < n; k++)
                    {
                        double tt = k / (double)Rate;
                        buf[s0 + k + rep * Rate / 8] += Math.Sin(2 * Math.PI * (55 + 60 * Math.Exp(-tt * 18)) * tt) * Math.Exp(-tt * 9) * 0.32;
                    }
            }
        }
        double[] penta = m switch
        {
            Mood.Wonder => new[] { 1046.5, 1174.7, 1318.5, 1568.0, 1760.0 },
            Mood.War => new[] { 440.0, 523.3, 587.3, 659.3 },
            Mood.Dread => new[] { 233.1, 246.9 },
            _ => new[] { 587.3, 659.3, 784.0, 880.0, 1046.5 },
        };
        double gapMin = m == Mood.Wonder ? 0.6 : m == Mood.Dread ? 4.0 : 1.2, gapVar = m == Mood.Dread ? 5.0 : 2.5;
        for (double at = 1.5; at < len - 3; at += gapMin + rng.NextDouble() * gapVar)
        {
            double f = penta[rng.Next(penta.Length)];
            int s0 = (int)(at * Rate);
            for (int k = 0; k < Rate * 3 && s0 + k < n; k++) buf[s0 + k] += Bell(k / (double)Rate, f) * (m == Mood.Wonder ? 0.28 : 0.35);
        }
        var data = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            short s = (short)Math.Clamp(buf[i] * 32767, -32767, 32767);
            data[2 * i] = (byte)(s & 0xFF);
            data[2 * i + 1] = (byte)((s >> 8) & 0xFF);
        }
        var stream = new AudioStreamWav
        {
            Data = data, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopBegin = 0, LoopEnd = n,
        };
        _moods[m] = stream;
        return stream;
    }

    public void ApplyVolumes()
    {
        _music.VolumeDb = Muted || MusicVolume <= 0.001f ? -80 : Mathf.LinearToDb(MusicVolume);
        if (_music2 != null && Muted) _music2.VolumeDb = -80;
        foreach (var p in _players) p.VolumeDb = Muted || EffectsVolume <= 0.001f ? -80 : Mathf.LinearToDb(EffectsVolume);
        if (Muted) StopLoop();
    }

    /// <summary>Проиграть эффект. minGap — не чаще, чем раз в столько секунд (чтобы поток событий не превращался в шум).</summary>
    public void Play(string name, double minGap = 0.12, float pitch = 1f)
    {
        if (Muted || !_bank.TryGetValue(name, out var s)) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - _lastPlay < minGap) return;
        _lastPlay = now;
        var p = _players[_next];
        _next = (_next + 1) % _players.Count;
        p.Stream = s;
        p.PitchScale = pitch;
        p.VolumeDb = Mathf.LinearToDb(Math.Max(0.001f, EffectsVolume));
        p.Play();
    }
}
