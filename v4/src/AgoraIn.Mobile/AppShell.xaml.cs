namespace AgoraIn.Mobile;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// 注册非 TabBar 页面路由（GoToAsync 目标必须先注册，否则 ArgumentException 闪退）
		Routing.RegisterRoute("tasks", typeof(Pages.TasksPage));
		Routing.RegisterRoute("dashboard", typeof(Pages.DashboardPage));
		Routing.RegisterRoute("scanner", typeof(Pages.AnswerSheetScannerPage));

		// 根据登录状态决定初始页面
		if (App.Api.IsLoggedIn)
		{
			LoginTab.IsVisible = false;
			MainTab.IsVisible = true;
			CurrentItem = MainTab.Items[0];
		}
		else
		{
			LoginTab.IsVisible = true;
			MainTab.IsVisible = false;
			CurrentItem = LoginTab;
		}
	}

	/// <summary>登录成功后切换到主界面。</summary>
	public static void SwitchToMain()
	{
		if (Current is AppShell shell)
		{
			shell.LoginTab.IsVisible = false;
			shell.MainTab.IsVisible = true;
			shell.CurrentItem = shell.MainTab.Items[0];
		}
	}

	/// <summary>退出登录/未登录时切换回登录页。</summary>
	public static void SwitchToLogin()
	{
		if (Current is AppShell shell)
		{
			shell.MainTab.IsVisible = false;
			shell.LoginTab.IsVisible = true;
			shell.CurrentItem = shell.LoginTab;
		}
	}
}
