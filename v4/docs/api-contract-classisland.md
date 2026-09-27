# ClassIsland 插件兼容契约

> 来源：通读 `AgoraIn.ClassIslandPlugin/`（`Services/CallPoller.cs`、`Models/CallMessage.cs`、`Services/PluginSettings.cs`）提取。
> 约束：**插件禁止修改**（规格 6.7 / 11.1），因此 v4 服务端必须提供下列**原样路径**的等价接口。
> 实现：`v4/src/AgoraIn.Server/Controllers/ClassIslandCompatController.cs`

## 1. 调用方式总览

| 项 | 值 |
| --- | --- |
| 基地址 | 插件设置的 `ServerUrl`（教师端「设置 → 集控平台」中配置，形如 `http://192.168.1.100:5250`） |
| 鉴权 | 请求体携带 `password` 字段（服务器连接密码），**非 JWT**、无需 RSA 密钥 |
| 超时 | 注册 8s；轮询 6s |
| 路径前缀 | **无 `/v4` 前缀**——插件硬编码 `api/calls_*` |

插件轮询间隔由 ClassIsland 定时器驱动（默认 3 秒）。

## 2. `POST api/calls_register`

呼叫接收端自登记。插件启动时调用，凭返回的 UUID 作为教师端「设备列表」中的发送目标；
广播呼叫（`TargetUuid` 为空）总会送达所有已登记接收端。

**请求**

```json
{
  "name": "ClassIsland-教室一体机",
  "password": "<服务器连接密码>",
  "client_version": "ClassIslandPlugin-2.5.0"
}
```

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `name` | string | 设备名，插件自动生成 `"ClassIsland-" + 主机名` |
| `password` | string | 服务器连接密码（未配置时服务端放行，便于本地联调） |
| `client_version` | string | 插件版本标识（服务端仅记录） |

**响应** `200 OK`

```json
{ "uuid": "3f2a9c1e8b7d4a5f9e0c1b2a3d4e5f60" }
```

| 状态码 | 含义 |
| --- | --- |
| 200 | 成功，`uuid` 为登记标识（同名设备幂等复用） |
| 401 | 密码错误 |

## 3. `POST api/calls_pull`

拉取待展示的呼叫队列。服务端返回**未确认**且（定向给本机或广播）的呼叫，按创建时间升序，最多 20 条。

**请求**

```json
{ "uuid": "<登记返回的 uuid>", "password": "<服务器连接密码>" }
```

**响应** `200 OK`

```json
{
  "calls": [
    {
      "id": 128,
      "type": "prenotice",
      "title": "待下课通知",
      "message": "请张三同学下课后到办公室",
      "minutes_before": 5,
      "student_names": "张三",
      "sender": "刘老师"
    }
  ]
}
```

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | int | 呼叫 ID，用于 `calls_ack` |
| `type` | string | `prenotice`=待下课通知（受插件「待下课通知」开关控制）；`summon`=下课传唤（忽略开关，始终显示）；`emergency`=上课应急 |
| `title` | string | 提示栏标题 |
| `message` | string | 正文 |
| `minutes_before` | int | 提前分钟数（`prenotice` 用，插件据此在下课前 N 分钟呼出） |
| `student_names` | string | 涉及学生姓名，逗号分隔 |
| `sender` | string | 发送人（教师名） |

> 插件行为要点：`prenotice` 类型会经 ClassIsland 的 `IPublicLessonsService` 查询「距下课剩余时间」，
> 若课程服务不可用则**保持待触发**不呼出；因此 `minutes_before` 语义为「距下课 N 分钟时呼出」。

## 4. `POST api/calls_ack`

确认已展示，防止重复拉取。插件在展示后立即调用。

**请求**

```json
{ "id": 128, "uuid": "<登记返回的 uuid>", "password": "<服务器连接密码>" }
```

**响应** `200 OK`

```json
{ "ok": true }
```

## 5. v4 新增：教师端下发呼叫

以下接口为 v4 新增（v3 由桌面端直连服务器实现），供桌面端/Web 端发起呼叫，**JWT 鉴权**。

### `POST /api/v4/calls`

```json
{
  "type": "summon",
  "title": "下课传唤",
  "message": "请李四同学到办公室",
  "minutesBefore": 0,
  "studentNames": "李四",
  "targetUuid": null
}
```

`targetUuid` 为空表示广播；否则定向发送到指定接收端。

**响应** `201 Created` → `{ "id": 129 }`

### `GET /api/v4/calls?take=50`

返回呼叫历史（教师端查看发送记录）。

## 6. 与桌面端广播的关系

- **广播/播报能力完全由 ClassIsland 插件承担**（规格 6.7）：v4 桌面端与服务端**不重新实现**呼叫弹窗与 TTS，
  只负责把呼叫写入队列（`Calls` 表）并回应插件的三个接口。
- 桌面端的「远程 → 呼叫」入口调用 v4 的 `POST /api/v4/calls` 写入队列，
  插件在下次轮询（≤3 秒）时拉取并展示。
- 课表时段驱动的自动呼出由插件侧基于 ClassIsland 课表判定，服务端不参与。

## 7. 兼容性验证清单

部署 v4 服务端后，按下表逐项验证插件行为不变：

| # | 场景 | 期望 |
| --- | --- | --- |
| 1 | 插件启动 | 日志出现登记成功，设备列表出现该设备（名称 `ClassIsland-*`） |
| 2 | 教师端广播呼叫 | 插件顶部提示栏出现标题+正文，语音朗读一遍 |
| 3 | 教师端定向呼叫某设备 | 仅该设备显示 |
| 4 | 插件展示后 | 服务端该呼叫 `Acked=true`，不再重复拉取 |
| 5 | 密码错误 | 插件提示「无法连接集控平台」，不显示呼叫 |
| 6 | 断网恢复 | 插件自动重连（注册幂等，UUID 不变） |

## 8. 数据模型

服务端 `Calls` 表（`CallEntity`）：

| 列 | 类型 | 说明 |
| --- | --- | --- |
| `Id` | int | 主键自增 |
| `Type` | string | prenotice / summon / emergency |
| `Title` / `Message` | string | 展示内容 |
| `MinutesBefore` | int | 提前分钟数 |
| `StudentNames` | string | 涉及学生 |
| `Sender` | string | 发送人 |
| `TargetUuid` | string? | 定向设备；null = 广播 |
| `Acked` / `AckedAt` | bool / DateTime? | 确认状态 |
| `CreatedAt` | DateTime | 创建时间 |
