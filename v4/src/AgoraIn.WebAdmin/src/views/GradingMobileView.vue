<template>
  <!-- 手机端嵌入式批改页（WebView 直接加载）：与网页端同一套批改面板 -->
  <div class="mobile-grading">
    <div class="head">
      <span class="title">{{ header?.paperTitle || '逐题批改' }}</span>
      <span class="student">{{ header?.studentName || header?.studentRef || '未识别考生' }}</span>
      <span class="status">{{ statusText }}</span>
    </div>

    <div v-if="error" class="error">{{ error }}</div>
    <div v-else-if="!rows.length" class="error">加载中…</div>

    <GradingPanel v-else ref="panelEl"
                  :submission-id="submissionId"
                  :paper-id="header?.paperId || ''"
                  :rows="rows"
                  :saving="saving"
                  @save="save">
      <!-- 按钮放在面板底栏（在面板元素内部）：进全屏后依然可见 —— 旧版在外部 div 里，
           全屏时整条 foot 被隐藏，手机上就"没有确认按钮"了 -->
      <template #actions>
        <el-button size="small" :loading="saving" @click="panelEl?.save()">保存本题</el-button>
        <el-button size="small" @click="panelEl?.save(true)">下一题</el-button>
        <span style="flex:1" />
        <el-button size="small" @click="markHuman">待人工</el-button>
        <el-button size="small" @click="finish">完成</el-button>
        <el-button type="primary" size="small" :loading="confirming" @click="confirm">✔ 确认出分</el-button>
      </template>
    </GradingPanel>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { examApi, applyToken, type ResultRow } from '@/api/client'
import GradingPanel from '@/components/GradingPanel.vue'

const route = useRoute()
const router = useRouter()
const submissionId = computed(() => String(route.params.submissionId || ''))

const panelEl = ref<InstanceType<typeof GradingPanel> | null>(null)
const header = ref<Awaited<ReturnType<typeof examApi.submissionHeader>> | null>(null)
const rows = ref<(ResultRow & { editScore?: number; editComment?: string })[]>([])
const error = ref('')
const saving = ref(false)

const statusText = computed(() =>
  ({ NotGraded: '未批', AiGraded: 'AI已批', NeedsHuman: '待人工', Confirmed: '已确认' })[header.value?.status || ''] || header.value?.status || '')

onMounted(async () => {
  // WebView 通过 ?token= 注入登录态（写入后立即从地址栏抹掉）
  const token = route.query.token as string | undefined
  if (token) {
    applyToken(token)
    await router.replace({ path: route.path, query: {} })
  }
  try {
    header.value = await examApi.submissionHeader(submissionId.value)
    const data = await examApi.getResults(submissionId.value)
    rows.value = (data || []).map(r => ({ ...r, editScore: r.score ?? undefined, editComment: r.comment ?? '' }))
  } catch (e: any) {
    error.value = e?.response?.status === 401
      ? '登录已过期，请回到 App 重新进入'
      : (e?.response?.data?.error || '加载失败')
  }
})

async function save(row: ResultRow & { editScore?: number; editComment?: string }, next: boolean) {
  saving.value = true
  try {
    await examApi.overrideResult(submissionId.value, row.questionId,
      { score: row.editScore ?? 0, comment: row.editComment || undefined })
    row.score = row.editScore ?? 0
    row.comment = row.editComment || null
    row.source = 'Teacher'
    ElMessage.success(`第 ${row.index + 1} 题已记 ${row.editScore} 分`)
    if (next) {
      const i = rows.value.findIndex(r => r.questionId === row.questionId)
      if (i >= 0 && i < rows.value.length - 1) {
        const n = rows.value[i + 1]
        n.editScore = n.score ?? undefined
        n.editComment = n.comment ?? ''
      }
    }
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.error || '保存失败')
  } finally { saving.value = false }
}

const confirming = ref(false)

async function confirm() {
  try { await ElMessageBox.confirm('确认后成绩计入统计，确认出分？', '确认出分', { type: 'warning' }) } catch { return }
  confirming.value = true
  try {
    await examApi.confirm(submissionId.value)
    ElMessage.success('已确认出分')
    if (header.value) header.value.status = 'Confirmed'
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.error || '确认失败')
  } finally { confirming.value = false }
}

async function markHuman() {
  try {
    await examApi.markReview(submissionId.value)
    ElMessage.success('已标记待人工')
    if (header.value) header.value.status = 'NeedsHuman'
  } catch (e: any) {
    ElMessage.error('操作失败')
  }
}

function finish() {
  // WebView 里没有"关闭页签"，显示完成态
  document.body.innerHTML = '<div style="font-family:sans-serif;text-align:center;padding-top:40vh;color:#67c23a;font-size:20px">✔ 批改完成<br><small style="color:#999">返回 App 即可</small></div>'
}
</script>

<style scoped>
.mobile-grading {
  height: 100vh;
  display: flex;
  flex-direction: column;
  background: #fff;
}
.head {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  border-bottom: 1px solid #ebeef5;
  flex-wrap: wrap;
}
.title { font-weight: 700; font-size: 15px; }
.student { color: #4285f4; font-size: 13px; }
.status { color: #909399; font-size: 12px; margin-left: auto; }
.error { padding: 40px 16px; text-align: center; color: #909399; }
.mobile-grading :deep(.grading-panel) { height: auto; flex: 1; min-height: 0; }
</style>
