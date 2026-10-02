using AgoraIn.Core.Entities;

namespace AgoraIn.Server.Services;

/// <summary>一个可填涂标记（气泡）在纸面上的位置，单位毫米，原点 = 纸张左上角。</summary>
public sealed record BubbleMark(double Xmm, double Ymm, double Wmm, double Hmm)
{
    public double CenterX => Xmm + Wmm / 2;
    public double CenterY => Ymm + Hmm / 2;
}

/// <summary>某题某个选项的气泡。</summary>
public sealed record OptionMark(int QuestionIndex, string Option, BubbleMark Bubble);

/// <summary>考号某列某个数字的气泡（Column 从 0 起，Digit 为 0-9）。</summary>
public sealed record IdMark(int Column, int Digit, BubbleMark Bubble);

/// <summary>主观题作答框外框（人工复盘时按题切图用）。</summary>
public sealed record SubjectiveFrame(int QuestionIndex, BubbleMark Box);

/// <summary>单页版面几何（毫米）：气泡的精确坐标，供本地 OMR 识别按同一套坐标采样。</summary>
public sealed record SheetPageLayout(
    SheetPaper Paper,
    int PageNo,
    int TotalPages,
    IReadOnlyList<OptionMark> Options,
    IReadOnlyList<IdMark> IdDigits,
    IReadOnlyList<SubjectiveFrame> Frames)
{
    /// <summary>页面墨迹外框（用于快速判断切卡是否合理）。</summary>
    public double PaperWidthMm => Paper.WidthMm;
    public double PaperHeightMm => Paper.HeightMm;
}

/// <summary>
/// 答题卡版面几何计算：与 <see cref="AnswerSheetRenderer"/> 的 CSS **逐项对齐**的唯一事实来源。
///
/// 之所以要单独算一遍：识别端在切卡归正后需要在毫米空间里直接采样气泡，
/// 而浏览器的 flex/字体度量不可预测，所以 CSS 中被"钉死"的那些尺寸
/// （标题 7mm、副标题 4.5mm、注意事项 18mm、信息区 70/31mm、考号区宽 74mm 等）
/// 在这里以常量复现；两者不一致会导致气泡整体错位。
/// 一致性由 tools 侧脚本"渲染 PDF → 实测气泡矩形 → 与模型比对"来保证。
/// </summary>
public static class AnswerSheetLayout
{
    // ── 与 CSS 一致的基础尺寸（mm）──
    public const double PagePadTop = 14.0;
    public const double PagePadSide = 14.0;
    public const double TitleH = 7.0, TitleMb = 2.0;
    public const double SubtitleH = 4.5, SubtitleMb = 4.0;
    public const double NotesH = 18.0, NotesMb = 3.0;

    public const double InfoH_Bubble = 70.0;
    public const double InfoH_Plain = 31.0;
    public const double InfoMb = 4.0;
    public const double InfoBorder = 0.5, InfoPad = 3.0;

    public const double IdAreaW = 74.0, IdAreaPad = 2.0, IdAreaBorder = 0.5;
    public const double IdTitleH = 4.4, IdTitleMb = 1.0;
    public const double IdWriteH = 7.0, IdWriteMb = 1.5;
    public const double IdLabelW = 12.0, IdLabelMr = 1.0;
    public const double IdWboxW = 6.5, IdWboxMr = 0.8;
    public const double IdColNoH = 4.0;
    public const double IdBubbleW = 6.0, IdBubbleH = 3.6, IdBubbleMb = 0.4;
    public const double IdColMr = 0.8;
    public const int IdColumns = 8, IdRows = 10;

    // 以下三个是「浏览器渲染实测值」：CSS 里标称 0.5mm 的边框/内边距实际折算出 ~1.5px，
    // 比标称小约 0.24mm。用实测值可让模型与真实版面误差 < 0.1mm；
    // 改动任何 CSS 尺寸后都要重跑 tools 侧的一致性校验（渲染 DOM 实测 vs 本模型）。
    public const double InfoInset = 3.26;        // .info 边框 + 内边距（标称 0.5 + 3）
    public const double IdAreaInset = 2.26;      // .id-area 边框 + 内边距（标称 0.5 + 2）
    public const double QBlockInsetX = 2.26;     // .qblock 左右边框 + 内边距（标称 0.5 + 2）
    public const double QBlockTopInset = 7.49;   // .qblock 上边框 + 内边距 + .block-title（含其 margin）
    public const double QBlockMb = 3.0;
    public const double QBlockTitleH = 4.23;  // .block-title 行盒高（10pt x 1.2；其 margin 1.5 已含在 QBlockTopInset）
    public const double QBodyPadX = 6.0;

    public const double ObjRowH = 6.0;           // .omr-item 高度
    public const double ObjNoW = 7.0, ObjNoGap = 1.0;
    public const double ObjOptW = 6.0, ObjOptH = 4.0, ObjOptMr = 1.5;
    public const double ObjColGap = 3.0;

    public const double SubjHeadH = 6.0, SubjMb = 3.0;
    /// <summary>CSS 标称 0.5mm 的边框，浏览器实际渲染约 0.265mm/条（实测 DOM）。
    /// 作答框逐个累计时若用标称值，每个框会漂移约 0.47mm，第 4 个框就偏出 ±1.2mm 的搜索窗。</summary>
    public const double SubjBorderRendered = 0.265;
    public const double SubjBlankBody = 18.0, SubjShortBody = 35.0, SubjEssayBody = 64.0, SubjDefaultBody = 30.0;

    /// <summary>主观题作答框外框的渲染高度（.answer-box 边框盒，不含下间距）——与 DOM 实测一致。</summary>
    public static double SubjectiveFrameHeightMm(Question q) => SubjectiveBodyMm(q) + SubjHeadH + 2 * SubjBorderRendered;

    // ── 页脚二维码（页码的唯一权威来源）────────────────────────────────
    // 卡面右下角印着 agorain:sheet:{paperId}:p{页号} 的二维码。定位标记 + 版面环匹配
    // 只能"猜"页码：同卷各页结构相近，环图案匹配会把第 1 页认成第 5 页，进而用错的
    // 题目几何去采样答案。所以页码必须从二维码读。
    // 几何与 CSS 对齐：.page 高 = 纸高 − 6；.footer{ position:absolute; bottom:16mm;
    // height:18mm; left/right:14mm }；.qr{ width/height:16mm; border:0.3mm } 且靠右居中。
    public const double PageBoxShorterThanPaperMm = 6.0;   // .page 高度相对纸面短 6mm
    public const double FooterBottomMm = 16.0;             // .footer bottom:16mm
    public const double FooterHeightMm = 18.0;
    public const double PagePadSideMm = 14.0;              // .page / .footer 左右 padding
    public const double QrSizeMm = 16.0, QrBorderMm = 0.3;

    /// <summary>页脚二维码的外框（含 0.3mm 边框）在纸面的毫米矩形。</summary>
    public static BubbleMark QrBox(SheetPaper paper)
    {
        var outer = QrSizeMm + 2 * QrBorderMm;
        var right = paper.WidthMm - PagePadSideMm;                     // 靠右对齐到页脚右边
        var footerTop = paper.HeightMm - PageBoxShorterThanPaperMm - FooterBottomMm - FooterHeightMm;
        var top = footerTop + Math.Max(0, (FooterHeightMm - outer) / 2);   // align-items:center
        return new BubbleMark(right - outer, top, outer, outer);
    }

    private static double SubjectiveBodyMm(Question q) => q.Type switch
    {
        QuestionType.Blank => SubjBlankBody,
        QuestionType.ShortAnswer => SubjShortBody,
        QuestionType.Essay => SubjEssayBody,
        _ => SubjDefaultBody,
    };

    /// <summary>主观题作答框总高（含边框、题头、下间距）——与渲染器 SubjectiveHeightMm 一致。</summary>
    public static double SubjectiveHeightMm(Question q) => q.Type switch
    {
        QuestionType.Blank => SubjBlankBody + 1 + SubjHeadH + SubjMb,          // 28
        QuestionType.ShortAnswer => SubjShortBody + 1 + SubjHeadH + SubjMb,    // 45
        QuestionType.Essay => SubjEssayBody + 1 + SubjHeadH + SubjMb,          // 74
        _ => SubjDefaultBody + 1 + SubjHeadH + SubjMb,                         // 40
    };

    public static bool IsObjective(QuestionType t)
        => t is QuestionType.SingleChoice or QuestionType.MultipleChoice or QuestionType.Judge;

    /// <summary>填空题：与客观题同一套"比标准答案对错"的判分，但版面是每行一条横线的窄框。</summary>
    public static bool IsBlank(QuestionType t) => t == QuestionType.Blank;

    /// <summary>填空题行高与行间距（mm）——必须与渲染 CSS 的 .blank-row 完全一致。</summary>
    public const double BlankRowMm = 9.0, BlankRowGapMm = 2.0;

    /// <summary>定位标记边长与相对页容器的内缩（与 CSS 一致）。</summary>
    public const double MarkerMm = 6.0;
    public const double MarkerInsetMm = 7.0;

    /// <summary>
    /// 四个定位标记中心在纸面上的毫米坐标（顺序 TL、TR、BR、BL），与渲染出的 CSS 完全一致。
    /// 四个标记都用 top:7mm 相对页容器定位（页容器比纸张矮 6mm，下方仍在容器内），
    /// 因此四边对称，中心均距纸边 10mm。识别端必须照这份坐标做透视校正，
    /// 一旦 CSS 里 anchor 改了位置而这里没同步，归正图会整体偏移数毫米。
    /// </summary>
    public static (double X, double Y)[] MarkerCentersMm(SheetPaper paper)
    {
        var inset = MarkerInsetMm + MarkerMm / 2;     // 7 + 3 = 10mm，四个标记距纸边一致
        var bottomY = paper.HeightMm - inset;
        return
        [
            (inset, inset),
            (paper.WidthMm - inset, inset),
            (paper.WidthMm - inset, bottomY),
            (inset, bottomY),
        ];
    }

    /// <summary>标记中心距纸边的距离（mm）——识别端据此把像素间距换算成纸张尺寸。</summary>
    public const double MarkerCenterInsetMm = MarkerInsetMm + MarkerMm / 2;

    /// <summary>首页表头总高（标题 + 副标题 + 可选注意事项 + 信息区）。</summary>
    public static double HeaderMm(AnswerSheetOptions opt)
        => TitleH + TitleMb + SubtitleH + SubtitleMb
           + (opt.ShowNotes ? NotesH + NotesMb : 0)
           + (opt.IdArea == IdAreaKind.Bubble ? InfoH_Bubble : InfoH_Plain) + InfoMb;

    /// <summary>客观题列数（与渲染器一致：窄纸两列）。</summary>
    public static int ObjColumns(AnswerSheetOptions opt) => opt.ObjColumns;

    /// <summary>本题的选项列表（与渲染器同规则）。</summary>
    private static List<string> OptionsOf(Question q, IReadOnlyDictionary<string, List<string>> options)
        => q.Type == QuestionType.Judge
            ? ["√", "×"]
            : options.TryGetValue(q.Id, out var list) && list.Count > 0 ? list : ["A", "B", "C", "D"];

    /// <summary>Objective 题在某一页上的行数（从该页第一个题开始）。</summary>
    private static int RowsOnPage(int count, int columns) => Math.Max(1, (int)Math.Ceiling(count / (double)columns));

    /// <summary>
    /// 计算全部页面的几何。分页规则与渲染器完全一致（顺序装箱：客观题占满页再排主观题）。
    /// </summary>
    public static List<SheetPageLayout> Compute(
        IReadOnlyList<Question> questions,
        IReadOnlyDictionary<string, List<string>> options,
        AnswerSheetOptions? sheetOptions = null)
    {
        var opt = sheetOptions ?? AnswerSheetOptions.Default;
        var objective = questions.Where(q => IsObjective(q.Type)).ToList();
        var blanks = questions.Where(q => IsBlank(q.Type)).ToList();
        var subjective = questions.Where(q => !IsObjective(q.Type) && !IsBlank(q.Type)).ToList();
        var columns = opt.ObjColumns;

        // ── 与渲染器相同的顺序装箱（槽位 = 页 × 栏），得到每页各栏的内容 ──
        // 单栏时每页 1 个槽位，与旧算法等价；双栏时左栏排满接右栏。
        var pages = new List<(bool HasHeader, List<(List<Question> Obj, List<Question> Blank, List<Question> Sub)> Cols)>();
        var objIdx = 0;
        var blankIdx = 0;
        var subIdx = 0;
        while (objIdx < objective.Count || blankIdx < blanks.Count || subIdx < subjective.Count)
        {
            var capacity = opt.CapacityMm(pages.Count);
            var cols = new List<(List<Question>, List<Question>, List<Question>)>();

            for (var c = 0; c < opt.Columns; c++)
            {
                if (objIdx >= objective.Count && blankIdx >= blanks.Count && subIdx >= subjective.Count) break;   // 排完了不占空栏
                var used = 0.0;
                var pageObj = new List<Question>();
                var pageBlank = new List<Question>();
                var pageSub = new List<Question>();

                if (objIdx < objective.Count)
                {
                    var rows = (int)Math.Floor((capacity - used - BlockChromeMm) / ObjRowH);
                    var take = Math.Min(objective.Count - objIdx, Math.Max(0, rows) * columns);
                    if (take > 0)
                    {
                        pageObj = objective.Skip(objIdx).Take(take).ToList();
                        objIdx += take;
                        used += BlockChromeMm + RowsOnPage(take, columns) * ObjRowH;
                    }
                }

                if (objIdx >= objective.Count && blankIdx < blanks.Count)
                {
                    var rowH = BlankRowMm + BlankRowGapMm;
                    var rows = (int)Math.Floor((capacity - used - BlockChromeMm + BlankRowGapMm) / rowH);
                    var take = Math.Min(blanks.Count - blankIdx, Math.Max(0, rows));
                    if (take > 0)
                    {
                        pageBlank = blanks.Skip(blankIdx).Take(take).ToList();
                        blankIdx += take;
                        used += BlockChromeMm + take * rowH;   // 最后一行的 margin 也在块内
                    }
                }

                if (objIdx >= objective.Count && blankIdx >= blanks.Count && subIdx < subjective.Count)
                {
                    var availSub = capacity - used - BlockChromeMm;
                    var subUsed = 0.0;
                    while (subIdx < subjective.Count)
                    {
                        var h = SubjectiveHeightMm(subjective[subIdx]);
                        if (pageSub.Count > 0 && subUsed + h > availSub) break;
                        pageSub.Add(subjective[subIdx]);
                        subUsed += h;
                        subIdx++;
                    }
                }

                if (pageObj.Count == 0 && pageBlank.Count == 0 && pageSub.Count == 0)
                {
                    if (objIdx < objective.Count)
                    {
                        pageObj = objective.Skip(objIdx).Take(columns).ToList();
                        objIdx += pageObj.Count;
                    }
                    else if (blankIdx < blanks.Count)
                    {
                        pageBlank = blanks.Skip(blankIdx).Take(1).ToList();
                        blankIdx += pageBlank.Count;
                    }
                    else
                    {
                        pageSub = subjective.Skip(subIdx).Take(1).ToList();
                        subIdx += pageSub.Count;
                    }
                }

                cols.Add((pageObj, pageBlank, pageSub));
            }

            pages.Add((pages.Count == 0, cols));
        }

        if (pages.Count == 0) pages.Add((true, [([], [], [])]));

        // ── 逐页逐栏算坐标 ──
        var result = new List<SheetPageLayout>();
        for (var p = 0; p < pages.Count; p++)
        {
            var (hasHeader, pageCols) = pages[p];
            var optMarks = new List<OptionMark>();
            var idMarks = new List<IdMark>();
            var frames = new List<SubjectiveFrame>();
            var y = PagePadTop;

            if (hasHeader)
            {
                y += TitleH + TitleMb + SubtitleH + SubtitleMb;                 // 标题 + 副标题
                if (opt.ShowNotes) y += NotesH + NotesMb;                        // 注意事项
                if (opt.IdArea == IdAreaKind.Bubble) idMarks.AddRange(IdBubbles(opt, y));
                y += (opt.IdArea == IdAreaKind.Bubble ? InfoH_Bubble : InfoH_Plain) + InfoMb;  // 信息区
            }
            else
            {
                y += SubtitleH + 2.0;                                            // 页眉"第 N / M 页"（margin 2mm）
            }

            // 表头通栏；其下每栏各自从列区顶部开始往下排（与 .cols/.col 的 flex 起点一致）
            var colsTop = y;
            for (var c = 0; c < pageCols.Count; c++)
            {
                var (pageObj, pageBlank, pageSub) = pageCols[c];
                var colLeft = PagePadSide + c * (opt.BlockWidthMm + AnswerSheetOptions.ColumnGapMm);
                var blockW = opt.BlockWidthMm;
                // 每栏的纵向游标都从列区顶部重新开始：右栏不受左栏排到哪儿的影响
                // （这条曾经写错过——复用同一个 y——右栏作答框整体上移了 84mm，靠 DOM 实测比对才发现）
                var colY = colsTop;

                // 客观题块
                if (pageObj.Count > 0)
                {
                    var bodyTop = colY + QBlockTopInset;   // 已含 .block-title 高度与 margin
                    var colW = (blockW - 2 * QBlockInsetX - 2 * QBodyPadX
                                - (columns - 1) * ObjColGap) / columns;
                    var perCol = RowsOnPage(pageObj.Count, columns);
                    for (var bc = 0; bc < columns; bc++)
                    {
                        var colX = colLeft + QBlockInsetX + QBodyPadX + bc * (colW + ObjColGap);
                        for (var r = 0; r < perCol; r++)
                        {
                            var idx = bc * perCol + r;
                            if (idx >= pageObj.Count) break;
                            var q = pageObj[idx];
                            var rowY = bodyTop + r * ObjRowH;
                            var keys = OptionsOf(q, options);
                            for (var k = 0; k < keys.Count; k++)
                            {
                                var optX = colX + ObjNoW + ObjNoGap + k * (ObjOptW + ObjOptMr);
                                var optY = rowY + (ObjRowH - ObjOptH) / 2;
                                optMarks.Add(new OptionMark(q.Index, keys[k],
                                    new BubbleMark(optX, optY, ObjOptW, ObjOptH)));
                            }
                        }
                    }
                    colY = colsTop + BlockChromeMm + RowsOnPage(pageObj.Count, columns) * ObjRowH;
                }

                // 填空题块：一题一行、行高固定（与 .blank-row 的 9mm 一致）。
                // 每行登记成"作答框"，切图与阅卷都按一题一块走，识别端的整页指纹也用它。
                if (pageBlank.Count > 0)
                {
                    var rowTop = colY + QBlockTopInset;
                    for (var i = 0; i < pageBlank.Count; i++)
                    {
                        frames.Add(new SubjectiveFrame(pageBlank[i].Index, new BubbleMark(
                            colLeft + QBlockInsetX + QBodyPadX, rowTop,
                            blockW - 2 * (QBlockInsetX + QBodyPadX),
                            BlankRowMm)));
                        rowTop += BlankRowMm + BlankRowGapMm;
                    }
                    colY += BlockChromeMm + pageBlank.Count * (BlankRowMm + BlankRowGapMm);
                }

                // 主观题块：作答框外框也是"这一页"的指纹（纯主观页没有选项气泡，
                // 页码识别全靠这些外框 + 客观题气泡）
                if (pageSub.Count > 0)
                {
                    var subTop = colY + QBlockTopInset;   // 作答框顶 = 块顶 + 边框/内边距/块标题（实测 7.49mm，与客观题首行同基准）
                    foreach (var q in pageSub)
                    {
                        var frameH = SubjectiveFrameHeightMm(q);
                        frames.Add(new SubjectiveFrame(q.Index, new BubbleMark(
                            colLeft + QBlockInsetX + QBodyPadX, subTop,
                            blockW - 2 * (QBlockInsetX + QBodyPadX),
                            frameH)));
                        subTop += frameH + SubjMb;      // 框间距 = 下间距 3mm
                    }
                }
            }

            result.Add(new SheetPageLayout(opt.Paper, p + 1, pages.Count, optMarks, idMarks, frames));
        }

        return result;
    }

    /// <summary>考号区 8 列 × 10 行气泡的坐标（infoTop = 信息区顶边）。</summary>
    private static IEnumerable<IdMark> IdBubbles(AnswerSheetOptions opt, double infoTop)
    {
        // 信息区右侧的考号区
        var areaRight = PagePadSide + opt.ContentWidthMm - InfoInset;
        var areaLeft = areaRight - IdAreaW;

        var rowLabelsW = 5.0;   // .id-rowlabel 宽度
        var colsLeft = areaLeft + IdAreaInset + rowLabelsW;
        var gridTop = infoTop + InfoInset + IdAreaInset + IdTitleH + IdTitleMb + IdWriteH + IdWriteMb
                      + IdColNoH;

        for (var c = 0; c < IdColumns; c++)
        {
            var colX = colsLeft + c * (IdBubbleW + IdColMr);
            for (var d = 0; d < IdRows; d++)
            {
                var y = gridTop + d * (IdBubbleH + IdBubbleMb);
                yield return new IdMark(c, d, new BubbleMark(colX, y, IdBubbleW, IdBubbleH));
            }
        }
    }

    /// <summary>每个区块的外框开销：块高减内容高 + 块间距 = 12.24（DOM 实测校准，与渲染器 BlockChromeMm 一致）。</summary>
    private const double BlockChromeMm = 12.24;
}
