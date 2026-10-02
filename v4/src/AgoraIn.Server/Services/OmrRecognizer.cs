using System.Runtime.InteropServices;
using AgoraIn.Core.Entities;
using AgoraIn.Server.Models;
using SkiaSharp;

namespace AgoraIn.Server.Services;

/// <summary>本地 OMR 识别结果（切卡 + 客观题 + 考号，不调用大模型）。</summary>
public sealed record OmrLocalResult(
    string PaperName,
    int PageNo,
    int TotalPages,
    string? StudentRef,
    List<OmrQuestionAnswer> Answers,
    double Confidence,
    string Debug);

/// <summary>
/// 答题卡本地识别（不花 token）：靠卡面四角定位标记做透视校正（"切卡"），
/// 再按 <see cref="AnswerSheetLayout"/> 算出的毫米坐标采样气泡判定填涂。
///
/// 流程：解码灰度 → 自适应二值化 → 连通域找四角标记 → 识别纸张规格（由标记间距反推）
///       → 单应变换把卡片归正成固定 mm/px 的图 → 按布局采样气泡（内区判填涂、外环判本页是否匹配）
///
/// 识别不出四角标记（没拍全/太模糊）时返回 null，调用方回退到视觉模型。
/// </summary>
public static class OmrRecognizer
{
    private const int MaxSidePx = 1800;      // 先降采样，够用且快
    private const int WarpPpm = 8;           // 归正图分辨率：每毫米 8 像素
    private const double MarkerMm = 6.0;     // 定位标记边长（与渲染器一致）
    private const double MarkerInsetMm = 7.0;
    private const double MarkerCenterMm = MarkerInsetMm + MarkerMm / 2;   // 10mm
    private const double FillInkThreshold = 0.42;   // 内区墨量阈值（1=全黑）
    private const double RingInkThreshold = 0.25;   // 外框存在阈值（用于判断本页布局是否匹配）
    private const double PageMatchThreshold = 0.55;

    /// <summary>
    /// 识别失败时的分期诊断（闸门 400 响应带出）：解码 / 二值化 / 定位标记 / 四角 / 版面匹配各到哪一步。
    /// 只在识别失败的路径上被调用，不在正常路径付成本。
    /// </summary>
    internal static string Diagnose(
        byte[] imageData,
        IReadOnlyList<Question> questions,
        IReadOnlyDictionary<string, List<string>> options,
        IReadOnlyDictionary<string, QuestionPlacement>? placements = null)
    {
        var gray = DecodeGray(imageData);
        if (gray == null) return "diag=decode_failed";
        var ink = AdaptiveBinarize(gray, out var stats);
        var markers = FindMarkers(ink, gray.W, gray.H);
        if (markers.Count < 4) return $"diag=markers<{4} markers={markers.Count} {gray.W}x{gray.H} {stats}";
        var quad = PickCorners(markers, gray.W, gray.H);
        if (quad == null) return $"diag=quad_null markers={markers.Count} {stats}";

        var (guessed, pxPerMm) = IdentifyPaper(quad.Value, quad.Value.SidePx);
        var candidates = new List<SheetPaper>();
        if (guessed != null) candidates.Add(guessed);
        candidates.AddRange(SheetPaper.All.Where(p => p != guessed).OrderBy(p => Error(quad.Value, p)));

        var src = new[] { quad.Value.Tl, quad.Value.Tr, quad.Value.Br, quad.Value.Bl };
        var parts = new List<string> { $"diag=match markers={markers.Count} guess={guessed?.Name ?? "-"} ppm={pxPerMm:0.0}" };
        var tried = 0;
        foreach (var p in candidates)
        {
            if (++tried > 3) break;
            var h = SolveHomography(AnswerSheetLayout.MarkerCentersMm(p), src);
            if (h == null) { parts.Add($"{p.Name}:h_null"); continue; }
            var canon = Warp(gray, h, p, WarpPpm);
            for (var rot = 0; rot < 4; rot++)
            {
                var srcRot = RotateSrc(src, rot);
                var hRot = rot == 0 ? h : SolveHomography(AnswerSheetLayout.MarkerCentersMm(p), srcRot);
                if (hRot == null) { parts.Add($"{p.Name}:r{rot}:h_null"); continue; }
                var c = rot == 0 ? canon : Warp(gray, hRot, p, WarpPpm);
                var (pg, score, _) = MatchLayout(c, questions, options, p, placements: placements);
                if (pg != null) parts.Add($"{p.Name}:r{rot}:p{pg.PageNo}={score:0.00}");
                else parts.Add($"{p.Name}:r{rot}:none");
                if (score >= 0.80) break;
            }
        }
        return string.Join(" ", parts);
    }

    public static OmrLocalResult? Recognize(
        byte[] imageData,
        IReadOnlyList<Question> questions,
        IReadOnlyDictionary<string, List<string>> options,
        IReadOnlyDictionary<string, QuestionPlacement>? placements = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var gray = DecodeGray(imageData);
        if (gray == null) return null;

        var ink = AdaptiveBinarize(gray, out var stats);
        var markers = FindMarkers(ink, gray.W, gray.H);
        if (markers.Count < 4) return null;

        var quad = PickCorners(markers, gray.W, gray.H);
        if (quad == null) return null;

        var (guessed, pxPerMm) = IdentifyPaper(quad.Value, quad.Value.SidePx);
        // 先用估算出的纸型试；不行再按"长宽比接近"的顺序试其它纸型（最多 3 种）
        var candidates = new List<SheetPaper>();
        if (guessed != null) candidates.Add(guessed);
        candidates.AddRange(SheetPaper.All
            .Where(p => p != guessed)
            .OrderBy(p => Error(quad.Value, p)));

        var src = new[] { quad.Value.Tl, quad.Value.Tr, quad.Value.Br, quad.Value.Bl };
        SheetPaper? paper = null;
        SheetPageLayout? page = null;
        double bestScore = 0.0;
        List<string> pageScores = [];
        Gray? canon = null;
        double[]? hOfBest = null;
        var tried = 0;
        foreach (var p in candidates)
        {
            tried++;
            if (tried > 3) break;
            var model = AnswerSheetLayout.MarkerCentersMm(p);
            // 先按"照片是正的"试；分不够再看它是不是整体转了 90/180/270°（横版答题卡手机随手拍很常见）。
            // 正的那次一旦匹配上就直接跳出，日常不付额外代价。
            for (var rot = 0; rot < 4; rot++)
            {
                var h = SolveHomography(model, RotateSrc(src, rot));
                if (h == null) continue;
                var canonRot = Warp(gray, h, p, WarpPpm);
                var (pg, score, scores) = MatchLayout(canonRot, questions, options, p, placements: placements);
                if (score > bestScore)
                {
                    bestScore = score;
                    paper = p;
                    page = pg;
                    pageScores = scores;
                    hOfBest = h;
                    canon = canonRot;
                }
                if (bestScore >= 0.80) break;      // 已经足够确定，不必再试
            }
            if (bestScore >= 0.80) break;
        }
        if (paper == null || page == null || bestScore < PageMatchThreshold || canon == null) return null;

        // ── 页码以页脚二维码为准 ──
        // 环匹配猜错页的后果不只是页码难看：后续采样用的是"那一页"的题目几何，
        // 报"第 5 页"就意味着答案也是按第 5 页的位置采的。读得出二维码就按它选页。
        var (qrPageNo, qrPaperId) = hOfBest != null ? DecodePageQr(gray, hOfBest, paper) : (null, null);
        if (qrPageNo == null && hOfBest != null && gray.W < 2400)
        {
            // 大纸（8K 等）降到 1800px 后二维码不足 2px/模块，解不出来。
            // 只在第一次失败时按高分辨率重解一次，常态路径不付这份代价。
            var hiRes = DecodeGray(imageData, maxSide: 3600);
            if (hiRes != null) (qrPageNo, qrPaperId) = DecodePageQr(hiRes, hOfBest, paper);
        }
        var pageFromRing = page.PageNo;
        if (qrPageNo is { } qn && qn >= 1 && qn <= page.TotalPages && qn != page.PageNo)
        {
            var (pgQr, scoreQr, scoresQr) = MatchLayout(canon, questions, options, paper, onlyPageNo: qn, placements: placements);
            if (pgQr != null && scoreQr >= PageMatchThreshold)
            {
                page = pgQr;
                bestScore = scoreQr;
                pageScores = scoresQr;
            }
        }
        // 版面按二维码那页匹配不上时也认二维码的页码（印刷事实优先于图案猜测）
        if (qrPageNo is { } q2 && q2 >= 1 && q2 <= page.TotalPages) page = page with { PageNo = q2 };


        // ── 采样气泡 ──
        const double white = 235.0;   // 纸面白参考（照片已二值化判框，这里只用于换算墨量）
        var byIndex = questions.ToDictionary(q => q.Index);
        var answers = new List<OmrQuestionAnswer>();
        foreach (var group in page.Options.GroupBy(o => o.QuestionIndex))
        {
            var marks = group.ToList();
            var samples = marks.Select(m => (
                m.Option,
                Ink: InnerInk(canon, m.Bubble, WarpPpm, white))).ToList();
            var best = samples.OrderByDescending(s => s.Ink).First();
            if (best.Ink < FillInkThreshold) continue;   // 该题未作答

            var isMulti = byIndex.TryGetValue(group.Key, out var q) && q.Type == QuestionType.MultipleChoice;
            var chosen = isMulti
                ? samples.Where(s => s.Ink >= FillInkThreshold && s.Ink >= best.Ink * 0.6)
                         .OrderBy(s => s.Option, StringComparer.Ordinal).Select(s => s.Option).ToList()
                : [best.Option];

            var second = samples.Where(s => s.Option != best.Option).Max(s => s.Ink);
            var conf = Math.Clamp(0.55 + (best.Ink - second) * 0.5, 0, 0.99);
            answers.Add(new OmrQuestionAnswer
            {
                Index = group.Key + 1,             // 卡面印刷题号（1 起）
                Answer = string.Concat(chosen),
                Confidence = conf,
            });
        }

        // ── 考号（仅首页有考号区）──
        string? studentRef = null;
        if (page.IdDigits.Count > 0)
        {
            var digits = new char[AnswerSheetLayout.IdColumns];
            var filled = 0;
            foreach (var col in page.IdDigits.GroupBy(d => d.Column))
            {
                var best = col.Select(d => (d.Digit, Ink: InnerInk(canon, d.Bubble, WarpPpm, white)))
                              .OrderByDescending(x => x.Ink).First();
                if (best.Ink >= FillInkThreshold)
                {
                    digits[col.Key] = (char)('0' + best.Digit);
                    filled++;
                }
                else digits[col.Key] = 'x';        // 未涂
            }
            if (filled == AnswerSheetLayout.IdColumns) studentRef = new string(digits);
        }

        var total = page.Options.Select(o => o.QuestionIndex).Distinct().Count();
        var confidence = total == 0 ? 0 : Math.Clamp(answers.Count / (double)total, 0, 1);

        return new OmrLocalResult(
            paper.Name, page.PageNo, page.TotalPages, studentRef, answers, confidence,
            $"markers=4 paper={paper.Name} {paper.WidthMm:0}x{paper.HeightMm:0}mm pxPerMm={pxPerMm:0.0} " +
            $"page={page.PageNo}/{page.TotalPages} qr={qrPageNo?.ToString() ?? "-"} ring={pageFromRing} " +
            $"qrPaper={Short(qrPaperId) ?? "-"} ringScore={bestScore:0.00} " +
            $"all=[{string.Join(" ", pageScores.Take(6))}] " +
            $"answers={answers.Count}/{total} id={studentRef ?? "-"} " +
            $"binarize={stats} ms={sw.ElapsedMilliseconds}");
    }

    private static string? Short(string? id) => string.IsNullOrEmpty(id) ? null : id[..Math.Min(8, id.Length)];

    /// <summary>
    /// 在归正图上匹配"哪种版式 + 第几页"：打印时的「注意事项开关 / 考号区形式」不入库，
    /// 于是把 6 种组合都算一遍，谁的气泡外框在图上"到处都是"就用谁（外框分数 0-1）。
    /// </summary>
    private static (SheetPageLayout? Page, double Score, List<string> Scores) MatchLayout(
        Gray canon, IReadOnlyList<Question> questions,
        IReadOnlyDictionary<string, List<string>> options, SheetPaper paper,
        int? onlyPageNo = null,
        IReadOnlyDictionary<string, QuestionPlacement>? placements = null)
    {
        const double white = 235.0;
        SheetPageLayout? best = null;
        var bestScore = 0.0;
        var bestMarks = 0;
        var scores = new List<string>();
        foreach (var showNotes in new[] { true, false })
        foreach (var idArea in new[] { IdAreaKind.Bubble, IdAreaKind.Handwrite, IdAreaKind.None })
        {
            var variant = new AnswerSheetOptions { Paper = paper, ShowNotes = showNotes, IdArea = idArea };
            var pages = AnswerSheetLayout.Compute(questions, options, variant, placements);
            for (var i = 0; i < pages.Count; i++)
            {
                if (onlyPageNo is { } only && pages[i].PageNo != only) continue;
                var score = RingMatchScore(canon, pages[i], WarpPpm, white);
                if (score < 0.4) continue;
                scores.Add($"{(showNotes ? "N" : "-")}{(idArea == IdAreaKind.Bubble ? "B" : idArea == IdAreaKind.Handwrite ? "H" : "-")}p{i + 1}:{score:0.00}");
                // 平分时选"标记更多"的版式：内容较少的页是较多页的子集（如纯主观页 2 与 3 结构相近），
                // 子集页也能拿满分，必须取题目更全的那一页
                var marks = pages[i].Options.Count + pages[i].Frames.Count;
                if (score > bestScore + 0.02 || (score > bestScore - 0.02 && marks > bestMarks))
                {
                    bestScore = Math.Max(bestScore, score);
                    bestMarks = marks;
                    best = pages[i];
                }
            }
        }
        return (best, bestScore, scores);
    }

    /// <summary>
    /// 读页脚二维码（<c>agorain:sheet:{试卷Id}:p{页号}</c>）取回权威页码。
    ///
    /// 环图案匹配只能"猜"页码：同卷各页结构相近（纯主观页互为子集），第 2 页会被认成第 6 页——
    /// 页码错，采样用的题目几何也就跟着错。二维码是印在卡上的事实，读它才是正解。
    ///
    /// 二维码只有 16mm，手机照片里常常不足 2px/模块，所以不读降采样后的灰度图，
    /// 而是按单应矩阵从原图直接放大采样一个方块再解码（顺带留 2mm 静区）。
    /// 读不出（模糊/遮挡/老版本卡）返回 (null, null)，调用方回退到环匹配的猜测。
    /// </summary>
    internal static (int? PageNo, string? PaperId) DecodePageQr(Gray src, double[] h, SheetPaper paper)
    {
        const int targetPx = 360;      // 约 20px/mm ≈ 8px/模块，ZXing 的舒适区
        const double pad = 2.0;        // 静区：二维码四周留白，否则定位图案难找
        var box = AnswerSheetLayout.QrBox(paper);
        var x0 = box.Xmm - pad;
        var y0 = box.Ymm - pad;
        var side = box.Wmm + 2 * pad;

        var px = new byte[targetPx * targetPx];
        for (var oy = 0; oy < targetPx; oy++)
        for (var ox = 0; ox < targetPx; ox++)
        {
            var mx = x0 + (ox + 0.5) * side / targetPx;
            var my = y0 + (oy + 0.5) * side / targetPx;
            var d = h[6] * mx + h[7] * my + 1.0;
            if (Math.Abs(d) < 1e-9) { px[oy * targetPx + ox] = 255; continue; }
            var sx = (h[0] * mx + h[1] * my + h[2]) / d;
            var sy = (h[3] * mx + h[4] * my + h[5]) / d;
            px[oy * targetPx + ox] = Bilinear(src, sx, sy);
        }

        try
        {
            var luminance = new ZXing.RGBLuminanceSource(px, targetPx, targetPx);   // 灰度数组
            var bmp = new ZXing.BinaryBitmap(new ZXing.Common.HybridBinarizer(luminance));
            var hints = new Dictionary<ZXing.DecodeHintType, object>
            {
                [ZXing.DecodeHintType.TRY_HARDER] = true,
                [ZXing.DecodeHintType.POSSIBLE_FORMATS] = new List<ZXing.BarcodeFormat> { ZXing.BarcodeFormat.QR_CODE },
            };
            var text = new ZXing.MultiFormatReader().decode(bmp, hints)?.Text;
            if (string.IsNullOrEmpty(text)) return (null, null);

            // agorain:sheet:{paperId}:p{页号}
            var parts = text.Split(':');
            if (parts.Length < 4 || parts[0] != "agorain" || parts[1] != "sheet") return (null, null);
            var pageNo = parts[3].StartsWith('p') && int.TryParse(parts[3][1..], out var n) ? n : (int?)null;
            return (pageNo, parts[2]);
        }
        catch
        {
            // 读不出二维码不是错误：老卡、模糊、遮挡都会走到这里，交给调用方回退
            return (null, null);
        }
    }

    /// <summary>调试版：同时回传各变体得分明细。</summary>
    internal static (SheetPageLayout? Page, double Score, List<string>) MatchLayoutDebug(
        Gray canon, IReadOnlyList<Question> questions,
        IReadOnlyDictionary<string, List<string>> options, SheetPaper paper,
        out List<string> details,
        IReadOnlyDictionary<string, QuestionPlacement>? placements = null)
    {
        const double white = 235.0;
        SheetPageLayout? best = null;
        var bestScore = 0.0;
        var bestMarks = 0;
        var all = new List<string>();
        foreach (var showNotes in new[] { true, false })
        foreach (var idArea in new[] { IdAreaKind.Bubble, IdAreaKind.Handwrite, IdAreaKind.None })
        {
            var variant = new AnswerSheetOptions { Paper = paper, ShowNotes = showNotes, IdArea = idArea };
            var pages = AnswerSheetLayout.Compute(questions, options, variant, placements);
            for (var i = 0; i < pages.Count; i++)
            {
                var score = RingMatchScore(canon, pages[i], WarpPpm, white);
                if (score < 0.4) continue;
                var marks = pages[i].Options.Count + pages[i].Frames.Count;
                all.Add($"{(showNotes ? "N" : "-")}{(idArea == IdAreaKind.Bubble ? "B" : idArea == IdAreaKind.Handwrite ? "H" : "-")}p{i + 1}:{score:0.00}({marks})");
                if (score > bestScore + 0.02 || (score > bestScore - 0.02 && marks > bestMarks))
                { bestScore = Math.Max(bestScore, score); bestMarks = marks; best = pages[i]; }
            }
        }
        details = all;
        return (best, bestScore, all);
    }

    /// <summary>
    /// 某纸型与该四边形的长宽比误差（用于换纸型时的尝试顺序）。
    /// 横竖两个方向取较小值：手机拍横版答题卡时可能整体转了 90°，纸型判断不该因此跑偏。
    /// </summary>
    private static double Error(Quad q, SheetPaper p)
    {
        var top = Dist(q.Tl, q.Tr);
        var left = Dist(q.Tl, q.Bl);
        var want = p.WidthMm / p.HeightMm;
        var got = top / Math.Max(1.0, left);
        return Math.Min(Math.Abs(got - want), Math.Abs(1.0 / Math.Max(1e-6, got) - want));
    }

    /// <summary>把四边形顶点按顺时针转 k 格：模拟"照片整体转了 90°×k"的假设。</summary>
    private static (double X, double Y)[] RotateSrc((double X, double Y)[] src, int k)
    {
        if (k == 0) return src;
        return [.. src.Skip(k), .. src.Take(k)];
    }

    /// <summary>
    /// 人工复盘切图：把某道题的作答区域从照片里裁出来（自动切分只保留本题区域）。
    /// 复用切卡流水线：定位标记 → 单应归正 → 按版面模型取该题外框（主观题 = 作答框，
    /// 客观题 = 选项行），四周留 2mm 余量，裁剪后转 JPEG。
    /// 识别不出定位标记（照片不全/模糊）返回 null，调用方回退显示整张原图。
    /// </summary>
    public static byte[]? CropQuestion(
        byte[] imageData,
        IReadOnlyList<Question> questions,
        IReadOnlyDictionary<string, List<string>> options,
        int questionIndex,
        IReadOnlyDictionary<string, QuestionPlacement>? placements = null)
    {
        var gray = DecodeGray(imageData);
        if (gray == null) return null;
        var ink = AdaptiveBinarize(gray, out _);
        var markers = FindMarkers(ink, gray.W, gray.H);
        if (markers.Count < 4) return null;
        var quad = PickCorners(markers, gray.W, gray.H);
        if (quad == null) return null;

        var (guessed, _) = IdentifyPaper(quad.Value, quad.Value.SidePx);
        var candidates = new List<SheetPaper>();
        if (guessed != null) candidates.Add(guessed);
        candidates.AddRange(SheetPaper.All.Where(p => p != guessed).OrderBy(p => Error(quad.Value, p)));

        var src = new[] { quad.Value.Tl, quad.Value.Tr, quad.Value.Br, quad.Value.Bl };
        foreach (var paper in candidates.Take(3))
        {
            var h = SolveHomography(AnswerSheetLayout.MarkerCentersMm(paper), src);
            if (h == null) continue;
            var canon = Warp(gray, h, paper, WarpPpm);

            // 先用与整卷识别相同的"整页匹配"认页（多纸型候选 + 版式变体 + 平分取多），
            // 匹配可信（≥0.7）才继续；再在该页上验证"本题区域"真实存在并裁剪。
            // 只验证本题区域不够：别的页/别的纸型的框可能与本照片上其它元素重合造成假阳性。
            var (bestPage, pageScore, _) = MatchLayout(canon, questions, options, paper, placements: placements);
            if (bestPage == null || pageScore < 0.7) continue;

            // 环匹配可能把本页认成结构相近的另一页（第 2 页 ↔ 第 6 页）——那会让"本题"落到
            // 错误的位置上。二维码写着这是第几页，按它重新匹配。
            var (qrPageNo, _) = DecodePageQr(gray, h, paper);
            if (qrPageNo is { } qn && qn != bestPage.PageNo && qn >= 1 && qn <= bestPage.TotalPages)
            {
                var (pgQr, scoreQr, _) = MatchLayout(canon, questions, options, paper, onlyPageNo: qn, placements: placements);
                if (pgQr != null && scoreQr >= 0.7) bestPage = pgQr;
            }

            var frame = bestPage.Frames.FirstOrDefault(f => f.QuestionIndex == questionIndex);
            BubbleMark region;
            if (frame != null)
            {
                region = frame.Box;
                if (!RingPresent(canon, region, WarpPpm, search: true)) continue;
            }
            else
            {
                var row = bestPage.Options.Where(o => o.QuestionIndex == questionIndex).ToList();
                if (row.Count == 0) continue;
                var x0 = row.Min(o => o.Bubble.Xmm) - 9.0;    // 含左侧题号
                var y0 = row.Min(o => o.Bubble.Ymm);
                region = new BubbleMark(x0, y0,
                    row.Max(o => o.Bubble.Xmm + o.Bubble.Wmm) - x0 + 1.0,
                    row.Max(o => o.Bubble.Ymm + o.Bubble.Hmm) - y0);
                // 客观题行没有外框：验证选项气泡外框（与页面匹配同口径）
                if (row.Count(o => RingPresent(canon, o.Bubble, WarpPpm)) < Math.Max(1, row.Count / 2)) continue;
            }

            const double margin = 2.0;
            var x = Math.Max(0, region.Xmm - margin);
            var y = Math.Max(0, region.Ymm - margin);
            var w = Math.Min(paper.WidthMm - x, region.Wmm + 2 * margin);
            var hh = Math.Min(paper.HeightMm - y, region.Hmm + 2 * margin);
            return EncodeCrop(canon, x, y, w, hh);
        }
        return null;
    }

    /// <summary>从归正图裁剪毫米矩形并编码为 JPEG。</summary>
    private static byte[] EncodeCrop(Gray canon, double xMm, double yMm, double wMm, double hMm)
    {
            var px0 = (int)Math.Round(xMm * WarpPpm);
            var py0 = (int)Math.Round(yMm * WarpPpm);
            var pw = (int)Math.Round(wMm * WarpPpm);
            var ph = (int)Math.Round(hMm * WarpPpm);
            px0 = Math.Clamp(px0, 0, canon.W - 2);
            py0 = Math.Clamp(py0, 0, canon.H - 2);
            pw = Math.Clamp(pw, 8, canon.W - px0);
            ph = Math.Clamp(ph, 8, canon.H - py0);

            using var skBitmap = new SKBitmap(pw, ph, SKColorType.Gray8, SKAlphaType.Opaque);
            var span = skBitmap.GetPixelSpan();
            for (var row = 0; row < ph; row++)
            {
                var srcOff = (py0 + row) * canon.W + px0;
                canon.Px.AsSpan(srcOff, pw).CopyTo(span.Slice(row * pw, pw));
            }
            using var skImage = SKImage.FromBitmap(skBitmap);
            using var skData = skImage.Encode(SKEncodedImageFormat.Jpeg, 88);
            return skData.ToArray();
    }

    // ── 图像基础 ──────────────────────────────────────────────────────

    internal sealed class Gray(int w, int h, byte[] px)
    {
        public readonly int W = w, H = h;
        public readonly byte[] Px = px;
    }

    /// <summary>解码并降采样为灰度图（0=黑 255=白）。</summary>
    internal static Gray? DecodeGray(byte[] data, int maxSide = MaxSidePx)
    {
        try
        {
            using var bmp = SKBitmap.Decode(data);
            if (bmp == null || bmp.Width < 32 || bmp.Height < 32) return null;

            var scale = Math.Min(1.0, (double)maxSide / Math.Max(bmp.Width, bmp.Height));
            var tw = Math.Max(1, (int)Math.Round(bmp.Width * scale));
            var th = Math.Max(1, (int)Math.Round(bmp.Height * scale));

            using var resized = bmp.Resize(
                new SKImageInfo(tw, th, SKColorType.Gray8, SKAlphaType.Opaque),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            if (resized == null) return null;

            return new Gray(tw, th, resized.GetPixelSpan().ToArray());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>自适应二值化（积分图局部均值），返回 ink 掩码。</summary>
    internal static bool[] AdaptiveBinarize(Gray g, out string stats)
    {
        var w = g.W; var h = g.H;
        var integral = new long[(w + 1) * (h + 1)];
        for (var y = 0; y < h; y++)
        {
            long rowSum = 0;
            for (var x = 0; x < w; x++)
            {
                rowSum += g.Px[y * w + x];
                integral[(y + 1) * (w + 1) + x + 1] = integral[y * (w + 1) + x + 1] + rowSum;
            }
        }

        var win = Math.Max(15, Math.Min(w, h) / 12) | 1;
        var r = win / 2;
        var mask = new bool[w * h];
        var inkCount = 0;
        for (var y = 0; y < h; y++)
        {
            var y0 = Math.Max(0, y - r); var y1 = Math.Min(h - 1, y + r);
            for (var x = 0; x < w; x++)
            {
                var x0 = Math.Max(0, x - r); var x1 = Math.Min(w - 1, x + r);
                var area = (long)(x1 - x0 + 1) * (y1 - y0 + 1);
                var sum = integral[(y1 + 1) * (w + 1) + x1 + 1] - integral[y0 * (w + 1) + x1 + 1]
                          - integral[(y1 + 1) * (w + 1) + x0] + integral[y0 * (w + 1) + x0];
                var mean = sum / (double)area;
                var isInk = g.Px[y * w + x] < mean * 0.85;
                mask[y * w + x] = isInk;
                if (isInk) inkCount++;
            }
        }
        stats = $"ink={inkCount * 100.0 / (w * h):0.0}% win={win}";
        return mask;
    }

    internal readonly record struct Comp(int MinX, int MinY, int MaxX, int MaxY, int Area)
    {
        public double Cx => (MinX + MaxX) / 2.0;
        public double Cy => (MinY + MaxY) / 2.0;
        public int W => MaxX - MinX + 1;
        public int H => MaxY - MinY + 1;
    }

    /// <summary>调试用：返回全部连通域（不过滤）。</summary>
    internal static List<Comp> DebugComponents(bool[] mask, int w, int h)
        => Components(mask, w, h, filter: false);

    /// <summary>连通域标记（8 邻域），返回满足"实心方块"特征的候选（四角定位标记）。</summary>
    internal static List<Comp> FindMarkers(bool[] mask, int w, int h) => Components(mask, w, h, filter: true);

    private static List<Comp> Components(bool[] mask, int w, int h, bool filter)
    {
        var total = w * h;
        var visited = new bool[total];
        var result = new List<Comp>();
        var minSide = Math.Min(w, h) * 0.010;
        var maxSide = Math.Min(w, h) * 0.25;
        var stack = new Stack<int>();

        for (var start = 0; start < total; start++)
        {
            if (!mask[start] || visited[start]) continue;
            visited[start] = true;
            stack.Clear();
            stack.Push(start);
            int minX = w, minY = h, maxX = 0, maxY = 0, area = 0;
            var touchesBorder = false;
            while (stack.Count > 0)
            {
                var idx = stack.Pop();
                var x = idx % w; var y = idx / w;
                area++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1) touchesBorder = true;

                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = x + dx; var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    var ni = ny * w + nx;
                    if (!mask[ni] || visited[ni]) continue;
                    visited[ni] = true;
                    stack.Push(ni);
                }
            }

            if (touchesBorder && filter) continue;           // 背景大块
            var cw = maxX - minX + 1; var ch = maxY - minY + 1;
            if (filter)
            {
                if (cw < minSide || ch < minSide || cw > maxSide || ch > maxSide) continue;
                var aspect = cw / (double)ch;
                // 定位标记是 6x6mm 正方形；气泡（6x4 / 6x3.6）与文字都不满足 aspect≈1
                if (aspect < 0.72 || aspect > 1.38) continue;
                var fill = area / (double)(cw * ch);
                if (fill < 0.82) continue;                    // 实心方块填充率高（气泡只有细边框）
            }
            result.Add(new Comp(minX, minY, maxX, maxY, area));
        }

        return result;
    }

    internal readonly record struct Quad((double X, double Y) Tl, (double X, double Y) Tr,
        (double X, double Y) Br, (double X, double Y) Bl, double SidePx);

    /// <summary>
    /// 从候选方块里挑出四角：四个标记大小相近、构成凸四边形、且覆盖画面足够大。
    /// 用组合搜索而不是"取极值点"——页面上气泡/文字的碎片也满足尺寸范围，
    /// 取极值会被这些碎片带偏（实测 73 个候选时直接选不中）。
    /// </summary>
    internal static Quad? PickCorners(List<Comp> comps, int imgW, int imgH)
    {
        var cands = comps.OrderByDescending(c => c.Area).Take(16).ToList();
        if (cands.Count < 4) return null;
        var imageArea = (double)imgW * imgH;

        Quad? best = null;
        var bestArea = 0.0;
        for (var a = 0; a < cands.Count - 3; a++)
        for (var b = a + 1; b < cands.Count - 2; b++)
        for (var c = b + 1; c < cands.Count - 1; c++)
        for (var d = c + 1; d < cands.Count; d++)
        {
            var pick = new[] { cands[a], cands[b], cands[c], cands[d] };
            var minA = pick.Min(p => (double)p.Area);
            var maxA = pick.Max(p => (double)p.Area);
            if (maxA / Math.Max(1.0, minA) > 1.8) continue;      // 四个标记应差不多大

            var quad = OrderQuad(pick.Select(p => (p.Cx, p.Cy)).ToArray());
            if (quad == null) continue;
            var q = quad.Value with { SidePx = pick.Average(p => (p.W + p.H) / 2.0) };
            if (!IsConvex(q)) continue;
            var area = QuadArea(q);
            if (area < imageArea * 0.20 || area > imageArea * 1.05) continue;
            // 打分 = 面积 ÷（1 + 4×长宽比误差）：只看面积容易被"几个大气泡碰巧拼成的四边形"骗到
            // （横版答题卡随手拍时尤其明显），要求长宽比也像某一种纸张才当选。
            var aspectErr = SheetPaper.All.Min(p2 => Error(q, p2));
            // 四个标记应各占一个象限（纸张铺满画面时必然如此）；"几个墨团拼成的四边形"通常挤在一角
            var quadrants = pick.Select(c => (c.Cx < imgW / 2 ? 0 : 1) + (c.Cy < imgH / 2 ? 0 : 2)).Distinct().Count();
            var score = area / (1.0 + 4.0 * aspectErr) * (quadrants == 4 ? 1.0 : 0.35);
            if (score > bestArea) { bestArea = score; best = q; }
        }
        return best;
    }

    /// <summary>把四个点排成 TL → TR → BR → BL（允许卡片旋转，y 轴向下）。</summary>
    internal static Quad? OrderQuad((double X, double Y)[] pts)
    {
        var cx = pts.Average(p => p.X);
        var cy = pts.Average(p => p.Y);
        var ordered = pts.OrderBy(p => Math.Atan2(p.Y - cy, p.X - cx)).ToList();
        // 旋转到以左上角（x+y 最小）开头
        var start = 0;
        var minSum = double.MaxValue;
        for (var i = 0; i < ordered.Count; i++)
        {
            var sum = ordered[i].X + ordered[i].Y;
            if (sum < minSum) { minSum = sum; start = i; }
        }
        // y 轴向下时，按 atan2 升序即为屏幕上顺时针 TL→TR→BR→BL（竖版卡片同样成立，不能按边长猜横竖）
        var rot = Enumerable.Range(0, 4).Select(i => ordered[(start + i) % 4]).ToArray();
        var tl = (rot[0].X, rot[0].Y); var tr = (rot[1].X, rot[1].Y);
        var br = (rot[2].X, rot[2].Y); var bl = (rot[3].X, rot[3].Y);
        if (tl.Item1 >= tr.Item1 || bl.Item1 >= br.Item1) return null;
        if (tl.Item2 >= bl.Item2 || tr.Item2 >= br.Item2) return null;
        return new Quad(tl, tr, br, bl, 0);
    }

    private static bool IsConvex(Quad q)
    {
        var pts = new[] { q.Tl, q.Tr, q.Br, q.Bl };
        var sign = 0;
        for (var i = 0; i < 4; i++)
        {
            var (x0, y0) = pts[i];
            var (x1, y1) = pts[(i + 1) % 4];
            var (x2, y2) = pts[(i + 2) % 4];
            var cross = (x1 - x0) * (y2 - y1) - (y1 - y0) * (x2 - x1);
            var s = Math.Sign(cross);
            if (s == 0) continue;
            if (sign == 0) sign = s;
            else if (s != sign) return false;
        }
        return true;
    }

    private static double QuadArea(Quad q)
    {
        var pts = new[] { q.Tl, q.Tr, q.Br, q.Bl };
        double area = 0;
        for (var i = 0; i < 4; i++)
        {
            var (x0, y0) = pts[i];
            var (x1, y1) = pts[(i + 1) % 4];
            area += x0 * y1 - x1 * y0;
        }
        return Math.Abs(area) / 2;
    }

    /// <summary>由标记间距反推纸张规格（4 个标记中心构成 (W-20)mm x (H-20)mm 的矩形）。</summary>
    internal static (SheetPaper? Paper, double PxPerMm) IdentifyPaper(Quad q, double markerSidePx)
    {
        // 标记边长恒为 6mm —— 用它当比例尺，与"这是哪种纸"无关，
        // 比按长宽比猜纸型可靠（A4/8K/B4/16K 的长宽比都在 0.70 附近，根本分不出来）
        // 二值化 + JPEG 会让黑方块外扩约 1px，直接拿外接框当边长会把比例尺估大约 4%
        // （A4 尚可，8K 260mm 与 B4 250mm 只差 4%，就会认错纸型）
        var sidePx = Math.Max(1.0, markerSidePx - 1.0);
        if (sidePx < 4) return (null, 0);
        var pxPerMm = sidePx / AnswerSheetLayout.MarkerMm;

        var top = Dist(q.Tl, q.Tr);
        var bottom = Dist(q.Bl, q.Br);
        var left = Dist(q.Tl, q.Bl);
        var right = Dist(q.Tr, q.Br);
        var wPx = (top + bottom) / 2;
        var hPx = (left + right) / 2;

        // 四个标记中心距纸边均为 10mm（见 AnswerSheetLayout.MarkerCentersMm）
        var wMm = wPx / pxPerMm + 2 * AnswerSheetLayout.MarkerCenterInsetMm;
        var hMm = hPx / pxPerMm + 2 * AnswerSheetLayout.MarkerCenterInsetMm;

        SheetPaper? best = null;
        var bestErr = double.MaxValue;
        foreach (var p in SheetPaper.All)
        {
            var err = Math.Abs(wMm - p.WidthMm) / p.WidthMm + Math.Abs(hMm - p.HeightMm) / p.HeightMm;
            if (err < bestErr) { bestErr = err; best = p; }
        }
        if (best == null || bestErr > 0.12) return (null, pxPerMm);
        return (best, pxPerMm);
    }

    private static double Dist((double X, double Y) a, (double X, double Y) b)
        => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    // ── 单应变换 ──────────────────────────────────────────────────────

    /// <summary>解 8 自由度单应矩阵（模型 mm 坐标 → 图像像素坐标）。</summary>
    internal static double[]? SolveHomography((double X, double Y)[] model, (double X, double Y)[] img)
    {
        var a = new double[8, 9];
        for (var i = 0; i < 4; i++)
        {
            var (x, y) = model[i];
            var (X, Y) = img[i];
            a[i * 2, 0] = x; a[i * 2, 1] = y; a[i * 2, 2] = 1;
            a[i * 2, 6] = -X * x; a[i * 2, 7] = -X * y; a[i * 2, 8] = X;
            a[i * 2 + 1, 3] = x; a[i * 2 + 1, 4] = y; a[i * 2 + 1, 5] = 1;
            a[i * 2 + 1, 6] = -Y * x; a[i * 2 + 1, 7] = -Y * y; a[i * 2 + 1, 8] = Y;
        }

        // 高斯消元（列主元）
        for (var col = 0; col < 8; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < 8; r++)
                if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col])) pivot = r;
            if (Math.Abs(a[pivot, col]) < 1e-9) return null;
            if (pivot != col)
                for (var c = col; c < 9; c++) (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);

            for (var r = 0; r < 8; r++)
            {
                if (r == col) continue;
                var f = a[r, col] / a[col, col];
                if (f == 0) continue;
                for (var c = col; c < 9; c++) a[r, c] -= f * a[col, c];
            }
        }

        var h = new double[9];
        for (var i = 0; i < 8; i++) h[i] = a[i, 8] / a[i, i];
        h[8] = 1;
        return h;
    }

    /// <summary>把原图按单应矩阵归正成固定 mm/px 的灰度图。</summary>
    internal static Gray Warp(Gray src, double[] h, SheetPaper paper, int ppm)
    {
        var outW = (int)Math.Round(paper.WidthMm * ppm);
        var outH = (int)Math.Round(paper.HeightMm * ppm);
        var outPx = new byte[outW * outH];

        for (var oy = 0; oy < outH; oy++)
        {
            var my = (oy + 0.5) / ppm;
            for (var ox = 0; ox < outW; ox++)
            {
                var mx = (ox + 0.5) / ppm;
                var d = h[6] * mx + h[7] * my + 1.0;
                if (Math.Abs(d) < 1e-9) { outPx[oy * outW + ox] = 255; continue; }
                var sx = (h[0] * mx + h[1] * my + h[2]) / d;
                var sy = (h[3] * mx + h[4] * my + h[5]) / d;
                outPx[oy * outW + ox] = Bilinear(src, sx, sy);
            }
        }
        return new Gray(outW, outH, outPx);
    }

    private static byte Bilinear(Gray g, double x, double y)
    {
        if (x < 0 || y < 0 || x > g.W - 1 || y > g.H - 1) return 255;
        var x0 = (int)x; var y0 = (int)y;
        var x1 = Math.Min(x0 + 1, g.W - 1); var y1 = Math.Min(y0 + 1, g.H - 1);
        var fx = x - x0; var fy = y - y0;
        var v = g.Px[y0 * g.W + x0] * (1 - fx) * (1 - fy)
              + g.Px[y0 * g.W + x1] * fx * (1 - fy)
              + g.Px[y1 * g.W + x0] * (1 - fx) * fy
              + g.Px[y1 * g.W + x1] * fx * fy;
        return (byte)Math.Clamp(v, 0, 255);
    }

    // ── 采样与判定 ────────────────────────────────────────────────────

    /// <summary>气泡内区墨量（1=全黑，0=纯白）。</summary>
    internal static double InnerInk(Gray img, BubbleMark b, int ppm, double white, double inset = 0.30)
    {
        var x0 = b.Xmm + b.Wmm * inset; var y0 = b.Ymm + b.Hmm * inset;
        var x1 = b.Xmm + b.Wmm * (1 - inset); var y1 = b.Ymm + b.Hmm * (1 - inset);
        var mean = MeanRegion(img, x0, y0, x1, y1, ppm);
        return Math.Clamp(1.0 - mean / white, 0, 1);
    }

    /// <summary>气泡外框是否存在（用于判断当前页布局是否匹配照片）。</summary>
    private static bool RingPresent(Gray img, BubbleMark b, int ppm, bool search = false)
    {
        // 沿边框取 4 条薄带，平均灰度越暗说明外框在
        const double t = 0.45;    // 带宽 mm
        double Top(double dy) => MeanRegion(img, b.Xmm, b.Ymm + dy, b.Xmm + b.Wmm, b.Ymm + dy + t, ppm);
        double Bottom(double dy) => MeanRegion(img, b.Xmm, b.Ymm + b.Hmm - t - dy, b.Xmm + b.Wmm, b.Ymm + b.Hmm - dy, ppm);
        double Left(double dx) => MeanRegion(img, b.Xmm + dx, b.Ymm, b.Xmm + dx + t, b.Ymm + b.Hmm, ppm);
        double Right(double dx) => MeanRegion(img, b.Xmm + b.Wmm - t - dx, b.Ymm, b.Xmm + b.Wmm - dx, b.Ymm + b.Hmm, ppm);

        if (!search)
        {
            var ring = (Top(0) + Bottom(0) + Left(0) + Right(0)) / 4;
            return 1.0 - ring / 235.0 > RingInkThreshold;
        }

        // 大外框（主观题作答框）：模型与实印可能有亚毫米级偏差，固定位置的带会扫空；
        // 在 ±1.2mm 内给每条边找最暗的带，四条边都各自找到暗带才算这一页匹配
        var thr = 235.0 * (1.0 - RingInkThreshold);
        return MinBand(Top) < thr && MinBand(Bottom) < thr && MinBand(Left) < thr && MinBand(Right) < thr;
    }

    /// <summary>在 ±1.2mm 范围内以 0.3mm 步进平移采样带，返回最暗（最小）的带均值。</summary>
    private static double MinBand(Func<double, double> band)
    {
        var best = double.MaxValue;
        for (var d = -1.2; d <= 1.21; d += 0.3)
        {
            var v = band(d);
            if (v < best) best = v;
        }
        return best;
    }

    internal static double RingMatchScore(Gray img, SheetPageLayout page, int ppm, double white)
    {
        // 页面指纹 = 客观题气泡外框 + 主观题作答框外框（纯主观页没有气泡，靠作答框识别页码）
        var total = page.Options.Count + page.Frames.Count;
        if (total == 0) return 0;
        var present = page.Options.Count(o => RingPresent(img, o.Bubble, ppm))
                    + page.Frames.Count(f => RingPresent(img, f.Box, ppm, search: true));
        return present / (double)total;
    }

    internal static double MeanRegion(Gray img, double x0mm, double y0mm, double x1mm, double y1mm, int ppm)
    {
        var x0 = Math.Clamp((int)Math.Round(x0mm * ppm), 0, img.W - 1);
        var x1 = Math.Clamp((int)Math.Round(x1mm * ppm), x0 + 1, img.W);
        var y0 = Math.Clamp((int)Math.Round(y0mm * ppm), 0, img.H - 1);
        var y1 = Math.Clamp((int)Math.Round(y1mm * ppm), y0 + 1, img.H);
        long sum = 0; var n = 0;
        for (var y = y0; y < y1; y++)
        for (var x = x0; x < x1; x++)
        {
            sum += img.Px[y * img.W + x];
            n++;
        }
        return n == 0 ? 255 : sum / (double)n;
    }
}
