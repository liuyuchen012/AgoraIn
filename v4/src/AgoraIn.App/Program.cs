using System;
using System.Linq;
using System.Runtime.InteropServices;
using AgoraIn.App.Services;
using Avalonia;

namespace AgoraIn.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 无头冒烟自测：AgoraIn.exe --selftest [module...] [--out 报告文件]，退出码 0 = 全部通过。
        // 不初始化 Avalonia UI，可在 CI / 终端直接运行；GUI 子进程无控制台时用 --out 落盘审计。
        if (args.Length > 0 && args[0] == "--selftest")
        {
            AttachParentConsole();
            return SelfTestRunner.Run(SelfTestOptions.Parse(args.Skip(1).ToArray()));
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    private const uint AttachParentProcess = 0xFFFFFFFF;

    /// <summary>从真实控制台启动时把 stdout 重新挂到父控制台；无控制台环境（CI 管道）静默失败，用 --out 落盘。</summary>
    private static void AttachParentConsole()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (AttachConsole(AttachParentProcess))
        {
            _ = NativeMethods.ReopenStdHandles();
        }
    }
}

internal static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareWrite = 0x2;
    private const uint OpenExisting = 3;

    public static bool ReopenStdHandles()
    {
        var ok = true;
        ok &= Redirect(StdOutputHandle, "CONOUT$");
        ok &= Redirect(StdErrorHandle, "CONOUT$");
        return ok;
    }

    private static bool Redirect(int stdHandle, string target)
    {
        var handle = CreateFileW(target, GenericWrite, FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return false;
        }

        return SetStdHandle(stdHandle, handle) && GetStdHandle(stdHandle) == handle;
    }
}
