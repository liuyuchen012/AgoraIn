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
///   · 学生信息区 + 黑框考号填涂区（手写行 / 列序号 / 0-9 涂卡列）
///   · 客观题块：黑框 + 左右 OMR 定时黑条（等距，供扫描端定位每一行）
///   · 主观题块：黑框 + 左右 OMR 定时黑条（对齐每个作答框顶部）
///   · 页脚二维码（试卷 ID + 页码）
/// </summary>
public static class AnswerSheetRenderer
{
    // ── 版面容量常量（mm）─────────────────────────────────────────────
    // 这些常量必须与下方 CSS 中的显式高度严格一致；宁可留有余量，也不允许溢出
    // （.content 为 overflow:hidden，一旦高估容量就会静默裁切末尾题目）。
    //
    // 纸张几何（@page margin:0 + 页容器自带内边距）：
    //   纸张 210×297mm；.page 210×289mm（留 8mm 余量，浏览器页盒略小时也不跨页漂移）
    //   .page 内边距 14/14/16mm → 内容盒 182×259mm；页脚 18mm 贴在内容区底部
    //   四角定位标记 6mm 见方、距纸边 7mm（避开打印机不可打印边距，且不被内容框压住）
    private const double PageUsableMm = 241.0;   // .content 可用高度
    private const double SafetyMm = 6.0;         // 排版误差安全余量（字体度量/行高取整）

    private const double HeaderBlockMm = 92.0;   // 首页：标题 + 副标题 + 信息区 + 考号区
    private const int IdDigitRows = 10;          // 考号位数 0-9
    private const int IdDigitColumns = 8;        // 考号列数（8 位考号）
    private const double IdRowPitchMm = 4.0;     // 考号涂卡行距（3.6mm 框 + 0.4mm 间距）

    private const double BlockChromeMm = 12.0;   // .qblock 边框 1 + 内边距 3 + 标题 5 + 外边距 3
    private const double ObjRowMm = 6.0;         // 客观题行距（与 .omr-item 高度一致）
    private const double ObjTickMm = 4.0;        // OMR 定时黑条高度（与选项框等高）
    private const int ObjColumnCount = 3;        // 客观题列数

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

        // ── 服务端分页 ──
        // 顺序装箱：先排完客观题，再排主观题；每页用「剩余容量」继续装下一块，
        // 装不下的题目留给下一页（绝不为了塞满而挤压，也不会让任何题目被裁切）。
        var pages = new List<string>();
        var objIdx = 0;
        var subIdx = 0;

        while (objIdx < objective.Count || subIdx < subjective.Count)
        {
            var capacity = SectionCapacityMm(pages.Count);
            var used = 0.0;
            var blocks = new StringBuilder();

            // 1) 客观题：按 3 列 x N 行装填本页剩余容量
            if (objIdx < objective.Count)
            {
                var availRows = capacity - used - BlockChromeMm - SafetyMm;
                var rows = (int)Math.Floor(availRows / ObjRowMm);
                var take = Math.Min(objective.Count - objIdx, Math.Max(0, rows) * ObjColumnCount);
                if (take > 0)
                {
                    var chunk = objective.Skip(objIdx).Take(take).ToList();
                    objIdx += take;
                    blocks.Append(BuildObjectiveBlock(chunk, options, rows));
                    used += BlockChromeMm + Math.Ceiling(take / (double)ObjColumnCount) * ObjRowMm;
                }
            }

            // 2) 主观题：客观题排完后，继续用本页剩余容量装主观题
            if (objIdx >= objective.Count && subIdx < subjective.Count)
            {
                var availSub = capacity - used - BlockChromeMm - SafetyMm;
                var chunk = new List<Question>();
                var chunkUsed = 0.0;
                while (subIdx < subjective.Count)
                {
                    var h = SubjectiveHeightMm(subjective[subIdx]);
                    if (chunk.Count > 0 && chunkUsed + h > availSub) break;
                    chunk.Add(subjective[subIdx]);
                    chunkUsed += h;
                    subIdx++;
                }
                if (chunk.Count > 0)
                {
                    blocks.Append(BuildSubjectiveBlock(chunk));
                }
            }

            // 兜底：容量估算若异常（理论上不会）也必须有内容，避免死循环
            if (blocks.Length == 0)
            {
                if (objIdx < objective.Count)
                {
                    var one = objective.Skip(objIdx).Take(ObjColumnCount).ToList();
                    objIdx += one.Count;
                    blocks.Append(BuildObjectiveBlock(one, options, 1));
                }
                else
                {
                    var one = subjective.Skip(subIdx).Take(1).ToList();
                    subIdx += one.Count;
                    blocks.Append(BuildSubjectiveBlock(one));
                }
            }

            pages.Add(blocks.ToString());
        }

        // 无题兜底：至少生成一页（含空客观题区）
        if (pages.Count == 0)
        {
            pages.Add(BuildObjectiveBlock([], options, 1));
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
  /* 纸张不交给浏览器算边距：@page margin:0 + 页容器自带内边距。
     浏览器若按打印机硬件边距重算页盒（页盒变小），固定高度的页容器会跨页拆开，
     表现为「第二页起内容整体下移」；页容器固定 289mm（比 A4 矮 8mm）后不再依赖页盒精度。 */
  @page { size: A4; margin: 0; }
  * { box-sizing: border-box; }
  body { font-family: "SimSun", "Songti SC", serif; margin: 0; color: #000; }

  /* ── 物理页容器：一容器 = 一张打印页；overflow:hidden 防溢出串页 ── */
  .page { position: relative; width: 210mm; height: 289mm; padding: 14mm 14mm 16mm;
          overflow: hidden; page-break-after: always; break-after: page; }
  .page:last-child { page-break-after: auto; break-after: auto; }

  /* ── 四角定位标记：距纸边 7mm，避开打印机不可打印边距；每页各自一份，绝不越页 ── */
  .anchor { position: absolute; width: 6mm; height: 6mm; background: #000; z-index: 3; }
  .anchor.tl { top: 7mm; left: 7mm; }
  .anchor.tr { top: 7mm; right: 7mm; }
  .anchor.bl { bottom: 7mm; left: 7mm; }
  .anchor.br { bottom: 7mm; right: 7mm; }

  /* ── 内容区（241mm）与页脚 ── */
  .content { height: 241mm; overflow: hidden; }

  .title { font-size: 16pt; line-height: 1.2; text-align: center; font-weight: bold; margin: 0 0 2mm; }
  .subtitle { font-size: 10pt; line-height: 1.2; text-align: center; color: #333; margin: 0 0 4mm; }

  /* ── 学生信息区（左：手写姓名等；右：黑框考号填涂区） ── */
  .info { border: 0.5mm solid #000; padding: 3mm; margin: 0 0 4mm; display: flex; gap: 4mm; }
  .info-left { flex: 1; font-size: 10pt; line-height: 8mm; }
  .write-line { border-bottom: 0.3mm solid #666; display: inline-block; min-width: 40mm; }

  /* ── 考号填涂区（黑框包裹：手写行 + 列序号 + 0-9 涂卡列） ── */
  .id-area { border: 0.5mm solid #000; padding: 2mm; }
  .id-title { font-size: 8pt; line-height: 1.2; margin: 0 0 1mm; }
  .id-write { display: flex; align-items: center; margin: 0 0 1.5mm; }
  .id-write-label { font-size: 9pt; margin-right: 1mm; }
  .wbox { width: 6.5mm; height: 7mm; border: 0.4mm solid #000; margin-right: 0.8mm; }
  .id-grid { display: flex; }
  .id-rowlabels { width: 5mm; }
  .id-rowlabel { height: 4mm; font-size: 7pt; line-height: 4mm; text-align: right;
                 padding-right: 1mm; }
  .id-rowlabel.head { height: 4mm; }
  .id-cols { display: flex; }
  .id-col { margin-right: 0.8mm; text-align: center; }
  .col-no { height: 4mm; font-size: 7pt; line-height: 4mm; }
  .bubble { width: 6mm; height: 3.6mm; border: 0.3mm solid #000; margin: 0 auto 0.4mm; }

  /* ── 题目区块：黑框 + 左右 OMR 定时黑条 ── */
  .qblock { border: 0.5mm solid #000; padding: 1.5mm 2mm; margin: 0 0 3mm; }
  .block-title { font-size: 10pt; font-weight: bold; line-height: 1.2; margin: 0 0 1.5mm 1mm; }
  .qbody { position: relative; padding: 0 6mm; }
  .tmarks { position: absolute; top: 0; bottom: 0; width: 3mm; }
  .tmarks.left { left: 1mm; }
  .tmarks.right { right: 1mm; }
  .tick { position: absolute; left: 0; width: 3mm; background: #000; }

  /* ── 客观题（固定行高 6mm，与分页常量 ObjRowMm 一致） ── */
  .omr-cols { display: flex; gap: 3mm; }
  .omr-col { flex: 1; }
  .omr-item { height: 6mm; overflow: hidden; }
  .omr-row { display: flex; align-items: center; gap: 1mm; height: 6mm; font-size: 9pt; }
  .omr-no { width: 7mm; text-align: right; font-weight: bold; }
  .omr-opts { display: flex; }
  .omr-opt { width: 6mm; height: 4mm; border: 0.3mm solid #000; margin-right: 1.5mm;
             font-size: 7pt; line-height: 4mm; text-align: center; }

  /* ── 主观题作答区（高度与 SubjectiveHeightMm 一致） ── */
  .answer-box { border: 0.5mm solid #000; margin: 0 0 3mm; break-inside: avoid; page-break-inside: avoid; }
  .answer-head { font-size: 9pt; height: 6mm; line-height: 6mm; padding: 0 2mm;
                 border-bottom: 0.3mm dashed #888; }
  .answer-body { height: var(--h, 30mm); }
  .answer-lines { background-image: repeating-linear-gradient(transparent, transparent 7mm, #ccc 7mm, #ccc 7.2mm); }

  /* ── 页脚：绝对定位在本页容器内（每页各一份），位于内容区底部，上方是题目、下方是定位标记 ── */
  .footer { position: absolute; left: 14mm; right: 14mm; bottom: 16mm; height: 18mm;
            display: flex; justify-content: space-between; align-items: center;
            font-size: 8pt; color: #444; border-top: 0.3mm solid #999; padding: 1mm 0 0; }
  .qr { width: 16mm; height: 16mm; border: 0.3mm solid #000; display: flex;
        align-items: center; justify-content: center; font-size: 6pt; text-align: center; }
  .barcode { height: 12mm; width: 45mm; border: 0.3mm solid #000; display: flex;
             align-items: center; justify-content: center; font-size: 7pt; letter-spacing: 1mm; }
</style>
</head>
<body>
""");

        for (var i = 0; i < totalPages; i++)
        {
            var pageNo = i + 1;
            var hasHeader = i == 0;

            sb.Append("<div class=\"page\">");
            sb.Append("<div class=\"anchor tl\"></div><div class=\"anchor tr\"></div>");
            sb.Append("<div class=\"anchor bl\"></div><div class=\"anchor br\"></div>");
            sb.Append("<div class=\"content\">");

            if (hasHeader)
            {
                sb.Append($"<div class=\"title\">{Escape(paper.Title)}</div>");
                sb.Append($"<div class=\"subtitle\">科目：{Escape(paper.Subject ?? "—")}　总分：{paper.TotalScore:0.#}　第 {pageNo} / {totalPages} 页</div>");
                sb.Append("<div class=\"info\">");
                sb.Append("<div class=\"info-left\">班级：<span class=\"write-line\"></span><br>姓名：<span class=\"write-line\">");
                sb.Append(Escape(studentName ?? ""));
                sb.Append("</span><br>学号：<span class=\"write-line\">");
                sb.Append(Escape(studentNo ?? ""));
                sb.Append("</span></div>");
                sb.Append(BuildIdArea());
                sb.Append("</div>");
            }
            else
            {
                sb.Append($"<div class=\"subtitle\" style=\"margin-bottom:2mm\">第 {pageNo} / {totalPages} 页</div>");
            }

            sb.Append(pages[i]);
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

    /// <summary>本页可用内容高度（首页需扣除标题/信息/考号区）。</summary>
    private static double SectionCapacityMm(int pageOrdinal)
        => PageUsableMm - SafetyMm - (pageOrdinal == 0 ? HeaderBlockMm : 0);

    private static bool IsObjective(QuestionType t)
        => t is QuestionType.SingleChoice or QuestionType.MultipleChoice or QuestionType.Judge;

    /// <summary>
    /// 主观题作答框高度（mm，含边框、题头与下间距），必须与 CSS 中
    /// .answer-box / .answer-head / .answer-body 的显式高度一致：
    /// 0.5×2 边框 + 6 题头 + 正文 + 3 下间距。
    /// </summary>
    private static double SubjectiveHeightMm(Question q) => q.Type switch
    {
        QuestionType.Blank => 28.0,       // 正文 18mm
        QuestionType.ShortAnswer => 45.0, // 正文 35mm
        QuestionType.Essay => 74.0,       // 正文 64mm
        _ => 40.0,                        // 正文 30mm
    };

    /// <summary>主观题正文高度（CSS --h）。</summary>
    private static string SubjectiveBodyHeight(QuestionType t) => t switch
    {
        QuestionType.Blank => "18mm",
        QuestionType.ShortAnswer => "35mm",
        QuestionType.Essay => "64mm",
        _ => "30mm",
    };

    /// <summary>考号填涂区（黑框）：手写考号行 + 列序号 + 8 列 × 10 行涂卡格。</summary>
    private static string BuildIdArea()
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"id-area\">");
        sb.Append("<div class=\"id-title\">考号填涂区（每列涂一个数字，共 8 位）</div>");

        // 手写考号行：与下方涂卡列一一对齐
        sb.Append("<div class=\"id-write\"><span class=\"id-write-label\">考号：</span>");
        for (var c = 0; c < IdDigitColumns; c++)
        {
            sb.Append("<span class=\"wbox\"></span>");
        }
        sb.Append("</div>");

        // 左侧行标（0-9）+ 右侧 8 个涂卡列（每列顶部带列序号）
        sb.Append("<div class=\"id-grid\">");
        sb.Append("<div class=\"id-rowlabels\"><div class=\"id-rowlabel head\"></div>");
        for (var n = 0; n < IdDigitRows; n++)
        {
            sb.Append($"<div class=\"id-rowlabel\">{n}</div>");
        }
        sb.Append("</div>");

        sb.Append("<div class=\"id-cols\">");
        for (var c = 0; c < IdDigitColumns; c++)
        {
            sb.Append("<div class=\"id-col\">");
            sb.Append($"<div class=\"col-no\">{c + 1}</div>");
            for (var n = 0; n < IdDigitRows; n++)
            {
                sb.Append("<div class=\"bubble\"></div>");
            }
            sb.Append("</div>");
        }
        sb.Append("</div></div>");

        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>
    /// 左右 OMR 定时黑条：在 .qbody 内按给定纵向偏移（mm）放置等宽黑块，
    /// 供扫描端标定行距与透视校正。偏移基准为 .qbody 顶部。
    /// </summary>
    private static string BuildTimingMarks(IReadOnlyList<double> offsetsMm)
    {
        if (offsetsMm.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var side in new[] { "left", "right" })
        {
            sb.Append($"<div class=\"tmarks {side}\">");
            foreach (var off in offsetsMm)
            {
                sb.Append($"<div class=\"tick\" style=\"top:{off:0.##}mm;height:{ObjTickMm}mm\"></div>");
            }
            sb.Append("</div>");
        }
        return sb.ToString();
    }

    /// <summary>客观题块：黑框 + 左右定时条 + 3 列涂卡区（列优先排布）。</summary>
    private static string BuildObjectiveBlock(
        IReadOnlyList<Question> questions,
        IReadOnlyDictionary<string, List<string>> options,
        int rowsPerColumn)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"qblock\">");
        sb.Append("<div class=\"block-title\">一、客观题（请填涂所选选项）</div>");
        sb.Append("<div class=\"qbody\">");

        if (questions.Count == 0)
        {
            sb.Append("<div style=\"font-size:9pt;color:#666\">（无客观题）</div>");
            sb.Append("</div></div>");
            return sb.ToString();
        }

        // 行数按本页实际题量计算（不是页容量），否则定时条会越过内容悬浮在黑框之外
        var perCol = Math.Max(1, (int)Math.Ceiling(questions.Count / (double)ObjColumnCount));

        // 定时条：与每一行选项框对齐（行距 ObjRowMm，黑条高 ObjTickMm 居中）
        var offsets = new List<double>();
        for (var r = 0; r < perCol; r++)
        {
            offsets.Add(r * ObjRowMm + (ObjRowMm - ObjTickMm) / 2);
        }
        sb.Append(BuildTimingMarks(offsets));

        sb.Append("<div class=\"omr-cols\">");
        for (var c = 0; c < ObjColumnCount; c++)
        {
            sb.Append("<div class=\"omr-col\">");
            foreach (var q in questions.Skip(c * perCol).Take(perCol))
            {
                var keys = q.Type == QuestionType.Judge
                    ? ["√", "×"]
                    : options.TryGetValue(q.Id, out var list) && list.Count > 0
                        ? list
                        : ["A", "B", "C", "D"];

                sb.Append("<div class=\"omr-item\"><div class=\"omr-row\">");
                sb.Append($"<div class=\"omr-no\">{q.Index + 1}.</div><div class=\"omr-opts\">");
                foreach (var k in keys)
                {
                    sb.Append($"<div class=\"omr-opt\">{Escape(k)}</div>");
                }
                sb.Append("</div></div></div>");
            }
            sb.Append("</div>");
        }
        sb.Append("</div>");

        sb.Append("</div></div>");
        return sb.ToString();
    }

    /// <summary>主观题块：黑框 + 左右定时条（对齐每个作答框顶部）+ 作答框。</summary>
    private static string BuildSubjectiveBlock(IReadOnlyList<Question> questions)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"qblock\">");
        sb.Append("<div class=\"block-title\">二、主观题（请在框内作答）</div>");
        sb.Append("<div class=\"qbody\">");

        // 定时条：每个作答框顶部一条，末尾补一条闭合标记
        var offsets = new List<double>();
        double cursor = 0;
        foreach (var q in questions)
        {
            offsets.Add(cursor);
            cursor += SubjectiveHeightMm(q);
        }
        offsets.Add(cursor);
        sb.Append(BuildTimingMarks(offsets));

        foreach (var q in questions)
        {
            var height = SubjectiveBodyHeight(q.Type);
            var lines = q.Type is QuestionType.ShortAnswer or QuestionType.Essay ? " answer-lines" : "";

            sb.Append($$"""
<div class="answer-box" style="--h:{{height}}">
  <div class="answer-head">第 {{q.Index + 1}} 题（{{q.Score:0.#}} 分）</div>
  <div class="answer-body{{lines}}"></div>
</div>
""");
        }

        sb.Append("</div></div>");
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
