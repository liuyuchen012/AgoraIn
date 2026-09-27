# AgoraIn v4 架构文档

## 1. 总体结构

```
v4/
├── AgoraIn.sln                 # 主解决方案（Core / Data / App / Server / tests）
├── Directory.Build.props        # 公共构建属性（net10.0、CETCompat=false）
├── scripts/gate.ps1             # 质量门禁
├── deploy-server.sh             # 服务端部署脚本
├── src/
│   ├── AgoraIn.Core/            # 领域层（零依赖：不含 UI / EF / 平台库）
│   ├── AgoraIn.Data/            # EF Core + SQLite 本地存储 + v3 数据迁移器
│   ├── AgoraIn.App/             # Avalonia 桌面端（大屏/控制/教师三模式）
│   ├── AgoraIn.Server/          # ASP.NET Core 服务端（闭源，私有部署）
│   ├── AgoraIn.WebAdmin/        # Vue 3 Web 管理面板
│   └── AgoraIn.Mobile/          # MAUI 移动端（Android/iOS/Windows）
├── tests/
│   ├── AgoraIn.Core.Tests/
│   └── AgoraIn.Data.Tests/
└── docs/                        # 本目录
```

> `miniprogram-parent/`（微信家长端）为**独立仓库**，不在本解决方案内，已由 `.gitignore` 排除。

## 2. 分层与依赖方向

```
        ┌───────────────┐   ┌───────────────┐   ┌───────────────┐
        │  App (Avalonia)│   │ Server (ASP)  │   │ Mobile (MAUI) │
        └───────┬───────┘   └───────┬───────┘   └───────┬───────┘
                │                   │                   │
                └───────────┬───────┴───────────────────┘
                            ▼
                    ┌───────────────┐        ┌──────────────────┐
                    │ AgoraIn.Data  │───────▶│  AgoraIn.Core    │
                    │ (EF Core)     │        │  (领域模型+服务)  │
                    └───────────────┘        └──────────────────┘
```

- **Core 不引用任何 UI / EF / 平台库**，保证纯单测（当前 33 个领域测试）。
- Data 依赖 Core；App/Server 依赖两者。
- WebAdmin / Mobile / 小程序通过 HTTP 与服务端通信，不直接引用 Core。

## 3. 本地优先（Offline-first）

桌面端**无服务器也能完整工作**：

| 数据 | 本地位置 | 说明 |
| --- | --- | --- |
| 应用配置 | `data/app-config.json` | 启动场景、按钮网格、课时设置、服务器连接 |
| 任务与名单 | `data/tabs/<id>/name.txt` | 每行一个学生姓名（沿用 v3 布局） |
| 打卡数据 | `data/tabs/<id>/attendance.dat` | `姓名:次数:首次时间:历史1\|历史2` |
| 课时 | `data/classhours.json` | 学生账户 + 流水（SlotKey 幂等）+ 排课 + 不排课日 |
| 工作区 | `workspace.json` | 打开的标签页列表与活动标签 |
| 课表 | `data/timetable.json` | CSES 结构：科目 / 时间布局 / 班级课表 |
| 座位图 | `data/seat-chart.json` | 行列 + 学生分配快照 |

联网后由同步引擎（`/api/v4/sync`）与服务端合并；不可达时静默降级，状态栏显示离线灯（红 `#ea4335` / 绿 `#34a853`）。

## 4. 桌面端三模式

| 模式 | 左侧栏 | 内容区 |
| --- | --- | --- |
| 大屏模式 | 任务树（文件夹/任务两级） | 标签页 + 320px 打卡排名卡 + 学生按钮网格 |
| 控制模式 | 划课 / 设备列表 / 任务中心 / 集控平台 | 课时管理 + 42 格月历 + 课时流水；集控平台打开服务端 Web |
| 教师模式 | 点名 / 积分 / 值日 / 座位 / 课表 | 对应面板 |

- 标签栏**仅大屏模式**显示。
- 启动场景由设置决定（`app-config.json` 的 `StartupMode`）。
- **课表驱动**（可选开关）：上课 → 切大屏；下课/课间 → 切控制；上课前 N 分钟触发点名提醒；下课后触发课时划消联动。实现见 `App/Services/TimetableDriver.cs`。

## 5. 服务端

- ASP.NET Core 10 + EF Core + SQLite（默认）/ 可选 PostgreSQL。
- JWT 鉴权（`/api/v4/auth/login`），密码 BCrypt 哈希。
- SignalR Hub `/hub/live`：按班级分组，事件 `CheckInUpdate` / `RollCallUpdate` / `PointsUpdate` / `Notification`。
- 同步引擎：实体级时间戳 + 变更集拉取，冲突按「服务器时间戳裁定 + 教师端最终确认」。
- 文件存储：`data/resources/`（本地磁盘），上传接口 `POST /api/v4/resources/upload`。
- 首次初始化：`GET /api/v4/setup/status` 返回 `needsSetup`，`POST /api/v4/auth/setup` 创建管理员。

## 6. AI 阅卷流水线

```
出卷(Web) → 渲染答题卡(A4 HTML) → 打印 → 作答 → 拍照上传
   → DeepSeek 识别（考号涂卡 + 客观题 OMR）
   → 客观题自动判分 / 主观题 AI 批改（按评分要点）
   → 低置信度转人工 → 教师确认 → 计入成绩
```

- 批改状态机：`未批 → AI已批 →（低置信度）待人工 → 已确认`；**只有「已确认」计入成绩**。
- Provider 抽象：`DeepSeekGradingService`（OpenAI 兼容协议，支持图像输入），配置项 `DeepSeek:ApiKey/BaseUrl/Model`。
- **API Key 只存服务端**，客户端不下发。
- 答题卡版面要素（四角定位标记 / 考号涂卡区 / OMR 气泡 / 主观题边界框 / 页脚二维码）见 `Server/Services/AnswerSheetRenderer.cs`。

## 7. ClassIsland 联动

- **插件不可修改**，v4 通过兼容层提供其依赖的 3 个接口（`api/calls_register` / `calls_pull` / `calls_ack`）。
- 契约细节见 [api-contract-classisland.md](./api-contract-classisland.md)。
- 课表交换使用 CSES 格式：`ClassIslandCompat` 支持 JSON 与 YAML 双格式导入/导出。

## 8. 质量门禁

```powershell
powershell -ExecutionPolicy Bypass -File scripts/gate.ps1
# = dotnet build AgoraIn.sln -c Debug
# + dotnet test AgoraIn.sln
# + AgoraIn.App --selftest --out out/selftest-report.txt
```

- `--selftest [module]` 为无头冒烟自测；新领域模块实现 `ISelfTestModule` 并在 `SelfTestRunner` 注册。
- MAUI 项目**不在** `AgoraIn.sln`（多目标 RID 特化还原会干扰门禁），构建命令见 [deployment.md](./deployment.md)。

## 9. 技术栈与许可证

| 组件 | 技术 | 许可证 |
| --- | --- | --- |
| 桌面端 | Avalonia 11.3.22 + CommunityToolkit.Mvvm 8.4.2 | MIT |
| 领域/数据 | .NET 10 + EF Core 10.0.12 + SQLite | MIT |
| 服务端 | ASP.NET Core 10 + JwtBearer + Swashbuckle + BCrypt.Net | MIT |
| 课表解析 | YamlDotNet 16.x | MIT |
| Web 面板 | Vue 3.5 + TypeScript + Vite 6 + Element Plus 2.9 + Pinia | MIT |
| 移动端 | .NET MAUI 10 | MIT |
| 测试 | xUnit 2.9.3 + Microsoft.NET.Test.Sdk | Apache-2.0 / MIT |

> 全部为 MIT / Apache-2.0 宽松许可，满足闭源合规要求（规格 11.3）。
