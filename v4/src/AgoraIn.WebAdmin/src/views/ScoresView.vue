<template>
  <div class="scores">
    <el-card shadow="never" class="page-card">
      <template #header>
        <div class="card-header">
          <span class="card-title">成绩统计</span>
          <div style="display:flex;gap:8px;align-items:center">
            <el-select v-model="paperId" placeholder="选择试卷" style="width:280px" @change="loadStats">
              <el-option v-for="p in papers" :key="p.id" :label="p.title" :value="p.id" />
            </el-select>
            <el-button :disabled="!paperId" @click="exportCsv">导出 Excel (CSV)</el-button>
          </div>
        </div>
      </template>

      <el-empty v-if="!stats && !loading" description="选择试卷后查看成绩统计" />

      <template v-if="stats">
        <el-row :gutter="12" style="margin-bottom:14px">
          <el-col :span="4"><el-statistic title="已确认份数" :value="stats.confirmedCount" /></el-col>
          <el-col :span="4"><el-statistic title="待批/待确认" :value="stats.pendingCount" /></el-col>
          <el-col :span="4"><el-statistic title="平均分" :value="stats.avgScore" /></el-col>
          <el-col :span="4"><el-statistic title="最高分" :value="stats.maxScore" /></el-col>
          <el-col :span="4"><el-statistic title="最低分" :value="stats.minScore" /></el-col>
          <el-col :span="4"><el-statistic title="及格率(%)" :value="stats.passRate" /></el-col>
        </el-row>

        <el-tabs>
          <el-tab-pane label="按学生总分">
            <el-table :data="stats.perStudent" size="small" stripe max-height="420" border>
              <el-table-column type="index" label="名次" width="60">
                <template #default="{ $index }">
                  <span :class="{ gold: $index === 0, silver: $index === 1, bronze: $index === 2 }">{{ $index + 1 }}</span>
                </template>
              </el-table-column>
              <el-table-column prop="studentName" label="学生" min-width="120" />
              <el-table-column prop="totalScore" label="总分" width="100" sortable />
              <el-table-column label="得分率" min-width="240">
                <template #default="{ row }">
                  <el-progress :percentage="Math.min(100, Math.round(row.totalScore / (stats.totalPaperScore || 1) * 100))" />
                </template>
              </el-table-column>
            </el-table>
          </el-tab-pane>
          <el-tab-pane label="按题得分率">
            <el-table :data="stats.perQuestion" size="small" stripe max-height="420" border>
              <el-table-column label="题号" width="60">
                <template #default="{ row }">{{ row.index + 1 }}</template>
              </el-table-column>
              <el-table-column label="题型" width="80">
                <template #default="{ row }">{{ typeNames[row.type] || '—' }}</template>
              </el-table-column>
              <el-table-column prop="fullScore" label="满分" width="70" />
              <el-table-column prop="avgScore" label="平均得分" width="90" />
              <el-table-column label="得分率" min-width="240">
                <template #default="{ row }">
                  <el-progress :percentage="Math.round(row.scoreRate)"
                               :color="row.scoreRate >= 60 ? '#34a853' : row.scoreRate >= 40 ? '#e6a23c' : '#ea4335'" />
                </template>
              </el-table-column>
              <el-table-column prop="fullScoreCount" label="满分人数" width="90" />
              <el-table-column prop="answerCount" label="作答人数" width="90" />
            </el-table>
          </el-tab-pane>
        </el-tabs>
        <p style="color:#909399;font-size:12px;margin-top:10px">
          仅统计状态为"已确认"的提交；低置信度题目需先在试卷管理的批改工作台完成复判。
        </p>
      </template>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { examApi } from '@/api/client'

const papers = ref<any[]>([])
const paperId = ref('')
const stats = ref<any>(null)
const loading = ref(false)
const typeNames = ['单选', '多选', '判断', '填空', '简答', '作文']

onMounted(async () => {
  try { papers.value = await examApi.list() } catch {}
})

async function loadStats() {
  if (!paperId.value) return
  loading.value = true
  try { stats.value = await examApi.statistics(paperId.value) } catch { stats.value = null } finally { loading.value = false }
}

function exportCsv() {
  if (paperId.value) window.open(examApi.exportCsvUrl(paperId.value), '_blank')
}
</script>

<style scoped>
.scores { display: flex; flex-direction: column; gap: 16px; }
.page-card { border-radius: 10px; }
.card-header { display: flex; justify-content: space-between; align-items: center; }
.card-title { font-weight: 600; }
.gold { color: #d4a017; font-weight: 700; }
.silver { color: #8c9196; font-weight: 700; }
.bronze { color: #b87333; font-weight: 700; }
</style>
