# AgoraIn v4.0

课堂签到打卡与班级教学管理一体机平台。**本分支（`v4.0`）为闭源商业版本**，只包含 `v4/` 目录下的新代码。

> 历史版本（v3.2 及以前，GPLv3）的源码位于其他分支（`V2.6` / `v2.7` / `v2.8` / `v3.0` / `v3.2`），
> 在本分支不再跟踪，仅以只读形式保留在本地工作区供迁移参考。

## 快速开始

```bash
# 桌面端（Avalonia，Windows 优先）
dotnet build v4/src/AgoraIn.App/AgoraIn.App.csproj

# 服务端（ASP.NET Core，默认端口 5250）
dotnet build v4/src/AgoraIn.Server/AgoraIn.Server.csproj

# 质量门禁（build + test + selftest）
powershell -ExecutionPolicy Bypass -File v4/scripts/gate.ps1

# 服务端一键部署
bash v4/deploy-server.sh <host> <user> <password>
```

各端完整构建命令、部署步骤与常见问题见 **[v4/docs/deployment.md](v4/docs/deployment.md)**。

## 组成

| 模块 | 技术栈 | 说明 |
| --- | --- | --- |
| `v4/src/AgoraIn.Core` | .NET 10 类库 | 领域模型与领域服务（零依赖） |
| `v4/src/AgoraIn.Data` | EF Core + SQLite | 本地优先存储 + v3 数据迁移器 |
| `v4/src/AgoraIn.App` | Avalonia 11 | 桌面端：大屏 / 控制 / 教师三模式 |
| `v4/src/AgoraIn.Server` | ASP.NET Core 10 | 服务端：REST + SignalR + JWT |
| `v4/src/AgoraIn.WebAdmin` | Vue 3 + Vite | Web 管理面板 |
| `v4/src/AgoraIn.Mobile` | .NET MAUI | 移动端：Android / iOS / Windows |
| `miniprogram-parent/` | 微信小程序 | 家长端（独立仓库，不在本仓库内） |

## 文档

| 文档 | 内容 |
| --- | --- |
| [架构文档](v4/docs/architecture.md) | 分层结构、本地优先、三模式、AI 阅卷流水线、技术栈与许可证 |
| [API 契约](v4/docs/api-contract.md) | 全部 `/api/v4/*` 端点、鉴权、SignalR 事件、错误约定 |
| [ClassIsland 兼容契约](v4/docs/api-contract-classisland.md) | 插件依赖的 `api/calls_*` 接口（插件禁止修改） |
| [部署手册](v4/docs/deployment.md) | 服务端 / Web / 桌面 / 移动端 / 小程序部署与常见问题 |

在线文档：https://doc.615mc.cn

## 授权

闭源商业软件。本仓库全部代码为闭源商业资产，禁止以任何形式公开或再分发。
