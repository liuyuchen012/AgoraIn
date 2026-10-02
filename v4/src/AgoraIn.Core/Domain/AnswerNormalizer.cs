namespace AgoraIn.Core.Domain;

/// <summary>
/// 客观题/填空题答案归一化：识别结果与标准答案比对前的统一口径。
///
/// 判断题必须特判——答题卡选项方格印的是 √（U+221A）/ ×（U+00D7），
/// 而题库里标准答案存的是「对 / 错」。若按「只保留字母数字」处理，
/// 两者都会被清成空串而互相相等，导致所有判断题被判为答对。
///
/// 填空题是手写内容（视觉模型读字），故先做全角→半角折叠：学生写「３」、
/// 题库存「3」必须算同一个答案；全角括号/正负号同理。
/// </summary>
public static class AnswerNormalizer
{
    /// <summary>归一化后的答案：判断题统一为 "T"/"F"，其余为排序后的字母数字。</summary>
    public static string Normalize(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return "";
        var a = new string(answer.Trim().Select(FoldFullWidth).ToArray());

        // 判断题（精确匹配优先，避免与多选字母混淆）
        if (a is "对" or "√" or "✓" or "T" or "t") return "T";
        if (a is "错" or "×" or "✗" or "✘" or "F" or "f" or "X" or "x") return "F";
        if (a.Contains('错') || a.Contains('×') || a.Contains('✗') || a.Contains('✘')) return "F";
        if (a.Contains('对') || a.Contains('√') || a.Contains('✓')) return "T";
        if (a.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("YES", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("正确", StringComparison.OrdinalIgnoreCase)) return "T";
        if (a.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("NO", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("错误", StringComparison.OrdinalIgnoreCase)) return "F";

        // 客观题/填空题：只保留字母数字并排序，实现「多选 ABD == DBA」「3x == x3」这类等价
        var cleaned = new string(a.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return string.Concat(cleaned.OrderBy(c => c));
    }

    /// <summary>全角转半角（ASCII 区）：＋－（）＝ 等符号与全角数字字母都要能与半角对齐。</summary>
    private static char FoldFullWidth(char c) => c switch
    {
        >= '\uFF01' and <= '\uFF5E' => (char)(c - 0xFEE0),
        '\u3000' => ' ',                       // 全角空格
        _ => c,
    };

    /// <summary>识别结果与标准答案是否一致（双方都归一化后比较）。</summary>
    public static bool IsCorrect(string? recognized, string? standard)
        => !string.IsNullOrEmpty(standard) && Normalize(recognized) == Normalize(standard);
}
