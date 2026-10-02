namespace AgoraIn.Core.Entities;

/// <summary>题型。</summary>
public enum QuestionType
{
    /// <summary>单选。</summary>
    SingleChoice = 0,

    /// <summary>多选。</summary>
    MultipleChoice = 1,

    /// <summary>判断。</summary>
    Judge = 2,

    /// <summary>填空（多模态 OCR + 语义比对）。</summary>
    Blank = 3,

    /// <summary>简答（按评分要点给分）。</summary>
    ShortAnswer = 4,

    /// <summary>作文（维度点评：内容/结构/语言/书写）。</summary>
    Essay = 5,
}

/// <summary>批改来源。</summary>
public enum GradingSource
{
    /// <summary>教师未批（初始）。</summary>
    None = 0,

    /// <summary>AI 自动批改。</summary>
    Ai = 1,

    /// <summary>教师批改/复判。</summary>
    Teacher = 2,
}

/// <summary>答题卡批改状态机：未批 → AI已批 →（低置信度→）待人工 → 已确认；只有已确认才计入成绩。</summary>
public enum SubmissionStatus
{
    /// <summary>未批。</summary>
    NotGraded = 0,

    /// <summary>AI 已批。</summary>
    AiGraded = 1,

    /// <summary>待人工复判（低置信度或教师分配）。</summary>
    NeedsHuman = 2,

    /// <summary>已确认（计入成绩）。</summary>
    Confirmed = 3,
}

/// <summary>试卷：出卷（题型、分值、标准答案/评分要点、知识点），支持题库复用。</summary>
public sealed class ExamPaper
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>试卷标题。</summary>
    public string Title { get; set; } = "";

    /// <summary>班级（可空 = 通用试卷）。</summary>
    public string? ClassId { get; set; }

    /// <summary>科目（可空）。</summary>
    public string? Subject { get; set; }

    /// <summary>创建人（教师名）。</summary>
    public string CreatedBy { get; set; } = "";

    /// <summary>试卷总分（逐题分值之和，由服务维护）。</summary>
    public double TotalScore { get; set; }

    /// <summary>是否为题库模板（可复用出卷）。</summary>
    public bool IsTemplate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>题目：题型、分值、标准答案、评分要点（简答/作文强制填写）、知识点标签、选项。</summary>
public sealed class Question
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>所属试卷。</summary>
    public string PaperId { get; set; } = "";

    /// <summary>题号（卷面顺序，0 起）。</summary>
    public int Index { get; set; }

    /// <summary>题型。</summary>
    public QuestionType Type { get; set; } = QuestionType.SingleChoice;

    /// <summary>题目内容/题干。</summary>
    public string? Content { get; set; }

    /// <summary>分值。</summary>
    public double Score { get; set; } = 1;

    /// <summary>标准答案（客观题：选项字母/对错；填空：文本，支持多空分隔）。</summary>
    public string? StandardAnswer { get; set; }

    /// <summary>选项（JSON：[{key:"A",text:"…"},…]；仅选择题）。</summary>
    public string? OptionsJson { get; set; }

    /// <summary>
    /// 评分要点/rubric（分步给分点）。简答与作文出卷时强制填写；
    /// AI 提示词模板由服务端统一管理，此项为该题的要点输入。
    /// </summary>
    public string? Rubric { get; set; }

    /// <summary>知识点标签（JSON 数组，可空）。</summary>
    public string? KnowledgeTagsJson { get; set; }

    /// <summary>批改方式：null = 默认（AI 可批题型走 AI）；true = 强制 AI；false = 分配教师手判。</summary>
    public bool? AiGradingEnabled { get; set; }
}

/// <summary>答题卡模板：版面定义（定位标记、题块坐标、条码区等），服务端按此渲染/切分。</summary>
public sealed class AnswerSheetTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>所属试卷。</summary>
    public string PaperId { get; set; } = "";

    /// <summary>纸张规格（如 A4）。</summary>
    public string PageSize { get; set; } = "A4";

    /// <summary>页数。</summary>
    public int PageCount { get; set; } = 1;

    /// <summary>
    /// 版面定义 JSON：四角定位标记、学生信息区（条码优先）、客观题涂卡区、
    /// 主观题作答边界框、页脚二维码（试卷 ID + 页码）的坐标与尺寸。
    /// </summary>
    public string LayoutJson { get; set; } = "";

    /// <summary>模板版本号（试卷改版时递增，历史提交按版本切分）。</summary>
    public int Version { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>答题卡提交：一名学生一份试卷的作答图像与批改进度。</summary>
public sealed class AnswerSheetSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>试卷。</summary>
    public string PaperId { get; set; } = "";

    /// <summary>模板版本（提交时锁定）。</summary>
    public int TemplateVersion { get; set; } = 1;

    /// <summary>学生（条码识别后绑定；null = 待确认匹配）。</summary>
    public string? StudentId { get; set; }

    /// <summary>条码/考号原文（无条码时按页序/手写考号辅助匹配，须教师确认）。</summary>
    public string? StudentRef { get; set; }

    /// <summary>作答图像路径列表（页序）。</summary>
    public string ImagePathsJson { get; set; } = "[]";

    /// <summary>批改状态。</summary>
    public SubmissionStatus Status { get; set; } = SubmissionStatus.NotGraded;

    /// <summary>确认后总分（仅 Confirmed 状态计入成绩）。</summary>
    public double? TotalScore { get; set; }

    /// <summary>上传时间。</summary>
    public DateTime SubmittedAt { get; set; } = DateTime.Now;

    /// <summary>AI 批改完成时间。</summary>
    public DateTime? AiGradedAt { get; set; }

    /// <summary>教师确认时间。</summary>
    public DateTime? ConfirmedAt { get; set; }
}

/// <summary>
/// 逐题批改结果：得分、评语、置信度、批改来源；
/// 同题重新批改的历史版本保存在 HistoryJson 中可回溯。
/// </summary>
public sealed class QuestionResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>所属提交。</summary>
    public string SubmissionId { get; set; } = "";

    /// <summary>题目。</summary>
    public string QuestionId { get; set; } = "";

    /// <summary>识别到的作答内容（填空等，可空）。</summary>
    public string? RecognizedAnswer { get; set; }

    /// <summary>当前得分（null = 未定分）。</summary>
    public double? Score { get; set; }

    /// <summary>评语（作文含维度点评：内容/结构/语言/书写）。</summary>
    public string? Comment { get; set; }

    /// <summary>AI 置信度（0~1；低于阈值进入待人工）。</summary>
    public double? Confidence { get; set; }

    /// <summary>批改来源。</summary>
    public GradingSource Source { get; set; } = GradingSource.None;

    /// <summary>批改历史（JSON：[{score,comment,source,gradedAt}…]），同题重批可回溯。</summary>
    public string? HistoryJson { get; set; }

    /// <summary>最近批改时间。</summary>
    public DateTime? GradedAt { get; set; }

    // ── 双判 / 仲裁（开启批改分配+双判后使用）──

    /// <summary>第一判教师（用户名；单判模式下也是改分人）。</summary>
    public string? Grader { get; set; }

    /// <summary>第二判得分（双判模式：第二位教师的原始分）。</summary>
    public double? Score2 { get; set; }

    /// <summary>第二判教师（用户名）。</summary>
    public string? Grader2 { get; set; }

    /// <summary>两判分差超过阈值 → 待仲裁（第三位资深教师裁定）。</summary>
    public bool NeedArbitration { get; set; }

    /// <summary>仲裁教师（用户名；仲裁裁定的分数直接写入 Score）。</summary>
    public string? Arbiter { get; set; }
}
