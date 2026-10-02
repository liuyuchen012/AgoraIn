import axios, { type AxiosRequestConfig } from 'axios'
import { ElMessage } from 'element-plus'

declare module 'axios' {
  /**
   * silent=true：失败时不要弹全局错误提示，由调用方自己处理。
   * 用于"失败是可预期分支"的请求——例如本题切图 404（照片拍不全时本来就切不出来，
   * 调用方会回退显示整页原图），旧实现在这种情况下仍弹出 404 红条，看着像出了故障。
   */
  export interface AxiosRequestConfig {
    silent?: boolean
  }
}

const TOKEN_KEY = 'agorain_admin_token'

/** 供嵌入式批改页（手机 WebView）从 URL 注入令牌 */
export function applyToken(token: string) {
  localStorage.setItem(TOKEN_KEY, token)
}
const USER_KEY = 'agorain_admin_user'

/** 底层 axios 实例：自动附带 JWT，401 自动跳登录 */
const http = axios.create({
  baseURL: '/api/v4',
  timeout: 15000,
  headers: { 'Content-Type': 'application/json' },
})

http.interceptors.request.use((config) => {
  const token = localStorage.getItem(TOKEN_KEY)
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

http.interceptors.response.use(
  (res) => res.data,
  (err) => {
    if (err.response?.status === 401) {
      localStorage.removeItem(TOKEN_KEY)
      localStorage.removeItem(USER_KEY)
      ElMessage.error('登录已过期，请重新登录')
      setTimeout(() => { window.location.href = '/login' }, 600)
    } else if (err.config?.silent) {
      // 调用方自己处理（如切图失败回退整页），不打扰用户
    } else if (err.response?.status === 403) {
      ElMessage.error('权限不足')
    } else {
      ElMessage.error(err.response?.data?.error || err.message || '请求失败')
    }
    return Promise.reject(err)
  },
)

/** 类型化客户端：拦截器已解包 res.data，因此直接返回业务数据 */
const client = {
  get: <T = unknown>(url: string, config?: AxiosRequestConfig): Promise<T> =>
    http.get(url, config) as unknown as Promise<T>,
  post: <T = unknown>(url: string, data?: unknown, config?: AxiosRequestConfig): Promise<T> =>
    http.post(url, data, config) as unknown as Promise<T>,
  put: <T = unknown>(url: string, data?: unknown, config?: AxiosRequestConfig): Promise<T> =>
    http.put(url, data, config) as unknown as Promise<T>,
  delete: <T = unknown>(url: string, config?: AxiosRequestConfig): Promise<T> =>
    http.delete(url, config) as unknown as Promise<T>,
}

// ── 认证 ──
export const authApi = {
  login: (username: string, password: string) =>
    client.post<{ token: string; role: string; username: string; permissions: string[]; region?: string; isManager?: boolean }>('/auth/login', { username, password }),
  setup: (username: string, password: string) =>
    client.post<{ message: string }>('/auth/setup', { username, password }),
  changePassword: (oldPassword: string, newPassword: string) =>
    client.post<{ message: string }>('/auth/change-password', { oldPassword, newPassword }),
  setupStatus: () => client.get<{ needsSetup: boolean }>('/setup/status'),
}

// ── 自助注册（区域主账号 / 家长加入区域） ──
export const registerApi = {
  sendCode: (email: string) => client.post<{ message: string }>('/account/send-code', { email, purpose: 'register' }),
  register: (data: {
    email?: string; code?: string; username: string; password: string;
    displayName?: string; agreeTerms: boolean;
    mode: 'region' | 'join'; regionName?: string; regionId?: string; inviteCode?: string; agreeRegionTerms?: boolean;
  }) => client.post<{ message: string; loginName?: string; regionId?: string; studentName?: string }>('/account/register', data),
}

// ── 区域（租户）管理：仅主区域 manager 可用 ──
export const regionApi = {
  list: () => client.get<RegionRow[]>('/regions'),
  /** 区域自定义隐私协议（匿名读取，注册页展示） */
  getAgreement: (regionId: string) =>
    client.get<{ regionId: string; regionName: string; hasCustom: boolean; content?: string; updatedAt?: string }>(`/regions/${regionId}/agreement`),
  /** 保存本区域自定义隐私协议（区域主账号） */
  saveOwnAgreement: (content: string) =>
    client.put<{ regionId: string; hasCustom: boolean; updatedAt?: string }>('/regions/me/agreement', { content }),
  /** 保存指定区域自定义隐私协议（仅主区域） */
  saveAgreement: (regionId: string, content: string) =>
    client.put<{ regionId: string; hasCustom: boolean; updatedAt?: string }>(`/regions/${regionId}/agreement`, { content }),
  /** 当前区域状态（租户激活页用） */
  me: () =>
    client.get<{ regionId: string; name?: string; isManager?: boolean; activated?: boolean; isActive?: boolean; expireAt?: string | null; maxDevices?: number; remainingDays?: number }>('/regions/me'),
  /** 区域激活（输入主区域颁发的激活码） */
  activateRegion: (activationCode: string) =>
    client.post<{ regionId: string; expireAt?: string; maxDevices?: number }>('/regions/activate', { activationCode }),
  /** 当前区域 AI 额度（元 + 各模型折算 Token） */
  aiQuota: () =>
    client.get<{ regionId: string; balanceYuan: number; mimoTokens: number; deepseekTokens: number; rates: { mimoTokensPerYuan: number; deepseekTokensPerYuan: number } }>('/regions/ai-quota'),
  /** 兑换 AI 额度（输入 AGRT- 额度码） */
  redeemQuota: (code: string) =>
    client.post<{ redeemed: number; balanceYuan: number; mimoTokens: number; deepseekTokens: number }>('/regions/redeem-quota', { code }),
  /** 主区域为子区域签发 AI 额度兑换码 */
  issueQuotaCode: (regionId: string, amountYuan: number) =>
    client.post<{ activationCode: string; amountYuan: number; mimoTokens: number; deepseekTokens: number }>(`/regions/${regionId}/issue-quota-code`, { amountYuan }),
  create: (data: { regionId?: string; name: string; ownerUsername: string; ownerPassword: string; ownerDisplayName?: string }) =>
    client.post<RegionRow>('/regions', data),
  issueCode: (regionId: string, months: number, maxDevices: number) =>
    client.post<{ activationCode: string; months: number; maxDevices: number }>(`/regions/${regionId}/issue-code`, { months, maxDevices }),
  remove: (regionId: string) => client.delete<void>(`/regions/${regionId}`),
}

export interface RegionRow {
  id: number
  regionId: string
  name: string
  devicePassword?: string
  ownerUsername: string
  activated: boolean
  expireAt?: string | null
  maxDevices: number
  activatedAt?: string | null
  createdAt: string
  isActive: boolean
  remainingDays: number
}

// ── 班级 ──
export const classApi = {
  list: () => client.get<ClassRow[]>('/classes'),
  create: (data: unknown) => client.post<ClassRow>('/classes', data),
  update: (id: string, data: unknown) => client.put<ClassRow>(`/classes/${id}`, data),
  remove: (id: string) => client.delete<void>(`/classes/${id}`),
}

// ── 学生 ──
export const studentApi = {
  list: (classId?: string) =>
    client.get<StudentRow[]>('/students', { params: classId ? { classId } : {} }),
  create: (data: unknown) => client.post<StudentRow>('/students', data),
  update: (id: string, data: unknown) => client.put<StudentRow>(`/students/${id}`, data),
  remove: (id: string) => client.delete<void>(`/students/${id}`),
  /** 批量生成考号（写入学号字段，答题卡识别按此匹配） */
  generateExamNumbers: (data: { classId: string; digits: number; prefix?: string; start: number; overwrite: boolean; dryRun?: boolean }) =>
    client.post<ExamNumberResult>('/students/exam-numbers', data),
}

export interface ExamNumberResult {
  classId: string
  digits: number
  prefix: string
  changed: number
  skipped: number
  total: number
  dryRun?: boolean
  rows: { id: string; name: string; no: string | null; willChange?: boolean; changed: boolean }[]
}

// ── 设备 ──
export const deviceApi = {
  list: () => client.get<DeviceRow[]>('/devices'),
  heartbeat: (id: number) => client.post<void>(`/devices/${id}/heartbeat`),
}

// ── 打卡 ──
export const checkinApi = {
  records: (taskId?: string, studentId?: string) =>
    client.get<unknown[]>('/checkin/records', {
      params: { ...(taskId ? { taskId } : {}), ...(studentId ? { studentId } : {}) },
    }),
  // 扫码签到（v3 语义：短码 + 签到密码 + 教室/科目）
  createSignInCode: (data: { taskId: string; classroom?: string; subject?: string; password?: string; expiresMinutes?: number }) =>
    client.post<SignInCodeRow>('/checkin/signin-codes', data),
  listSignInCodes: () => client.get<SignInCodeRow[]>('/checkin/signin-codes'),
  deactivateSignInCode: (id: string) => client.delete<void>(`/checkin/signin-codes/${id}`),
}

// ── 课时 ──
export const classhourApi = {
  accounts: (classId?: string) =>
    client.get<ClassHourAccountRow[]>('/classhours/accounts', { params: classId ? { classId } : {} }),
  /** 手工划消/赠送课时（delta 正 = 赠送，负 = 划消） */
  adjust: (studentId: string, delta: number, note: string) =>
    client.post<{ count: number }>('/classhours/records', [
      { studentId, date: new Date().toISOString().slice(0, 10), delta, note, source: 0 },
    ]),
}

// ── 仪表盘 ──
export const dashboardApi = {
  overview: () => client.get<{ isManager?: boolean; region?: any; stats: Record<string, number>; recentCheckins: unknown[]; devices: unknown[] }>('/dashboard'),
}

// ── 通知公告 ──
export const noticeApi = {
  list: (classId?: string) => client.get<unknown[]>('/notices', { params: classId ? { classId } : {} }),
  create: (data: unknown) => client.post<unknown>('/notices', data),
  /** 未读名单（已绑定家长 - 已读回执） */
  unread: (noticeId: string) =>
    client.get<{ unreadCount: number; students: string[] }>(`/notices/${noticeId}/unread`),
}

// ── 资源库 ──
export const resourceApi = {
  list: (classId?: string) => client.get<unknown[]>('/resources', { params: classId ? { classId } : {} }),
  upload: (file: File, meta: { title?: string; subject?: string; classId?: string }) => {
    const fd = new FormData()
    fd.append('file', file)
    if (meta.title) fd.append('title', meta.title)
    if (meta.subject) fd.append('subject', meta.subject)
    if (meta.classId) fd.append('classId', meta.classId)
    return client.post<unknown>('/resources/upload', fd, { headers: { 'Content-Type': 'multipart/form-data' } })
  },
  publish: (id: string) => client.post<unknown>(`/resources/${id}/publish`),
}

// ── 试卷 ──
export const examApi = {
  list: () => client.get<unknown[]>('/exams/papers'),
  create: (data: unknown) => client.post<{ id: string }>('/exams/papers', data),
  update: (id: string, data: unknown) => client.put<unknown>(`/exams/papers/${id}`, data),
  remove: (id: string) => client.delete<void>(`/exams/papers/${id}`),
  // 题目
  getQuestions: (paperId: string) => client.get<unknown[]>(`/exams/papers/${paperId}/questions`),
  createQuestion: (paperId: string, data: unknown) => client.post<{ id: string }>(`/exams/papers/${paperId}/questions`, data),
  updateQuestion: (paperId: string, questionId: string, data: unknown) =>
    client.put<unknown>(`/exams/papers/${paperId}/questions/${questionId}`, data),
  // 答题卡
  getSubmissions: (paperId: string) => client.get<unknown[]>(`/exams/submissions?paperId=${paperId}`),
  /** 删除一份扫卡答卷（连带逐题结果与已存原图）；已确认出分的会被服务端拒绝 */
  deleteSubmission: (submissionId: string) =>
    client.delete<{ deleted: boolean; questionResults: number; images: number }>(`/exams/submissions/${submissionId}`),
  uploadSubmission: (paperId: string, file: File) => {
    const fd = new FormData()
    fd.append('file', file)
    return client.post<unknown>(`/exams/submissions?paperId=${paperId}`, fd, { headers: { 'Content-Type': 'multipart/form-data' } })
  },
  /** 整卷 AI 批改（主观题逐题调用大模型） */
  gradeAll: (submissionId: string) =>
    client.post<{ graded: number; status: string }>(`/exams/submissions/${submissionId}/grade-all`, {}),
  aiGrade: (submissionId: string, questionId: string, data?: unknown) =>
    client.post<unknown>(`/exams/submissions/${submissionId}/grade/${questionId}`, data ?? {}),
  /** 逐题结果（含题干/标准答案/满分） */
  getResults: (submissionId: string) => client.get<ResultRow[]>(`/exams/submissions/${submissionId}/results`),
  /** 答题卡扫描原图（人工复盘对照用；走鉴权 blob，拿到后用 URL.createObjectURL 显示） */
  submissionImage: (submissionId: string, page = 1) =>
    client.get(`/exams/submissions/${submissionId}/image?page=${page}`, { responseType: 'blob', timeout: 60000 }),
  /** 本题切图：从扫描原图自动裁出某道题的作答区域（题号=卡面题号 1 起；404 = 无法切图，调用方回退整页，故静默） */
  questionCrop: (submissionId: string, questionNo: number) =>
    client.get(`/exams/submissions/${submissionId}/image/crop/${questionNo}`,
      { responseType: 'blob', timeout: 120000, silent: true }),
  /** 整页原图同样静默：没存原图时回退到"没有扫描原图"占位即可 */
  submissionImageSilent: (submissionId: string, page = 1) =>
    client.get(`/exams/submissions/${submissionId}/image?page=${page}`,
      { responseType: 'blob', timeout: 60000, silent: true }),
  /** 批改策略（分题/双判/仲裁） */
  gradingPolicy: (paperId: string) => client.get<GradingPolicy>(`/exams/papers/${paperId}/grading-policy`),
  saveGradingPolicy: (paperId: string, policy: GradingPolicy) =>
    client.put(`/exams/papers/${paperId}/grading-policy`, policy),
  /** 批改分配（含仲裁教师） */
  submissionHeader: (submissionId: string) =>
    client.get<{ submissionId: string; paperId: string; paperTitle: string; studentName?: string | null; studentRef?: string | null; status: string }>(`/exams/submissions/${submissionId}/header`),
  gradingAssignments: (paperId: string) => client.get<GradingAssignment[]>(`/exams/papers/${paperId}/grading-assignments`),
  saveGradingAssignments: (paperId: string, list: GradingAssignment[]) =>
    client.put(`/exams/papers/${paperId}/grading-assignments`, list),
  /** 教师复判/改分 */
  overrideResult: (submissionId: string, questionId: string, data: { score: number; comment?: string }) =>
    client.put<unknown>(`/exams/submissions/${submissionId}/results/${questionId}`, data),
  /** 确认绑定学生（考号识别 → 学号） */
  bindStudent: (submissionId: string, studentId: string) =>
    client.post<unknown>(`/exams/submissions/${submissionId}/bind-student`, { studentId }),
  /** 标记待人工复判 */
  review: (submissionId: string) =>
    client.post<{ status: string }>(`/exams/submissions/${submissionId}/review`, {}),
  confirm: (submissionId: string) => client.post<unknown>(`/exams/submissions/${submissionId}/confirm`, {}),
  markReview: (submissionId: string) => client.post<unknown>(`/exams/submissions/${submissionId}/review`, {}),
  /** 成绩统计与导出 */
  statistics: (paperId: string) => client.get<StatisticsRow>(`/exams/papers/${paperId}/statistics`),
  exportCsvUrl: (paperId: string) => `/api/v4/exams/papers/${paperId}/export`,
  /** 题库复用：从模板试卷复制题目 */
  reuse: (paperId: string, templatePaperId: string) =>
    client.post<{ copied: number }>(`/exams/papers/${paperId}/reuse/${templatePaperId}`, {}),
  /** 上传试卷文件（docx/txt，可带答案文件）AI 识别出题（文本路径） */
  importFile: (paperId: string, questionFile: File, answerFile?: File) => {
    const fd = new FormData()
    fd.append('questionFile', questionFile)
    if (answerFile) fd.append('answerFile', answerFile)
    return client.post<{ imported: number; questions: unknown[] }>(`/exams/papers/${paperId}/import-file`, fd,
      { headers: { 'Content-Type': 'multipart/form-data' }, timeout: 600000 })
  },
  /** 分批视觉出题：一批页面图片（≤3 张）立即解析入库，返回本批 AI 原文（前端实时展示） */
  extractBatch: (paperId: string, images: Blob[], startNumber: number) => {
    const fd = new FormData()
    images.forEach((b, i) => fd.append('images', b, `p${startNumber + i}.jpg`))
    return client.post<{ imported: number; raw: string; questions: unknown[] }>(
      `/exams/papers/${paperId}/extract-batch?startNumber=${startNumber}`, fd,
      { headers: { 'Content-Type': 'multipart/form-data' }, timeout: 600000 })
  },
  /** 分批答案回填：一批答案页图片（≤5 张） */
  fillBatch: (paperId: string, images: Blob[]) => {
    const fd = new FormData()
    images.forEach((b, i) => fd.append('images', b, `a${i}.jpg`))
    return client.post<{ updated: number }>(`/exams/papers/${paperId}/fill-batch`, fd,
      { headers: { 'Content-Type': 'multipart/form-data' }, timeout: 600000 })
  },
  /** 上传试卷页面图片（前端把 PDF 渲染成图）AI 视觉识别出题；答案页图片可选 */
  importImages: (paperId: string, questionImages: Blob[], answerImages?: Blob[], onProgress?: (pct: number) => void) => {
    const fd = new FormData()
    questionImages.forEach((b, i) => fd.append('questionImages', b, `q${i}.jpg`))
    ;(answerImages || []).forEach((b, i) => fd.append('answerImages', b, `a${i}.jpg`))
    return client.post<{ accepted?: boolean; imported?: number; questions?: unknown[] }>(`/exams/papers/${paperId}/import-file`, fd,
      {
        headers: { 'Content-Type': 'multipart/form-data' },
        timeout: 600000,
        onUploadProgress: e => { if (onProgress && e.total) onProgress(Math.round(e.loaded * 100 / e.total)) },
      })
  },
  /** AI 生成缺失的标准答案与评分要点 */
  aiGenerateAnswers: (paperId: string) =>
    client.post<{ updated: number; total: number }>(`/exams/papers/${paperId}/ai-generate-answers`, {}, { timeout: 300000 }),
  // 答题卡渲染（匿名控制器；iframe 不带 JWT，区域用户需显式带 region 参数）
  // opts：纸张 A4/B4/8K/16K/A3、考号区 bubble(填涂)/handwrite(仅手写)/none、notes 注意事项
  sheetUrl: (paperId: string, opts?: SheetQuery) => `/api/v4/sheet/${paperId}${sheetQuery(opts)}`,
  /** 可视化排版的版面数据：每道题自动排版下的落位 + 已拖动的覆盖项（mm，纸面坐标系） */
  sheetLayout: (paperId: string, opts?: SheetQuery) =>
    client.get<SheetLayoutData>(`/exams/papers/${paperId}/sheet-layout${sheetQuery(opts)}`),
  /** 保存可视化排版的覆盖（整份替换；传空数组 = 恢复全自动排版） */
  saveSheetLayout: (paperId: string, items: SheetPlacement[]) =>
    client.put<{ saved: number }>(`/exams/papers/${paperId}/sheet-layout`, items),
  sheetForStudent: (paperId: string, studentId: string, opts?: SheetQuery) =>
    `/api/v4/sheet/${paperId}/student/${studentId}${sheetQuery(opts)}`,
  batchSheetUrl: (paperId: string, classId: string, opts?: SheetQuery) =>
    `/api/v4/sheet/${paperId}/batch${sheetQuery({ classId, ...opts })}`,
  /** 考号表：把班级考号打印张贴，学生据此填涂通用答题卡 */
  rosterUrl: (classId: string, opts?: Pick<SheetQuery, 'paper'>) =>
    `/api/v4/sheet/roster/${classId}${sheetQuery(opts)}`,
}

/** 答题卡渲染参数 */
export interface SheetPlacement {
  questionId: string
  questionNo: number
  type: string
  page: number
  kind: 'objective' | 'frame'
  x: number
  y: number
  w: number
  h: number
  pinned: boolean
}

export interface SheetLayoutData {
  paperId: string
  columns: number
  paperWidthMm: number
  paperHeightMm: number
  pageCount: number
  items: SheetPlacement[]
}

export interface SheetQuery {
  paper?: string
  idArea?: 'bubble' | 'handwrite' | 'none'
  notes?: boolean
  classId?: string
}

/** 拼接查询串并附带区域参数（匿名 iframe 需要显式 region） */
function sheetQuery(params: SheetQuery = {}): string {
  const q = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === '') continue
    q.set(k, String(v))
  }
  try {
    const user = JSON.parse(localStorage.getItem(USER_KEY) || 'null')
    const region = user?.region || 'manager'
    if (region !== 'manager') q.set('region', region)
  } catch { /* 未登录（理论上不会走到渲染） */ }
  const s = q.toString()
  return s ? `?${s}` : ''
}

/** 批改策略 */
export interface GradingPolicy {
  splitEnabled: boolean
  doubleGrading: boolean
  arbitrationThreshold: number
}

/** 批改分配：把指定题号的一定比例答卷分给某位教师（kind: 0=批改 1=仲裁） */
export interface GradingAssignment {
  teacherUsername: string
  questionNos: number[]
  percent: number
  kind: number
}

// ── 点名 ──
export const rollcallApi = {
  sessions: (classId?: string) =>
    client.get<RollCallSessionRow[]>('/rollcall/sessions', { params: classId ? { classId } : {} }),
  createSession: (data: { classId: string; mode: number; subject?: string; weightFairness?: boolean }) =>
    client.post<RollCallSessionRow>('/rollcall/sessions', data),
  endSession: (id: string) => client.post<unknown>(`/rollcall/sessions/${id}/end`, {}),
  pick: (id: string, studentId?: string) =>
    client.post<{ recordId: string; studentId: string; studentName: string }>(`/rollcall/sessions/${id}/pick`, { studentId }),
  markResult: (recordId: string, result: number, pointDelta = 0) =>
    client.post<unknown>(`/rollcall/records/${recordId}/result`, { result, pointDelta }),
  records: (params?: { sessionId?: string; studentId?: string; from?: string; to?: string }) =>
    client.get<unknown[]>('/rollcall/records', { params }),
  report: (id: string) => client.get<ReportRow>(`/rollcall/sessions/${id}/report`),
}

// ── 座位 ──
export const seatApi = {
  charts: (classId: string) => client.get<SeatChartRow[]>('/seats/charts', { params: { classId } }),
  chart: (id: string) =>
    client.get<{ chart: SeatChartRow; seats: SeatRow[] }>(`/seats/charts/${id}`),
  createChart: (data: { classId: string; name: string; rows: number; cols: number; podium: number }) =>
    client.post<SeatChartRow>('/seats/charts', data),
  removeChart: (id: string) => client.delete<void>(`/seats/charts/${id}`),
  activate: (id: string) => client.put<unknown>(`/seats/charts/${id}/activate`, {}),
  updateSeat: (seatId: string, data: { studentId?: string; clearStudent?: boolean; disabled?: boolean; groupName?: string }) =>
    client.put<unknown>(`/seats/seats/${seatId}`, data),
  shuffle: (id: string, excludeStudentIds?: string[]) =>
    client.post<{ snapshotId: string; moved: number }>(`/seats/charts/${id}/shuffle`, { excludeStudentIds }),
}

// ── CSES 课表 ──
export const timetableApi = {
  get: (classId?: string) => client.get<TimetableRow>('/timetable', { params: classId ? { classId } : {} }),
  createSubject: (data: { name: string; color?: string; teacher?: string; classId?: string }) =>
    client.post<unknown>('/timetable/subjects', data),
  deleteSubject: (id: string) => client.delete<void>(`/timetable/subjects/${id}`),
  saveLayout: (data: { id?: string; name: string; classId?: string; entries: { index: number; startTime: string; endTime: string; isBreak: boolean; name?: string }[] }) =>
    client.post<{ id: string; name: string }>('/timetable/layouts', data),
  savePlan: (data: { id?: string; name: string; classId?: string; timeLayoutId: string; isActive: boolean; entries: { weekDay: number; slotIndex: number; subjectId: string }[] }) =>
    client.post<{ id: string; name: string }>('/timetable/plans', data),
  active: (classId?: string) => client.get<{ hasPlan: boolean }>('/timetable/active', { params: classId ? { classId } : {} }),
  /** 导入 ClassIsland 档案（.json/.yml 文件或文本） */
  importCses: (file: File, classId?: string) => {
    const fd = new FormData()
    fd.append('file', file)
    if (classId) fd.append('classId', classId)
    return client.post<{ importedSubjects: number; importedSlots: number; layoutName: string }>('/timetable/import-cses', fd,
      { headers: { 'Content-Type': 'multipart/form-data' } })
  },
  /** 导出 ClassIsland 档案 JSON 地址 */
  exportCsesUrl: (classId?: string) => `/api/v4/timetable/export-cses${classId ? `?classId=${encodeURIComponent(classId)}` : ''}`,
  /** 推送课表给 ClassIsland 插件（插件 profile_pull 拉取覆盖本地档案） */
  push: (classId?: string) => {
    const fd = new FormData()
    if (classId) fd.append('classId', classId)
    return client.post<{ pushed: boolean; version: number }>('/timetable/push', fd)
  },
}

// ── 师生留言（教师端） ──
export const messageApi = {
  conversations: () => client.get<MessageConversationRow[]>('/messages/conversations'),
  conversation: (parentUserId: string, studentId?: string) =>
    client.get<MessageRow[]>(`/messages/conversation/${parentUserId}`, { params: studentId ? { studentId } : {} }),
  reply: (data: { parentUserId: string; studentId?: string; classId?: string; content: string }) =>
    client.post<unknown>('/messages/reply', data),
}

// ── 用户/子账户管理 ──
export const usersApi = {
  list: () => client.get<UserRow[]>('/users'),
  roles: () => client.get<{ all: RoleInfo[]; creatable: string[] }>('/users/roles'),
  create: (data: { username: string; password: string; role: string; displayName?: string; email?: string }) =>
    client.post<UserRow>('/users', data),
  update: (id: number, data: { displayName?: string; email?: string; role?: string; isActive?: boolean }) =>
    client.put<UserRow>(`/users/${id}`, data),
  resetPassword: (id: number, newPassword: string) =>
    client.post<{ message: string }>(`/users/${id}/reset-password`, { newPassword }),
  remove: (id: number) => client.delete<void>(`/users/${id}`),
}

// ── 授权管理 ──
export const licenseApi = {
  get: () => client.get<Record<string, unknown>>('/license'),
  activate: (activationCode: string, forceRebind = false) =>
    client.post<{ message: string; [k: string]: unknown }>('/license/activate', { activationCode, forceRebind }),
  verify: (activationCode: string) =>
    client.post<{ valid: boolean; [k: string]: unknown }>('/license/verify', { activationCode }),
}

// ── SMTP 邮件 ──
export const smtpApi = {
  get: () => client.get<SmtpConfig>('/smtp'),
  save: (data: SmtpConfig) =>
    // 服务端 SmtpController.Save 为 [HttpPut]，用 POST 会 405
    client.put<{ message: string; configured: boolean }>('/smtp', data),
  test: (to: string) =>
    client.post<{ message: string }>('/smtp/test', { to }),
}

// ── 家长绑定邀请码 ──
export const parentApi = {
  createInvite: (studentId: string) =>
    client.post<{ inviteCode: string; studentName: string }>(`/parent/invite/${studentId}`, {}),
  /** 批量生成/获取全班邀请码 */
  batchInvite: (classId: string) =>
    client.get<{ studentId: string; studentNo?: string; studentName: string; inviteCode: string }[]>('/parent/invite/batch', { params: { classId } }),
  /** 全班邀请码 CSV 导出地址 */
  batchInviteCsvUrl: (classId: string) => `/api/v4/parent/invite/batch/export?classId=${encodeURIComponent(classId)}`,
  /** 邀请码 → 所属区域与自定义协议（匿名，注册页展示用） */
  inviteRegion: (inviteCode: string) =>
    client.get<{ regionId: string; regionName: string; hasCustom: boolean; customPrivacy?: string; customPrivacyUpdatedAt?: string }>(
      `/parent/invite/${encodeURIComponent(inviteCode)}/region`),
}

// ── 数据类型 ──
export interface ClassRow {
  id?: string
  name: string
  grade: string
  remark: string
}

export interface StudentRow {
  id?: string
  name: string
  studentNo?: string
  gender?: string
  classId: string
  status?: number
}

export interface DeviceRow {
  id: number
  deviceUuid: string
  deviceName: string
  lastSeen: string
  isOnline: boolean
}

export interface ClassHourAccountRow {
  id: string
  studentId: string
  totalHours: number
  usedHours: number
  remark: string
}

export interface UserRow {
  id: number
  username: string
  role: string
  roleName: string
  displayName: string
  email: string
  isActive: boolean
  isSubAccount: boolean
  ownerUserId: number | null
  lastLoginAt: string
  createdAt: string
}

export interface RoleInfo {
  role: string
  name: string
  permissions: string[]
}

export interface SmtpConfig {
  configured: boolean
  host: string
  port: number
  user: string
  password: string
  from: string
  enableSsl: boolean
  displayName: string
  updatedAt?: string
}

// ── AI 批改设置（需 system.settings 权限） ──
export const aiApi = {
  get: () => client.get<AiSettings>('/settings/ai'),
  save: (data: Partial<AiSettings> & { resetRegion?: boolean }) => client.put<AiSettings>('/settings/ai', data),
  logs: (take = 100) =>
    client.get<{ totalTokens: number; logs: AiLogRow[] }>('/settings/ai/logs', { params: { take } }),
}

export interface AiSettings {
  baseUrl: string
  model: string
  visionModel: string
  temperature: number
  maxTokens: number
  allowImageToCloud: boolean
  humanReviewThreshold: number
  retries: number
  gradingPromptTemplate?: string | null
  hasApiKey?: boolean
  apiKeyMasked?: string
  apiKey?: string
  scope?: 'global' | 'region'
  hasRegionOverride?: boolean
}

export interface AiLogRow {
  id: number
  endpoint: string
  model: string
  promptTokens: number | null
  completionTokens: number | null
  totalTokens: number | null
  durationMs: number
  success: boolean
  error: string | null
  createdAt: string
}

export interface SignInCodeRow {
  id: string
  code: string
  taskId: string
  classroom?: string | null
  subject?: string | null
  hasPassword?: boolean
  createdAt: string
  expiresAt?: string | null
}

export interface RollCallSessionRow {
  id: string
  classId: string
  mode: number
  subject?: string | null
  startedAt: string
  endedAt?: string | null
  weightFairness: boolean
  calledCount: number
}

export interface ReportRow {
  totalCalled: number
  present: number
  late: number
  absent: number
  pending: number
  attendanceRate: number
  perStudent: { studentId: string; studentName: string; calledTimes: number; present: number; late: number; absent: number; pending: number }[]
}

export interface SeatChartRow {
  id: string
  classId: string
  name: string
  rows: number
  cols: number
  podium: number
  isActive: boolean
  createdAt: string
  seatCount?: number
  occupiedCount?: number
}

export interface SeatRow {
  id: string
  row: number
  col: number
  disabled: boolean
  groupName?: string | null
  studentId?: string | null
  studentName?: string | null
}

export interface TimetableRow {
  subjects: { id: string; name: string; color?: string | null; teacher?: string | null; classId?: string | null }[]
  timeLayouts: { id: string; name: string; entries: { index: number; startTime: string; endTime: string; kind: number; name?: string }[] }[]
  classPlans: { id: string; name: string; classId?: string; timeLayoutId: string; isActive: boolean; entries: { weekDay: number; slotIndex: number; subjectId: string }[] }[]
}

export interface MessageConversationRow {
  parentUserId: string
  studentId?: string | null
  className?: string
  lastMessage: string
  lastTime: string
  messageCount: number
  hasFlagged: boolean
}

export interface MessageRow {
  id: string
  senderRole: number
  senderName: string
  content: string
  isImage: boolean
  flagged: boolean
  createdAt: string
}

export interface ResultRow {
  questionId: string
  index: number
  type: number
  content?: string | null
  standardAnswer?: string | null
  fullScore: number
  rubric?: string | null
  aiGradingEnabled?: boolean | null
  recognizedAnswer?: string | null
  score?: number | null
  comment?: string | null
  confidence?: number | null
  source?: string | null
  gradedAt?: string | null
  grader?: string | null
  score2?: number | null
  grader2?: string | null
  needArbitration?: boolean
  arbiter?: string | null
}

export interface StatisticsRow {
  paperId: string
  paperTitle: string
  totalPaperScore: number
  confirmedCount: number
  pendingCount: number
  avgScore: number
  maxScore: number
  minScore: number
  passRate: number
  perStudent: { studentId: string; studentName: string; totalScore: number; attempts: number }[]
  perQuestion: { questionId: string; index: number; type: number; fullScore: number; answerCount: number; avgScore: number; scoreRate: number; fullScoreCount: number }[]
}

export { TOKEN_KEY, USER_KEY }
export default client
