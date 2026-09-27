# AgoraIn v4 API 契约

- 基础路径：`/api/v4/*`（ClassIsland 兼容层除外，见 [api-contract-classisland.md](./api-contract-classisland.md)）
- 在线文档：服务端以 Development 模式运行时，`/swagger` 提供 OpenAPI 交互文档
- 鉴权：除标注 `匿名` 的接口外，均需 `Authorization: Bearer <JWT>`

## 认证 `auth`

| 方法 | 路径 | 说明 | 鉴权 |
| --- | --- | --- | --- |
| GET | `/api/v4/setup/status` | 是否需首次初始化 | 匿名 |
| POST | `/api/v4/auth/setup` | 创建首个管理员 `{username,password}` | 匿名 |
| POST | `/api/v4/auth/login` | 登录 → `{token,role,username}` | 匿名 |
| POST | `/api/v4/auth/change-password` | 修改密码 `{oldPassword,newPassword}` | JWT |

## 班级 / 学生

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET/POST | `/api/v4/classes` | 班级列表 / 新建 |
| GET/PUT/DELETE | `/api/v4/classes/{id}` | 详情 / 更新 / 删除 |
| GET/POST | `/api/v4/students` | 学生列表（`?classId=` 筛选）/ 新建 |
| GET/PUT/DELETE | `/api/v4/students/{id}` | 详情 / 更新 / 删除 |

## 打卡 `checkin`

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/v4/checkin/records?taskId=` | 任务打卡记录 |
| POST | `/api/v4/checkin/records` | 批量提交（按 `taskId+studentId+checkedAt` 幂等去重） |

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
| POST | `/api/v4/parent/invite/{studentId}` | 教师生成 6 位邀请码 | admin/teacher |

> 权限：家长接口会校验「该学生已绑定到当前用户」，越权返回 403。

## 答题卡与 AI 阅卷 `exams`

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| POST | `/api/v4/exams/papers` | 创建试卷 |
| GET | `/api/v4/exams/papers/{paperId}/sheet` | **空白通用答题卡**（A4 HTML，浏览器打印） |
| GET | `/api/v4/exams/papers/{paperId}/sheet/{studentId}` | **学生专属卡**（含姓名 + 学号条码） |
| GET | `/api/v4/exams/papers/{paperId}/sheets/batch?classId=` | **全班批量**（一人一页，打印为一册） |
| POST | `/api/v4/exams/submissions` | 上传答题卡照片（multipart）→ DeepSeek 识别考号+客观题 |
| POST | `/api/v4/exams/submissions/{sid}/grade/{qid}` | AI 批改主观题 |
| POST | `/api/v4/exams/submissions/{sid}/confirm` | 教师确认（汇总总分，状态 → 已确认） |

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
- 服务端事件：`CheckInUpdate`、`RollCallUpdate`、`PointsUpdate`、`Notification`
- 服务端方法：`BroadcastCheckIn`、`BroadcastRollCall`、`BroadcastPoints`、`SendNotification`

## 错误约定

| 状态码 | 含义 |
| --- | --- |
| 400 | 参数错误（响应体 `{ "error": "说明" }`） |
| 401 | 未登录 / 令牌过期 / 密码错误 |
| 403 | 权限不足（如家长访问未绑定的孩子） |
| 404 | 资源不存在 |
