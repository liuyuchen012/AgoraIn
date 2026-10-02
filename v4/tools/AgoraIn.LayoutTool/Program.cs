using System.Text.Json;
using AgoraIn.Core.Entities;
using AgoraIn.Server.Services;

// 答题卡版面校验：导出渲染 HTML 与版面模型 JSON，交给 tools/verify_layout.py 在浏览器里实测比对。
//
//   dotnet run --project tools/AgoraIn.LayoutTool -- export <A4|16K|B4|8K|A3> [题目.json] [输出目录] [覆盖.json]
//
// 题目.json 可选（[{index,type,options,score}]，生产试卷导出的格式）；缺省用 24 客观 + 6 主观的合成题。
// 覆盖.json 可选（可视化编辑器的拖动结果：[{questionId,pageNo,xMm,yMm,wMm,hMm}]），用来验证"拖过之后仍然对得上"。
// 校验流程见 docs/answer-sheet-spec.md 第 1 节：模型与 DOM 逐气泡偏差应 < 0.3mm。
//   python tools/verify_layout.py <输出目录>/sheet.html <输出目录>/layout.json
var args0 = args.Length > 0 ? args[0] : "export";
if (args0 != "export")
{
    Console.WriteLine("用法: export <A4|16K|B4|8K|A3> [题目.json] [输出目录] [覆盖.json]");
    return 1;
}

var paperName = args.Length > 1 ? args[1] : "8K";
var questionsFile = args.Length > 2 ? args[2] : null;
var outDir = args.Length > 3 ? args[3] : Path.Combine(Path.GetTempPath(), "agorain-layout-check");
var placementsFile = args.Length > 4 ? args[4] : null;
Directory.CreateDirectory(outDir);

var paper = SheetPaper.FromName(paperName);
if (paper.Name != paperName)
    Console.WriteLine($"⚠ 未知纸型 {paperName}，回退 {paper.Name}（可选：{string.Join("/", SheetPaper.All.Select(p => p.Name))}）");

// 与生产渲染同一条路径：通用答题卡 = 涂卡考号区 + 注意事项
var opt = new AnswerSheetOptions { Paper = paper, IdArea = IdAreaKind.Bubble, ShowNotes = true };

List<Question> questions;
Dictionary<string, List<string>> options;
if (questionsFile != null && File.Exists(questionsFile))
{
    questions = [];
    options = [];
    foreach (var el in JsonDocument.Parse(File.ReadAllText(questionsFile)).RootElement.EnumerateArray())
    {
        var id = "Q" + el.GetProperty("index").GetInt32();
        questions.Add(new Question
        {
            Id = id,
            PaperId = "layout-check",
            Index = el.GetProperty("index").GetInt32(),
            Type = (QuestionType)el.GetProperty("type").GetInt32(),
            Score = el.TryGetProperty("score", out var sc) ? sc.GetDouble() : 2,
        });
        var keys = new List<string>();
        if (el.TryGetProperty("options", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var o in arr.EnumerateArray()) keys.Add(o.GetString() ?? "");
        if (keys.Count > 0) options[id] = keys;
    }
}
else
{
    questions = [];
    options = [];
    for (var i = 0; i < 24; i++)                       // 客观题：单选/判断/多选 混合
    {
        var type = i % 7 == 6 ? QuestionType.Judge : i % 5 == 4 ? QuestionType.MultipleChoice : QuestionType.SingleChoice;
        var id = "Q" + i;
        questions.Add(new Question { Id = id, PaperId = "layout-check", Index = i, Type = type, Score = 2 });
        if (type != QuestionType.Judge) options[id] = ["A", "B", "C", "D"];
    }
    var subTypes = new[] { QuestionType.Blank, QuestionType.ShortAnswer, QuestionType.Essay };
    for (var i = 0; i < 6; i++)
        questions.Add(new Question
        {
            Id = "S" + i, PaperId = "layout-check", Index = 24 + i,
            Type = subTypes[i % subTypes.Length], Score = i % 3 == 0 ? 8 : 3,
        });
}

var sheet = new ExamPaper { Id = "layout-check", Title = "版面校验卷", Subject = "数学", TotalScore = 150 };

// 可视化编辑器的覆盖：拖动/缩放过的题按这份坐标绝对定位
var placements = new Dictionary<string, QuestionPlacement>();
if (placementsFile != null && File.Exists(placementsFile))
{
    foreach (var el in JsonDocument.Parse(File.ReadAllText(placementsFile)).RootElement.EnumerateArray())
    {
        var pl = new QuestionPlacement(
            el.GetProperty("questionId").GetString() ?? "",
            el.TryGetProperty("pageNo", out var pn) ? pn.GetInt32() : 1,
            el.TryGetProperty("xMm", out var xm) ? xm.GetDouble() : 0,
            el.TryGetProperty("yMm", out var ym) ? ym.GetDouble() : 0,
            el.GetProperty("wMm").GetDouble(),
            el.GetProperty("hMm").GetDouble(),
            el.TryGetProperty("sizeOnly", out var so) && so.GetBoolean());
        placements[pl.QuestionId] = pl;
    }
    Console.WriteLine($"覆盖项 {placements.Count} 条：{string.Join(", ", placements.Keys)}");
}

var html = AnswerSheetRenderer.Render(sheet, questions, options, sheetOptions: opt, placements: placements);
var pages = AnswerSheetLayout.Compute(questions, options, opt, placements);

var htmlPath = Path.Combine(outDir, "sheet.html");
var jsonPath = Path.Combine(outDir, "layout.json");
File.WriteAllText(htmlPath, html);
File.WriteAllText(jsonPath, JsonSerializer.Serialize(pages, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
}));

Console.WriteLine($"纸型 {paper.Name} {paper.WidthMm}×{paper.HeightMm}mm  栏数 {opt.Columns}  块宽 {opt.BlockWidthMm:0.#}mm  题 {questions.Count}");
Console.WriteLine($"页数 {pages.Count}  客观题气泡 {pages.Sum(p => p.Options.Count)}  考号气泡 {pages.Sum(p => p.IdDigits.Count)}  作答框 {pages.Sum(p => p.Frames.Count)}");
Console.WriteLine($"已写出：{htmlPath}");
Console.WriteLine($"        {jsonPath}");
Console.WriteLine($"下一步：python tools/verify_layout.py \"{htmlPath}\" \"{jsonPath}\"");
return 0;
