using System.Text;
using AgoraIn.Core.Entities;
using QRCoder;

namespace AgoraIn.Server.Services;

/// <summary>答题卡纸张预设（mm）。速印机常用 8 开/16 开，普通打印机用 A4。</summary>
public sealed record SheetPaper(string Name, double WidthMm, double HeightMm)
{
    public static readonly SheetPaper A4 = new("A4", 210, 297);
    public static readonly SheetPaper B4 = new("B4", 250, 353);
    // 8K/A3 答题卡是**横向**使用：一张纸 = 左右两半，各是一张 A4 样式的页，一次拍照两半同时识别
    public static readonly SheetPaper K8 = new("8K", 370, 260);
    public static readonly SheetPaper K16 = new("16K", 185, 260);
    public static readonly SheetPaper A3 = new("A3", 420, 297);   // 横向 = 恰好两张 A4（420 = 2×210）

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

    /// <summary>
    /// 内容栏数：大纸（8K/A3/B4，内容宽 ≥ 200mm）自动排成左右两栏，内容左栏排满接右栏。
    /// 旧版大纸只是把每行拉宽（8K 每行 77mm、页数几乎不比 A4 少），两栏才能真的用上纸面。
    /// 由纸宽推导而非用户选项：渲染、识别、切图三处必须算出同一套坐标，留个可变的开关只会让它们对不上。
    /// </summary>
    public int Columns => ContentWidthMm >= 200 ? 2 : 1;

    /// <summary>两栏之间的间距（mm）= 左右两半各自的 14mm 内边距，凑成"两张 A4 页并排"的样子。</summary>
    public const double ColumnGapMm = 28.0;

    /// <summary>一个内容块的可用宽度（mm）：单栏 = 整幅页内容宽，双栏 = 单栏宽。</summary>
    public double BlockWidthMm => Columns == 1
        ? ContentWidthMm
        : (ContentWidthMm - ColumnGapMm) / 2;

    /// <summary>客观题气泡列数：按「本块可用宽度」决定（窄块两列，常规三列）。</summary>
    public int ObjColumns => BlockWidthMm >= 180 ? 3 : 2;

    /// <summary>首页表头高度（标题 + 副标题 + 注意事项 + 学生信息区）。</summary>
    public double HeaderMm => 92 + (ShowNotes ? 18 : 0);

    /// <summary>无底部安全余量的可排高度（分页用）。</summary>
    public double CapacityMm(int pageOrdinal) => UsableMm - 6 - (pageOrdinal == 0 ? HeaderMm : 0);

    /// <summary>续栏/续页的页眉高度（"第 N / M 页"一行 + 2mm 间距）。</summary>
    public const double SmallHeaderMm = 4.5 + 2.0;

    /// <summary>
    /// 某一"栏"的可排高度：只有第 1 张的左栏带整块页眉（标题/注意事项/信息区+考号区），
    /// 其余栏（同张的右半、后续各张）都只有一行"第 N / M 页"页眉。
    /// </summary>
    public double ColumnCapacityMm(int pageOrdinal, int column)
        => UsableMm - 6 - (pageOrdinal == 0 && column == 0 ? HeaderMm : SmallHeaderMm);
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
    // 区块外框总开销（mm）：块高减内容高 + 块间距 = 12.24（DOM 实测）。
    // 构成：渲染边框 0.53（CSS 标称 0.5mm 实际渲染约 0.265mm/条）+ 上下内边距 3 + 标题行盒 4.225
    // + 标题下间距 1.5 + 块下间距 3。旧值 12.0 是按标称尺寸估的：同一页只放一种块时看不出来，
    // 一旦块后面还紧跟别的块（客观题→填空题→主观题），后面的块就会累计偏 0.24mm/块。
    private const double BlockChromeMm = 12.24;
    /// <summary>与版面模型同名的常量：.qblock 边框 + 左右内边距 / .qbody 左右内边距。</summary>
    private const double BlockInsetX = 2.26;
    private const double QBodyPadX = 6.0;
    private const double ObjRowMm = 6.0;         // 客观题行距（与 .omr-item 高度一致）
    /// <summary>填空题行高（mm）：每题一条横线的作答区——不需要主观题那么大的框。</summary>
    private const double BlankRowMm = 9.0;
    private const double BlankRowGapMm = 2.0;    // .blank-row 的 margin-bottom
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

    /// <summary>生成通用答题卡 HTML（自动服务端分页，每页独立定位标记与页码 QR）。
    /// placements：可视化编辑器拖过/缩放的题目——这些题从自动流里摘出，按覆盖坐标绝对定位。</summary>
    public static string Render(
        ExamPaper paper,
        IReadOnlyList<Question> questions,
        IReadOnlyDictionary<string, List<string>> options,
        string? studentName = null,
        string? studentNo = null,
        int pageIndex = 1,
        int pageCount = 1,
        AnswerSheetOptions? sheetOptions = null,
        IReadOnlyDictionary<string, QuestionPlacement>? placements = null)
    {
        var opt = sheetOptions ?? AnswerSheetOptions.Default;
        // 三个分组：客观题（选/判断，涂卡）→ 填空题（一条横线）→ 主观题（大作答框）。
        // 填空题判分与客观题同一套（比标准答案对错），版面却要紧凑得多，故单独成节。
        // 拖过的题（placements）不参与流动，最后按覆盖坐标绝对定位，避免被自动分页挤走。
        var pinned = placements is { Count: > 0 }
            ? questions.Where(q => placements.TryGetValue(q.Id, out var pl) && !pl.SizeOnly).ToList()
            : [];
        var flow = pinned.Count > 0 ? questions.Where(q => !pinned.Contains(q)).ToList() : questions;
        // 尺寸覆盖（只改宽高、仍留在自动流）——下方题目按它让位/回收
        var sz = new SizeOverrides(placements is { Count: > 0 }
            ? placements.Where(kv => kv.Value.SizeOnly).ToDictionary(kv => kv.Key, kv => kv.Value)
            : new Dictionary<string, QuestionPlacement>());
        var objective = flow.Where(q => IsObjective(q.Type)).ToList();
        var blanks = flow.Where(q => IsBlank(q.Type)).ToList();
        var subjective = flow.Where(q => !IsObjective(q.Type) && !IsBlank(q.Type)).ToList();

        // 只给实际存在的分组编号：没有填空题时"主观题"就是二
        var ordinal = 0;
        var objTitle = objective.Count > 0 ? SectionTitle(ObjTitleFallback, ++ordinal) : ObjTitleFallback;
        var blankTitle = blanks.Count > 0 ? SectionTitle(BlankTitleFallback, ++ordinal) : BlankTitleFallback;
        var subjTitle = subjective.Count > 0 ? SectionTitle(SubjTitleFallback, ++ordinal) : SubjTitleFallback;

        // ── 服务端分页 ──
        // 顺序装箱：先排完客观题，再填空题，最后主观题；满载就换下一个"槽位"——双栏时同页右栏，
        // 单栏时下一页。单栏模式每页只有 1 个槽位，输出与旧版逐字节一致（A4 识别已验证过）。
        var pages = new List<List<string>>();
        var objIdx = 0;
        var blankIdx = 0;
        var subIdx = 0;

        while (objIdx < objective.Count || blankIdx < blanks.Count || subIdx < subjective.Count)
        {
            var cols = new List<string>();

            for (var c = 0; c < opt.Columns; c++)
            {
                if (objIdx >= objective.Count && blankIdx >= blanks.Count && subIdx >= subjective.Count) break;   // 排完了就别占空栏
                // 每栏容量：第 1 张左栏带整块页眉，其余栏只有一行"第 N / M 页"
                var capacity = opt.ColumnCapacityMm(pages.Count, c);
                var used = 0.0;
                var blocks = new StringBuilder();

                // 1) 客观题：按 N 列 x M 行装填本栏剩余容量
                if (objIdx < objective.Count)
                {
                    // 客观题行高统一（块内任一题被缩放 → 整块跟随），与版面模型同口径
                    var chunkRowH = ObjRowMm;
                    foreach (var q in objective.Skip(objIdx)) chunkRowH = Math.Max(chunkRowH, sz.H(q.Id, ObjRowMm));
                    var rows = (int)Math.Floor((capacity - used - BlockChromeMm) / chunkRowH);
                    var take = Math.Min(objective.Count - objIdx, Math.Max(0, rows) * opt.ObjColumns);
                    if (take > 0)
                    {
                        var chunk = objective.Skip(objIdx).Take(take).ToList();
                        objIdx += take;
                        blocks.Append(BuildObjectiveBlock(chunk, options, opt.ObjColumns, objTitle, sz));
                        used += BlockChromeMm + Math.Ceiling(take / (double)opt.ObjColumns) * chunkRowH;
                    }
                }

                // 2) 填空题：一题一行；逐行累计——每题可能被单独拉高/压低，装不下留给下一栏
                if (objIdx >= objective.Count && blankIdx < blanks.Count)
                {
                    var fit = 0;
                    var acc = 0.0;
                    var limit = capacity - used - BlockChromeMm + BlankRowGapMm;
                    while (blankIdx + fit < blanks.Count)
                    {
                        var rh = sz.H(blanks[blankIdx + fit].Id, BlankRowMm) + BlankRowGapMm;
                        // 栏里已经有内容就不再硬塞（只有完全空的一栏才允许"至少放一条"，保证不死循环）
                        if (acc + rh > limit && (fit > 0 || used > 0)) break;
                        acc += rh;
                        fit++;
                    }
                    var take = fit;
                    if (take > 0)
                    {
                        var chunk = blanks.Skip(blankIdx).Take(take).ToList();
                        blankIdx += take;
                        blocks.Append(BuildBlankBlock(chunk, blankTitle, sz, opt.BlockWidthMm - 2 * (BlockInsetX + QBodyPadX)));
                        used += BlockChromeMm + chunk.Sum(q => sz.H(q.Id, BlankRowMm) + BlankRowGapMm);
                    }
                }

                // 3) 主观题：客观/填空排完后，继续用本栏剩余容量装主观题
                if (objIdx >= objective.Count && blankIdx >= blanks.Count && subIdx < subjective.Count)
                {
                    var availSub = capacity - used - BlockChromeMm;
                    var chunk = new List<Question>();
                    var chunkUsed = 0.0;
                    while (subIdx < subjective.Count)
                    {
                        var h = SubjectiveItemHeightMm(subjective[subIdx], sz);
                        // 同上：栏里已有内容就不硬塞
                        if (chunkUsed + h > availSub && (chunk.Count > 0 || used > 0)) break;
                        chunk.Add(subjective[subIdx]);
                        chunkUsed += h;
                        subIdx++;
                    }
                    if (chunk.Count > 0) blocks.Append(BuildSubjectiveBlock(chunk, subjTitle, sz, opt.BlockWidthMm - 2 * (BlockInsetX + QBodyPadX)));
                }

                // 兜底：容量估算若异常（理论上不会）也必须有内容，避免死循环
                if (blocks.Length == 0)
                {
                    if (objIdx < objective.Count)
                    {
                        var one = objective.Skip(objIdx).Take(opt.ObjColumns).ToList();
                        objIdx += one.Count;
                        blocks.Append(BuildObjectiveBlock(one, options, opt.ObjColumns, objTitle, sz));
                    }
                    else if (blankIdx < blanks.Count)
                    {
                        var one = blanks.Skip(blankIdx).Take(1).ToList();
                        blankIdx += one.Count;
                        blocks.Append(BuildBlankBlock(one, blankTitle, sz, opt.BlockWidthMm - 2 * (BlockInsetX + QBodyPadX)));
                    }
                    else
                    {
                        var one = subjective.Skip(subIdx).Take(1).ToList();
                        subIdx += one.Count;
                        blocks.Append(BuildSubjectiveBlock(one, subjTitle, sz, opt.BlockWidthMm - 2 * (BlockInsetX + QBodyPadX)));
                    }
                }

                cols.Add(blocks.ToString());
            }

            pages.Add(cols);
        }

        // 无题兜底：至少生成一页
        if (pages.Count == 0) pages.Add([BuildObjectiveBlock([], options, opt.ObjColumns)]);

        // 拖过的题可能被放到自动流之外的页上（例如单独摆到第 3 页）→ 补出这些页
        var pinnedByPage = new Dictionary<int, List<Question>>();
        foreach (var q in pinned)
        {
            var pg = Math.Max(1, placements![q.Id].PageNo);
            if (!pinnedByPage.TryGetValue(pg, out var list)) pinnedByPage[pg] = list = [];
            list.Add(q);
        }
        var totalPages = Math.Max(pages.Count, pinnedByPage.Count == 0 ? 0 : pinnedByPage.Keys.Max());

        var sb = new StringBuilder();
        sb.Append(CssHeader(opt, Escape(paper.Title) + " - 答题卡"));
        sb.Append("<body>\n");

        var halfPages = totalPages * opt.Columns;   // "半页"数 = 页数 × 栏数（大纸一张 = 两个半页）
        for (var i = 0; i < totalPages; i++)
        {
            var pageNo = i + 1;
            sb.Append(PageOpen(opt));
            if (opt.Columns == 1)
            {
                // 单栏（A4/16K）：页眉仍在内容之前
                sb.Append(i == 0
                    ? BigHeaderHtml(paper, 1, halfPages) + (opt.ShowNotes ? NotesHtml : "") + BuildInfoArea(opt.IdArea, studentName, studentNo)
                    : SmallHeaderHtml(pageNo, totalPages));
            }
            // 自动流内容（补出来的页没有流内容）
            var cols = i < pages.Count ? pages[i] : [];
            if (opt.Columns > 1)
                sb.Append(WrapColumnsWithHeaders(cols, opt, paper, i, totalPages, halfPages, studentName, studentNo));
            else
                sb.Append(string.Concat(cols));

            // 拖过/缩放过的题：按覆盖坐标绝对定位在本页（.page 是 position:relative，
            // 且无边框，所以 left/top 的 0 点就是纸面左上角，与定位标记同一坐标系）
            if (pinnedByPage.TryGetValue(pageNo, out var pagePinned))
                foreach (var q in pagePinned)
                    sb.Append(BuildPinned(q, options, placements![q.Id]));

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

    /// <summary>
    /// 一道"拖过"的题：按覆盖坐标绝对定位（mm，纸面坐标系）。
    /// 三种题型各自的排版与自动流里保持一致——同一道题不管是自动排的还是拖过去的，
    /// 识别端看到的几何必须一样（.pinned .omr-row / .blank-row / .answer-box）。
    /// </summary>
    private static string BuildPinned(
        Question q, IReadOnlyDictionary<string, List<string>> options, QuestionPlacement pl)
    {
        var box = $"left:{pl.Xmm:0.##}mm;top:{pl.Ymm:0.##}mm;width:{pl.Wmm:0.##}mm;height:{pl.Hmm:0.##}mm";

        if (IsObjective(q.Type))
        {
            var keys = q.Type == QuestionType.Judge
                ? ["√", "×"]
                : options.TryGetValue(q.Id, out var list) && list.Count > 0 ? list : ["A", "B", "C", "D"];
            var sb = new StringBuilder($"<div class=\"pinned\" style=\"{box}\" data-qid=\"{q.Id}\" data-q=\"{q.Index + 1}\">");
            sb.Append("<div class=\"omr-row\">");
            sb.Append($"<div class=\"omr-no\">{q.Index + 1}.</div><div class=\"omr-opts\">");
            foreach (var k in keys)
                sb.Append($"<div class=\"omr-opt\" data-q=\"{q.Index + 1}\" data-opt=\"{Escape(k)}\">{Escape(k)}</div>");
            sb.Append("</div></div></div>");
            return sb.ToString();
        }

        if (IsBlank(q.Type))
            return $"""
<div class="pinned" style="{box}" data-qid="{q.Id}">
  <div class="blank-row"><div class="blank-no">第 {q.Index + 1} 题（{q.Score:0.#} 分）</div><div class="blank-line"></div></div>
</div>
""";

        // 主观题：整框高度 = 题头 6mm + 正文 + 上下边框（渲染边框约 0.265mm/条）
        var body = Math.Max(6.0, pl.Hmm - 6.0 - 0.53);
        var lines = q.Type is QuestionType.ShortAnswer or QuestionType.Essay ? " answer-lines" : "";
        return $"""
<div class="pinned" style="{box}" data-qid="{q.Id}">
  <div class="answer-box" style="--h:{body:0.##}mm;margin:0">
    <div class="answer-head">第 {q.Index + 1} 题（{q.Score:0.#} 分）</div>
    <div class="answer-body{lines}"></div>
  </div>
</div>
""";
    }

    /// <summary>大纸（8K/A3 横向）标题行：一张纸 = 两张 A4 样式的页并排。</summary>
    private static string BigHeaderHtml(ExamPaper paper, int halfNo, int totalHalves)
        => $"<div class=\"title\">{Escape(paper.Title)}</div>"
         + $"<div class=\"subtitle\">科目：{Escape(paper.Subject ?? "—")}　总分：{paper.TotalScore:0.#}　第 {halfNo} / {totalHalves} 页</div>";

    /// <summary>续页/右半的页眉：只有一行"第 N / M 页"。</summary>
    private static string SmallHeaderHtml(int halfNo, int totalHalves)
        => $"<div class=\"subtitle\" style=\"margin-bottom:2mm\">第 {halfNo} / {totalHalves} 页</div>";

    /// <summary>
    /// 把一张纸的内容按栏包进 .cols/.col，并给每栏配上自己的页眉：
    /// 第 1 张的左栏 = 标题 + 注意事项 + 信息区/考号区；其余各栏（含第 1 张的右半）= 一行"第 N / M 页"。
    /// 页号按"半页"连续编（一张大纸 = 第 1 页 + 第 2 页），与老师"两张 A4 合在一起"的心智一致。
    /// </summary>
    private static string WrapColumnsWithHeaders(
        List<string> cols, AnswerSheetOptions opt, ExamPaper paper,
        int pageIdx, int totalPages, int totalHalves, string? studentName, string? studentNo)
    {
        var sb = new StringBuilder("<div class=\"cols\">");
        for (var c = 0; c < cols.Count; c++)
        {
            var halfNo = pageIdx * opt.Columns + c + 1;
            sb.Append("<div class=\"col\">");
            if (pageIdx == 0 && c == 0)
            {
                sb.Append(BigHeaderHtml(paper, halfNo, totalHalves));
                if (opt.ShowNotes) sb.Append(NotesHtml);
                sb.Append(BuildInfoArea(opt.IdArea, studentName, studentNo));
            }
            else
            {
                sb.Append(SmallHeaderHtml(halfNo, totalHalves));
            }
            sb.Append(cols[c]);
            sb.Append("</div>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

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

  /* 双栏（大纸）：表头通栏，其下内容左栏排满接右栏。宽度用固定 mm 而不是 flex 比例，
     识别端按同一组 mm 坐标采样，任何"浏览器自己算"的布局都会让切卡偏位。 */
  .cols { display: flex; align-items: flex-start; }
  .col { flex: none; width: {{opt.BlockWidthMm}}mm; }
  .col + .col { margin-left: {{AnswerSheetOptions.ColumnGapMm}}mm; }

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
  /* 行高被放大时气泡在整行里居中（.omr-row 固定 6mm，靠 flex 居中；与版面模型同口径） */
  .omr-item { height: 6mm; overflow: hidden; display: flex; align-items: center; }
  .omr-row { display: flex; align-items: center; gap: 1mm; height: 6mm; width: 100%; font-size: 9pt; }
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

  /* ── 填空题：一题一行，行内一条横线（高度与 BlankRowMm / BlankRowGapMm 一致） ──
     四边框不能省：识别端用"框在不在"做整页指纹，纯横线会让整页认不出来。 */
  .blank-row { height: 9mm; border: 0.5mm solid #000; margin: 0 0 2mm;
               display: flex; align-items: center; padding: 0 2mm; break-inside: avoid; }
  .blank-no { font-size: 9pt; white-space: nowrap; margin-right: 2mm; }
  .blank-line { flex: 1; height: 5mm; border-bottom: 0.3mm dashed #888; }

  /* ── 可视化编辑器拖过的题：绝对定位在纸面坐标系（.page 无边框，left/top 的 0 点即纸面左上角） ── */
  .pinned { position: absolute; z-index: 2; }
  .pinned .omr-row { display: flex; align-items: center; height: 100%; }
  .pinned .blank-row { height: 100%; margin: 0; }
  .pinned .answer-box { height: 100%; }

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

    /// <summary>尺寸覆盖（SizeOnly）：只改宽高、仍留在自动流里的题。空表表示没有任何尺寸改动。</summary>
    private sealed record SizeOverrides(IReadOnlyDictionary<string, QuestionPlacement> Map)
    {
        public static readonly SizeOverrides None = new(new Dictionary<string, QuestionPlacement>());

        public bool Has(string qid) => Map.ContainsKey(qid);
        public double H(string qid, double fallback) => Map.TryGetValue(qid, out var pl) ? Math.Max(6, pl.Hmm) : fallback;
        public double W(string qid, double fallback) => Map.TryGetValue(qid, out var pl) ? Math.Min(fallback, Math.Max(20, pl.Wmm)) : fallback;
    }

    /// <summary>主观题"一条"占位高度：覆盖时 = 框高 + 下间距 3mm（与模型 SubjMb 一致）。</summary>
    private static double SubjectiveItemHeightMm(Question q, SizeOverrides sz)
        => sz.Has(q.Id) ? sz.H(q.Id, 0) + 3.0 : SubjectiveHeightMm(q);

    /// <summary>主观题作答框的正文高度（框高 − 题头 6mm − 上下边框）。</summary>
    private static double SubjectiveBodyMmOf(Question q, SizeOverrides sz)
        => sz.Has(q.Id) ? Math.Max(6.0, sz.H(q.Id, 0) - 6.53) : DefaultBodyMm(q.Type);

    private static double DefaultBodyMm(QuestionType t) => t switch
    {
        QuestionType.Blank => 18.0,
        QuestionType.ShortAnswer => 35.0,
        QuestionType.Essay => 64.0,
        _ => 30.0,
    };

    private static bool IsObjective(QuestionType t)
        => t is QuestionType.SingleChoice or QuestionType.MultipleChoice or QuestionType.Judge;

    /// <summary>填空题：判分按"对/错"比标准答案（与客观题同一套自动判分），
    /// 但作答是手写一行字，版面既不能像选择题那样挤成网格，也不需要主观题那种大框。</summary>
    private static bool IsBlank(QuestionType t) => t == QuestionType.Blank;

    /// <summary>填空/客观题的分节标题：只给实际存在的分组编号（没有填空时不出现"二、填空题"）。</summary>
    private const string ObjTitleFallback = "客观题（请填涂所选选项）";
    private const string BlankTitleFallback = "填空题（请在横线上作答）";
    private const string SubjTitleFallback = "主观题（请在框内作答）";

    private static string SectionTitle(string name, int ordinal)
    {
        var cn = ordinal switch { 1 => "一", 2 => "二", 3 => "三", _ => (ordinal).ToString() };
        return $"{cn}、{name}";
    }

    /// <summary>
    /// 填空题块：每题一条横线（9mm 高的窄框 + 中间虚线），一题一行，不挤成选择题那种网格，
    /// 也不占主观题的大作答框。**四边框是刻意保留的**：识别端用"框在不在"做整页指纹，
    /// 纯横线没有四条边，整页就会认不出来（页码、答案都会跟着错）。
    /// </summary>
    private static string BuildBlankBlock(IReadOnlyList<Question> questions, string title = BlankTitleFallback,
        SizeOverrides? sizes = null, double blockContentW = 0)
    {
        var sz = sizes ?? SizeOverrides.None;
        var sb = new StringBuilder();
        sb.Append("<div class=\"qblock\">");
        sb.Append($"<div class=\"block-title\">{title}</div>");
        sb.Append("<div class=\"qbody\">");

        var offsets = new List<double>();
        var acc = 0.0;
        foreach (var q in questions)
        {
            offsets.Add(acc);
            acc += sz.H(q.Id, BlankRowMm) + BlankRowGapMm;
        }
        if (questions.Count > 0) offsets.Add(Math.Max(0, acc - BlankRowGapMm));
        sb.Append(BuildTimingMarks(offsets));

        foreach (var q in questions)
        {
            var rowH = sz.H(q.Id, BlankRowMm);
            // 只改高度时宽度保持整栏（兜底 = 整栏内容宽）；.blank-row 是 border-box，宽度即外框宽
            var w = sz.Has(q.Id) && blockContentW > 0 ? $" width:{sz.W(q.Id, blockContentW):0.##}mm;" : "";   // 分号不能少：否则 height 声明整条被浏览器丢掉
            sb.Append($$"""
<div class="blank-row" style="height:{{rowH:0.##}}mm;{{w}}" data-qid="{{q.Id}}">
  <div class="blank-no">第 {{q.Index + 1}} 题（{{q.Score:0.#}} 分）</div>
  <div class="blank-line"></div>
</div>
""");
        }

        sb.Append("</div></div>");
        return sb.ToString();
    }

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
        int columnCount,
        string title = ObjTitleFallback,
        SizeOverrides? sizes = null)
    {
        var sz = sizes ?? SizeOverrides.None;
        // 客观题是统一网格：块内任一题被缩放时整块按该行高排（与版面模型同口径）
        var rowH = ObjRowMm;
        foreach (var q in questions) rowH = Math.Max(rowH, sz.H(q.Id, ObjRowMm));
        var sb = new StringBuilder();
        sb.Append("<div class=\"qblock\">");
        sb.Append($"<div class=\"block-title\">{title}</div>");
        sb.Append("<div class=\"qbody\">");

        if (questions.Count == 0)
        {
            sb.Append("<div style=\"font-size:9pt;color:#666\">（无客观题）</div></div></div>");
            return sb.ToString();
        }

        // 行数按本页实际题量计算（不是页容量），否则定时条会越过内容悬浮在黑框之外
        var perCol = Math.Max(1, (int)Math.Ceiling(questions.Count / (double)columnCount));

        var offsets = new List<double>();
        for (var r = 0; r < perCol; r++) offsets.Add(r * rowH + (ObjRowMm - ObjTickMm) / 2);
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

                sb.Append($"<div class=\"omr-item\" style=\"height:{rowH:0.##}mm\"><div class=\"omr-row\">");
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
    private static string BuildSubjectiveBlock(IReadOnlyList<Question> questions, string title = SubjTitleFallback,
        SizeOverrides? sizes = null, double blockContentW = 0)
    {
        var sz = sizes ?? SizeOverrides.None;
        var sb = new StringBuilder();
        sb.Append("<div class=\"qblock\">");
        sb.Append($"<div class=\"block-title\">{title}</div>");
        sb.Append("<div class=\"qbody\">");

        var offsets = new List<double>();
        double cursor = 0;
        foreach (var q in questions)
        {
            offsets.Add(cursor);
            cursor += SubjectiveItemHeightMm(q, sz);
        }
        offsets.Add(cursor);
        sb.Append(BuildTimingMarks(offsets));

        foreach (var q in questions)
        {
            // 被缩放过就用覆盖高度（单位 mm）；宽度覆盖写成行内宽（默认铺满整栏）
            var body = SubjectiveBodyMmOf(q, sz);
            var w = sz.Has(q.Id) && blockContentW > 0 ? $"width:{sz.W(q.Id, blockContentW):0.##}mm;" : "";
            var lines = q.Type is QuestionType.ShortAnswer or QuestionType.Essay ? " answer-lines" : "";

            sb.Append($$"""
<div class="answer-box" style="{{w}}--h:{{body:0.##}}mm">
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
