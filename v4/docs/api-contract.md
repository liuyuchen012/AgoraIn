# AgoraIn v4 API 契约

- 基础路径：`/api/v4/*`（ClassIsland 兼容层除外，见 [api-contract-classisland.md](./api-contract-classisland.md)）
- 在线文档：服务端以 Development 模式运行时，`/swagger` 提供 OpenAPI 交互文档
- 鉴权：除标注 `匿名` 的接口外，均需 `Authorization: Bearer <JWT>`

## 认证 `auth`

| 方法 | 路径 | 说明 | 鉴权 |
| --- | --- | --- | --- |
| GET | `/api/v4/setup/status` | 是否需首次初始化 | 匿名 |
| POST | `/api/v4/auth/setup` | 创建首个管理员 `{username,password}` | 匿名 |
| POST | `/api/v4/auth/login` | 登录 `{username,password}`；`username` 格式 `用户名@区域Id`（主区域为 `用户名@manager`，不带 `@` 按主区域处理）；返回 `region`/`isManager` | 匿名 |
| POST | `/api/v4/auth/change-password` | 修改密码 `{oldPassword,newPassword}` | JWT |
| GET | `/api/v4/auth/me` | 当前用户资料与权限（含 `region`/`loginName`） | JWT |

## 账户 `account`（自助注册/重置密码）

| 方法 | 路径 | 说明 | 鉴权 |
| --- | --- | --- | --- |
| POST | `/api/v4/account/send-code` | 发送邮箱验证码 `{email,purpose}` | 匿名 |
| POST | `/api/v4/account/register` | 注册；`mode=region`（默认）创建新区域并成为主账号（需邮箱验证码，区域名称全局唯一）；`mode=join` **家长凭绑定邀请码注册**（需 `inviteCode`，注册即绑定孩子，无需邮箱） | 匿名 |
| POST | `/api/v4/account/reset-password` | 重置密码 `{email,code,newPassword}` | 匿名 |

## 区域 `regions`（多租户管理）

| 方法 | 路径 | 说明 | 鉴权 |
| --- | --- | --- | --- |
| GET | `/api/v4/regions` | 区域列表（仅主区域） | JWT admin/owner |
| POST | `/api/v4/regions` | 创建子区域 `{name,ownerUsername,ownerPassword,regionId?}`（区域名称/代号全局唯一） | JWT admin/owner |
| POST | `/api/v4/regions/{regionId}/issue-code` | 颁发激活码 `{months,maxDevices}` | JWT admin/owner |
| POST | `/api/v4/regions/activate` | 子区域激活 `{activationCode}`（由主区域颁发，HMAC 签名） | JWT region owner |
| GET | `/api/v4/regions/me` | 当前区域状态（名称/激活/到期/设备上限） | JWT |
| DELETE | `/api/v4/regions/{regionId}` | 删除子区域（仅主区域） | JWT admin/owner |

> **多区域数据隔离**：33 张教学数据表通过影子属性 `RegionId` 按请求区域过滤（全局查询过滤器），SaveChanges 自动回填新增实体的区域；`User` 表手动过滤（用户名在区域内唯一，登录格式 `用户名@区域Id`）；不参与隔离的全局表：`User`/`Device`/`Region`/`AiCallLog`/`AppSetting` 等。

## 班级 / 学生

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET/POST | `/api/v4/classes` | 班级列表 / 新建 |
| GET/PUT/DELETE | `/api/v4/classes/{id}` | 详情 / 更新 / 删除 |
| GET/POST | `/api/v4/students` | 学生列表（`?classId=` 筛选）/ 新建 |
| GET/PUT/DELETE | `/api/v4/students/{id}` | 详情 / 更新 / 删除 |

## 打卡 `checkin`

| 方法 | 路径 | 说明 | 鉴权 |
| --- | --- | --- | --- |
| GET | `/api/v4/checkin/tasks` | 任务列表（含签到进度） | JWT |
| GET | `/api/v4/checkin/records?taskId=&studentId=` | 打卡记录（可按任务/学生筛选） | JWT |
| POST | `/api/v4/checkin/records` | 批量提交（按 `taskId+studentId+checkedAt` 幂等去重） | JWT |
| POST | `/api/v4/checkin/signin-codes` | 生成扫码签到码 `{taskId,classroom?,subject?,password?,expiresMinutes?}` | JWT |
| GET | `/api/v4/checkin/signin-codes` | 当前生效签到码（大屏渲染二维码用） | JWT |
| DELETE | `/api/v4/checkin/signin-codes/{id}` | 下码（结束该签到） | JWT |
| POST | `/api/v4/checkin/scan` | 学生扫码签到 `{code,password?,studentId?,name?}` → `{success,rank,…}`；响应含 SignalR 推送 | 匿名 |

## 课时 `classhours`

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/v4/classhours/accounts` | 课时账户列表 |
| POST | `/api/v4/classhours/records` | 批量提交流水（`slotKey` 幂等） |

## 设备 `devices`

| 方法 | 路径 | 说明 | 鉴权 |
| --- | --- | --- | --- |
| GET | `/api/v4/devices` | 设备列表（含 `isOnline`） | JWT |
| POST | `/api/v4/devices/register` | 设备注册 `{uuid,name,publicKey}` | 匿名 |
| POST | `/api/v4/devices/{id}/heartbeat` | 心跳 | JWT |

## 积分 `points`

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/v4/points/rules?classId=` | 规则列表（含通用规则） |
| POST | `/api/v4/points/rules` | 新建规则 |
| GET | `/api/v4/points/records?studentId=&take=` | 积分流水 |
| POST | `/api/v4/points/records` | 批量提交（按 `sourceId+studentId` 幂等） |
| GET | `/api/v4/points/ranking?classId=&take=` | 积分榜（按总分降序） |

## 值日 `duty`

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET/POST | `/api/v4/duty/posts` | 岗位列表 / 新建 |
| GET | `/api/v4/duty/records?classId=&date=` | 值日完成记录 |
| POST | `/api/v4/duty/records` | 记录完成/未完成 |

## 通知 `notices`

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/v4/notices?classId=` | 通知列表 |
| POST | `/api/v4/notices` | 发布（`publishAt` 设为未来时刻即定时发布） |
| GET | `/api/v4/notices/{id}/unread` | 未读名单（已绑定家长 − 已读回执） |

## 资源库 `resources`

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/v4/resources?classId=&subject=` | 资源列表 |
| POST | `/api/v4/resources/upload` | 上传文件（multipart，限 200MB） |
| POST | `/api/v4/resources/{id}/publish` | 下发给家长端 |
| GET | `/api/v4/resources/{id}/file` | 下载 / 预览（链接类型重定向） |

## 留言 `messages`

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/v4/messages/conversations` | 会话列表（按家长分组，含 `hasFlagged`） |
| GET | `/api/v4/messages/conversation/{parentUserId}?studentId=` | 完整会话 |
| POST | `/api/v4/messages/reply` | 教师回复（命中敏感词置 `flagged`） |

## 课表 `timetable`（CSES）

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/v4/timetable?classId=` | 完整课表（科目 + 时间布局 + 班级课表） |
| POST | `/api/v4/timetable/subjects` | 新建科目 |
| DELETE | `/api/v4/timetable/subjects/{id}` | 删除科目 |
| POST | `/api/v4/timetable/layouts` | 保存时间布局（全量覆盖节次） |
| POST | `/api/v4/timetable/plans` | 保存班级课表（全量覆盖排课） |
| GET | `/api/v4/timetable/active?classId=` | 当前生效课表（供桌面端课表驱动） |
| POST | `/api/v4/timetable/import-cses` | 导入 ClassIsland 档案（.json/.yml 文件或 `text` 字段，自动识别 JSON/YAML） | JWT |
| GET | `/api/v4/timetable/export-cses?classId=` | 导出 ClassIsland 档案 JSON | JWT |
| POST | `/api/v4/timetable/push` | 推送课表：写入推送存储并递增版本号，插件轮询 `api/profile_pull` 覆盖本地档案 | JWT |

## 家长端 `parent`

| 方法 | 路径 | 说明 | 鉴权 |
| --- | --- | --- | --- |
| POST | `/api/v4/parent/bind` | 邀请码绑定孩子 `{inviteCode,alias}` | JWT |
| GET | `/api/v4/parent/children` | 已绑定孩子列表 | JWT |
| POST | `/api/v4/parent/unbind/{bindingId}` | 解绑（留审计） | JWT |
| GET | `/api/v4/parent/child/{studentId}/overview` | 孩子概览（打卡/出勤率/积分） | JWT |
| GET | `/api/v4/parent/child/{studentId}/scores` | 成绩概览（受隐私开关控制） | JWT |
| GET | `/api/v4/parent/notices?studentId=` | 通知（含已读状态） | JWT |
| POST | `/api/v4/parent/notices/{id}/read` | 标记已读 | JWT |
| GET/POST | `/api/v4/parent/messages` | 留言列表 / 发送 | JWT |
| GET | `/api/v4/parent/resources?studentId=` | 已下发资源列表 | JWT |
| GET | `/api/v4/parent/resources/{id}/file` | 资源下载（仅已下发资源） | JWT |
| GET | `/api/v4/parent/child/{studentId}/duty` | 值日表现（完成率 + 记录） | JWT |
| POST | `/api/v4/parent/invite/{studentId}` | 教师生成 6 位邀请码 | admin/teacher |
| GET | `/api/v4/parent/invite/batch?classId=` | 批量生成/获取全班邀请码（已有待绑码直接复用） | admin/teacher |
| GET | `/api/v4/parent/invite/batch/export?classId=` | 全班邀请码导出 CSV（UTF-8 BOM） | admin/teacher |

> 权限：家长接口会校验「该学生已绑定到当前用户」，越权返回 403。

## 答题卡与 AI 阅卷 `exams`

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET/POST | `/api/v4/exams/papers` | 试卷列表 / 创建（`isTemplate=true` 可作题库被复用） |
| PUT/DELETE | `/api/v4/exams/papers/{paperId}` | 更新 / 删除（级联删题） |
| GET/POST | `/api/v4/exams/papers/{paperId}/questions` | 题目列表 / 添加 |
| PUT | `/api/v4/exams/papers/{paperId}/questions/{qid}` | 更新题目 |
| POST | `/api/v4/exams/papers/{paperId}/reuse/{templatePaperId}` | 从题库模板复制题目 |
| POST | `/api/v4/exams/papers/{paperId}/import-file` | 上传试卷文件（docx/pdf/txt；`questionFile` + 可选 `answerFile`，支持题目答案同文件或分两个文件）AI 识别出题并生成答题卡题目 | JWT |
| POST | `/api/v4/exams/papers/{paperId}/ai-generate-answers` | AI 生成缺失的标准答案与评分要点（评分要点即 AI 阅卷提示词素材） | JWT |
| GET | `/api/v4/sheet/{paperId}` | **空白通用答题卡**（A4 HTML，浏览器打印，匿名） |
| GET | `/api/v4/sheet/{paperId}/student/{studentId}` | **学生专属卡**（含学号条码区，匿名） |
| GET | `/api/v4/sheet/{paperId}/batch?classId=` | **全班批量**（一人一页带条码，匿名） |
| GET | `/api/v4/exams/submissions?paperId=` | 提交记录列表 |
| POST | `/api/v4/exams/submissions` | 上传答题卡照片（multipart）→ 视觉模型识别考号+客观题，客观题自动判分 |
| POST | `/api/v4/exams/submissions/{sid}/grade-all` | 整卷 AI 批改（主观题逐题调用） |
| POST | `/api/v4/exams/submissions/{sid}/grade/{qid}` | 单题 AI 批改 |
| GET | `/api/v4/exams/submissions/{sid}/results` | 逐题结果（含题干/标准答案/满分/置信度/来源） |
| PUT | `/api/v4/exams/submissions/{sid}/results/{qid}` | 教师复判改分（原结果进 HistoryJson 留痕） |
| POST | `/api/v4/exams/submissions/{sid}/bind-student` | 确认绑定学生（考号识别 → 学号） |
| POST | `/api/v4/exams/submissions/{sid}/review` | 标记待人工复判 |
| POST | `/api/v4/exams/submissions/{sid}/confirm` | 教师确认（汇总总分，状态 → 已确认） |
| GET | `/api/v4/exams/papers/{paperId}/statistics` | 成绩统计（按学生总分/按题得分率，仅已确认） |
| GET | `/api/v4/exams/papers/{paperId}/export` | 成绩单导出 CSV（UTF-8 BOM） |

## 点名 `rollcall`

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| POST | `/api/v4/rollcall/sessions` | 创建场次 `{classId,mode,subject?,weightFairness?}` |
| GET | `/api/v4/rollcall/sessions?classId=` | 场次列表（历史） |
| POST | `/api/v4/rollcall/sessions/{id}/end` | 结束场次 |
| POST | `/api/v4/rollcall/sessions/{id}/pick` | 点下一名（随机加权/顺序轮转/指定 `studentId`），落点即推送 |
| POST | `/api/v4/rollcall/records/{recordId}/result` | 标记结果 `{result,pointDelta?}`（非 0 产生积分联动，幂等） |
| GET | `/api/v4/rollcall/records?sessionId=&studentId=&from=&to=` | 记录查询（导出用） |
| GET | `/api/v4/rollcall/sessions/{id}/report` | 场次报表（出勤率、逐生被点次数） |

## 座位 `seats`

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/v4/seats/charts?classId=` | 布局列表（含历史快照） |
| POST | `/api/v4/seats/charts` | 创建布局（自动生成行列网格） |
| GET | `/api/v4/seats/charts/{id}` | 布局详情（含座位与学生姓名） |
| DELETE | `/api/v4/seats/charts/{id}` | 删除布局 |
| PUT | `/api/v4/seats/charts/{id}/activate` | 设为当前生效布局 |
| PUT | `/api/v4/seats/seats/{seatId}` | 更新座位 `{studentId?,clearStudent,disabled?,groupName?}` |
| POST | `/api/v4/seats/charts/{id}/shuffle` | 随机换座（换座前自动留存快照） |

## 同步 `sync`

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/v4/sync/pull?since=&take=` | 变更集拉取（班级/学生/任务/课时账户全量 + 记录类按水位增量），返回 `serverTime` 作下次水位 |
| POST | `/api/v4/sync/push` | 冲突裁定推送（可变实体 `{classHourAccounts?,students?}`）；服务器时间戳裁定——客户端较旧拒绝并返回 `conflicts` 数组 |

## AI 设置 `settings/ai`（需 system.settings 权限）

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/v4/settings/ai` | 读取（模型/视觉模型/温度/低置信度阈值/**API 密钥脱敏**/图像外发开关/**阅卷提示词模板**） |
| PUT | `/api/v4/settings/ai` | 保存（`apiKey`：null=不变、空串=清除、非空=更新，存 AppSetting） |
| GET | `/api/v4/settings/ai/logs?take=` | AI 调用日志（Token 消耗与失败原因） |

## 呼叫（ClassIsland）

| 方法 | 路径 | 说明 | 鉴权 |
| --- | --- | --- | --- |
| POST | `/api/calls_register` | 呼叫接收端登记 | 密码 |
| POST | `/api/calls_pull` | 拉取待展示呼叫 | 密码 |
| POST | `/api/calls_ack` | 确认已展示 | 密码 |
| POST | `/api/v4/calls` | 教师端下发呼叫 | JWT |
| GET | `/api/v4/calls?take=` | 呼叫历史 | JWT |

## 实时推送 SignalR

- Hub 路径：`/hub/live`
- 客户端调用 `JoinClass(classId)` 加入班级分组
- 服务端事件：`CheckInUpdate`、`RollCallUpdate`、`PointsUpdate`、`Notification`、`SeatChartUpdate`
- 服务端方法：`BroadcastCheckIn`、`BroadcastRollCall`、`BroadcastPoints`、`SendNotification`
- 定向推送（`Clients.User`）按 JWT 的 `name_identifier` claim 匹配（本系统以用户名作为用户标识）
- 服务端在扫码签到、座位变更、点名落点/结果等写入时主动推送

## 错误约定

| 状态码 | 含义 |
| --- | --- |
| 400 | 参数错误（响应体 `{ "error": "说明" }`） |
| 401 | 未登录 / 令牌过期 / 密码错误 |
| 403 | 权限不足（如家长访问未绑定的孩子） |
| 404 | 资源不存在 |
