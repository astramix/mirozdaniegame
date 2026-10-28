using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Events;
using Mirozdanie.Core.Planet;

namespace Mirozdanie.Core.Sim;

/// <summary>Сводка группы для отрисовки.</summary>
public readonly record struct GroupView(int Id, int Cell, float Pop, string Name, int Language, int Religion, int Polity, Attitude Attitude);

/// <summary>
/// Неизменяемый снимок для отрисовки (docs/design/e0-skeleton.md). Статические слои планеты — общие ссылки,
/// динамические — копии в заранее выделенные буферы. Два снимка чередуются (двойная буферизация).
/// </summary>
public sealed class WorldSnapshot
{
    public long Year;
    public Era Era;
    public int LifeTick;
    public PlanetData Planet = null!;
    public int[] Owner = Array.Empty<int>();
    public int[] Language = Array.Empty<int>();
    public int[] Religion = Array.Empty<int>();
    public int[] Polity = Array.Empty<int>();
    public byte[] Attitude = Array.Empty<byte>();
    public GroupView[] Groups = Array.Empty<GroupView>();
    public int GroupCount;
    public float CreatorEnergy;
    public float Kardashev;
    public WorldEvent[] Recent = new WorldEvent[50];
    public int RecentCount;
    public int Version;
}

public sealed class SnapshotPublisher
{
    private readonly WorldSnapshot[] _buf = { new(), new() };
    private int _front;
    private readonly object _lock = new();

    /// <summary>Последний опубликованный снимок (читается потоком отрисовки).</summary>
    public WorldSnapshot Front { get { lock (_lock) return _buf[_front]; } }

    /// <summary>Заполняет задний буфер из мира и меняет буферы местами. Без аллокаций после первого вызова.</summary>
    public WorldSnapshot Publish(World w)
    {
        var s = _buf[1 - _front];
        int n = w.Planet.EW * w.Planet.EH;
        if (s.Owner.Length != n)
        {
            s.Owner = new int[n]; s.Language = new int[n]; s.Religion = new int[n]; s.Polity = new int[n]; s.Attitude = new byte[n];
        }
        s.Planet = w.Planet;
        s.Year = w.Year; s.Era = w.Era; s.LifeTick = w.LifeTick;
        s.CreatorEnergy = w.CreatorEnergy; s.Kardashev = w.Kardashev;
        var owner = w.CellOwner;
        for (int c = 0; c < n; c++)
        {
            int g = owner.Length > c ? owner[c] : -1;
            s.Owner[c] = g;
            if (g < 0) { s.Language[c] = s.Religion[c] = s.Polity[c] = -1; s.Attitude[c] = 255; continue; }
            var gr = w.Groups[g];
            s.Language[c] = gr.LanguageId; s.Religion[c] = gr.ReligionId; s.Polity[c] = gr.PolityId; s.Attitude[c] = (byte)gr.Attitude;
        }
        if (s.Groups.Length < w.Groups.Count) s.Groups = new GroupView[Math.Max(64, w.Groups.Count * 2)];
        int k = 0;
        foreach (var g in w.Groups)
            if (g.Alive) s.Groups[k++] = new GroupView(g.Id, g.Cell, g.Pop, g.Name.Native, g.LanguageId, g.ReligionId, g.PolityId, g.Attitude);
        s.GroupCount = k;
        var log = w.Events.Log;
        int m = Math.Min(s.Recent.Length, log.Count);
        for (int i = 0; i < m; i++) s.Recent[i] = log[log.Count - m + i];
        s.RecentCount = m;
        s.Version++;
        lock (_lock) _front = 1 - _front;
        return s;
    }
}
