using System.Text;
using AgoraIn.Core.Entities;
using QRCoder;

namespace AgoraIn.Server.Services;

/// <summary>
/// 答题卡渲染器：按试卷生成 A4 可打印 HTML（<c>@media print</c>）。
///
/// **服务端分页**：每张物理打印页 = 一个固定高度（<see cref="PageHeightMm"/>）的 .page 容器，
/// 页容量在服务端精确计算后切分题目，因此每一页都自带四角定位标记与页码二维码，
/// 不依赖浏览器的 position:fixed 重复渲染（浏览器页眉页脚为浏览器控制，网页无法注入内容）。
///
/// 版面要素（规格 6.8）：
///   · 页面四角定位标记（拍照透视校正）
///   · 学生信息区（姓名手写 + 考号涂卡区 + 可选条码）
///   · 客观题涂卡区（标准 OMR 方块，3 列）
///   · 主观题作答区（题号 + 边界框，用于切分）
///   · 页脚二维码（试卷 ID + 页码）
/// </summary>
public static class AnswerSheetRenderer
{
    // ── 版面容量常量（mm），用于服务端分页 ──
    private const double PageHeightMm = 277.0;   // A4 内容区高度（297 - 上下 10mm 页边距）
    private const double FooterReserveMm = 24.0; // 页脚（QR + 说明）保留区
    private const double HeaderBlockMm = 48.0;   // 首页标题 + 学生信息区
    private const double SectionTitleMm = 10.0;  // 区块标题
    private const double ObjItemHeightMm = 5.6;  // 单个客观题项（4mm 框 + 1.6mm 间距）
    private const double ObjColumns = 3;         // 客观题列数

    /// <summary>生成答题卡 HTML（自动服务端分页，每页独立定位标记与页码 QR）。</summary>
    public static string Render(
        ExamPaper paper,
        IReadOnlyList<Question> questions,
        IReadOnlyDictionary<string, List<string>> options,
        string? studentName = null,
        string? studentNo = null,
        int pageIndex = 1,
        int pageCount = 1)
    {
        var objective = questions.Where(q => IsObjective(q.Type)).ToList();
        var subjective = questions.Where(q => !IsObjective(q.Type)).ToList();

        // ── 服务端分页：把题目切成若干页，每页内容保证不溢出 ──
        var pages = new List<(string Html, bool HasHeader)>();

        // 1) 客观题分页（3 列布局）
        var objIdx = 0;
        var firstPage = true;
        while (objIdx < objective.Count)
        {
            var avail = PageHeightMm - FooterReserveMm - SectionTitleMm - (firstPage ? HeaderBlockMm : 0);
            var rows = Math.Max(1, (int)Math.Floor(avail / ObjItemHeightMm));
            var take = Math.Min(objective.Count - objIdx, rows * (int)ObjColumns);
            var chunk = objective.Skip(objIdx).Take(take).ToList();
            objIdx += take;
            pages.Add((BuildObjectiveSection(chunk, options), firstPage));
            firstPage = false;
        }

        // 2) 主观题分页（按作答框高度累计）
        var subIdx = 0;
        while (subIdx < subjective.Count)
        {
            var avail = PageHeightMm - FooterReserveMm - SectionTitleMm - (firstPage ? HeaderBlockMm : 0);
            var chunk = new List<Question>();
            double used = 0;
            while (subIdx < subjective.Count)
            {
                var h = SubjectiveHeightMm(subjective[subIdx]);
                if (chunk.Count > 0 && used + h > avail) break;
                chunk.Add(subjective[subIdx]);
                used += h;
                subIdx++;
            }
            pages.Add((BuildSubjectiveSection(chunk), firstPage));
            firstPage = false;
        }

        // 3) 无题兜底：至少生成一页（含空客观题区）
        if (pages.Count == 0)
        {
            pages.Add((BuildObjectiveSection([], options), true));
        }

        var totalPages = pages.Count;

        // ── 组装 HTML ──
        var sb = new StringBuilder();
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

  /* ── 物理页容器：高度固定为 A4 内容区，一容器 = 一张打印页；overflow:hidden 防溢出串页 ── */
  .page { position: relative; width: 190mm; height: 277mm; overflow: hidden;
          page-break-after: always; }
  .page:last-child { page-break-after: auto; }

  /* ── 四角定位标记（每页各自一份，absolute 相对本页，绝不越页） ── */
  .anchor { position: absolute; width: 6mm; height: 6mm; background: #000; z-index: 2; }
  .anchor.tl { top: 2mm; left: 2mm; }
  .anchor.tr { top: 2mm; right: 2mm; }
  .anchor.bl { bottom: 2mm; left: 2mm; }
  .anchor.br { bottom: 2mm; right: 2mm; }

  /* ── 内容区：底部留出页脚高度 ── */
  .content { padding: 6mm 0 0; height: calc(277mm - 24mm); overflow: hidden; }

  .title { text-align: center; font-size: 16pt; font-weight: bold; margin-bottom: 2mm; }
  .subtitle { text-align: center; font-size: 10pt; color: #333; margin-bottom: 4mm; }

  /* ── 学生信息区 ── */
  .info { border: 0.4mm solid #000; padding: 3mm; margin-bottom: 4mm; display: flex; gap: 4mm; }
  .info-left { flex: 1; font-size: 10pt; line-height: 8mm; }
  .info-right { width: 70mm; }
  .write-line { border-bottom: 0.3mm solid #666; display: inline-block; min-width: 30mm; }

  /* ── 考号涂卡区（标准 OMR 方块） ── */
  .id-grid { display: flex; gap: 1.5mm; }
  .id-col { text-align: center; }
  .id-col .col-label { font-size: 7pt; margin-bottom: 0.5mm; }
  .bubble { width: 6mm; height: 4mm; border: 0.3mm solid #000; margin: 0.4mm auto; }

  /* ── 客观题涂卡区（3 列） ── */
  .section-title { font-size: 11pt; font-weight: bold; margin: 2mm 0 2mm; border-left: 1mm solid #000; padding-left: 2mm; }
  .omr-grid { column-count: {{ObjColumns}}; column-gap: 4mm; }
  .omr-item { break-inside: avoid; margin-bottom: 1.6mm; font-size: 9pt; }
  .omr-row { display: flex; align-items: center; gap: 1mm; }
  .omr-no { width: 7mm; text-align: right; font-weight: bold; }
  .omr-opts { display: flex; gap: 2mm; }
  .omr-opt { width: 6mm; height: 4mm; border: 0.3mm solid #000;
             font-size: 7pt; text-align: center; line-height: 4mm; }

  /* ── 主观题作答区 ── */
  .answer-box { border: 0.4mm solid #000; margin-bottom: 3mm; break-inside: avoid; page-break-inside: avoid; }
  .answer-head { font-size: 9pt; padding: 1mm 2mm; border-bottom: 0.3mm dashed #888; }
  .answer-body { height: var(--h, 30mm); }
  .answer-lines { background-image: repeating-linear-gradient(transparent, transparent 7mm, #ccc 7mm, #ccc 7.2mm); }

  /* ── 页脚：绝对定位在本页容器内（每页各一份）──
     左右内边距 10mm：避开四角定位标记（占角部 2-8mm 区域），QR 与文字不被遮挡 */
  .footer { position: absolute; bottom: 0; left: 0; right: 0; height: 22mm;
            display: flex; justify-content: space-between; align-items: center;
            font-size: 8pt; color: #444; border-top: 0.3mm solid #999; padding: 2mm 10mm 0; }
  .qr { width: 18mm; height: 18mm; border: 0.3mm solid #000; display: flex;
        align-items: center; justify-content: center; font-size: 6pt; text-align: center; }
  .barcode { height: 12mm; width: 45mm; border: 0.3mm solid #000; display: flex;
             align-items: center; justify-content: center; font-size: 7pt; letter-spacing: 1mm; }
</style>
</head>
<body>
""");

        for (var i = 0; i < totalPages; i++)
        {
            var (content, hasHeader) = pages[i];
            var pageNo = i + 1;
            sb.Append("<div class=\"page\">");
            sb.Append("<div class=\"anchor tl\"></div><div class=\"anchor tr\"></div>");
            sb.Append("<div class=\"anchor bl\"></div><div class=\"anchor br\"></div>");
            sb.Append("<div class=\"content\">");

            if (hasHeader)
            {
                sb.Append($"<div class=\"title\">{Escape(paper.Title)}</div>");
                sb.Append($"<div class=\"subtitle\">科目：{Escape(paper.Subject ?? "—")}　总分：{paper.TotalScore:0.#}　第 {pageNo} / {totalPages} 页</div>");
                sb.Append("""
<div class="info">
  <div class="info-left">
    班级：<span class="write-line" style="min-width:40mm"></span><br>
    姓名：<span class="write-line" style="min-width:40mm">
""");
                sb.Append(Escape(studentName ?? ""));
                sb.Append("</span><br>学号：<span class=\"write-line\" style=\"min-width:40mm\">");
                sb.Append(Escape(studentNo ?? ""));
                sb.Append("</span></div><div class=\"info-right\">");
                sb.Append("<div style=\"font-size:8pt;margin-bottom:1mm\">考号填涂区（每列涂一个数字）</div>");
                sb.Append($"<div class=\"id-grid\">{BuildIdBubbles()}</div></div></div>");
            }
            else
            {
                sb.Append($"<div class=\"subtitle\" style=\"margin-bottom:2mm\">第 {pageNo} / {totalPages} 页</div>");
            }

            sb.Append(content);
            sb.Append("</div>"); // .content

            // 页脚：本页独立的页码 QR + 学号条码
            sb.Append("<div class=\"footer\">");
            sb.Append($"<div><span style=\"font-size:7pt\">试卷编号：{Escape(paper.Id)}　请用 2B 铅笔填涂，保持卡面整洁</span></div>");
            sb.Append(string.IsNullOrEmpty(studentNo) ? "" : $"<div class=\"barcode\">{Escape(studentNo)}</div>");
            sb.Append($"<div class=\"qr\">{BuildQrCode(paper.Id, pageNo)}</div>");
            sb.Append("</div>");

            sb.Append("</div>"); // .page
        }

        sb.Append("</body>\n</html>");
        return sb.ToString();
    }

    private static bool IsObjective(QuestionType t)
        => t is QuestionType.SingleChoice or QuestionType.MultipleChoice or QuestionType.Judge;

    /// <summary>主观题作答框高度（mm，含间距），用于分页累计。</summary>
    private static double SubjectiveHeightMm(Question q) => q.Type switch
    {
        QuestionType.Blank => 21.0,       // 18mm 框 + 3mm 间距
        QuestionType.ShortAnswer => 38.0, // 35 + 3
        QuestionType.Essay => 73.0,       // 70 + 3
        _ => 33.0,
    };

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

    /// <summary>客观题区：每题一行题号 + 选项方块。</summary>
    private static string BuildObjectiveSection(IReadOnlyList<Question> questions, IReadOnlyDictionary<string, List<string>> options)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"section-title\">一、客观题（请填涂所选选项）</div>");
        if (questions.Count == 0)
        {
            sb.Append("<div style=\"font-size:9pt;color:#666\">（无客观题）</div>");
            return sb.ToString();
        }
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
            var payload = pageIndex > 0 ? $"agorain:sheet:{paperId}:p{pageIndex}" : $"agorain:sheet:{paperId}";
            using var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var pngQr = new PngByteQRCode(qrData);
            var png = pngQr.GetGraphic(4);
            var base64 = Convert.ToBase64String(png);
            return $"<img src=\"data:image/png;base64,{base64}\" style=\"width:16mm;height:16mm\" />";
        }
        catch
        {
            // QRCoder 可能在某些环境不可用，回退为文本
            return $"<div style=\"font-size:6pt;text-align:center;line-height:1.2\">QR<br/>{Escape(paperId[..8])}…<br/>P{pageIndex}</div>";
        }
    }

    private static string Escape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
