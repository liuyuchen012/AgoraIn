namespace AgoraIn.Core.Entities;

/// <summary>学校。</summary>
public sealed class School
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>学校名称。</summary>
    public string Name { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 班级（聚合根）：学生、座位、点名、积分、值日等均以班级为作用域。
/// </summary>
public sealed class ClassInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>所属学校（可空：单校部署时不必填写）。</summary>
    public string? SchoolId { get; set; }

    /// <summary>班级名称，如"三年级2班"。</summary>
    public string Name { get; set; } = "";

    /// <summary>年级（如"三年级"或入学年份）。</summary>
    public string Grade { get; set; } = "";

    /// <summary>备注。</summary>
    public string Remark { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>教师。</summary>
public sealed class Teacher
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>姓名。</summary>
    public string Name { get; set; } = "";

    /// <summary>任教学科（可空）。</summary>
    public string? Subject { get; set; }

    /// <summary>联系电话（可空）。</summary>
    public string? Phone { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>学生在读状态。</summary>
public enum StudentStatus
{
    /// <summary>在读。</summary>
    Enrolled = 0,

    /// <summary>转出（保留历史数据，不参与新操作）。</summary>
    Transferred = 1,
}

/// <summary>学生：归属于班级，班级内唯一。</summary>
public sealed class Student
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>所属班级。</summary>
    public string ClassId { get; set; } = "";

    /// <summary>学号（班级内可排序展示，可空）。</summary>
    public string? StudentNo { get; set; }

    /// <summary>姓名（班级内唯一，v3 迁移按姓名合并身份）。</summary>
    public string Name { get; set; } = "";

    /// <summary>性别（可空："男"/"女"）。</summary>
    public string? Gender { get; set; }

    /// <summary>头像路径（可空）。</summary>
    public string? Avatar { get; set; }

    /// <summary>在读状态。</summary>
    public StudentStatus Status { get; set; } = StudentStatus.Enrolled;

    /// <summary>备注。</summary>
    public string Remark { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
