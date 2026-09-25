using AgoraIn.Core.SelfTest;

namespace AgoraIn.App.Services;

/// <summary>--selftest 命令行参数。</summary>
/// <param name="Modules">要运行的模块名；空列表 = 全部。</param>
/// <param name="OutputPath">结果报告落盘路径（可空）。GUI 子系统在无控制台环境下 stdout 不可见，门禁脚本用此参数留痕。</param>
public sealed record SelfTestOptions(IReadOnlyList<string> Modules, string? OutputPath)
{
    public static SelfTestOptions Parse(IReadOnlyList<string> args)
    {
        var modules = new List<string>();
        string? outputPath = null;

        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] is "--out" or "-o" && i + 1 < args.Count)
            {
                outputPath = args[i + 1];
                i++;
            }
            else
            {
                modules.Add(args[i]);
            }
        }

        return new SelfTestOptions(modules, outputPath);
    }
}

/// <summary>
/// --selftest 冒烟自测入口：注册各领域自测模块，按名筛选执行，全过返回 0。
/// 新领域模块（points / duty / rollcall……）在此追加即可。
/// </summary>
public static class SelfTestRunner
{
    public static IReadOnlyList<ISelfTestModule> DefaultModules { get; } =
    [
        new CoreSelfTestModule(),
        new DataSelfTestModule(),
    ];

    /// <summary>执行自测并输出结果；返回值作为进程退出码（0 = 全部通过）。</summary>
    public static int Run(SelfTestOptions options)
    {
        var modules = DefaultModules
            .Where(m => options.Modules.Count == 0
                        || options.Modules.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (options.Modules.Count > 0 && modules.Count == 0)
        {
            var message = $"--selftest: 未找到模块 [{string.Join(", ", options.Modules)}]，可用模块：{string.Join(", ", DefaultModules.Select(m => m.Name))}";
            Console.Error.WriteLine(message);
            WriteReport(options.OutputPath, [message]);
            return 2;
        }

        var results = new List<SelfTestModuleResult>();
        var lines = new List<string>();
        foreach (var module in modules)
        {
            lines.Add($"== selftest [{module.Name}] ==");
            List<SelfTestItem> items;
            try
            {
                items = module.Run().ToList();
            }
            catch (Exception ex)
            {
                items = [new SelfTestItem("模块执行", false, ex.ToString())];
            }

            foreach (var item in items)
            {
                lines.Add($"  {(item.Passed ? "PASS" : "FAIL")}  {item.Name}{(string.IsNullOrEmpty(item.Detail) ? "" : $"  — {item.Detail}")}");
            }

            results.Add(new SelfTestModuleResult(module.Name, items));
        }

        var allPassed = SelfTestReport.AllPassed(results);
        lines.Add(allPassed
            ? $"selftest OK：{results.Count} 个模块全部通过"
            : "selftest FAILED：存在失败项");

        foreach (var line in lines)
        {
            Console.WriteLine(line);
        }

        WriteReport(options.OutputPath, lines);
        return allPassed ? 0 : 1;
    }

    private static void WriteReport(string? outputPath, IReadOnlyList<string> lines)
    {
        if (string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllLines(outputPath, lines);
    }
}
