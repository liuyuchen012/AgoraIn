using System.Text;
using AgoraIn.Core.Entities;
using QRCoder;

namespace AgoraIn.Server.Services;

/// <summary>答题卡纸张预设（mm）。速印机常用 8 开/16 开，普通打印机用 A4。</summary>
public sealed record SheetPaper(string Name, double WidthMm, double HeightMm)
{
    public static readonly SheetPaper A4 = new("A4", 210, 297);
    public static readonly SheetPaper B4 = new("B4", 250, 353);
    public static readonly SheetPaper K8 = new("8K", 260, 370);
    public static readonly SheetPaper K16 = new("16K", 185, 260);
    public static readonly SheetPaper A3 = new("A3", 297, 420);

    public static readonly SheetPaper[] All = [A4, B4, K8, K16, A3];

    /// <summary>按名称取纸张（不区分大小写，未知回落 A4）。</summary>
    public static SheetPaper FromName(string? name)
        => All.FirstOrDefault(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? A4;
}

/// <summary>考号区形式。</summary>
public enum IdAreaKind
{
    /// <summary>考号填涂区（手写行 + 列序号 + 0-9 气泡），机器识别考号。</summary>
    Bubble,

    /// <summary>仅手写考号行，适合速印机批量印刷后学生手写。</summary>
    Handwrite,

    /// <summary>不显示考号区（只保留班级/姓名/学号手写行）。</summary>
    None,
}

/// <summary>答题卡渲染选项（对应 WebAdmin「答题卡配置」）。</summary>
public sealed record AnswerSheetOptions
{
    public SheetPaper Paper { get; init; } = SheetPaper.A4;
    public IdAreaKind IdArea { get; init; } = IdAreaKind.Bubble;
    public bool ShowNotes { get; init; } = true;

    public static AnswerSheetOptions Default { get; } = new();

    /// <summary>物理页容器高度：比纸张矮 6mm，浏览器页盒略小时也不会跨页漂移。</summary>
    public double PageBoxHeightMm => Paper.HeightMm - 6;

    /// <summary>可排内容高度 = 纸张高 - 顶部内边距 14 - 底部内边距 16 - 页脚 18 - 8mm 余量。</summary>
    public double UsableMm => Paper.HeightMm - 54;

    /// <summary>页面内容宽度 = 纸张宽 - 左右内边距 14×2。</summary>
    public double ContentWidthMm => Paper.WidthMm - 28;

    /// <summary>客观题列数：窄纸（16 开等）两列，常规纸三列。</summary>
    public int ObjColumns => ContentWidthMm >= 180 ? 3 : 2;

    /// <summary>首页表头高度（标题 + 副标题 + 注意事项 + 学生信息区）。</summary>
    public double HeaderMm => 92 + (ShowNotes ? 18 : 0);

    /// <summary>无底部安全余量的可排高度（分页用）。</summary>
    public double CapacityMm(int pageOrdinal) => UsableMm - 6 - (pageOrdinal == 0 ? HeaderMm : 0);
}

/// <summary>
/// 答题卡 / 考号表渲染器：生成可打印 HTML（<c>@media print</c>）。
///
/// **服务端分页**：每张物理打印页 = 一个固定高度的 .page 容器，
/// 页容量在服务端精确计算后切分题目，因此每一页都自带定位标记与页码二维码，
/// 不依赖浏览器的 position:fixed 重复渲染。
///
/// **通用答题卡**：不预填任何学生信息（姓名/考号留空手写），
/// 全班共用一张版，适合速印机（一体机）一次制版批量印刷。
///
/// 版面要素（规格 6.8）：
///   · 页面四角定位标记（拍照透视校正，距纸边 7mm 避开打印机不可打印边距）
///   · 学生信息区 + 考号区（填涂 / 手写 / 不显示）
///   · 客观题块：黑框 + 左右 OMR 定时黑条（等距，供扫描端定位每一行）
///   · 主观题块：黑框 + 左右 OMR 定时黑条（对齐每个作答框顶部）
/// </summary>
public static class AnswerSheetRenderer
{
    // ── 版面容量常量（mm）─────────────────────────────────────────────
    // 必须与下方 CSS 中的显式高度严格一致；宁可留有余量，也不允许溢出
    // （.content 为 overflow:hidden，一旦高估容量就会静默裁切末尾题目）。
    private const double BlockChromeMm = 12.0;   // .qblock 边框 1 + 内边距 3 + 标题 5 + 外边距 3
    private const double ObjRowMm = 6.0;         // 客观题行距（与 .omr-item 高度一致）
    private const double ObjTickMm = 4.0;        // OMR 定时黑条高度（与选项框等高）

    private const int IdDigitRows = 10;          // 考号位数 0-9
    private const int IdDigitColumns = 8;        // 考号列数（8 位考号）

    private const string NotesHtml = """
<div class="notes">
  <b>注意事项</b>
  1. 答题前请用黑色签字笔在左侧填写班级、姓名、学号，并按自己的考号填涂（或书写）考号区。<br>
  2. 客观题用 2B 铅笔填涂，修改时用橡皮擦净；主观题在框内作答，超出答题区域的答案无效。<br>
  3. 保持卡面整洁，禁止折叠、污损；考号填涂错误将影响成绩录入。
</div>
""";

    /// <summary>生成通用答题卡 HTML（自动服务端分页，每页独立定位标记与页码 QR）。</summary>
    public static string Render(
        ExamPaper paper,
        IReadOnlyList<Question> questions,
        IReadOnlyDictionary<string, List<string>> options,
        string? studentName = null,
        string? studentNo = null,
        int pageIndex = 1,
        int pageCount = 1,
        AnswerSheetOptions? sheetOptions = null)
    {
        var opt = sheetOptions ?? AnswerSheetOptions.Default;
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
            var capacity = opt.CapacityMm(pages.Count);
            var used = 0.0;
            var blocks = new StringBuilder();

            // 1) 客观题：按 N 列 x M 行装填本页剩余容量
            if (objIdx < objective.Count)
            {
                var rows = (int)Math.Floor((capacity - used - BlockChromeMm) / ObjRowMm);
                var take = Math.Min(objective.Count - objIdx, Math.Max(0, rows) * opt.ObjColumns);
                if (take > 0)
                {
                    var chunk = objective.Skip(objIdx).Take(take).ToList();
                    objIdx += take;
                    blocks.Append(BuildObjectiveBlock(chunk, options, opt.ObjColumns));
                    used += BlockChromeMm + Math.Ceiling(take / (double)opt.ObjColumns) * ObjRowMm;
                }
            }

            // 2) 主观题：客观题排完后，继续用本页剩余容量装主观题
            if (objIdx >= objective.Count && subIdx < subjective.Count)
            {
                var availSub = capacity - used - BlockChromeMm;
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
                if (chunk.Count > 0) blocks.Append(BuildSubjectiveBlock(chunk));
            }

            // 兜底：容量估算若异常（理论上不会）也必须有内容，避免死循环
            if (blocks.Length == 0)
            {
                if (objIdx < objective.Count)
                {
                    var one = objective.Skip(objIdx).Take(opt.ObjColumns).ToList();
                    objIdx += one.Count;
                    blocks.Append(BuildObjectiveBlock(one, options, opt.ObjColumns));
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

        // 无题兜底：至少生成一页
        if (pages.Count == 0) pages.Add(BuildObjectiveBlock([], options, opt.ObjColumns));

        var totalPages = pages.Count;
        var sb = new StringBuilder();
        sb.Append(CssHeader(opt, Escape(paper.Title) + " - 答题卡"));
        sb.Append("<body>\n");

        for (var i = 0; i < totalPages; i++)
        {
            var pageNo = i + 1;
            sb.Append(PageOpen(opt));
            if (i == 0)
            {
                sb.Append($"<div class=\"title\">{Escape(paper.Title)}</div>");
                sb.Append($"<div class=\"subtitle\">科目：{Escape(paper.Subject ?? "—")}　总分：{paper.TotalScore:0.#}　第 {pageNo} / {totalPages} 页</div>");
                if (opt.ShowNotes) sb.Append(NotesHtml);
                sb.Append(BuildInfoArea(opt.IdArea, studentName, studentNo));
            }
            else
            {
                sb.Append($"<div class=\"subtitle\" style=\"margin-bottom:2mm\">第 {pageNo} / {totalPages} 页</div>");
            }

            sb.Append(pages[i]);
            sb.Append(PageClose(paper.Id, studentNo, pageNo));
        }

        sb.Append("</body>\n</html>");
        return sb.ToString();
    }

    /// <summary>
    /// 考号表：把生成的考号打印张贴（或发给学生），供学生在通用答题卡上填涂。
    /// 每页两栏、每栏 28 行，一页可容纳 56 人。
    /// </summary>
    public static string RenderRoster(
        string title,
        IReadOnlyList<(string No, string Name)> entries,
        AnswerSheetOptions? sheetOptions = null)
    {
        const int rowsPerColumn = 28;
        const int columns = 2;
        var perPage = rowsPerColumn * columns;
        var opt = sheetOptions ?? AnswerSheetOptions.Default;
        var totalPages = Math.Max(1, (int)Math.Ceiling(entries.Count / (double)perPage));

        var sb = new StringBuilder();
        sb.Append(CssHeader(opt, Escape(title) + " - 考号表"));
        sb.Append("<body>\n");

        for (var p = 0; p < totalPages; p++)
        {
            sb.Append(PageOpen(opt, withAnchors: false));
            sb.Append($"<div class=\"roster-title\">{Escape(title)}　考号表</div>");
            sb.Append($"<div class=\"roster-sub\">共 {entries.Count} 人　第 {p + 1} / {totalPages} 页　" +
                      "（请在自己的通用答题卡上按此考号填涂/书写）</div>");
            sb.Append("<div class=\"roster-cols\">");

            for (var c = 0; c < columns; c++)
            {
                sb.Append("<div class=\"roster-col\">");
                for (var r = 0; r < rowsPerColumn; r++)
                {
                    var idx = p * perPage + c * rowsPerColumn + r;
                    if (idx >= entries.Count) break;
                    var (no, name) = entries[idx];
                    sb.Append($"<div class=\"roster-row\"><span class=\"seq\">{idx + 1}</span>" +
                              $"<span class=\"no\">{Escape(no)}</span><span class=\"name\">{Escape(name)}</span></div>");
                }
                sb.Append("</div>");
            }
            sb.Append("</div>");
            sb.Append(PageClose(null, null, p + 1, totalPages));
        }

        sb.Append("</body>\n</html>");
        return sb.ToString();
    }

    // ── 页面骨架 ──────────────────────────────────────────────────────

    private static string CssHeader(AnswerSheetOptions opt, string title)
    {
        var obj = opt.ObjColumns;
        return $$"""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<title>{{title}}</title>
<style>
  /* 纸张不交给浏览器算边距：@page margin:0 + 页容器自带内边距。
     浏览器若按打印机硬件边距重算页盒（页盒变小），固定高度的页容器会跨页拆开，
     表现为「第二页起内容整体下移」；页容器比纸张矮 8mm 后不再依赖页盒精度。
     速印机（一体机）建议：单面、100% 实际大小、关闭页眉页脚。 */
  @page { size: {{opt.Paper.WidthMm}}mm {{opt.Paper.HeightMm}}mm; margin: 0; }
  * { box-sizing: border-box; }
  body { font-family: "SimSun", "Songti SC", serif; margin: 0; color: #000; }

  .page { position: relative; width: {{opt.Paper.WidthMm}}mm; height: {{opt.PageBoxHeightMm}}mm;
          padding: 14mm 14mm 16mm; overflow: hidden; page-break-after: always; break-after: page; }
  .page:last-child { page-break-after: auto; break-after: auto; }

  /* 四角定位标记：距纸边 7mm，避开打印机不可打印边距，每页各自一份 */
  .anchor { position: absolute; width: 6mm; height: 6mm; background: #000; z-index: 3; }
  /* 四个标记都用 top 定位（相对纸面顶边），中心距纸边各 10mm：
     页容器比纸张矮 8mm，若用 bottom 定位，下方两个标记会落到距纸底 15mm 处，
     与识别端假定的对称 10mm 不符，切卡后会整体纵向偏移数毫米。 */
  .anchor.tl { top: 7mm; left: 7mm; }
  .anchor.tr { top: 7mm; right: 7mm; }
  .anchor.bl { top: {{opt.Paper.HeightMm - 13}}mm; left: 7mm; }
  .anchor.br { top: {{opt.Paper.HeightMm - 13}}mm; right: 7mm; }

  .content { height: {{opt.UsableMm}}mm; overflow: hidden; }

  /* 标题/副标题/注意事项的高度全部钉死：识别端要在毫米空间里定位气泡，
     字体度量造成的几毫米浮动会让整页气泡错位 */
  .title { font-size: 16pt; height: 7mm; line-height: 7mm; text-align: center; font-weight: bold; margin: 0 0 2mm; overflow: hidden; }
  .subtitle { font-size: 10pt; height: 4.5mm; line-height: 4.5mm; text-align: center; color: #333; margin: 0 0 4mm; overflow: hidden; }

  /* ── 注意事项 ── */
  .notes { border: 0.5mm solid #000; padding: 1.5mm 2mm; margin: 0 0 3mm;
           font-size: 8pt; line-height: 4.2mm; height: 18mm; overflow: hidden; }
  .notes b { font-size: 8.5pt; }

  /* ── 学生信息区（左：手写姓名等；右：考号区） ── */
  .info { border: 0.5mm solid #000; padding: 3mm; margin: 0 0 4mm; display: flex; gap: 4mm; }
  .info-left { flex: 1; font-size: 10pt; line-height: 8mm; }
  .write-line { border-bottom: 0.3mm solid #666; display: inline-block; min-width: 40mm; }

  /* ── 考号填涂区（黑框包裹：手写行 + 列序号 + 0-9 涂卡列） ── */
  .id-area { border: 0.5mm solid #000; padding: 2mm; width: 74mm; }
  .id-title { font-size: 8pt; height: 4.4mm; line-height: 4.4mm; margin: 0 0 1mm; overflow: hidden; }
  .id-write { display: flex; align-items: center; margin: 0 0 1.5mm; }
  .id-write-label { font-size: 9pt; margin-right: 1mm; white-space: nowrap; width: 12mm; }
  .wbox { width: 6.5mm; height: 7mm; border: 0.4mm solid #000; margin-right: 0.8mm; }
  .wbox.big { width: 9mm; height: 9mm; margin-right: 1mm; }
  .id-grid { display: flex; }
  .id-rowlabel { height: 4mm; font-size: 7pt; line-height: 4mm; text-align: right;
                 padding-right: 1mm; width: 5mm; }
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

  /* ── 考号表 ── */
  .roster-title { font-size: 15pt; font-weight: bold; text-align: center; margin: 0 0 1.5mm; }
  .roster-sub { font-size: 9pt; text-align: center; margin: 0 0 4mm; color: #333; }
  .roster-cols { display: flex; gap: 6mm; }
  .roster-col { flex: 1; }
  .roster-row { display: flex; align-items: center; height: 8mm; border-bottom: 0.3mm solid #999;
                font-size: 11pt; }
  .roster-row .seq { width: 8mm; color: #666; font-size: 9pt; }
  .roster-row .no { width: 30mm; font-weight: bold; letter-spacing: 0.6mm; }
  .roster-row .name { flex: 1; }

  /* ── 页脚：绝对定位在本页容器内（每页各一份） ── */
  .footer { position: absolute; left: 14mm; right: 14mm; bottom: 16mm; height: 18mm;
            display: flex; justify-content: space-between; align-items: center;
            font-size: 8pt; color: #444; border-top: 0.3mm solid #999; padding: 1mm 0 0; }
  .qr { width: 16mm; height: 16mm; border: 0.3mm solid #000; display: flex;
        align-items: center; justify-content: center; font-size: 6pt; text-align: center; }
  .barcode { height: 12mm; width: 45mm; border: 0.3mm solid #000; display: flex;
             align-items: center; justify-content: center; font-size: 7pt; letter-spacing: 1mm; }
</style>
</head>
""";
    }

    private static string PageOpen(AnswerSheetOptions opt, bool withAnchors = true)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"page\">");
        if (withAnchors)
        {
            sb.Append("<div class=\"anchor tl\"></div><div class=\"anchor tr\"></div>");
            sb.Append("<div class=\"anchor bl\"></div><div class=\"anchor br\"></div>");
        }
        sb.Append("<div class=\"content\">");
        return sb.ToString();
    }

    private static string PageClose(string? paperId, string? studentNo, int pageNo, int? totalPages = null)
    {
        var sb = new StringBuilder();
        sb.Append("</div>"); // .content
        sb.Append("<div class=\"footer\">");
        sb.Append(paperId != null
            ? $"<div><span style=\"font-size:7pt\">试卷编号：{Escape(paperId)}　请用 2B 铅笔填涂，保持卡面整洁</span></div>"
            : "<div><span style=\"font-size:7pt\">考号表（张贴或发给学生，供填涂通用答题卡）</span></div>");
        sb.Append(string.IsNullOrEmpty(studentNo) ? "" : $"<div class=\"barcode\">{Escape(studentNo)}</div>");
        if (paperId != null)
        {
            sb.Append($"<div class=\"qr\">{BuildQrCode(paperId, pageNo)}</div>");
        }
        else
        {
            sb.Append($"<div style=\"font-size:8pt\">第 {pageNo}" +
                      (totalPages.HasValue ? $" / {totalPages} 页" : " 页") + "</div>");
        }
        sb.Append("</div>");
        sb.Append("</div>"); // .page
        return sb.ToString();
    }

    // ── 版面片段 ──────────────────────────────────────────────────────

    private static bool IsObjective(QuestionType t)
        => t is QuestionType.SingleChoice or QuestionType.MultipleChoice or QuestionType.Judge;

    /// <summary>
    /// 主观题作答框高度（mm，含边框、题头与下间距），必须与 CSS 中
    /// .answer-box / .answer-head / .answer-body 的显式高度一致。
    /// </summary>
    private static double SubjectiveHeightMm(Question q) => q.Type switch
    {
        QuestionType.Blank => 28.0,       // 正文 18mm
        QuestionType.ShortAnswer => 45.0, // 正文 35mm
        QuestionType.Essay => 74.0,       // 正文 64mm
        _ => 40.0,                        // 正文 30mm
    };

    private static string SubjectiveBodyHeight(QuestionType t) => t switch
    {
        QuestionType.Blank => "18mm",
        QuestionType.ShortAnswer => "35mm",
        QuestionType.Essay => "64mm",
        _ => "30mm",
    };

    /// <summary>学生信息区（含考号区；考号区按选项渲染为填涂/手写/无）。</summary>
    private static string BuildInfoArea(IdAreaKind kind, string? studentName, string? studentNo)
    {
        var sb = new StringBuilder();
        // 高度必须与 AnswerSheetLayout.InfoHeightMm 一致：填涂区 70mm，其余 31mm
        var infoH = kind == IdAreaKind.Bubble ? 70.0 : 31.0;
        sb.Append($"<div class=\"info\" style=\"height:{infoH:0.#}mm\">");
        sb.Append("<div class=\"info-left\">班级：<span class=\"write-line\"></span><br>姓名：<span class=\"write-line\">");
        sb.Append(Escape(studentName ?? ""));
        sb.Append("</span><br>学号：<span class=\"write-line\">");
        sb.Append(Escape(studentNo ?? ""));
        sb.Append("</span></div>");
        if (kind != IdAreaKind.None) sb.Append(BuildIdArea(kind));
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>考号区：填涂区（手写行 + 列序号 + 8 列 × 10 行）或仅手写行。</summary>
    private static string BuildIdArea(IdAreaKind kind)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"id-area\">");

        if (kind == IdAreaKind.Handwrite)
        {
            sb.Append("<div class=\"id-title\">考号（请用黑色签字笔工整书写）</div>");
            sb.Append("<div class=\"id-write\"><span class=\"id-write-label\">考号：</span>");
            for (var c = 0; c < IdDigitColumns; c++) sb.Append("<span class=\"wbox big\"></span>");
            sb.Append("</div></div>");
            return sb.ToString();
        }

        sb.Append("<div class=\"id-title\">考号填涂区（每列涂一个数字，共 8 位）</div>");

        // 手写考号行：与下方涂卡列一一对齐
        sb.Append("<div class=\"id-write\"><span class=\"id-write-label\">考号：</span>");
        for (var c = 0; c < IdDigitColumns; c++) sb.Append("<span class=\"wbox\"></span>");
        sb.Append("</div>");

        // 左侧行标（0-9）+ 右侧 8 个涂卡列（每列顶部带列序号）
        sb.Append("<div class=\"id-grid\">");
        sb.Append("<div class=\"id-rowlabel head\"></div>");
        sb.Append("<div class=\"id-cols\">");
        for (var c = 0; c < IdDigitColumns; c++)
        {
            sb.Append("<div class=\"id-col\">");
            sb.Append($"<div class=\"col-no\">{c + 1}</div>");
            for (var n = 0; n < IdDigitRows; n++)
                sb.Append($"<div class=\"bubble\" data-col=\"{c + 1}\" data-digit=\"{n}\"></div>");
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
                sb.Append($"<div class=\"tick\" style=\"top:{off:0.##}mm;height:{ObjTickMm}mm\"></div>");
            sb.Append("</div>");
        }
        return sb.ToString();
    }

    /// <summary>客观题块：黑框 + 左右定时条 + N 列涂卡区（列优先排布）。</summary>
    private static string BuildObjectiveBlock(
        IReadOnlyList<Question> questions,
        IReadOnlyDictionary<string, List<string>> options,
        int columnCount)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"qblock\">");
        sb.Append("<div class=\"block-title\">一、客观题（请填涂所选选项）</div>");
        sb.Append("<div class=\"qbody\">");

        if (questions.Count == 0)
        {
            sb.Append("<div style=\"font-size:9pt;color:#666\">（无客观题）</div></div></div>");
            return sb.ToString();
        }

        // 行数按本页实际题量计算（不是页容量），否则定时条会越过内容悬浮在黑框之外
        var perCol = Math.Max(1, (int)Math.Ceiling(questions.Count / (double)columnCount));

        var offsets = new List<double>();
        for (var r = 0; r < perCol; r++) offsets.Add(r * ObjRowMm + (ObjRowMm - ObjTickMm) / 2);
        sb.Append(BuildTimingMarks(offsets));

        sb.Append("<div class=\"omr-cols\">");
        for (var c = 0; c < columnCount; c++)
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
                    sb.Append($"<div class=\"omr-opt\" data-q=\"{q.Index + 1}\" data-opt=\"{Escape(k)}\">{Escape(k)}</div>");
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
