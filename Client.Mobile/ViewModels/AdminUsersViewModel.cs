using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using CheckIn.Client.Mobile.Services;

namespace CheckIn.Client.Mobile.ViewModels;

/// <summary>
/// 管理员用户管理 ViewModel（仅 admin 角色可访问）
/// </summary>
public class AdminUsersViewModel : INotifyPropertyChanged
{
    private readonly ApiService _api;

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }

    private string _newUsername = "";
    public string NewUsername { get => _newUsername; set { _newUsername = value; OnPropertyChanged(); } }

    private string _newPassword = "";
    public string NewPassword { get => _newPassword; set { _newPassword = value; OnPropertyChanged(); } }

    private string _newDisplayName = "";
    public string NewDisplayName { get => _newDisplayName; set { _newDisplayName = value; OnPropertyChanged(); } }

    private string _newRole = "student";
    public string NewRole { get => _newRole; set { _newRole = value; OnPropertyChanged(); } }

    private string _message = "";
    public string Message { get => _message; set { _message = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMessage)); } }

    private bool _isMessageError;
    public bool IsMessageError { get => _isMessageError; set { _isMessageError = value; OnPropertyChanged(); } }

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public ObservableCollection<UserItem> Users { get; } = new();
    public List<string> RoleOptions { get; } = new() { "student", "parent", "teacher", "owner", "admin" };

    public ICommand RefreshCommand { get; }
    public ICommand CreateUserCommand { get; }
    public ICommand DeleteUserCommand { get; }
    public ICommand ToggleUserCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AdminUsersViewModel(ApiService api)
    {
        _api = api;
        RefreshCommand = new Command(async () =>
        {
            try { await LoadUsersAsync(); }
            catch { /* 防止 async void 异常崩溃 */ }
        });
        CreateUserCommand = new Command(async () =>
        {
            try { await CreateUserAsync(); }
            catch { /* 防止 async void 异常崩溃 */ }
        });
        DeleteUserCommand = new Command<int>(async (id) =>
        {
            try { await DeleteUserAsync(id); }
            catch { /* 防止 async void 异常崩溃 */ }
        });
        ToggleUserCommand = new Command<UserItem>(async (user) =>
        {
            try { await ToggleUserAsync(user); }
            catch { /* 防止 async void 异常崩溃 */ }
        });
    }

    public async Task LoadUsersAsync()
    {
        IsLoading = true;
        try
        {
            var result = await _api.GetAsync("/api/users");
            if (ApiService.GetError(result) != null) return;

            Users.Clear();
            if (result.ValueKind == JsonValueKind.Array)
            {
                foreach (var u in result.EnumerateArray())
                {
                    Users.Add(new UserItem
                    {
                        Id = GetInt(u, "id"),
                        Username = ApiService.GetString(u, "username") ?? "",
                        Role = ApiService.GetString(u, "role") ?? "viewer",
                        DisplayName = ApiService.GetString(u, "display_name") ?? "",
                        IsActive = u.TryGetProperty("is_active", out var ia) && ia.GetBoolean(),
                        CreatedAt = ApiService.GetString(u, "created_at") ?? ""
                    });
                }
            }
        }
        catch (Exception ex)
        {
            ShowMessage($"加载失败: {ex.Message}", true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task CreateUserAsync()
    {
        if (string.IsNullOrWhiteSpace(NewUsername) || string.IsNullOrWhiteSpace(NewPassword))
        {
            ShowMessage("用户名和密码不能为空", true);
            return;
        }

        IsLoading = true;
        try
        {
            var result = await _api.PostAsync("/api/users", new
            {
                username = NewUsername.Trim(),
                password = NewPassword,
                display_name = NewDisplayName.Trim(),
                role = NewRole
            });

            var error = ApiService.GetError(result);
            if (error != null)
            {
                ShowMessage(error, true);
            }
            else
            {
                ShowMessage("用户创建成功", false);
                NewUsername = ""; NewPassword = ""; NewDisplayName = "";
                await LoadUsersAsync();
            }
        }
        catch (Exception ex)
        {
            ShowMessage($"创建失败: {ex.Message}", true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task DeleteUserAsync(int id)
    {
        if (!await Shell.Current.DisplayAlertAsync("确认删除", "确定要删除该用户吗？", "删除", "取消"))
            return;

        IsLoading = true;
        try
        {
            var result = await _api.DeleteAsync($"/api/users/{id}");
            var error = ApiService.GetError(result);
            if (error != null)
                ShowMessage(error, true);
            else
                await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            ShowMessage($"删除失败: {ex.Message}", true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ToggleUserAsync(UserItem user)
    {
        IsLoading = true;
        try
        {
            var result = await _api.PutAsync($"/api/users/{user.Id}", new
            {
                is_active = !user.IsActive
            });
            var error = ApiService.GetError(result);
            if (error != null)
                ShowMessage(error, true);
            else
            {
                ShowMessage(user.IsActive ? $"已禁用 {user.Username}" : $"已启用 {user.Username}", false);
                await LoadUsersAsync();
            }
        }
        catch (Exception ex)
        {
            ShowMessage($"操作失败: {ex.Message}", true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ShowMessage(string msg, bool isError)
    {
        Message = msg;
        IsMessageError = isError;
    }

    private static int GetInt(JsonElement json, string key) =>
        json.TryGetProperty(key, out var val) && val.TryGetInt32(out var i) ? i : 0;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class UserItem
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Role { get; set; } = "student";
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string CreatedAt { get; set; } = "";

    public string RoleText => Role switch
    {
        "admin" => "管理员",
        "owner" => "区域主账号",
        "teacher" => "教师",
        "operator" => "教师",
        "student" => "学生",
        "viewer" => "学生",
        "parent" => "家长",
        _ => "未知"
    };

    public Color RoleColor => Role switch
    {
        "admin" => Color.FromArgb("#ea4335"),
        "owner" => Color.FromArgb("#e37400"),
        "teacher" => Color.FromArgb("#4285f4"),
        "operator" => Color.FromArgb("#4285f4"),
        "student" => Color.FromArgb("#34a853"),
        "viewer" => Color.FromArgb("#34a853"),
        "parent" => Color.FromArgb("#9333ea"),
        _ => Color.FromArgb("#888888")
    };

    public string StatusText => IsActive ? "启用" : "禁用";
    public Color StatusColor => IsActive ? Color.FromArgb("#34a853") : Color.FromArgb("#888888");
}
