# AgoraIn v4

课堂签到打卡与班级教学管理一体机平台（v4.0 全量重构）。架构与阶段计划见仓库根目录《AI重构提示词.md》。

## 目录结构

```
v4/
├── AgoraIn.sln
├── src/
│   ├── AgoraIn.Core/     # 领域模型与领域服务（禁止引用 UI / EF / 平台库）
│   ├── AgoraIn.Data/     # EF Core 10 + SQLite 本地存储（本地优先架构）
│   └── AgoraIn.App/      # Avalonia 11 桌面端（大屏 / 控制 / 教师三模式）
├── tests/
│   ├── AgoraIn.Core.Tests/
│   └── AgoraIn.Data.Tests/
├── scripts/gate.ps1      # 质量门禁：build → test → selftest
└── docs/                 # v4 架构文档（后续阶段补齐）
```

## 质量门禁

```powershell
powershell -ExecutionPolicy Bypass -File scripts/gate.ps1
```

等价于依次执行：

```powershell
dotnet build AgoraIn.sln -c Debug
dotnet test AgoraIn.sln
src/AgoraIn.App/bin/Debug/net10.0/AgoraIn.exe --selftest --out out/selftest-report.txt
```

- `--selftest [module...]`：无头冒烟自测（不启动 UI），退出码 0 = 通过；`--out` 把报告落盘（GUI 子系统在无控制台环境下 stdout 不可见）。
- 模块化扩展：新领域模块实现 `AgoraIn.Core.SelfTest.ISelfTestModule`，在 `App/Services/SelfTestRunner.cs` 注册即可（如 `--selftest points`）。

## 各端构建

| 项目 | 构建命令 | 说明 |
| --- | --- | --- |
| 桌面端 | `dotnet build src/AgoraIn.App` | Avalonia 11，Windows 优先 |
| 服务端 | `dotnet build src/AgoraIn.Server` | ASP.NET Core，默认端口 5250 |
| Web 面板 | `cd src/AgoraIn.WebAdmin && npm install && npm run build` | Vue 3 + Vite，产物 `dist/` 供服务端托管 |
| 移动端（Android） | `dotnet build src/AgoraIn.Mobile -f net10.0-android` | 需 Android SDK |
| 移动端（Windows） | `dotnet restore src/AgoraIn.Mobile -p:TargetFramework=net10.0-windows10.0.19041.0 -p:RuntimeIdentifier=win-x64`<br>`dotnet build src/AgoraIn.Mobile -f net10.0-windows10.0.19041.0 -r win-x64 --no-restore` | 需先按 RID 还原 |
| 移动端（iOS） | `dotnet build src/AgoraIn.Mobile -f net10.0-ios` | 需 macOS 或配对 Mac |
| 家长端小程序 | 微信开发者工具打开 `miniprogram-parent/` | 独立仓库（见下） |

> 移动端 `AgoraIn.Mobile` 未加入 `AgoraIn.sln`：MAUI 多目标需要 RID 特化还原，会干扰主门禁。按上表单独构建。

## 服务端部署

```bash
bash deploy-server.sh <host> <user> <password>
# 例：bash deploy-server.sh 192.168.31.3 liuyuchen <password>
```

脚本执行：本地 publish → scp 上传 → 安装 systemd 服务 → 重启并验证 `/api/v4/setup/status`。

首次初始化：`POST /api/v4/auth/setup` 创建管理员（或在 Web 面板登录页首次打开时自动引导）。

## Windows 10 兼容

.NET 10 在 Windows 10（ntdll < 19041.5007）上默认 CET 检查会致命报错，`Directory.Build.props` 统一设置 `CETCompat=false`（沿用 v3.2 commit `32509aa` 方案）。

## 阶段进度

| 阶段 | 状态 | 说明 |
| --- | --- | --- |
| P0 脚手架 | ✅ 完成 | 解决方案骨架、Avalonia 主窗口（v3.2 同款视觉 + 三模式框架）、`--selftest` 门禁、CET 兼容 |
| P1 领域与数据 | ✅ 完成 | Core 11 组实体、3 个领域服务（课时幂等/积分上限/值日轮换）、EF Core 建模、v3 迁移器（38 测试全绿） |
| P2 桌面端 parity | ⬜ | 大屏/控制模式功能自 v3.2 等价迁移（P0 已复刻外壳视觉） |
| P3~P9 | ⬜ | 班级中心 / 课表 / 服务端 / Web / 答题卡 / 家长端 / 发布 |

> 闭源边界：v4 全部代码为闭源商业资产，禁止推送到任何公开仓库。
