<template>
  <el-card shadow="never" class="page-card">
    <template #header>
      <div class="card-header">
        <div class="filters">
          <span>学生管理</span>
          <el-select v-model="classId" placeholder="全部班级" clearable style="width: 180px" @change="loadData">
            <el-option v-for="c in classes" :key="c.id" :label="c.name" :value="c.id" />
          </el-select>
          <el-button size="small" type="success" plain :disabled="!classId" @click="batchInvite">批量生成邀请码</el-button>
          <el-button size="small" :disabled="!classId" @click="exportInvites">导出邀请码 (CSV)</el-button>
        </div>
        <div>
          <el-button size="small" @click="loadData">刷新</el-button>
          <el-button type="primary" size="small" @click="openDialog()">新增学生</el-button>
        </div>
      </div>
    </template>

    <el-table :data="students" v-loading="loading" empty-text="暂无学生">
      <el-table-column prop="studentNo" label="学号" width="120" />
      <el-table-column prop="name" label="姓名" min-width="140" />
      <el-table-column prop="gender" label="性别" width="80" />
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag :type="row.status === 0 ? 'success' : 'info'" size="small">
            {{ row.status === 0 ? '在读' : '转出' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="230" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" size="small" @click="openDialog(row)">编辑</el-button>
          <el-button link type="success" size="small" @click="onInvite(row)">生成家长邀请码</el-button>
          <el-button link type="danger" size="small" @click="onRemove(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>
  </el-card>

  <el-dialog v-model="dialogVisible" :title="editing ? '编辑学生' : '新增学生'" width="420px">
    <el-form :model="form" label-width="80px">
      <el-form-item label="姓名" required>
        <el-input v-model="form.name" />
      </el-form-item>
      <el-form-item label="学号">
        <el-input v-model="form.studentNo" />
      </el-form-item>
      <el-form-item label="性别">
        <el-radio-group v-model="form.gender">
          <el-radio label="男">男</el-radio>
          <el-radio label="女">女</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="班级" required>
        <el-select v-model="form.classId" placeholder="选择班级" style="width: 100%">
          <el-option v-for="c in classes" :key="c.id" :label="c.name" :value="c.id" />
        </el-select>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="dialogVisible = false">取消</el-button>
      <el-button type="primary" :loading="saving" @click="onSave">保存</el-button>
    </template>
  </el-dialog>

  <el-dialog v-model="batchVisible" title="全班家长绑定邀请码" width="520px">
    <el-alert type="info" :closable="false" style="margin-bottom:10px"
              title="家长在 App/小程序用邀请码注册后即自动绑定对应孩子；也可打印 CSV 发放。" />
    <el-table :data="batchInvites" size="small" max-height="360" border>
      <el-table-column prop="studentNo" label="学号" width="70" />
      <el-table-column prop="studentName" label="学生" width="100" />
      <el-table-column prop="inviteCode" label="邀请码" width="110">
        <template #default="{ row }"><span style="font-family:monospace;font-weight:600">{{ row.inviteCode }}</span></template>
      </el-table-column>
    </el-table>
    <template #footer>
      <el-button @click="batchVisible = false">关闭</el-button>
      <el-button type="primary" @click="exportInvites">导出 CSV</el-button>
    </template>
  </el-dialog>

  <el-dialog v-model="inviteVisible" title="家长绑定邀请码" width="380px">
    <div class="invite-box">
      <div class="invite-code">{{ inviteCode }}</div>
      <p class="invite-tip">请将此 6 位邀请码发给家长，家长在小程序「绑定孩子」中输入即可完成绑定。每个邀请码仅可使用一次。</p>
    </div>
    <template #footer>
      <el-button type="primary" @click="copyInvite">复制邀请码</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { studentApi, classApi, parentApi, type StudentRow, type ClassRow } from '@/api/client'

// 批量邀请码
const batchVisible = ref(false)
const batchInvites = ref<{ studentId: string; studentNo?: string; studentName: string; inviteCode: string }[]>([])

async function batchInvite() {
  if (!classId.value) return
  try {
    batchInvites.value = await parentApi.batchInvite(classId.value)
    batchVisible.value = true
  } catch {}
}

function exportInvites() {
  if (!classId.value) return
  window.open(parentApi.batchInviteCsvUrl(classId.value), '_blank')
}


const loading = ref(false)
const saving = ref(false)
const students = ref<StudentRow[]>([])
const classes = ref<ClassRow[]>([])
const classId = ref('')

const dialogVisible = ref(false)
const editing = ref(false)
const form = reactive<StudentRow>({ name: '', studentNo: '', gender: '男', classId: '' })

const inviteVisible = ref(false)
const inviteCode = ref('')

onMounted(async () => {
  const res = await classApi.list()
  classes.value = Array.isArray(res) ? res : []
  await loadData()
})

async function loadData() {
  loading.value = true
  try {
    const res = await studentApi.list(classId.value || undefined)
    students.value = Array.isArray(res) ? res : []
  } finally {
    loading.value = false
  }
}

function openDialog(row?: StudentRow) {
  editing.value = !!row
  Object.assign(
    form,
    row || { id: undefined, name: '', studentNo: '', gender: '男', classId: classes.value[0]?.id || '' },
  )
  dialogVisible.value = true
}

async function onSave() {
  if (!form.name || !form.classId) {
    ElMessage.warning('请填写姓名并选择班级')
    return
  }
  saving.value = true
  try {
    if (editing.value && form.id) {
      await studentApi.update(form.id, { ...form })
    } else {
      await studentApi.create({ ...form })
    }
    ElMessage.success('保存成功')
    dialogVisible.value = false
    await loadData()
  } finally {
    saving.value = false
  }
}

async function onRemove(row: StudentRow) {
  await ElMessageBox.confirm(`确定删除学生「${row.name}」吗？`, '提示', { type: 'warning' })
  await studentApi.remove(row.id!)
  ElMessage.success('已删除')
  await loadData()
}

async function onInvite(row: StudentRow) {
  const res: { inviteCode: string } = await parentApi.createInvite(row.id!)
  inviteCode.value = res.inviteCode
  inviteVisible.value = true
}

function copyInvite() {
  navigator.clipboard.writeText(inviteCode.value)
  ElMessage.success('已复制到剪贴板')
}
</script>

<style scoped>
.page-card {
  border-radius: 10px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-weight: 600;
}

.filters {
  display: flex;
  align-items: center;
  gap: 16px;
}

.invite-box {
  text-align: center;
}

.invite-code {
  font-size: 36px;
  font-weight: 700;
  letter-spacing: 6px;
  color: #4285f4;
  font-family: ui-monospace, monospace;
  margin-bottom: 16px;
}

.invite-tip {
  font-size: 13px;
  color: #909399;
  line-height: 1.6;
}
</style>
