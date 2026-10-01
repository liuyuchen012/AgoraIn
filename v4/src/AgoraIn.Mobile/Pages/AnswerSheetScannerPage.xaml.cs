using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;

namespace AgoraIn.Mobile.Pages;

public partial class AnswerSheetScannerPage : ContentPage
{
    private readonly Services.ApiClient _api;
    private byte[]? _imageBytes;
    private string? _selectedPaperId;
    private bool _loading;

    // ── 多页答题卡连续扫描状态 ──
    // 扫到第 1 页后服务端返回 submissionId；后续页带上它归并到同一份答卷。
    // 扫到下一份的第 1 页（考号不同）时服务端会自动开新份，这里的状态只是跟随更新。
    private string? _chainSubmissionId;
    private int _chainPage = 1;
    private int _chainTotal = 1;

    public AnswerSheetScannerPage() : this(App.Api) { }

    public AnswerSheetScannerPage(Services.ApiClient api)
    {
        InitializeComponent();
        _api = api;
        BindingContext = this;
    }

    /// <summary>必须用 ObservableCollection：试卷是进页面后异步取回的，
    /// 普通 List 不触发集合变更通知，Picker 绑定后不会再刷新（表现为下拉框一直空白）。</summary>
    public ObservableCollection<PaperOption> Papers { get; } = new();
    public bool ShowPlaceholder => _imageBytes == null;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadPapersAsync();
    }

    private async Task LoadPapersAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var papers = await _api.GetAsync<List<PaperInfo>>("/api/v4/exams/papers");
            Papers.Clear();
            if (papers == null)
            {
                // 4xx/5xx 都被 GetAsync 吞成 null（无权限 403 / 未登录 401 / 网络异常）
                StatusLabel.Text = "加载试卷列表失败：请确认已登录，且账号有「试卷管理」权限";
                return;
            }
            foreach (var p in papers)
                Papers.Add(new PaperOption { Id = p.Id, Title = p.Title });
            StatusLabel.Text = Papers.Count > 0
                ? $"请选择试卷后拍照（共 {Papers.Count} 份）"
                : "账号下暂无试卷：请先在电脑端「试卷管理」创建试卷";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"加载试卷列表失败：{ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnPaperSelected(object? sender, EventArgs e)
    {
        if (PaperPicker.SelectedIndex >= 0)
            _selectedPaperId = Papers[PaperPicker.SelectedIndex].Id;
    }

    private async void OnTakePhoto(object? sender, EventArgs e)
    {
        if (!MediaPicker.Default.IsCaptureSupported)
        {
            await DisplayAlertAsync("提示", "当前设备不支持拍照", "确定");
            return;
        }

        try
        {
            // 先要权限再拍照：相机权限是必需的；存储写入权限只有 Android 13 以下才需要
            //（MediaPicker.CapturePhotoAsync 内部会校验，缺清单声明会直接抛英文异常）
            if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted)
            {
                StatusLabel.Text = "未授予相机权限，无法拍照";
                return;
            }
            if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                var storage = await Permissions.RequestAsync<Permissions.StorageWrite>();
                if (storage != PermissionStatus.Granted)
                {
                    StatusLabel.Text = "未授予存储权限，无法保存照片（Android 13 以下必需）";
                    return;
                }
            }

            StatusLabel.Text = "正在打开相机…";
            var photo = await MediaPicker.Default.CapturePhotoAsync();
            if (photo != null) await LoadImage(photo);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"拍照失败：{ex.Message}";
        }
    }

    private async void OnPickPhoto(object? sender, EventArgs e)
    {
        try
        {
            var photo = await MediaPicker.Default.PickPhotoAsync();
            if (photo != null) await LoadImage(photo);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"选择图片失败：{ex.Message}";
        }
    }

    private async Task LoadImage(FileResult photo)
    {
        await using var stream = await photo.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var raw = ms.ToArray();
        // 手机原图常 15-30MB，移动网络上传易超时/中断；识别也不需要原分辨率，长边压到 1600px
        _imageBytes = await CompressAsync(raw);

        PreviewImage.Source = ImageSource.FromStream(() => new MemoryStream(_imageBytes));
        OnPropertyChanged(nameof(ShowPlaceholder));

        StatusLabel.Text = $"已选择：{photo.FileName}";
        ResultLabel.Text = _imageBytes.Length < raw.Length
            ? $"图片大小：{raw.Length / 1024} KB → {_imageBytes.Length / 1024} KB（已压缩）"
            : $"图片大小：{_imageBytes.Length / 1024} KB";
        UploadButton.IsEnabled = true;
    }

    /// <summary>长边压缩到 <paramref name="maxEdge"/> 像素的 JPEG；失败时原样返回，不阻断上传。</summary>
    private static async Task<byte[]> CompressAsync(byte[] raw, int maxEdge = 1600, float quality = 0.82f)
    {
        try
        {
            using var input = new MemoryStream(raw);
            using var image = PlatformImage.FromStream(input);
            if (image == null) return raw;
            if (Math.Max(image.Width, image.Height) <= maxEdge) return raw;

            using var resized = image.Downsize(maxEdge);
            using var output = new MemoryStream();
            await resized.SaveAsync(output, ImageFormat.Jpeg, quality);
            var bytes = output.ToArray();
            return bytes.Length > 0 && bytes.Length < raw.Length ? bytes : raw;
        }
        catch
        {
            return raw;
        }
    }

    private async void OnUpload(object? sender, EventArgs e)
    {
        if (_imageBytes == null || _selectedPaperId == null)
        {
            await DisplayAlertAsync("提示", "请先选择试卷并拍照/选择图片", "确定");
            return;
        }

        UploadButton.IsEnabled = false;
        StatusLabel.Text = "正在上传识别…";
        ResultLabel.Text = "";

        try
        {
            using var content = new MultipartFormDataContent();
            var imageContent = new ByteArrayContent(_imageBytes);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            content.Add(imageContent, "file", "answersheet.jpg");

            var chain = _chainSubmissionId != null ? $"&submissionId={_chainSubmissionId}" : "";
            var result = await _api.PostAsync<SubmissionResult>(
                $"/api/v4/exams/submissions?paperId={_selectedPaperId}{chain}", content);

            if (result != null && !string.IsNullOrEmpty(result.Warning) && result.SubmissionId == null)
            {
                // 服务端无法确定该页归属（如未扫第 1 页就先扫了第 2 页）
                StatusLabel.Text = "⚠️ 该页没有并入任何答卷";
                ResultLabel.Text = result.Warning;
                _chainSubmissionId = null;
                NewSheetButton.IsVisible = false;
                return;
            }

            if (result != null)
            {
                // 更新连续扫描状态：第 1 页起链，后续页跟随；换考生时服务端自动开新份
                if (!string.IsNullOrEmpty(result.SubmissionId))
                {
                    _chainSubmissionId = result.SubmissionId;
                    _chainPage = Math.Max(1, result.PageNo);
                    _chainTotal = Math.Max(_chainPage, result.TotalPages);
                    NewSheetButton.IsVisible = _chainTotal > 1;
                }

                var pageMark = _chainTotal > 1 ? $"第 {_chainPage}/{_chainTotal} 页 " : "";
                StatusLabel.Text = result.Status switch
                {
                    "AiGraded" => $"✅ {pageMark}识别并自动判分完成",
                    "NeedsHuman" => $"⚠️ {pageMark}已识别，待人工复核",
                    _ => $"✅ {pageMark}识别完成",
                };
                var lines = new List<string>();
                if (!string.IsNullOrEmpty(result.Warning))
                    lines.Add($"⚠️ {result.Warning}");
                if (!string.IsNullOrEmpty(result.RecognizedStudent))
                    lines.Add($"识别考号：{result.RecognizedStudent}");
                if (result.Answers is { Count: > 0 })
                    lines.Add($"识别答案：{string.Join(", ", result.Answers)}");
                else
                    lines.Add("未识别到涂卡答案，请在电脑端手动批改");
                if (result.Confidence.HasValue)
                    lines.Add($"置信度：{result.Confidence.Value:P0}");
                ResultLabel.Text = string.Join("\n", lines);
            }
            else
            {
                StatusLabel.Text = "⚠️ 识别结果为空";
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"上传失败：{ex.Message}";
        }
        finally
        {
            UploadButton.IsEnabled = true;
        }
    }

    private void OnNewSheet(object? sender, EventArgs e)
    {
        _chainSubmissionId = null;
        _chainPage = 1;
        _chainTotal = 1;
        NewSheetButton.IsVisible = false;
        _imageBytes = null;
        PreviewImage.Source = null;
        OnPropertyChanged(nameof(ShowPlaceholder));
        UploadButton.IsEnabled = false;
        StatusLabel.Text = "已开始新的一份，请扫第 1 页";
        ResultLabel.Text = "";
    }

    public class PaperOption
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
    }

    private class PaperInfo
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
    }

    private class SubmissionResult
    {
        public string? SubmissionId { get; set; }
        public string? Status { get; set; }
        public string? RecognizedStudent { get; set; }
        public List<string>? Answers { get; set; }
        public double? Confidence { get; set; }
        /// <summary>识别未返回结果时的原因（如上游 AI 欠费 402）。</summary>
        public string? Warning { get; set; }
        /// <summary>本页页码 / 该答卷总页数（多页答题卡归并用）。</summary>
        public int PageNo { get; set; }
        public int TotalPages { get; set; }
        /// <summary>是否新建了答卷（false = 并入已有答卷）。</summary>
        public bool IsNew { get; set; }
    }
}
