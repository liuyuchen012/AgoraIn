using AgoraIn.Core.Domain;
using Xunit;

namespace AgoraIn.Core.Tests;

/// <summary>
/// AI 返回文本的清洗：推理模型会把思考过程一起返回（带 &lt;think&gt; 标签的、以及不带标签的散文），
/// 思考里常出现 { } 和"要输出 JSON"之类的话，不先剥掉/不按括号配对找，JSON 提取必然失败。
/// 现场报错样例见「图片导题 JSON 解析失败，返回前 200 字：我们需要回答用户要求…」。
/// </summary>
public class AiResponseSanitizerTests
{
    [Fact]
    public void 剥掉think标签块只留JSON()
    {
        var raw = "用户在问要不要按左右栏读。\n\n<think>\n先看第一张图的标题 {不是 JSON}\n再决定顺序\n</think>\n" +
                  """[{"index":1,"type":0,"content":"题干"}]""";
        var json = AiResponseSanitizer.ExtractJson(raw, expectArray: true);
        Assert.Equal("""[{"index":1,"type":0,"content":"题干"}]""", json);
    }

    [Fact]
    public void 剥掉未闭合的think_被max_tokens截断的思考()
    {
        var raw = "我们需要按顺序读题。<think>第一张图是 A3 横向，左侧…（被截断";
        Assert.Null(AiResponseSanitizer.ExtractJson(raw, expectArray: true));
        Assert.Equal("我们需要按顺序读题。", AiResponseSanitizer.StripThinking(raw));
    }

    [Fact]
    public void 无标签的思考散文里含大括号也能取到真正的JSON()
    {
        // 模型思考里举例写了半截 JSON，且散文里散布 { }——旧算法会从思考区起截而失败
        var raw = "我们需要回答用户要求：识别全部题目。注意返回格式应为 {\"index\":1} 这样的对象，" +
                  "放在数组里 [ … ]，从 1 开始编号。\n" +
                  """[{"index":1,"type":0,"content":"1. 计算 1+1"},{"index":2,"type":3,"content":"2. 填空"}]""";
        var json = AiResponseSanitizer.ExtractJson(raw, expectArray: true);
        Assert.NotNull(json);
        Assert.StartsWith("[{\"index\":1", json);
        Assert.Contains("填空", json);
    }

    [Fact]
    public void 取最后一个符合形状的块_忽略思考里的示例数组()
    {
        var raw = """
            示例输出长这样：[{"index":1,"type":0,"content":"示例"}]，但真正的结果在下面。
            [{"index":1,"type":0,"content":"真题一"},{"index":2,"type":4,"content":"真题二"}]
            """;
        var json = AiResponseSanitizer.ExtractJson(raw, expectArray: true);
        Assert.Contains("真题一", json!);
        Assert.DoesNotContain("示例", json);
    }

    [Fact]
    public void 剥掉think围栏与代码围栏()
    {
        var raw = """
            ```think
            这里在想要不要把两栏合并读
            ```
            ```json
            {"student_ref":"20260042","answers":[{"index":1,"answer":"A","confidence":0.9}]}
            ```
            """;
        var json = AiResponseSanitizer.ExtractJson(raw, expectArray: false);
        Assert.NotNull(json);
        Assert.StartsWith("{\"student_ref\"", json);
        Assert.DoesNotContain("两栏", json);
    }

    [Fact]
    public void 数组被截断时做尾部修复()
    {
        var raw = "说明文字\n" + """[{"index":1,"type":0,"content":"题一"},{"index":2,"type":0,"conte""";
        var json = AiResponseSanitizer.ExtractJson(raw, expectArray: true);
        Assert.NotNull(json);
        Assert.EndsWith("]", json);
        Assert.Contains("题一", json);
    }

    [Fact]
    public void 字符串里的括号不影响配对()
    {
        var raw = """[{"index":1,"content":"集合 {a, b} 与 [0,1] 区间"}]""";
        var json = AiResponseSanitizer.ExtractJson(raw, expectArray: true);
        Assert.Equal(raw, json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("模型只说了话，没有 JSON")]
    public void 没有JSON时返回null(string raw)
        => Assert.Null(AiResponseSanitizer.ExtractJson(raw, expectArray: true));

    [Fact]
    public void 思考里的数学括号式子不会被当成JSON()
    {
        // 现场样例（22:49 那次失败响应）：整段都是推理，中间出现 [80 ± √169792] 这种式子，
        // 旧算法会把它当"最后一个括号块"返回，调用方拿着数学式去解析必然失败
        var raw = "我们需要按顺序读。判别式 Δ = [80 ± √169792]，所以两根为…（后面还在推理";
        Assert.Null(AiResponseSanitizer.ExtractJson(raw, expectArray: true));
        Assert.Null(AiResponseSanitizer.ExtractJson(raw, expectArray: false));
    }
}
