using CheckIn.Client.Mobile.Services;
using CheckIn.Client.Mobile.ViewModels;

namespace CheckIn.Client.Mobile.Pages;

public partial class SchedulePage : ContentPage
{
    private readonly ScheduleViewModel _vm;

    public SchedulePage(ApiService api)
    {
        InitializeComponent();
        _vm = new ScheduleViewModel(api);
        BindingContext = _vm;

        // 注册 PickDate 命令（XAML 中通过 CommandParameter 传递日期字符串）
        _vm.PickDateCommand = new Command<string>(dateStr => _vm.PickDate(dateStr));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_vm.Devices.Count == 0)
            await _vm.LoadDevicesAsync();
    }
}