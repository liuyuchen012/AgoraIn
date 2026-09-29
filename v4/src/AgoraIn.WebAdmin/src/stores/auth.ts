import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi, TOKEN_KEY, USER_KEY } from '@/api/client'

interface UserInfo {
  username: string
  role: string
  permissions: string[]
  region?: string
  isManager?: boolean
}

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string>(localStorage.getItem(TOKEN_KEY) || '')
  const user = ref<UserInfo | null>(
    JSON.parse(localStorage.getItem(USER_KEY) || 'null'),
  )

  const isLoggedIn = computed(() => !!token.value)
  const isAdmin = computed(() => user.value?.role === 'admin')
  const canManage = computed(() => ['admin', 'owner'].includes(user.value?.role || ''))
  /** 当前区域（manager = 主区域/房主） */
  const region = computed(() => user.value?.region || 'manager')
  const isManager = computed(() => region.value === 'manager')

  function hasPermission(perm: string) {
    return user.value?.permissions?.includes(perm) ?? false
  }

  async function login(loginName: string, password: string) {
    const res: {
      token: string; role: string; username: string; permissions: string[];
      region?: string; isManager?: boolean;
    } = await authApi.login(loginName, password)
    token.value = res.token
    user.value = {
      username: res.username,
      role: res.role,
      permissions: res.permissions || [],
      region: res.region || 'manager',
      isManager: res.isManager ?? (res.region || 'manager') === 'manager',
    }
    localStorage.setItem(TOKEN_KEY, res.token)
    localStorage.setItem(USER_KEY, JSON.stringify(user.value))
  }

  function logout() {
    token.value = ''
    user.value = null
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(USER_KEY)
  }

  return { token, user, isLoggedIn, isAdmin, canManage, region, isManager, hasPermission, login, logout }
})
