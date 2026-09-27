namespace AgoraIn.Core.Security;

/// <summary>
/// 系统角色。分级关系：
/// <code>
///   admin（系统管理员）      最高权限，管理所有账户、设备、系统设置、授权
///     └ owner（机构管理员）  管理本机构：子账户、班级学生、设备分配、成绩与通知
///         └ teacher（教师）  管理所带班级：打卡、点名、积分、值日、答题卡批改、资源
///             ├ student（学生）  查看自己的任务并签到、查看自己的记录
///             └ parent（家长）   只读自己孩子的数据 + 与教师留言
/// </code>
/// </summary>
public static class AppRoles
{
    public const string Admin = "admin";
    public const string Owner = "owner";
    public const string Teacher = "teacher";
    public const string Student = "student";
    public const string Parent = "parent";

    /// <summary>全部合法角色。</summary>
    public static readonly IReadOnlyList<string> All = [Admin, Owner, Teacher, Student, Parent];

    /// <summary>主账户可创建的子账户角色（管理员不受限）。</summary>
    public static readonly IReadOnlyList<string> SubAccountRoles = [Teacher, Student, Parent];

    /// <summary>
    /// 规范化角色名（兼容 v3.2 的历史取值：operator → teacher，viewer → student）。
    /// </summary>
    public static string Normalize(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        "operator" => Teacher,
        "viewer" => Student,
        "owner" => Owner,
        "admin" => Admin,
        "teacher" => Teacher,
        "student" => Student,
        "parent" => Parent,
        _ => Teacher, // 未知角色按最低可管理角色处理，避免越权
    };

    /// <summary>是否为合法角色（存储前校验用）。</summary>
    public static bool IsValid(string? role)
        => role is not null && All.Contains(role.Trim().ToLowerInvariant());

    /// <summary>显示名。</summary>
    public static string DisplayName(string? role) => Normalize(role) switch
    {
        Admin => "系统管理员",
        Owner => "机构管理员",
        Teacher => "教师",
        Parent => "家长",
        _ => "学生",
    };

    /// <summary>该角色是否可创建子账户。</summary>
    public static bool CanCreateSubAccount(string? role) => Normalize(role) is Admin or Owner;

    /// <summary>是否为可创建子账户的角色集合（管理类角色）。</summary>
    public static bool IsManager(string? role) => Normalize(role) is Admin or Owner or Teacher;
}
