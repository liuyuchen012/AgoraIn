using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgoraIn.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace AgoraIn.Server.Services;

/// <summary>批改策略（按试卷）。</summary>
public sealed record GradingPolicy(
    bool SplitEnabled,
    bool DoubleGrading,
    double ArbitrationThreshold)
{
    public static GradingPolicy Default { get; } = new(false, false, 1.0);
}

/// <summary>批改分配：把指定题号（可多个）的一定比例答卷分给某位教师。</summary>
public sealed record GradingAssignment(
    string TeacherUsername,
    List<int> QuestionNos,
    double Percent,
    int Kind)   // 0=批改 1=仲裁（资深教师；双判分差超阈值时接手）
{
    public const int KindGrading = 0;
    public const int KindArbitration = 1;
}

/// <summary>
/// 批改分配 / 双判 / 仲裁的配置与路由。
/// 配置存 AppSettings（region 隔离自动生效），避免动数据库结构：
///   grading.policy.{paperId}     = GradingPolicy
///   grading.assign.{paperId}     = GradingAssignment[]
/// </summary>
public sealed class GradingPolicyService(ServerDbContext db)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private const string PolicyPrefix = "grading.policy.";
    private const string AssignPrefix = "grading.assign.";

    public async Task<GradingPolicy> LoadPolicyAsync(string paperId, CancellationToken ct = default)
    {
        var row = await db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Key == PolicyPrefix + paperId, ct);
        if (row == null || string.IsNullOrWhiteSpace(row.Value)) return GradingPolicy.Default;
        try
        {
            return JsonSerializer.Deserialize<GradingPolicy>(row.Value, JsonOpts) ?? GradingPolicy.Default;
        }
        catch { return GradingPolicy.Default; }
    }

    public async Task SavePolicyAsync(string paperId, GradingPolicy policy, CancellationToken ct = default)
    {
        if (policy.DoubleGrading && !policy.SplitEnabled)
            throw new InvalidOperationException("开启双判必须先开启分题功能");
        await UpsertAsync(PolicyPrefix + paperId, JsonSerializer.Serialize(policy), ct);
    }

    public async Task<List<GradingAssignment>> LoadAssignmentsAsync(string paperId, CancellationToken ct = default)
    {
        var row = await db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Key == AssignPrefix + paperId, ct);
        if (row == null || string.IsNullOrWhiteSpace(row.Value)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<GradingAssignment>>(row.Value, JsonOpts) ?? [];
        }
        catch { return []; }
    }

    public async Task SaveAssignmentsAsync(string paperId, List<GradingAssignment> assignments, CancellationToken ct = default)
    {
        var policy = await LoadPolicyAsync(paperId, ct);
        if (!policy.SplitEnabled && assignments.Count > 0)
            throw new InvalidOperationException("请先开启分题功能再配置分配");
        var hasArbiter = assignments.Any(a => a.Kind == GradingAssignment.KindArbitration);
        if (hasArbiter && !policy.DoubleGrading)
            throw new InvalidOperationException("仲裁教师只在开启双判后有意义");
        await UpsertAsync(AssignPrefix + paperId, JsonSerializer.Serialize(assignments), ct);
    }

    /// <summary>
    /// 答卷 → 分配桶：以提交 Id 的稳定哈希取 0-99，累计分配区间落位。
    /// 例如两位教师各 50%：A 覆盖 [0,50)，B 覆盖 [50,100)，互补且稳定（同一答卷永远同一桶）。
    /// </summary>
    public static int BucketOf(string submissionId)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(submissionId));
        return ((hash[0] << 24 | hash[1] << 16 | hash[2] << 8 | hash[3]) & 0x7FFFFFFF) % 100;
    }

    /// <summary>
    /// 某教师在某份答卷上**被分配到的题号**（卡面题号，1 起）。
    /// 批改分配：题号匹配 + 比例落位；仲裁分配：该题正处待仲裁状态即归仲裁教师。
    /// </summary>
    public async Task<HashSet<int>?> AssignedQuestionNosAsync(
        string paperId, string submissionId, string username, bool isPrivileged,
        IReadOnlyDictionary<int, bool>? questionArbitrationState = null, CancellationToken ct = default)
    {
        // 管理员/机构管理员不受分配限制（可看全部、可改全部）
        if (isPrivileged) return null;

        var policy = await LoadPolicyAsync(paperId, ct);
        var assignments = await LoadAssignmentsAsync(paperId, ct);
        if (!policy.SplitEnabled || assignments.Count == 0) return null;

        var mine = assignments.Where(a =>
            string.Equals(a.TeacherUsername, username, StringComparison.OrdinalIgnoreCase)).ToList();
        if (mine.Count == 0) return [];

        var bucket = BucketOf(submissionId);
        var result = new HashSet<int>();
        foreach (var a in mine)
        {
            if (a.Kind == GradingAssignment.KindArbitration)
            {
                // 仲裁教师只看到"待仲裁"的题
                if (questionArbitrationState != null)
                    foreach (var no in a.QuestionNos)
                        if (questionArbitrationState.TryGetValue(no, out var pending) && pending)
                            result.Add(no);
            }
            else
            {
                // 批改教师：按比例区间落位——区间在**全部批改分配**上按列表顺序累计，
                // A 50% + B 50% 才会切成互补的 [0,50) / [50,100)
                var start = RangeStart(assignments, a);
                if (bucket >= start && bucket < start + a.Percent)
                    foreach (var no in a.QuestionNos) result.Add(no);
            }
        }
        return result;
    }

    /// <summary>某条批改分配的百分比区间起点（所有批改分配按列表顺序累计，各占一段互不重叠）。</summary>
    private static double RangeStart(List<GradingAssignment> all, GradingAssignment target)
    {
        double start = 0;
        foreach (var a in all)
        {
            if (a.Kind != GradingAssignment.KindGrading) continue;
            if (ReferenceEquals(a, target)) break;
            start += a.Percent;
        }
        return start;
    }

    private async Task UpsertAsync(string key, string value, CancellationToken ct)
    {
        var existing = await db.AppSettings.FirstOrDefaultAsync(a => a.Key == key, ct);
        if (existing != null) existing.Value = value;
        else db.AppSettings.Add(new AgoraIn.Core.Entities.AppSetting { Key = key, Value = value });
        await db.SaveChangesAsync(ct);
    }
}
