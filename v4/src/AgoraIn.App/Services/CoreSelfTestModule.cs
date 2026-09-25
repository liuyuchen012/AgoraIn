using AgoraIn.Core;
using AgoraIn.Core.SelfTest;

namespace AgoraIn.App.Services;

/// <summary>core 模块自测：领域基础契约（模式枚举显示名覆盖完整且唯一）。</summary>
public sealed class CoreSelfTestModule : ISelfTestModule
{
    public string Name => "core";

    public IReadOnlyList<SelfTestItem> Run(CancellationToken cancellationToken = default)
    {
        var items = new List<SelfTestItem>();

        var names = Enum.GetValues<AppMode>()
            .Select(m => (Mode: m, Name: m.ToDisplayName()))
            .ToList();

        items.Add(new SelfTestItem(
            "AppMode 显示名覆盖完整",
            names.Count == 3 && names.All(n => !string.IsNullOrWhiteSpace(n.Name)),
            string.Join(", ", names.Select(n => $"{n.Mode}={n.Name}"))));

        items.Add(new SelfTestItem(
            "AppMode 显示名无重复",
            names.Select(n => n.Name).Distinct().Count() == names.Count,
            $"共 {names.Select(n => n.Name).Distinct().Count()} 个唯一显示名"));

        return items;
    }
}
