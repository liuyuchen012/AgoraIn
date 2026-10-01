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
}
