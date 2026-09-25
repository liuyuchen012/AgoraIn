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

## Windows 10 兼容

.NET 10 在 Windows 10（ntdll < 19041.5007）上默认 CET 检查会致命报错，`Directory.Build.props` 统一设置 `CETCompat=false`（沿用 v3.2 commit `32509aa` 方案）。

## 阶段进度

| 阶段 | 状态 | 说明 |
| --- | --- | --- |
| P0 脚手架 | ✅ 完成 | 解决方案骨架、Avalonia 主窗口（v3.2 同款视觉 + 三模式框架）、`--selftest` 门禁、CET 兼容 |
| P1 领域与数据 | ⬜ | Core 全部实体、EF Core 建模、v3 JSON 数据迁移器 |
| P2 桌面端 parity | ⬜ | 大屏/控制模式功能自 v3.2 等价迁移（P0 已复刻外壳视觉） |
| P3~P9 | ⬜ | 班级中心 / 课表 / 服务端 / Web / 答题卡 / 家长端 / 发布 |

> 闭源边界：v4 全部代码为闭源商业资产，禁止推送到任何公开仓库。
