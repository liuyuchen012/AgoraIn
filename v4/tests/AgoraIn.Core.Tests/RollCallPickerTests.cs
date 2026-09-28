using AgoraIn.Core.Domain;
using Xunit;

namespace AgoraIn.Core.Tests;

/// <summary>点名落点算法（随机加权 / 顺序轮转）单元测试。</summary>
public class RollCallPickerTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 10, 0, 0);

    [Fact]
    public void PickWeighted_空名单_返回null()
    {
        Assert.Null(RollCallPicker.PickWeighted([], Now, new Random(1)));
    }

    [Fact]
    public void PickWeighted_单人_必返回该人()
    {
        var candidates = new[] { new RollCallCandidate("s1", null, 0) };
        Assert.Equal("s1", RollCallPicker.PickWeighted(candidates, Now, new Random(1)));
    }

    [Fact]
    public void PickWeighted_近期未点过的学生_被点概率显著更高()
    {
        // s1 刚被点过，s2 从未被点：加权后 s2 应在绝大多数抽样中胜出
        var candidates = new[]
        {
            new RollCallCandidate("s1", Now.AddSeconds(-10), 5),
            new RollCallCandidate("s2", null, 0),
        };
        var rng = new Random(42);
        var s2Count = Enumerable.Range(0, 1000)
            .Count(_ => RollCallPicker.PickWeighted(candidates, Now, rng) == "s2");
        // 加权保证"人人可达"（保底权重 1），此处断言从未被点过的学生占绝对优势（≈86%）
        Assert.True(s2Count > 700, $"s2 仅被点 {s2Count}/1000 次，加权未生效");
    }

    [Fact]
    public void PickWeighted_从未被点过的学生_不会被完全排除()
    {
        // 即使一个学生连续被点，权重仍有保底 1，绝无零概率
        var candidates = new[]
        {
            new RollCallCandidate("s1", Now, 10),
            new RollCallCandidate("s2", Now, 10),
        };
        var picked = new HashSet<string>();
        var rng = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var p = RollCallPicker.PickWeighted(candidates, Now, rng);
            Assert.NotNull(p);
            picked.Add(p!);
        }
        Assert.Equal(2, picked.Count);
    }

    [Fact]
    public void PickSequential_按被点次数轮转_并列保持名单顺序()
    {
        var candidates = new[]
        {
            new RollCallCandidate("a", null, 0),
            new RollCallCandidate("b", null, 0),
            new RollCallCandidate("c", null, 0),
        };
        // 三人次数相同：应严格按传入顺序轮转
        Assert.Equal("a", RollCallPicker.PickSequential(candidates));

        var afterA = new[]
        {
            new RollCallCandidate("a", Now, 1),
            new RollCallCandidate("b", null, 0),
            new RollCallCandidate("c", null, 0),
        };
        Assert.Equal("b", RollCallPicker.PickSequential(afterA));

        var afterAB = new[]
        {
            new RollCallCandidate("a", Now, 1),
            new RollCallCandidate("b", Now, 1),
            new RollCallCandidate("c", null, 0),
        };
        Assert.Equal("c", RollCallPicker.PickSequential(afterAB));
    }

    [Fact]
    public void PickSequential_空名单_返回null()
    {
        Assert.Null(RollCallPicker.PickSequential([]));
    }
}
