<template>
  <div class="users">
    <el-card shadow="never" class="page-card">
      <template #header>
        <div class="card-header">
          <span class="card-title">用户与子账户管理</span>
          <el-button type="primary" @click="showCreate">新建用户</el-button>
        </div>
      </template>

      <el-table :data="list" v-loading="loading" stripe border style="width: 100%">
        <el-table-column prop="id" label="ID" width="60" />
        <el-table-column prop="username" label="用户名" width="140" />
        <el-table-column prop="displayName" label="显示名" width="120" />
        <el-table-column prop="roleName" label="角色" width="100">
          <template #default="{ row }">
            <el-tag :type="roleTagType(row.role)" size="small">{{ row.roleName }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="email" label="邮箱" width="180" />
        <el-table-column prop="isSubAccount" label="子账户" width="80" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.isSubAccount" type="info" size="small">是</el-tag>
            <span v-else>—</span>
          </template>
        </el-table-column>
        <el-table-column prop="isActive" label="状态" width="80" align="center">
          <template #default="{ row }">
            <el-tag :type="row.isActive ? 'success' : 'danger'" size="small">
              {{ row.isActive ? '启用' : '禁用' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="lastLoginAt" label="最后登录" width="170">
          <template #default="{ row }">
            {{ row.lastLoginAt ? new Date(row.lastLoginAt).toLocaleString() : '从未' }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="220" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="showEdit(row)">编辑</el-button>
            <el-button link type="warning" size="small" @click="showResetPwd(row)">重置密码</el-button>
            <el-popconfirm title="确定要删除该用户吗？" @confirm="onDelete(row.id)">
              <template #reference>
                <el-button link type="danger" size="small">删除</el-button>
              </template>
            </el-popconfirm>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 新建 / 编辑对话框 -->
    <el-dialog v-model="dialogVisible" :title="editingId ? '编辑用户' : '新建用户'" width="440px" destroy-on-close>
      <el-form :model="form" label-width="80px">
        <el-form-item label="用户名" required>
          <el-input v-model="form.username" :disabled="!!editingId" placeholder="3–32 个字符" />
        </el-form-item>
        <el-form-item v-if="!editingId" label="密码" required>
          <el-input v-model="form.password" type="password" show-password placeholder="至少 6 个字符" />
        </el-form-item>
        <el-form-item label="显示名">
          <el-input v-model="form.displayName" placeholder="可选" />
        </el-form-item>
        <el-form-item label="邮箱">
          <el-input v-model="form.email" placeholder="可选" />
        </el-form-item>
        <el-form-item label="角色" required>
          <el-select v-model="form.role" style="width: 100%">
            <el-option v-for="r in creatableRoles" :key="r.role" :label="r.name" :value="r.role" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="editingId" label="启用">
          <el-switch v-model="form.isActive" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="onSave">保存</el-button>
      </template>
    </el-dialog>

    <!-- 重置密码对话框 -->
    <el-dialog v-model="resetPwdVisible" title="重置密码" width="360px" destroy-on-close>
      <p>为 <strong>{{ resetTarget?.username }}</strong> 重置密码：</p>
      <el-input v-model="newPassword" type="password" show-password placeholder="新密码（至少 6 个字符）" />
      <template #footer>
        <el-button @click="resetPwdVisible = false">取消</el-button>
        <el-button type="warning" :loading="resetting" @click="onResetPwd">重置</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { usersApi, type UserRow, type RoleInfo } from '@/api/client'

const loading = ref(false)
const saving = ref(false)
const list = ref<UserRow[]>([])
const creatableRoles = ref<RoleInfo[]>([])

const dialogVisible = ref(false)
const editingId = ref<number | null>(null)
const form = reactive({
  username: '',
  password: '',
  displayName: '',
  email: '',
  role: 'teacher',
  isActive: true,
})

const resetPwdVisible = ref(false)
const resetTarget = ref<UserRow | null>(null)
const newPassword = ref('')
const resetting = ref(false)

function roleTagType(role: string) {
  if (role === 'admin') return 'danger'
  if (role === 'owner') return 'warning'
  if (role === 'teacher') return ''
  if (role === 'parent') return 'success'
  return 'info'
}

async function loadList() {
  loading.value = true
  try {
    list.value = await usersApi.list()
  } finally {
    loading.value = false
  }
}

async function loadRoles() {
  try {
    const data = await usersApi.roles()
    creatableRoles.value = (data.all as RoleInfo[]).filter((r) =>
      (data.creatable as string[]).includes(r.role)
    )
  } catch { /* 静默 */ }
}

function showCreate() {
  editingId.value = null
  form.username = ''
  form.password = ''
  form.displayName = ''
  form.email = ''
  form.role = 'teacher'
  form.isActive = true
  dialogVisible.value = true
}

function showEdit(row: UserRow) {
  editingId.value = row.id
  form.username = row.username
  form.password = ''
  form.displayName = row.displayName || ''
  form.email = row.email || ''
  form.role = row.role
  form.isActive = row.isActive
  dialogVisible.value = true
}

async function onSave() {
  if (!form.username || form.username.length < 3) {
    ElMessage.warning('用户名需 3–32 个字符')
    return
  }
  if (!editingId.value && (!form.password || form.password.length < 6)) {
    ElMessage.warning('密码至少 6 个字符')
    return
  }
  saving.value = true
  try {
    if (editingId.value) {
      await usersApi.update(editingId.value, {
        displayName: form.displayName,
        email: form.email,
        role: form.role,
        isActive: form.isActive,
      })
      ElMessage.success('已更新')
    } else {
      await usersApi.create({
        username: form.username,
        password: form.password,
        displayName: form.displayName,
        email: form.email,
        role: form.role,
      })
      ElMessage.success('已创建')
    }
    dialogVisible.value = false
    await loadList()
  } finally {
    saving.value = false
  }
}

function showResetPwd(row: UserRow) {
  resetTarget.value = row
  newPassword.value = ''
  resetPwdVisible.value = true
}

async function onResetPwd() {
  if (!resetTarget.value || !newPassword.value || newPassword.value.length < 6) {
    ElMessage.warning('新密码至少 6 个字符')
    return
  }
  resetting.value = true
  try {
    await usersApi.resetPassword(resetTarget.value.id, newPassword.value)
    ElMessage.success('密码已重置')
    resetPwdVisible.value = false
  } finally {
    resetting.value = false
  }
}

async function onDelete(id: number) {
  try {
    await usersApi.remove(id)
    ElMessage.success('已删除')
    await loadList()
  } catch { /* 拦截器已处理 */ }
}

onMounted(() => {
  loadList()
  loadRoles()
})
</script>

<style scoped>
.users {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.page-card { border-radius: 10px; }
.card-header { display: flex; justify-content: space-between; align-items: center; }
.card-title { font-weight: 600; }
</style>
