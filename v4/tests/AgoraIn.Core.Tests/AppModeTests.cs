using AgoraIn.Core;
using AgoraIn.Core.SelfTest;
using Xunit;

namespace AgoraIn.Core.Tests;

public class AppModeTests
{
    [Fact]
    public void ToDisplayName_三种模式返回中文显示名()
    {
        Assert.Equal("大屏模式", AppMode.LargeScreen.ToDisplayName());
        Assert.Equal("控制模式", AppMode.Control.ToDisplayName());
        Assert.Equal("教师模式", AppMode.Teacher.ToDisplayName());
    }

    [Fact]
    public void ToDisplayName_未知枚举值抛出异常()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((AppMode)999).ToDisplayName());
    }

    [Fact]
    public void ToDisplayName_全部枚举值非空且唯一()
    {
        var names = Enum.GetValues<AppMode>().Select(m => m.ToDisplayName()).ToList();

        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}

public class SelfTestReportTests
{
    [Fact]
    public void AllPassed_全部通过返回true()
    {
        var results = new[]
        {
            new SelfTestModuleResult("core", [new SelfTestItem("a", true)]),
            new SelfTestModuleResult("data", [new SelfTestItem("b", true), new SelfTestItem("c", true)]),
        };

        Assert.True(SelfTestReport.AllPassed(results));
    }

    [Fact]
    public void AllPassed_任一失败返回false()
    {
        var results = new[]
        {
            new SelfTestModuleResult("core", [new SelfTestItem("a", true)]),
            new SelfTestModuleResult("data", [new SelfTestItem("b", false, "boom")]),
        };

        Assert.False(SelfTestReport.AllPassed(results));
    }

    [Fact]
    public void AllPassed_空结果返回false()
    {
        Assert.False(SelfTestReport.AllPassed([]));
    }

    [Fact]
    public void ModulePassed_无自测项视为未通过()
    {
        Assert.False(new SelfTestModuleResult("empty", []).Passed);
    }
}
