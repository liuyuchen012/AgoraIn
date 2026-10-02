using AgoraIn.Core.Domain;
using Xunit;

namespace AgoraIn.Core.Tests;

public class AnswerNormalizerTests
{
    [Theory]
    [InlineData("对", "√")]      // 卡面印 √，题库存「对」
    [InlineData("对", "T")]
    [InlineData("对", "t")]
    [InlineData("对", "正确")]
    [InlineData("对", "√ ")]
    [InlineData("错", "×")]
    [InlineData("错", "✗")]
    [InlineData("错", "F")]
    [InlineData("错", "错误")]
    public void 判断题_卡面符号与题库文字等价(string standard, string recognized)
    {
        Assert.True(AnswerNormalizer.IsCorrect(recognized, standard));
        Assert.Equal(AnswerNormalizer.Normalize(standard), AnswerNormalizer.Normalize(recognized));
    }

    [Theory]
    [InlineData("对", "×")]
    [InlineData("错", "√")]
    [InlineData("对", "")]
    [InlineData("错", "  ")]
    public void 判断题_答案不同或未作答不得判对(string standard, string recognized)
    {
        Assert.False(AnswerNormalizer.IsCorrect(recognized, standard));
    }

    [Theory]
    [InlineData("A", "a")]
    [InlineData("A", " A ")]
    [InlineData("ABD", "dba")]
    [InlineData("AC", "ca")]
    public void 客观题_大小写与顺序无关(string standard, string recognized)
        => Assert.True(AnswerNormalizer.IsCorrect(recognized, standard));

    [Theory]
    [InlineData("A", "B")]
    [InlineData("AB", "ABC")]
    [InlineData("AB", "")]
    public void 客观题_答案不同不得判对(string standard, string recognized)
        => Assert.False(AnswerNormalizer.IsCorrect(recognized, standard));

    [Fact]
    public void 空标准答案不参与自动判分()
        => Assert.False(AnswerNormalizer.IsCorrect("A", null));

    // ── 填空题：手写内容（视觉模型读字）与客观题同一套判分口径 ──

    [Theory]
    [InlineData("3", "３")]              // 全角数字
    [InlineData("AB", "ＡＢ")]            // 全角字母
    [InlineData("-2", "－2")]            // 全角负号（归一化后只剩数字）
    [InlineData("x=3", "Ｘ＝３")]         // 混合全角
    [InlineData("3.5", "３．５")]         // 全角小数点
    [InlineData("45", " 45 ")]           // 手写识别常带空格
    public void 填空题_全角半角与空格等价(string standard, string recognized)
        => Assert.True(AnswerNormalizer.IsCorrect(recognized, standard));

    [Theory]
    [InlineData("3", "4")]
    [InlineData("1/2", "1/3")]
    [InlineData("45", "")]
    [InlineData("3x", "3")]
    public void 填空题_内容不同不得判对(string standard, string recognized)
        => Assert.False(AnswerNormalizer.IsCorrect(recognized, standard));

    [Fact]
    public void 填空题_全角答案归一化后与半角一致()
        => Assert.Equal(AnswerNormalizer.Normalize("3.5"), AnswerNormalizer.Normalize("３．５"));
}
