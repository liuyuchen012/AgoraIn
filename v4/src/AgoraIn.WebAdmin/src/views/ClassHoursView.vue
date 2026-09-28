<template>
  <el-card shadow="never" class="page-card">
    <template #header>
      <div class="card-header">
        <span class="card-title">课时账户</span>
        <div style="display:flex;gap:8px">
          <el-select v-model="classId" clearable placeholder="按班级筛选学生" style="width:180px" @change="loadData">
            <el-option v-for="c in classes" :key="c.id" :label="c.name" :value="c.id!" />
          </el-select>
          <el-button size="small" @click="loadData">刷新</el-button>
        </div>
      </div>
    </template>

    <el-table :data="viewRows" v-loading="loading" empty-text="暂无课时数据">
      <el-table-column label="学生" min-width="120">
        <template #default="{ row }">{{ studentName(row.studentId) }}</template>
      </el-table-column>
      <el-table-column label="班级" width="120">
        <template #default="{ row }">{{ className(row.studentId) }}</template>
      </el-table-column>
      <el-table-column prop="totalHours" label="总课时" width="100" />
      <el-table-column prop="usedHours" label="已用课时" width="100" />
      <el-table-column label="剩余课时" width="100">
        <template #default="{ row }">
          <el-tag :type="remaining(row) > 0 ? 'success' : 'danger'" size="small">
            {{ remaining(row) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="remark" label="备注" min-width="140" />
      <el-table-column label="操作" width="150" fixed="right">
        <template #default="{ row }">
          <el-button link type="success" size="small" @click="openAdjust(row, 1)">赠送</el-button>
          <el-button link type="danger" size="small" @click="openAdjust(row, -1)">划消</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- 划消/赠送对话框 -->
    <el-dialog v-model="adjustVisible" :title="adjustDelta > 0 ? '赠送课时' : '划消课时'" width="380px">
      <el-form label-width="70px">
        <el-form-item label="学生">
          <span>{{ studentName(adjustStudentId) }}</span>
        </el-form-item>
        <el-form-item label="课时数">
          <el-input-number v-model="adjustHours" :min="0.5" :max="48" :step="0.5" style="width:140px" />
          <span style="margin-left:8px;color:#909399;font-size:12px">小时</span>
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="adjustNote" :placeholder="adjustDelta > 0 ? '如：续费赠送' : '如：请假划消'" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="adjustVisible = false">取消</el-button>
        <el-button type="primary" @click="doAdjust">确定</el-button>
      </template>
    </el-dialog>
  </el-card>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { classhourApi, classApi, studentApi, type ClassRow, type StudentRow } from '@/api/client'

const loading = ref(false)
const accounts = ref<any[]>([])
const classes = ref<ClassRow[]>([])
const students = ref<StudentRow[]>([])
const classId = ref('')

// 划消/赠送
const adjustVisible = ref(false)
const adjustStudentId = ref('')
const adjustDelta = ref(1)
const adjustHours = ref(1)
const adjustNote = ref('')

const viewRows = computed(() =>
  classId.value ? accounts.value.filter(a => className(a.studentId) === classNameOf(classId.value!)) : accounts.value,
)

onMounted(async () => {
  await loadData()
  try {
    classes.value = await classApi.list()
    students.value = await studentApi.list()
  } catch {}
})

async function loadData() {
  loading.value = true
  try {
    const res = await classhourApi.accounts()
    accounts.value = Array.isArray(res) ? res : []
  } finally {
    loading.value = false
  }
}

function openAdjust(row: any, sign: 1 | -1) {
  adjustStudentId.value = row.studentId
  adjustDelta.value = sign
  adjustHours.value = 1
  adjustNote.value = ''
  adjustVisible.value = true
}

async function doAdjust() {
  const delta = adjustDelta.value * adjustHours.value
  try {
    await classhourApi.adjust(adjustStudentId.value, delta, adjustNote.value || (adjustDelta.value > 0 ? '赠送' : '划消'))
    ElMessage.success(adjustDelta.value > 0 ? `已赠送 ${adjustHours.value} 课时` : `已划消 ${adjustHours.value} 课时`)
    adjustVisible.value = false
    await loadData()
  } catch {}
}

function studentName(studentId: string) {
  return students.value.find(s => s.id === studentId)?.name || studentId.slice(0, 8)
}
function className(studentId: string) {
  const stu = students.value.find(s => s.id === studentId)
  return stu ? classNameOf(stu.classId) : '—'
}
function classNameOf(classIdValue: string) {
  return classes.value.find(c => c.id === classIdValue)?.name || '—'
}

function remaining(row: any) {
  return Math.round(((row.totalHours || 0) - (row.usedHours || 0)) * 10) / 10
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
</style>
