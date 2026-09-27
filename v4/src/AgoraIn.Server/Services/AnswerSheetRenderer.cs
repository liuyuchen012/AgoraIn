using System.Text;
using AgoraIn.Core.Entities;
using QRCoder;

namespace AgoraIn.Server.Services;

/// <summary>
/// 答题卡渲染器：按试卷生成 A4 可打印 HTML（<c>@media print</c>）。
/// 版面要素（规格 6.8）：
///   · 页面四角定位标记（拍照透视校正）
///   · 学生信息区（姓名手写 + 考号涂卡区 + 可选条码）
///   · 客观题涂卡区（标准 OMR 间距气泡）
///   · 主观题作答区（题号 + 边界框，用于切分）
///   · 页脚二维码占位（试卷 ID + 页码）
/// </summary>
public static class AnswerSheetRenderer
{
    /// <summary>生成答题卡 HTML。</summary>
    /// <param name="paper">试卷。</param>
    /// <param name="questions">题目（按 Index 排序）。</param>
    /// <param name="options">每题选项（客观题为选项列表，主观题为空）。</param>
    /// <param name="studentName">指定学生时生成带姓名/条码的专属卡；null 生成通用空白卡。</param>
    /// <param name="studentNo">学号（用于条码）。</param>
    /// <param name="pageIndex">页码（1 起）。</param>
    /// <param name="pageCount">总页数。</param>
    public static string Render(
        ExamPaper paper,
        IReadOnlyList<Question> questions,
        IReadOnlyDictionary<string, List<string>> options,
        string? studentName = null,
        string? studentNo = null,
        int pageIndex = 1,
        int pageCount = 1)
    {
        var sb = new StringBuilder();
        var objective = questions.Where(q => IsObjective(q.Type)).ToList();
        var subjective = questions.Where(q => !IsObjective(q.Type)).ToList();

        sb.Append($$"""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<title>{{Escape(paper.Title)}} - 答题卡</title>
<style>
  @page { size: A4; margin: 10mm; }
  * { box-sizing: border-box; }
  body { font-family: "SimSun", "Songti SC", serif; margin: 0; color: #000; }

  /* ── 四角定位标记（黑色实心方块，用于透视校正） ── */
  .anchor { position: absolute; width: 6mm; height: 6mm; background: #000; }
  .anchor.tl { top: 4mm; left: 4mm; }
  .anchor.tr { top: 4mm; right: 4mm; }
  .anchor.bl { bottom: 4mm; left: 4mm; }
  .anchor.br { bottom: 4mm; right: 4mm; }

  .sheet { position: relative; width: 190mm; min-height: 277mm; padding: 14mm 10mm 10mm; }

  .title { text-align: center; font-size: 16pt; font-weight: bold; margin-bottom: 2mm; }
  .subtitle { text-align: center; font-size: 10pt; color: #333; margin-bottom: 4mm; }

  /* ── 学生信息区 ── */
  .info { border: 0.4mm solid #000; padding: 3mm; margin-bottom: 4mm; display: flex; gap: 4mm; }
  .info-left { flex: 1; font-size: 10pt; line-height: 8mm; }
  .info-right { width: 70mm; }
  .write-line { border-bottom: 0.3mm solid #666; display: inline-block; min-width: 30mm; }

  /* ── 考号涂卡区（OMR 气泡，0-9 十行） ── */
  .id-grid { display: flex; gap: 1.5mm; }
  .id-col { text-align: center; }
  .id-col .col-label { font-size: 7pt; margin-bottom: 0.5mm; }
  .bubble { width: 4.5mm; height: 4.5mm; border: 0.3mm solid #000; border-radius: 50%; margin: 0.6mm auto; }

  /* ── 客观题涂卡区 ── */
  .section-title { font-size: 11pt; font-weight: bold; margin: 3mm 0 2mm; border-left: 1mm solid #000; padding-left: 2mm; }
  .omr-grid { column-count: 3; column-gap: 4mm; }
  .omr-item { break-inside: avoid; margin-bottom: 1.6mm; font-size: 9pt; }
  .omr-row { display: flex; align-items: center; gap: 1mm; }
  .omr-no { width: 7mm; text-align: right; font-weight: bold; }
  .omr-opts { display: flex; gap: 1mm; }
  .omr-opt { width: 4.5mm; height: 4.5mm; border: 0.3mm solid #000; border-radius: 50%;
             font-size: 6pt; text-align: center; line-height: 4.5mm; }

  /* ── 主观题作答区（边界框供切分） ── */
  .answer-box { border: 0.4mm solid #000; margin-bottom: 3mm; }
  .answer-head { font-size: 9pt; padding: 1mm 2mm; border-bottom: 0.3mm dashed #888; }
  .answer-body { height: var(--h, 30mm); }
  .answer-lines { background-image: repeating-linear-gradient(transparent, transparent 7mm, #ccc 7mm, #ccc 7.2mm); }

  /* ── 页脚 ── */
  .footer { position: absolute; bottom: 12mm; left: 10mm; right: 10mm;
            display: flex; justify-content: space-between; align-items: center;
            font-size: 8pt; color: #444; border-top: 0.3mm solid #999; padding-top: 2mm; }
  .qr { width: 18mm; height: 18mm; border: 0.3mm solid #000; display: flex;
        align-items: center; justify-content: center; font-size: 6pt; text-align: center; }
  .barcode { height: 12mm; width: 45mm; border: 0.3mm solid #000; display: flex;
             align-items: center; justify-content: center; font-size: 7pt; letter-spacing: 1mm; }
</style>
</head>
<body>
<div class="sheet">
  <div class="anchor tl"></div><div class="anchor tr"></div>
  <div class="anchor bl"></div><div class="anchor br"></div>

  <div class="title">{{Escape(paper.Title)}}</div>
  <div class="subtitle">
    科目：{{Escape(paper.Subject ?? "—")}}　总分：{{paper.TotalScore:0.#}}　
    第 {{pageIndex}} / {{pageCount}} 页
  </div>

  <!-- ═══ 学生信息区 ═══ -->
  <div class="info">
    <div class="info-left">
      班级：<span class="write-line" style="min-width:40mm"></span><br>
      姓名：<span class="write-line" style="min-width:40mm">{{Escape(studentName ?? "")}}</span><br>
      学号：<span class="write-line" style="min-width:40mm">{{Escape(studentNo ?? "")}}</span>
    </div>
    <div class="info-right">
      <div style="font-size:8pt;margin-bottom:1mm">考号填涂区（每列涂一个数字）</div>
      <div class="id-grid">
        {{BuildIdBubbles()}}
      </div>
    </div>
  </div>

  {{(objective.Count > 0 ? BuildObjectiveSection(objective, options) : "")}}

  {{(subjective.Count > 0 ? BuildSubjectiveSection(subjective) : "")}}

  <div class="footer">
    <div>
      试卷编号：{{Escape(paper.Id)}}<br>
      <span style="font-size:7pt">请用 2B 铅笔填涂，保持卡面整洁</span>
    </div>
    {{(string.IsNullOrEmpty(studentNo) ? "" : $"<div class=\"barcode\">{Escape(studentNo)}</div>")}}
    <div class="qr">{{BuildQrCode(paper.Id, pageIndex)}}</div>
  </div>
</div>
</body>
</html>
""");
        return sb.ToString();
    }

    private static bool IsObjective(QuestionType t)
        => t is QuestionType.SingleChoice or QuestionType.MultipleChoice or QuestionType.Judge;

    /// <summary>考号涂卡区：8 列 × 10 行（0-9）。</summary>
    private static string BuildIdBubbles()
    {
        var sb = new StringBuilder();
        for (var col = 0; col < 8; col++)
        {
            sb.Append("<div class=\"id-col\">");
            sb.Append("<div class=\"col-label\">-</div>");
            for (var n = 0; n < 10; n++)
            {
                sb.Append("<div class=\"bubble\"></div>");
            }
            sb.Append("</div>");
        }
        return sb.ToString();
    }

    /// <summary>客观题区：每题一行题号 + 选项气泡。</summary>
    private static string BuildObjectiveSection(IReadOnlyList<Question> questions, IReadOnlyDictionary<string, List<string>> options)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"section-title\">一、客观题（请填涂所选选项）</div>");
        sb.Append("<div class=\"omr-grid\">");

        foreach (var q in questions)
        {
            if (q.Type == QuestionType.Judge)
            {
                sb.Append($$"""
<div class="omr-item">
  <div class="omr-row">
    <div class="omr-no">{{q.Index + 1}}.</div>
    <div class="omr-opts">
      <div class="omr-opt">√</div><div class="omr-opt">×</div>
    </div>
  </div>
</div>
""");
                continue;
            }

            // 选项键：优先用题目 OptionsJson 里配置的，否则默认 A-D
            var keys = options.TryGetValue(q.Id, out var list) && list.Count > 0
                ? list
                : ["A", "B", "C", "D"];

            sb.Append($$"""
<div class="omr-item">
  <div class="omr-row">
    <div class="omr-no">{{q.Index + 1}}.</div>
    <div class="omr-opts">
""");
            foreach (var k in keys)
            {
                sb.Append($"<div class=\"omr-opt\">{Escape(k)}</div>");
            }
            sb.Append("</div></div></div>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>主观题区：题号 + 作答边界框（高度按题型）。</summary>
    private static string BuildSubjectiveSection(IReadOnlyList<Question> questions)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"section-title\">二、主观题（请在框内作答）</div>");

        foreach (var q in questions)
        {
            var height = q.Type switch
            {
                QuestionType.Blank => "18mm",
                QuestionType.ShortAnswer => "35mm",
                QuestionType.Essay => "70mm",
                _ => "30mm",
            };
            var lines = q.Type is QuestionType.ShortAnswer or QuestionType.Essay ? " answer-lines" : "";

            sb.Append($$"""
<div class="answer-box" style="--h:{{height}}">
  <div class="answer-head">第 {{q.Index + 1}} 题（{{q.Score:0.#}} 分）</div>
  <div class="answer-body{{lines}}"></div>
</div>
""");
        }

        return sb.ToString();
    }

    /// <summary>生成 QR 码图片（base64 内嵌 HTML）。</summary>
    private static string BuildQrCode(string paperId, int pageIndex)
    {
        try
        {
            var payload = $"agorain:sheet:{paperId}:p{pageIndex}";
            using var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var pngQr = new PngByteQRCode(qrData);
            var png = pngQr.GetGraphic(4);
            var base64 = Convert.ToBase64String(png);
            return $"<img src=\"data:image/png;base64,{base64}\" style=\"width:16mm;height:16mm\" />";
        }
        catch (Exception ex)
        {
            // QRCoder 可能在某些环境不可用，回退为文本
            return $"<div style=\"font-size:6pt;text-align:center;line-height:1.2\">QR<br/>{Escape(paperId[..8])}…<br/>P{pageIndex}</div>";
        }
    }

    private static string Escape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
