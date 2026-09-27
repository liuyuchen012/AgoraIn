import axios, { type AxiosRequestConfig } from 'axios'
import { ElMessage } from 'element-plus'

const TOKEN_KEY = 'agorain_admin_token'
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
    client.post<{ token: string; role: string; username: string }>('/auth/login', { username, password }),
  setup: (username: string, password: string) =>
    client.post<{ message: string }>('/auth/setup', { username, password }),
  changePassword: (oldPassword: string, newPassword: string) =>
    client.post<{ message: string }>('/auth/change-password', { oldPassword, newPassword }),
  setupStatus: () => client.get<{ needsSetup: boolean }>('/setup/status'),
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
}

// ── 设备 ──
export const deviceApi = {
  list: () => client.get<DeviceRow[]>('/devices'),
  heartbeat: (id: number) => client.post<void>(`/devices/${id}/heartbeat`),
}

// ── 打卡 ──
export const checkinApi = {
  records: (taskId: string) => client.get<unknown[]>('/checkin/records', { params: { taskId } }),
}

// ── 课时 ──
export const classhourApi = {
  accounts: (classId?: string) =>
    client.get<ClassHourAccountRow[]>('/classhours/accounts', { params: classId ? { classId } : {} }),
}

// ── 家长绑定邀请码 ──
export const parentApi = {
  createInvite: (studentId: string) =>
    client.post<{ inviteCode: string; studentName: string }>(`/parent/invite/${studentId}`, {}),
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

export { TOKEN_KEY, USER_KEY }
export default client
