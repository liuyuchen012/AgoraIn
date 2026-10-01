namespace AgoraIn.Server.Models;

// 说明：试卷/题目/答题卡提交/逐题结果等实体定义在 AgoraIn.Core.Entities.Exams.cs，
// 服务端直接复用（见 ServerDbContext）。本文件只放服务端专有的识别/批改 DTO。

/// <summary>OMR 涂卡识别结果。</summary>
public sealed class OmrResult
{
    /// <summary>识别出的考号（涂卡区）。</summary>
    public string? StudentRef { get; set; }

    /// <summary>逐题识别结果。</summary>
    public List<OmrQuestionAnswer> Answers { get; set; } = new();

    /// <summary>整体置信度（0~1）。模型回传的字段名是 confidence，需显式映射（否则恒为 0）。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("confidence")]
    public double OverallConfidence { get; set; }
}

/// <summary>单题 OMR 识别结果。</summary>
public sealed class OmrQuestionAnswer
{
    public int Index { get; set; }
    public string? Answer { get; set; }
    public double Confidence { get; set; }
}

/// <summary>AI 批改请求。</summary>
public sealed class AiGradingRequest
{
    public string QuestionText { get; set; } = "";
    public string StudentAnswer { get; set; } = "";
    public string? Rubric { get; set; }
    public string? StandardAnswer { get; set; }

    /// <summary>题型（对应 Core 的 QuestionType 名称）。</summary>
    public string Type { get; set; } = "";

    public double MaxScore { get; set; }
}

/// <summary>AI 批改响应。</summary>
public sealed class AiGradingResponse
{
    public double Score { get; set; }
    public string Comment { get; set; } = "";
    public double Confidence { get; set; }
}
