namespace AgoraIn.Server.Models;

/// <summary>试卷。</summary>
public sealed class ExamPaper
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "";
    public string? ClassId { get; set; }
    public string? Subject { get; set; }
    public string CreatedBy { get; set; } = "";
    public double TotalScore { get; set; }
    public bool IsTemplate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>题目：题型、分值、标准答案、评分要点。</summary>
public sealed class Question
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string PaperId { get; set; } = "";
    public int Index { get; set; }
    public QuestionType Type { get; set; }
    public double Score { get; set; } = 1;
    public string? StandardAnswer { get; set; }
    public string? OptionsJson { get; set; }
    public string? Rubric { get; set; }
    public string? KnowledgeTagsJson { get; set; }
}

public enum QuestionType
{
    SingleChoice, MultipleChoice, Judge, Blank, ShortAnswer, Essay
}

/// <summary>答题卡提交。</summary>
public sealed class AnswerSheetSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string PaperId { get; set; } = "";
    public string? StudentId { get; set; }
    public string? StudentRef { get; set; }
    public string ImagePathsJson { get; set; } = "[]";
    public SubmissionStatus Status { get; set; } = SubmissionStatus.NotGraded;
    public double? TotalScore { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.Now;
}

public enum SubmissionStatus
{
    NotGraded, AiGraded, NeedsHuman, Confirmed
}

/// <summary>逐题批改结果。</summary>
public sealed class QuestionResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string SubmissionId { get; set; } = "";
    public string QuestionId { get; set; } = "";
    public string? RecognizedAnswer { get; set; }
    public double? Score { get; set; }
    public string? Comment { get; set; }
    public double? Confidence { get; set; }
    public GradingSource Source { get; set; } = GradingSource.None;
    public string? HistoryJson { get; set; }
    public DateTime? GradedAt { get; set; }
}

public enum GradingSource
{
    None, Ai, Teacher
}

/// <summary>OMR 涂卡识别结果。</summary>
public sealed class OmrResult
{
    public string? StudentRef { get; set; }
    public List<OmrQuestionAnswer> Answers { get; set; } = new();
    public double OverallConfidence { get; set; }
}

public sealed class OmrQuestionAnswer
{
    public int QuestionIndex { get; set; }
    public string? Answer { get; set; }
    public double Confidence { get; set; }
}

/// <summary>DeepSeek AI 批改请求。</summary>
public sealed class AiGradingRequest
{
    public string QuestionText { get; set; } = "";
    public string StudentAnswer { get; set; } = "";
    public string? Rubric { get; set; }
    public string? StandardAnswer { get; set; }
    public QuestionType Type { get; set; }
    public double MaxScore { get; set; }
}

/// <summary>DeepSeek AI 批改响应。</summary>
public sealed class AiGradingResponse
{
    public double Score { get; set; }
    public string Comment { get; set; } = "";
    public double Confidence { get; set; }
}
