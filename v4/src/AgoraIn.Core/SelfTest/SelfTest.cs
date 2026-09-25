namespace AgoraIn.Core.SelfTest;

/// <summary>单条自测项的结果。</summary>
/// <param name="Name">自测项名称。</param>
/// <param name="Passed">是否通过。</param>
/// <param name="Detail">失败原因或补充信息。</param>
public sealed record SelfTestItem(string Name, bool Passed, string Detail = "");

/// <summary>
/// 自测模块契约：桌面端 <c>--selftest [module]</c> 冒烟自测按模块扩展
/// （如 core / data / points……），每个领域模块实现一个 <see cref="ISelfTestModule"/>。
/// </summary>
public interface ISelfTestModule
{
    /// <summary>模块名，用于 <c>--selftest &lt;module&gt;</c> 按名筛选。</summary>
    string Name { get; }

    /// <summary>执行自测，返回逐项结果；实现应是确定性的、无需 UI 或网络。</summary>
    IReadOnlyList<SelfTestItem> Run(CancellationToken cancellationToken = default);
}

/// <summary>一个自测模块的完整结果。</summary>
public sealed record SelfTestModuleResult(string Module, IReadOnlyList<SelfTestItem> Items)
{
    /// <summary>模块内全部自测项是否通过。</summary>
    public bool Passed => Items.Count > 0 && Items.All(i => i.Passed);
}

/// <summary>多模块结果的汇总判定。</summary>
public static class SelfTestReport
{
    /// <summary>所有模块全部通过返回 true；任一模块无结果或存在失败项返回 false。</summary>
    public static bool AllPassed(IEnumerable<SelfTestModuleResult> results)
    {
        var any = false;
        foreach (var r in results)
        {
            if (!r.Passed)
            {
                return false;
            }

            any = true;
        }

        return any;
    }
}
