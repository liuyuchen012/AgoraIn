<template>
  <el-card shadow="never" class="page-card">
    <template #header>
      <div class="card-header">
        <span class="card-title">课程表（CSES）</span>
        <el-select v-model="classId" clearable placeholder="全部班级" style="width:200px" @change="loadTimetable">
          <el-option v-for="c in classes" :key="c.id" :label="c.name" :value="c.id!" />
        </el-select>
      </div>
    </template>

    <el-tabs v-model="tab">
      <!-- 科目管理 -->
      <el-tab-pane label="科目" name="subjects">
        <div style="display:flex;gap:8px;margin-bottom:12px">
          <el-input v-model="newSubject.name" placeholder="科目名（如：数学）" style="width:200px" />
          <el-input v-model="newSubject.teacher" placeholder="教师（可选）" style="width:160px" />
          <el-color-picker v-model="newSubject.color" />
          <el-button type="primary" :disabled="!newSubject.name" @click="addSubject">添加科目</el-button>
        </div>
        <el-table :data="timetable?.subjects || []" size="small" stripe>
          <el-table-column label="颜色" width="60">
            <template #default="{ row }"><span class="color-dot" :style="{ background: row.color || '#4285f4' }" /></template>
          </el-table-column>
          <el-table-column prop="name" label="科目" min-width="140" />
          <el-table-column prop="teacher" label="教师" width="140" />
          <el-table-column label="操作" width="90">
            <template #default="{ row }">
              <el-popconfirm title="删除该科目？" @confirm="removeSubject(row.id)">
                <template #reference><el-button link type="danger" size="small">删除</el-button></template>
              </el-popconfirm>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <!-- 时间布局 -->
      <el-tab-pane label="时间布局" name="layouts">
        <div style="display:flex;gap:8px;margin-bottom:12px;align-items:center">
          <el-input v-model="newLayout.name" placeholder="布局名（如：标准作息）" style="width:200px" />
          <el-button type="primary" @click="addSlot">+ 节次</el-button>
          <el-button type="success" :disabled="!newLayout.name || !newLayout.entries.length" @click="saveLayout">保存布局</el-button>
        </div>
        <el-table :data="newLayout.entries" size="small" border>
          <el-table-column label="#" width="50"><template #default="{ row }">{{ row.index + 1 }}</template></el-table-column>
          <el-table-column label="开始">
            <template #default="{ row }"><el-time-picker v-model="row.start" format="HH:mm" style="width:120px" /></template>
          </el-table-column>
          <el-table-column label="结束">
            <template #default="{ row }"><el-time-picker v-model="row.end" format="HH:mm" style="width:120px" /></template>
          </el-table-column>
          <el-table-column label="类型" width="120">
            <template #default="{ row }">
              <el-switch v-model="row.isBreak" active-text="课间" inactive-text="上课" />
            </template>
          </el-table-column>
          <el-table-column label="操作" width="80">
            <template #default="{ $index }">
              <el-button link type="danger" size="small" @click="newLayout.entries.splice($index, 1)">删</el-button>
            </template>
          </el-table-column>
        </el-table>
        <el-divider content-position="left">已有布局</el-divider>
        <el-tag v-for="l in timetable?.timeLayouts || []" :key="l.id" style="margin:0 8px 8px 0">
          {{ l.name }}（{{ (l.entries || []).length }} 节）
        </el-tag>
      </el-tab-pane>

      <!-- 班级课表（周视图网格） -->
      <el-tab-pane label="班级课表" name="plans">
        <div style="display:flex;gap:8px;margin-bottom:12px;align-items:center;flex-wrap:wrap">
          <el-input v-model="newPlan.name" placeholder="课表名（如：三年级2班课表）" style="width:220px" />
          <el-select v-model="newPlan.timeLayoutId" placeholder="选择时间布局" style="width:200px">
            <el-option v-for="l in timetable?.timeLayouts || []" :key="l.id" :label="l.name" :value="l.id" />
          </el-select>
          <el-switch v-model="newPlan.isActive" active-text="生效中" />
          <el-button type="success" :disabled="!newPlan.name || !newPlan.timeLayoutId" @click="savePlan">保存课表</el-button>
        </div>

        <el-table :data="gridRows" size="small" border v-if="activeLayoutEntries.length">
          <el-table-column label="节次" width="110" fixed>
            <template #default="{ row }">
              {{ row.slot.index + 1 }}<br />
              <span style="font-size:11px;color:#909399">{{ fmtTime(row.slot.start) }}-{{ fmtTime(row.slot.end) }}</span>
            </template>
          </el-table-column>
          <el-table-column v-for="d in 7" :key="d" :label="weekNames[d - 1]" min-width="90">
            <template #default="{ row }">
              <el-select v-if="row.slot.kind === 0" :model-value="cell(d - 1, row.slot.index)"
                         placeholder="—" size="small" style="width:100%" clearable
                         @update:model-value="(v: string) => setCell(d - 1, row.slot.index, v)">
                <el-option v-for="s in timetable?.subjects || []" :key="s.id" :label="s.name" :value="s.id" />
              </el-select>
            </template>
          </el-table-column>
        </el-table>
        <el-empty v-else description="请先在「时间布局」页创建节次" />

        <el-divider content-position="left">已有课表</el-divider>
        <el-tag v-for="p in timetable?.classPlans || []" :key="p.id" style="margin:0 8px 8px 0"
                :type="p.isActive ? 'success' : 'info'">
          {{ p.name }}{{ p.isActive ? '（生效中）' : '' }}
        </el-tag>
      </el-tab-pane>
    </el-tabs>
  </el-card>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { timetableApi, classApi, type ClassRow, type TimetableRow } from '@/api/client'

const classes = ref<ClassRow[]>([])
const classId = ref('')
const tab = ref('subjects')
const timetable = ref<TimetableRow | null>(null)
const weekNames = ['周一', '周二', '周三', '周四', '周五', '周六', '周日']

const newSubject = ref({ name: '', teacher: '', color: '' })
const newLayout = ref<{ name: string; entries: { index: number; start: Date; end: Date; isBreak: boolean }[] }>({ name: '', entries: [] })
const newPlan = ref<{ name: string; timeLayoutId: string; isActive: boolean }>({ name: '', timeLayoutId: '', isActive: true })
const planCells = ref<Record<string, string>>({})

const activeLayoutEntries = computed(() => {
  const l = timetable.value?.timeLayouts.find(t => t.id === newPlan.value.timeLayoutId)
  return (l?.entries || []).filter(e => e.kind === 0)
})

const gridRows = computed(() => activeLayoutEntries.value.map(e => ({ slot: e })))

function cell(weekDay: number, slotIndex: number) {
  return planCells.value[`${weekDay}:${slotIndex}`] || ''
}
function setCell(weekDay: number, slotIndex: number, v: string) {
  planCells.value[`${weekDay}:${slotIndex}`] = v
}

onMounted(async () => {
  try { classes.value = await classApi.list() } catch {}
  await loadTimetable()
})

async function loadTimetable() {
  timetable.value = await timetableApi.get(classId.value || undefined)
  // 预填现有课表条目（取第一个关联布局）
  const plan = timetable.value?.classPlans.find(p => !classId.value || p.classId === classId.value)
  if (plan) {
    newPlan.value = { name: plan.name, timeLayoutId: plan.timeLayoutId, isActive: plan.isActive }
    planCells.value = {}
    for (const e of plan.entries || []) planCells.value[`${e.weekDay}:${e.slotIndex}`] = e.subjectId
  } else {
    planCells.value = {}
  }
}

async function addSubject() {
  await timetableApi.createSubject({
    name: newSubject.value.name,
    teacher: newSubject.value.teacher || undefined,
    color: newSubject.value.color || undefined,
    classId: classId.value || undefined,
  })
  ElMessage.success('已添加')
  newSubject.value = { name: '', teacher: '', color: '' }
  await loadTimetable()
}

async function removeSubject(id: string) {
  await timetableApi.deleteSubject(id)
  await loadTimetable()
}

async function addSlot() {
  const idx = newLayout.value.entries.length
  const base = new Date(2000, 0, 1, 8 + idx, 0, 0)
  newLayout.value.entries.push({ index: idx, start: base, end: new Date(base.getTime() + 40 * 60000), isBreak: false })
}

async function saveLayout() {
  const { entries } = newLayout.value
  const fmt = (d: Date) => `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
  await timetableApi.saveLayout({
    name: newLayout.value.name,
    classId: classId.value || undefined,
    entries: entries.map((e, i) => ({ index: i, startTime: fmt(e.start), endTime: fmt(e.end), isBreak: e.isBreak })),
  })
  ElMessage.success('布局已保存')
  newLayout.value = { name: '', entries: [] }
  await loadTimetable()
}

async function savePlan() {
  const entries: { weekDay: number; slotIndex: number; subjectId: string }[] = []
  for (const [key, subjectId] of Object.entries(planCells.value)) {
    if (!subjectId) continue
    const [weekDay, slotIndex] = key.split(':').map(Number)
    entries.push({ weekDay, slotIndex, subjectId })
  }
  await timetableApi.savePlan({
    name: newPlan.value.name,
    classId: classId.value || undefined,
    timeLayoutId: newPlan.value.timeLayoutId,
    isActive: newPlan.value.isActive,
    entries,
  })
  ElMessage.success(`课表已保存（${entries.length} 格）`)
  await loadTimetable()
}

function fmtTime(t: string) { return (t || '').slice(0, 5) }
</script>

<style scoped>
.page-card { border-radius: 10px; }
.card-header { display: flex; justify-content: space-between; align-items: center; }
.card-title { font-weight: 600; }
.color-dot { display: inline-block; width: 14px; height: 14px; border-radius: 50%; }
</style>
