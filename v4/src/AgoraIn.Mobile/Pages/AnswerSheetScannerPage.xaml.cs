using System.Net.Http.Headers;

namespace AgoraIn.Mobile.Pages;

public partial class AnswerSheetScannerPage : ContentPage
{
    private readonly Services.ApiClient _api;
    private byte[]? _imageBytes;
    private string? _selectedPaperId;

    public AnswerSheetScannerPage(Services.ApiClient api)
    {
        InitializeComponent();
        _api = api;
        BindingContext = this;
        LoadPapers();
    }

    public List<PaperOption> Papers { get; } = new();
    public bool ShowPlaceholder => _imageBytes == null;

    private async void LoadPapers()
    {
        try
        {
            var papers = await _api.GetAsync<List<PaperInfo>>("/api/v4/exams/papers");
            Papers.Clear();
            foreach (var p in papers ?? new())
                Papers.Add(new PaperOption { Id = p.Id, Title = p.Title });
        }
        catch
        {
            StatusLabel.Text = "加载试卷列表失败";
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
            await DisplayAlert("提示", "当前设备不支持拍照", "确定");
            return;
        }

        try
        {
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
        _imageBytes = ms.ToArray();

        PreviewImage.Source = ImageSource.FromStream(() => new MemoryStream(_imageBytes));
        OnPropertyChanged(nameof(ShowPlaceholder));

        StatusLabel.Text = $"已选择：{photo.FileName}";
        ResultLabel.Text = $"图片大小：{_imageBytes.Length / 1024} KB";
        UploadButton.IsEnabled = true;
    }

    private async void OnUpload(object? sender, EventArgs e)
    {
        if (_imageBytes == null || _selectedPaperId == null)
        {
            await DisplayAlert("提示", "请先选择试卷并拍照/选择图片", "确定");
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

            var result = await _api.PostAsync<SubmissionResult>(
                $"/api/v4/exams/submissions?paperId={_selectedPaperId}", content);

            if (result != null)
            {
                StatusLabel.Text = "✅ 识别完成";
                var lines = new List<string>();
                if (!string.IsNullOrEmpty(result.RecognizedStudent))
                    lines.Add($"识别学生：{result.RecognizedStudent}");
                if (result.Answers != null)
                    lines.Add($"识别答案：{string.Join(", ", result.Answers)}");
                if (result.Confidence.HasValue)
                    lines.Add($"置信度：{result.Confidence:P0}");
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
        public string? RecognizedStudent { get; set; }
        public List<string>? Answers { get; set; }
        public double? Confidence { get; set; }
    }
}
