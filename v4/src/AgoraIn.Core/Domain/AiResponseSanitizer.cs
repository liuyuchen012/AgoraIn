using System.Text.RegularExpressions;

namespace AgoraIn.Core.Domain;

/// <summary>
/// AI 返回文本的清洗与 JSON 提取。
///
/// 推理模型（deepseek-flash 等）会把思考过程一起吐出来——形如
/// <c>&lt;think&gt;…&lt;/think&gt;</c> 的整块自然语言，甚至是没闭合就被 max_tokens 截断的半截思考。
/// 这段文字里经常出现 { } [ ] 和"JSON"字样（模型在自我提醒要输出 JSON），
/// 若不先剥掉，后面的"截取首个 [ { 到末个 ] }"就会从思考区里就开始截，解析必然失败。
/// </summary>
public static class AiResponseSanitizer
{
    private static readonly string[] ThinkingTags = ["think", "thinking", "reasoning", "analysis", "thought"];

    /// <summary>剥掉思考区（标签块 / 未闭合的开标签 / ```think 围栏）。</summary>
    public static string StripThinking(string? content)
    {
        if (string.IsNullOrEmpty(content)) return "";
        var text = content;
        foreach (var tag in ThinkingTags)
        {
            // 成对标签：<think …>…</think>
            text = Regex.Replace(text, $@"<{tag}\b[^>]*>.*?</{tag}\s*>", "",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            // 只有开标签（思考被 max_tokens 截断）：从开标签一直切到结尾
            text = Regex.Replace(text, $@"<{tag}\b[^>]*>.*$", "",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            // 被围栏包起来的思考：```think … ```
            text = Regex.Replace(text, $@"```\s*{tag}\b.*?```", "",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }
        return text;
    }

    /// <summary>
    /// 从 AI 返回文本中提取可解析的 JSON：先剥思考区，再剥 markdown 代码围栏，
    /// 然后截取首个 [/{ 到末个 /}]；数组被 max_tokens 截断时做尾部修复。
    /// </summary>
    public static string? ExtractJson(string? content, bool expectArray)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var text = StripThinking(content).Trim();
        if (text.Length == 0) return null;

        // 1) 剥 markdown 围栏（```json … ```）；只有一对围栏时才剥，多段时交给下面的截取
        var fence = text.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var start = text.IndexOf('\n', fence);
            var end = text.LastIndexOf("```", StringComparison.Ordinal);
            if (start >= 0 && end > start) text = text[(start + 1)..end].Trim();
        }

        // 2) 找候选 JSON 块：按括号配对扫描（跳过字符串内的括号），取"最后一个像样的"。
        //    不能简单取"首个 [ { 到末个 ] }"——思考区里常出现 { }、甚至写出 [80 ± √169792] 这类数学式子当括号用，
        //    那样会从思考区里起截，跨着整段散文去配对，必然解析失败。
        var candidates = BalancedBlocks(text);
        var picked = candidates.LastOrDefault(LooksLikeJson);
        return picked;   // 一个都不像 → 返回 null（交给调用方追问一次），别拿括号式子去硬解析

        // 3) 没找到闭合块（输出被 max_tokens 截断）时，BalancedBlocks 已把残余块做了尾部修复并放进候选
    }

    /// <summary>像不像我们要的 JSON：数组要看得出元素（对象或字符串），对象要有键值对。</summary>
    private static bool LooksLikeJson(string block)
    {
        if (block.Length < 4) return false;
        return block[0] switch
        {
            '[' => block.Contains('{') || block.Contains('"'),
            '{' => block.Contains('"') && block.Contains(':'),
            _ => false,
        };
    }

    /// <summary>
    /// 扫描出所有括号配对的 JSON 块（字符串与转义内的括号不算），按出现顺序返回。
    /// 末尾若有被截断的未闭合块，则把它也返回（做尾部修复用）。
    /// </summary>
    private static List<string> BalancedBlocks(string text)
    {
        var result = new List<string>();
        var stack = new List<int>();
        var inString = false;
        var escaped = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == '"') inString = false;
                continue;
            }
            switch (ch)
            {
                case '"':
                    inString = true;
                    break;
                case '[':
                case '{':
                    stack.Add(i);
                    break;
                case ']':
                case '}':
                    if (stack.Count == 0) break;
                    var openIdx = stack[^1];
                    var openCh = text[openIdx];
                    if ((openCh == '[' && ch != ']') || (openCh == '{' && ch != '}')) break;   // 括号不匹配：忽略
                    stack.RemoveAt(stack.Count - 1);
                    if (stack.Count == 0) result.Add(text[openIdx..(i + 1)]);
                    break;
            }
        }
        // 未闭合的残余（被截断）：从**最外层**那个未闭合括号起取，交给修复逻辑补闭合
        if (stack.Count > 0)
        {
            var tail = text[stack[0]..];
            if (tail.Length > 2) result.Add(RepairTruncated(tail));
        }
        return result.Where(r => r.Length > 2).ToList();
    }

    /// <summary>输出顶满 max_tokens 时的尾部修复：裁到最后一个完整对象 '}' 处，去尾逗号后补外层闭合。</summary>
    private static string RepairTruncated(string json)
    {
        var openCh = json[0];
        var closeCh = openCh == '[' ? ']' : '}';
        if (json[^1] == closeCh) return json;
        var cut = json.LastIndexOf('}');
        if (cut <= 0) return json;
        var trimmed = json[..(cut + 1)].TrimEnd();
        if (trimmed.EndsWith(",")) trimmed = trimmed[..^1];
        return openCh == '[' ? trimmed + closeCh : trimmed;
    }
}
