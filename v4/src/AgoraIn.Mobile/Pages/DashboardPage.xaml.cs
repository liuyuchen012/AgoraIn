namespace AgoraIn.Mobile.Pages;

public partial class DashboardPage : ContentPage
{
    public DashboardPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        Busy.IsRunning = true;
        Busy.IsVisible = true;
        try
        {
            var devices = await App.Api.GetDashboardAsync();
            if (devices != null)
            {
                DeviceCountLabel.Text = devices.TotalDevices.ToString();
                OnlineCountLabel.Text = devices.OnlineDevices.ToString();
            }
        }
        catch
        {
            DeviceCountLabel.Text = "—";
            OnlineCountLabel.Text = "—";
        }
        finally
        {
            Busy.IsRunning = false;
            Busy.IsVisible = false;
        }
    }
}
