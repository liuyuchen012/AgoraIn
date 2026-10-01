using System.Collections.ObjectModel;
using AgoraIn.Mobile.Services;

namespace AgoraIn.Mobile.Pages;

/// <summary>
/// 教师阅卷与出分：选试卷 → 选提交 → 逐题人工改分（客观题 AI 未启用时也可手判）→ 确认出分。
/// </summary>
public partial class GradingPage : ContentPage
{
    private enum Step { Papers, Submissions, Results }

    private Step _step = Step.Papers;
    private ExamPaperItem? _paper;
    private SubmissionItem? _submission;
    private readonly ObservableCollection<ExamPaperItem> _papers = new();
    private readonly ObservableCollection<SubmissionItem> _submissions = new();
    private readonly List<QuestionResultItem> _results = new();

    public GradingPage()
    {
        InitializeComponent();
        PapersView.Children.Add(new CollectionView
        {
            ItemsSource = _papers,
            ItemTemplate = MakePaperTemplate(),
            Margin = new Thickness(0, 4, 0, 0),
        });
        _papers.CollectionChanged += (_, e) =>
            PapersEmpty.IsVisible = PapersBusy.IsVisible == false && _papers.Count == 0;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!App.Api.IsLoggedIn)
        {
            AppShell.SwitchToLogin();
            return;
        }
        await ShowPapersAsync();
    }

    // ═══ 步骤 1：试卷列表 ═══

    private async Task ShowPapersAsync()
    {
        _step = Step.Papers;
        CrumbLabel.Text = "① 选择试卷";
        BackButton.IsVisible = false;
        PapersView.IsVisible = true;
        _papers.Clear();
        PapersBusy.IsVisible = PapersBusy.IsRunning = true;
        try
        {
            var list = await App.Api.GetPapersAsync();
            if (list != null)
                foreach (var p in list)
                    _papers.Add(p);
        }
        catch { }
        finally { PapersBusy.IsVisible = PapersBusy.IsRunning = false; }
    }

    private DataTemplate MakePaperTemplate() => new(() =>
    {
        var border = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            Padding = 16,
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (border.BindingContext is ExamPaperItem p) await ShowSubmissionsAsync(p);
        };
        border.GestureRecognizers.Add(tap);
        border.SetBinding(BindingContextProperty, ".");

        var stack = new VerticalStackLayout { Spacing = 4 };
        var title = new Label { FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#202124") };
        title.SetBinding(Label.TextProperty, "Title");
        var meta = new Label { FontSize = 12, TextColor = Colors.Gray };
        meta.SetBinding(Label.TextProperty, new Binding(".", converter: PaperMetaConverter.Instance));
        stack.Children.Add(title);
        stack.Children.Add(meta);
        border.Content = stack;
        return border;
    });

    private sealed class PaperMetaConverter : IValueConverter
    {
        public static readonly PaperMetaConverter Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => value is ExamPaperItem p
                ? $"{p.Subject ?? "试卷"} · {p.QuestionCount} 题 · 总分 {p.TotalScore:0.#} · 已扫卡 {p.SubmissionCount} 份"
                : "";
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => null;
    }

    // ═══ 步骤 2：提交列表 ═══

    private async Task ShowSubmissionsAsync(ExamPaperItem paper)
    {
        _paper = paper;
        _step = Step.Submissions;
        CrumbLabel.Text = $"② {paper.Title} — 选择提交";
        BackButton.IsVisible = true;
        PapersBusy.IsVisible = PapersBusy.IsRunning = true;
        _papers.Clear();
        PapersBusy.IsVisible = PapersBusy.IsRunning = false;

        var subs = await App.Api.GetSubmissionsAsync(paper.Id) ?? new List<SubmissionItem>();
        var header = new Label
        {
            Text = subs.Count == 0 ? "该试卷暂无扫卡记录" : "点按提交记录查看逐题详情",
            TextColor = Colors.Gray,
            FontSize = 12,
        };
        PapersView.Children.Insert(0, header);

        var list = new CollectionView
        {
            ItemsSource = subs,
            Margin = new Thickness(0, 4, 0, 0),
        };
        list.ItemTemplate = new DataTemplate(() =>
        {
            var border = new Border
            {
                BackgroundColor = Colors.White,
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
                Padding = 14,
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                if (border.BindingContext is SubmissionItem s) await ShowResultsAsync(s);
            };
            border.GestureRecognizers.Add(tap);
            border.SetBinding(BindingContextProperty, ".");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new Label { FontSize = 15 };
            name.SetBinding(Label.TextProperty, new Binding(".", converter: SubmissionNameConverter.Instance));
            var status = new Label { FontSize = 12 };
            status.SetBinding(Label.TextProperty, new Binding(".", converter: SubmissionStatusConverter.Instance));
            grid.Children.Add(name);
            grid.Children.Add(status);
            Grid.SetColumn(status, 1);
            border.Content = grid;
            return border;
        });
        PapersView.Children.Add(list);
    }

    private sealed class SubmissionNameConverter : IValueConverter
    {
        public static readonly SubmissionNameConverter Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => value is SubmissionItem s
                ? (string.IsNullOrEmpty(s.StudentName) ? $"考号 {s.StudentRef ?? "未识别"}" : s.StudentName)
                : "";
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => null;
    }

    private sealed class SubmissionStatusConverter : IValueConverter
    {
        private static readonly string[] Names = ["未批", "AI已批", "待人工", "已确认"];
        public static readonly SubmissionStatusConverter Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is not SubmissionItem s) return "";
            var score = s.TotalScore is { } t ? $" · {t:0.#} 分" : "";
            return $"{(s.Status >= 0 && s.Status < Names.Length ? Names[s.Status] : "?")}{score}";
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => null;
    }

    // ═══ 步骤 3：逐题改分 ═══

    private async Task ShowResultsAsync(SubmissionItem submission)
    {
        _submission = submission;
        _step = Step.Results;
        CrumbLabel.Text = $"③ {(_submission.StudentName ?? "未识别考生")} — 逐题阅卷";
        PapersView.IsVisible = false;

        var results = await App.Api.GetSubmissionResultsAsync(submission.Id) ?? new List<QuestionResultItem>();
        _results.Clear();
        _results.AddRange(results);

        var page = new ContentPage { Title = "逐题阅卷" };
        var layout = new VerticalStackLayout { Spacing = 10, Padding = 16 };

        // 确认出分按钮
        var confirm = new Button
        {
            Text = $"✔ 确认出分（{results.Where(r => r.Score != null).Sum(r => r.Score ?? 0):0.#} 分）",
            BackgroundColor = Color.FromArgb("#34a853"),
            TextColor = Colors.White,
            CornerRadius = 10,
            HeightRequest = 48,
        };
        confirm.Clicked += async (_, _) => await ConfirmAsync();
        layout.Children.Add(confirm);

        // 答题卡原图：人工复盘要对着学生原卷改分（点开可全屏放大查看）
        var sheetCard = await BuildSheetImageCardAsync(submission);
        if (sheetCard != null) layout.Children.Add(sheetCard);

        foreach (var r in _results)
        {
            layout.Children.Add(BuildQuestionCard(r));
        }

        page.Content = new ScrollView { Content = layout };
        await Navigation.PushAsync(page);
    }

    /// <summary>答题卡扫描原图卡片（没有原图时返回 null，界面不出现空块）。</summary>
    private async Task<View?> BuildSheetImageCardAsync(SubmissionItem submission)
    {
        var source = await App.Api.GetSubmissionImageAsync(submission.Id);
        if (source == null) return null;

        var border = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            Padding = 12,
        };
        var stack = new VerticalStackLayout { Spacing = 8 };

        stack.Children.Add(new Label
        {
            Text = "答题卡原图（点图放大）",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#4285f4"),
        });

        var image = new Image
        {
            Source = source,
            Aspect = Aspect.AspectFit,
            HeightRequest = 320,
            BackgroundColor = Color.FromArgb("#f0f2f5"),
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await ShowFullImageAsync(source, submission.StudentName ?? "答题卡");
        image.GestureRecognizers.Add(tap);
        stack.Children.Add(image);

        border.Content = stack;
        return border;
    }

    /// <summary>全屏查看原图：双指缩放 + 旋转 + 放大缩小按钮（课堂改卷常要看小字）。</summary>
    private async Task ShowFullImageAsync(ImageSource source, string title)
    {
        var image = new Image
        {
            Source = source,
            Aspect = Aspect.AspectFit,
            BackgroundColor = Colors.Black,
        };
        var scale = 1.0;
        var rotation = 0.0;

        void Apply()
        {
            image.Scale = scale;
            image.Rotation = rotation;
        }

        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += (_, e) =>
        {
            if (e.Status == GestureStatus.Running)
            {
                scale = Math.Clamp(e.Scale, 0.5, 6.0);
                Apply();
            }
        };
        image.GestureRecognizers.Add(pinch);
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            if (e.StatusType == GestureStatus.Running)
            {
                image.TranslationX += e.TotalX * 0.2;
                image.TranslationY += e.TotalY * 0.2;
            }
        };
        image.GestureRecognizers.Add(pan);

        var buttons = new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            Padding = new Thickness(0, 8),
        };
        Button MakeButton(string text, Action action)
        {
            var b = new Button { Text = text, FontSize = 14, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#4285f4"), CornerRadius = 8 };
            b.Clicked += (_, _) => action();
            return b;
        }
        buttons.Children.Add(MakeButton("－", () => { scale = Math.Max(0.5, scale - 0.25); Apply(); }));
        buttons.Children.Add(MakeButton("＋", () => { scale = Math.Min(6.0, scale + 0.25); Apply(); }));
        buttons.Children.Add(MakeButton("旋转", () => { rotation = (rotation + 90) % 360; Apply(); }));
        buttons.Children.Add(MakeButton("复位", () => { scale = 1; rotation = 0; image.TranslationX = 0; image.TranslationY = 0; Apply(); }));

        var page = new ContentPage
        {
            Title = title,
            BackgroundColor = Colors.Black,
            Content = new Grid
            {
                RowDefinitions = new RowDefinitionCollection(new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)),
                Children = { image, buttons },
            },
        };
        Grid.SetRow(image, 0);
        Grid.SetRow(buttons, 1);
        await Navigation.PushAsync(page);
    }

    private View BuildQuestionCard(QuestionResultItem r)
    {
        var border = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            Padding = 14,
        };
        var stack = new VerticalStackLayout { Spacing = 6 };

        var head = new Label
        {
            Text = $"第 {r.Index + 1} 题 · 满分 {r.FullScore:0.#}" +
                   (r.Source is { Length: > 0 } && r.Source != "None" ? $" · {(r.Source == "Teacher" ? "教师已改" : "AI 建议")}" : ""),
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#4285f4"),
        };
        stack.Children.Add(head);

        var content = new Label
        {
            Text = r.Content ?? "（无题干）",
            FontSize = 14,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        stack.Children.Add(content);

        var detail = new Label
        {
            Text = $"标准答案：{r.StandardAnswer ?? "—"}    识别作答：{r.RecognizedAnswer ?? "—"}",
            FontSize = 12,
            TextColor = Colors.Gray,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        stack.Children.Add(detail);

        var row = new HorizontalStackLayout { Spacing = 8 };
        var entry = new Entry
        {
            Keyboard = Keyboard.Numeric,
            WidthRequest = 90,
            Placeholder = "得分",
        };
        if (r.Score != null) entry.Text = r.Score.Value.ToString("0.#");

        var save = new Button
        {
            Text = "保存改分",
            BackgroundColor = Color.FromArgb("#4285f4"),
            TextColor = Colors.White,
            CornerRadius = 8,
            HeightRequest = 40,
        };
        save.Clicked += async (_, _) =>
        {
            if (!double.TryParse(entry.Text, out var score))
            {
                await DisplayAlertAsync("提示", "请输入有效分数", "知道了");
                return;
            }
            if (score < 0 || score > r.FullScore)
            {
                await DisplayAlertAsync("提示", $"分数须在 0 ~ {r.FullScore:0.#} 之间", "知道了");
                return;
            }
            try
            {
                await App.Api.OverrideResultAsync(_submission!.Id, r.QuestionId, score, null);
                await DisplayAlertAsync("已保存", $"第 {r.Index + 1} 题改为 {score:0.#} 分", "知道了");
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("保存失败", ex.Message, "知道了");
            }
        };
        row.Children.Add(entry);
        row.Children.Add(save);
        stack.Children.Add(row);

        border.Content = stack;
        return border;
    }

    private async Task ConfirmAsync()
    {
        if (_submission == null) return;
        try
        {
            var res = await App.Api.ConfirmSubmissionAsync(_submission.Id);
            await DisplayAlertAsync("已确认出分", $"总分：{res?.TotalScore:0.#} 分（已计入成绩统计）", "知道了");
            await Navigation.PopAsync();
            await ShowSubmissionsAsync(_paper!);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("确认失败", ex.Message, "知道了");
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        switch (_step)
        {
            case Step.Submissions:
                await ShowPapersAsync();
                break;
            case Step.Results:
                await Navigation.PopAsync();
                if (_paper != null) await ShowSubmissionsAsync(_paper);
                break;
            default:
                await Shell.Current.GoToAsync("..");
                break;
        }
    }
}
