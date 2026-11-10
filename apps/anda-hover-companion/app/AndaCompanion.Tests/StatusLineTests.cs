using AndaCompanion.Data;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

public class StatusLineTests
{
    private static readonly MockAndaRepository Repo = new();

    [Fact]
    public void At_risk_record_includes_the_action_and_a_plain_text_delta()
    {
        string line = StatusLine.Build(Repo.Find("216482")!);
        Assert.StartsWith("ANDA 216482 (Metformin HCl ER Tablets, 500 mg): Cycle 2, ", line);
        Assert.Contains("62% through review", line);
        Assert.Contains("goal Oct 3, 2026", line);
        Assert.Contains("forecast Oct 17, 2026 (+14 d vs goal, moderate confidence)", line);
        Assert.Contains("needs action: IR response due in 5 days", line);
        Assert.EndsWith(".", line);
    }

    [Fact]
    public void Healthy_record_has_no_action_clause()
    {
        string line = StatusLine.Build(Repo.Find("214990")!);
        Assert.DoesNotContain("needs action", line);
        Assert.Contains("9 d ahead of goal", line);
    }

    [Fact]
    public void Pasted_text_never_contains_card_glyphs()
    {
        foreach (var n in new[] { "216482", "214990" })
        {
            string line = StatusLine.Build(Repo.Find(n)!);
            Assert.DoesNotContain('▲', line);
            Assert.DoesNotContain('▼', line);
            Assert.DoesNotContain("null", line, StringComparison.OrdinalIgnoreCase);
        }
    }
}
