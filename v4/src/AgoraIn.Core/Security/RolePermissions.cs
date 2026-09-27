namespace AgoraIn.Core.Security;

/// <summary>
/// 权限点（细粒度，供 <see cref="RequirePermission"/> 与前端按钮显隐共用）。
/// 命名约定：<c>资源.动作</c>。
/// </summary>
public static class Permissions
{
    // ── 账户与权限 ──
    /// <summary>查看用户列表。</summary>
    public const string UsersView = "users.view";
    /// <summary>创建/修改/删除任意用户（系统管理员）。</summary>
    public const string UsersManage = "users.manage";
    /// <summary>管理自己的子账户（机构管理员/系统管理员）。</summary>
    public const string SubAccountsManage = "users.subaccounts";

    // ── 教学数据 ──
    /// <summary>查看班级与学生。</summary>
    public const string ClassesView = "classes.view";
    /// <summary>管理班级与学生（增删改）。</summary>
    public const string ClassesManage = "classes.manage";
    /// <summary>查看打卡记录与考勤。</summary>
    public const string CheckInView = "checkin.view";
    /// <summary>执行打卡/取消/清空记录。</summary>
    public const string CheckInOperate = "checkin.operate";
    /// <summary>查看课时账户与流水。</summary>
    public const string ClassHoursView = "classhours.view";
    /// <summary>划消/赠送课时、排课。</summary>
    public const string ClassHoursManage = "classhours.manage";
    /// <summary>点名。</summary>
    public const string RollCallOperate = "rollcall.operate";
    /// <summary>加减分与积分规则。</summary>
    public const string PointsManage = "points.manage";
    /// <summary>值日岗位与轮换。</summary>
    public const string DutyManage = "duty.manage";
    /// <summary>CSES 课表编辑与 ClassIsland 导入导出。</summary>
    public const string TimetableManage = "timetable.manage";

    // ── 考试与资源 ──
    /// <summary>出卷与答题卡生成。</summary>
    public const string ExamsManage = "exams.manage";
    /// <summary>批改（含 AI 批改与人工复判）。</summary>
    public const string GradingOperate = "grading.operate";
    /// <summary>查看成绩统计。</summary>
    public const string ScoresView = "scores.view";
    /// <summary>资源库上传/下发。</summary>
    public const string ResourcesManage = "resources.manage";
    /// <summary>发布通知公告。</summary>
    public const string NoticesManage = "notices.manage";
    /// <summary>与家长会话（教师端）。</summary>
    public const string MessagesHandle = "messages.handle";

    // ── 设备与系统 ──
    /// <summary>查看设备。</summary>
    public const string DevicesView = "devices.view";
    /// <summary>注册/重命名/删除设备、分配归属。</summary>
    public const string DevicesManage = "devices.manage";
    /// <summary>修改系统设置（邮件服务、隐私开关、JWT 等）。</summary>
    public const string SystemSettings = "system.settings";
    /// <summary>查看与激活授权。</summary>
    public const string LicenseManage = "system.license";

    // ── 个人数据（家长/学生）──
    /// <summary>查看自己（或自己孩子）的数据。</summary>
    public const string SelfDataView = "self.view";
}

/// <summary>
/// 角色 → 权限矩阵。分级权限的唯一事实来源，服务端鉴权与前端按钮显隐都依据它。
/// </summary>
public static class RolePermissions
{
    private static readonly HashSet<string> AllPermissions =
    [
        Permissions.UsersView, Permissions.UsersManage, Permissions.SubAccountsManage,
        Permissions.ClassesView, Permissions.ClassesManage,
        Permissions.CheckInView, Permissions.CheckInOperate,
        Permissions.ClassHoursView, Permissions.ClassHoursManage,
        Permissions.RollCallOperate, Permissions.PointsManage, Permissions.DutyManage,
        Permissions.TimetableManage,
        Permissions.ExamsManage, Permissions.GradingOperate, Permissions.ScoresView,
        Permissions.ResourcesManage, Permissions.NoticesManage, Permissions.MessagesHandle,
        Permissions.DevicesView, Permissions.DevicesManage,
        Permissions.SystemSettings, Permissions.LicenseManage,
        Permissions.SelfDataView,
    ];

    private static readonly Dictionary<string, HashSet<string>> Matrix = new(StringComparer.OrdinalIgnoreCase)
    {
        // 系统管理员：全部权限
        [AppRoles.Admin] = AllPermissions,

        // 机构管理员：除「系统设置」与「授权」外的全部（授权由系统管理员统一管理）
        [AppRoles.Owner] = AllPermissions
            .Where(p => p is not (Permissions.SystemSettings or Permissions.LicenseManage))
            .ToHashSet(),

        // 教师：教学相关，不含账户/设备/系统管理
        [AppRoles.Teacher] = new HashSet<string>
        {
            Permissions.ClassesView, Permissions.ClassesManage,
            Permissions.CheckInView, Permissions.CheckInOperate,
            Permissions.ClassHoursView, Permissions.ClassHoursManage,
            Permissions.RollCallOperate, Permissions.PointsManage, Permissions.DutyManage,
            Permissions.TimetableManage,
            Permissions.ExamsManage, Permissions.GradingOperate, Permissions.ScoresView,
            Permissions.ResourcesManage, Permissions.NoticesManage, Permissions.MessagesHandle,
            Permissions.DevicesView,
            Permissions.SelfDataView,
        },

        // 学生：查看任务与自己的记录
        [AppRoles.Student] = new HashSet<string>
        {
            Permissions.ClassesView,
            Permissions.CheckInView,
            Permissions.ScoresView,
            Permissions.SelfDataView,
        },

        // 家长：只读自己孩子的数据 + 留言
        [AppRoles.Parent] = new HashSet<string>
        {
            Permissions.ScoresView,
            Permissions.MessagesHandle,
            Permissions.SelfDataView,
        },
    };

    /// <summary>获取角色拥有的全部权限（未知/空角色返回空集合）。</summary>
    public static IReadOnlySet<string> For(string? role)
    {
        var normalized = AppRoles.Normalize(role);
        return Matrix.TryGetValue(normalized, out var set) ? set : new HashSet<string>();
    }

    /// <summary>判断角色是否拥有指定权限。</summary>
    public static bool Has(string? role, string permission)
        => !string.IsNullOrEmpty(permission) && For(role).Contains(permission);

    /// <summary>判断角色是否拥有全部指定权限。</summary>
    public static bool HasAll(string? role, params string[] permissions)
        => permissions.All(p => Has(role, p));
}
