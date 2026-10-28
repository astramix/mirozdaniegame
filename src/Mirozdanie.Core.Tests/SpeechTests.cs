using Mirozdanie.Core.Creator;
using Mirozdanie.Core.Culture;
using Mirozdanie.Core.Sim;

namespace Mirozdanie.Core.Tests;

/// <summary>Речь народов (docs/design/lore.md, §12.6).</summary>
public class SpeechTests
{
    private static readonly Lazy<Simulation> World1 = new(() =>
    {
        var sim = Simulation.Create(42, WorldParams.FromPreset("earth"));
        sim.Run(1200);
        return sim;
    });

    private static List<Group> Peoples(int n) => World1.Value.World.Groups.Where(g => g.Alive).Take(n).ToList();

    [Fact]
    public void ReplyVariety()
    {
        var w = World1.Value.World;
        var replies = new HashSet<string>();
        var attitudes = Enum.GetValues<Attitude>();
        int i = 0;
        foreach (var g in Peoples(50))
        {
            var saved = g.Attitude;
            g.Attitude = attitudes[i++ % attitudes.Length];
            replies.Add(Dialogue.Reply(w, g, DialogueAction.Bless).Translation);
            g.Attitude = saved;
        }
        Assert.True(replies.Count >= 30, $"различных ответов на благословение: {replies.Count}");
    }

    [Fact]
    public void NoRepeatsWithinConversation()
    {
        var w = World1.Value.World;
        var g = Peoples(1)[0];
        var conv = new Conversation();
        var seen = new HashSet<string>();
        for (int k = 0; k < 10; k++) Assert.True(seen.Add(Dialogue.Reply(w, g, DialogueAction.Bless, "", conv).Translation), $"повтор на реплике {k}");
    }

    [Fact]
    public void ReplyRelevance()
    {
        var w = World1.Value.World;
        var g = Peoples(3)[2];
        long saveP = g.LastPlague; int saveF = g.FamineYears; float saveR = g.FoodRatio; long lp = g.LastPrayer, pa = g.PrayerAnswered; var att = g.Attitude;
        try
        {
            g.Attitude = Attitude.Love;
            g.LastPlague = -1000; g.FamineYears = 3; g.FoodRatio = 0.5f;
            var hungry = Dialogue.Opening(w, g).Translation.ToLowerInvariant();
            Assert.True(hungry.Contains("голод") || hungry.Contains("хлеб") || hungry.Contains("есть") || hungry.Contains("зерно") || hungry.Contains("колос"), hungry);
            g.FamineYears = 0; g.FoodRatio = 1.4f; g.LastPlague = w.Year - 1;
            var plague = Dialogue.Opening(w, g).Translation.ToLowerInvariant();
            Assert.True(plague.Contains("мор") || plague.Contains("болезн") || plague.Contains("мёртв"), plague);
            // Безответная молитва — довод о молчании Создателя.
            g.LastPlague = -1000; g.LastPrayer = w.Year - 4; g.PrayerAnswered = -1;
            bool silent = false;
            var conv = new Conversation();
            for (int k = 0; k < 6 && !silent; k++) silent = Dialogue.Reply(w, g, DialogueAction.Ask, "", conv).Translation.Contains("молчал");
            Assert.True(silent, "народ не вспомнил безответную молитву");
        }
        finally { g.LastPlague = saveP; g.FamineYears = saveF; g.FoodRatio = saveR; g.LastPrayer = lp; g.PrayerAnswered = pa; g.Attitude = att; }
    }

    [Fact]
    public void ReplyDeterministic()
    {
        var w = World1.Value.World;
        foreach (var g in Peoples(5))
        {
            var a = Dialogue.Reply(w, g, DialogueAction.Appear, "", new Conversation());
            var b = Dialogue.Reply(w, g, DialogueAction.Appear, "", new Conversation());
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void ReplyLength()
    {
        var w = World1.Value.World;
        int total = 0, n = 0;
        foreach (var g in Peoples(30))
        {
            var t = Dialogue.Reply(w, g, DialogueAction.Bless).Translation;
            int sentences = t.Count(ch => ch is '.' or '!' or '?' or '…');
            total += sentences; n++;
        }
        float avg = total / (float)n;
        Assert.InRange(avg, 2f, 9f);
    }
}
