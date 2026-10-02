<template>
  <!-- 逐题批改面板：上=参考答案，下=学生答案（本题自动切图），右=快捷记分键 -->
  <div ref="panelEl" class="grading-panel" :class="{ fullscreen }">
    <!-- 题号导航条 -->
    <div class="nav-bar">
      <el-button size="small" :disabled="index <= 0" @click="go(-1)">← 上一题</el-button>
      <div class="q-dots">
        <button v-for="(q, i) in rows" :key="q.questionId" class="dot"
                :class="{
                  active: i === index,
                  done: q.score != null,
                  arb: q.needArbitration,
                }"
                :title="`第 ${q.index + 1} 题`"
                @click="index = i">
          {{ q.index + 1 }}
        </button>
      </div>
      <el-button size="small" :disabled="index >= rows.length - 1" @click="go(1)">下一题 →</el-button>
      <div style="flex:1" />
      <span v-if="rows[index]" style="font-size:12px;color:#909399">
        第 {{ rows[index].index + 1 }} 题 · {{ typeNames[rows[index].type] || '—' }} · 满分 {{ rows[index].fullScore }}
      </span>
      <el-button size="small" @click="toggleFullscreen">{{ fullscreen ? '退出全屏' : '⛶ 全屏' }}</el-button>
    </div>

    <div v-if="current" class="body">
      <!-- 左列：参考答案 / 学生答案 -->
      <div class="left">
        <div class="pane ref">
          <div class="pane-title">参考答案</div>
          <div class="pane-body">
            <div v-if="current.content" class="q-content">{{ current.content }}</div>
            <div class="q-answer"><b>标准答案：</b>{{ current.standardAnswer || '—' }}</div>
            <div v-if="current.rubric" class="q-rubric"><b>评分要点：</b>{{ current.rubric }}</div>
            <div class="q-recognized"><b>识别/作答：</b>{{ current.recognizedAnswer || '（空白）' }}</div>
            <div v-if="doubleInfo" class="q-double">
              <template v-if="current.needArbitration">
                <el-tag type="danger" size="small">待仲裁</el-tag>
                <span>两判分差超阈值（{{ current.grader }}: {{ fmt(current.score ?? 0) }} / {{ current.grader2 }}: {{ fmt(current.score2 ?? 0) }}）</span>
              </template>
              <template v-else-if="current.grader2">
                <el-tag type="success" size="small">双判完成</el-tag>
                <span>{{ current.grader }}: {{ fmt(current.score ?? 0) }} ＋ {{ current.grader2 }}: {{ fmt(current.score2 ?? 0) }} → 合分 {{ fmt(current.score ?? 0) }}</span>
              </template>
              <template v-else-if="current.grader">
                <el-tag type="warning" size="small">已一判</el-tag>
                <span>{{ current.grader }}: {{ fmt(current.score ?? 0) }}，等待第二判</span>
              </template>
              <template v-else-if="current.arbiter">
                <el-tag size="small">仲裁裁定</el-tag>
                <span>{{ current.arbiter }}</span>
              </template>
            </div>
          </div>
        </div>

        <div class="pane student">
          <div class="pane-title">
            学生答案（本题区域）
            <span v-if="cropFailed" style="color:#e6a23c;font-size:11px">切图失败，显示整页</span>
            <div style="flex:1" />
            <el-button link size="small" @click="rotateCrop">旋转</el-button>
          </div>
          <div class="pane-body img-body" v-loading="cropLoading">
            <img v-if="cropUrl" :src="cropUrl" :style="cropStyle" alt="学生作答区域" />
            <img v-else-if="fullImageUrl" :src="fullImageUrl" class="full-fallback" alt="整页原图" />
            <el-empty v-else :description="cropLoading ? '正在切图…' : '没有扫描原图'" :image-size="60" />
          </div>
        </div>
      </div>

      <!-- 右列：快捷记分 -->
      <div class="right">
        <div class="score-title">记分（间隔 {{ stepLabel }}）</div>
        <ScoreKeys :model-value="editScore ?? null" :max="current.fullScore" :step="0.5"
                   :visible-count="visibleCount" @update:model-value="editScore = $event ?? undefined" />
        <el-input-number v-model="editScore" :min="0" :max="current.fullScore" :step="0.5"
                         size="small" style="width:76px;margin-top:8px" controls-position="right" />
        <el-input v-model="editComment" type="textarea" :rows="3" placeholder="评语（可选）"
                  style="margin-top:8px" />
        <el-button type="primary" style="margin-top:10px;width:100%" :loading="saving" @click="save">
          保存本题
        </el-button>
        <el-button style="width:100%;margin:8px 0 0" :disabled="index >= rows.length - 1" @click="save(true)">
          保存并下一题
        </el-button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { examApi, type ResultRow } from '@/api/client'
import ScoreKeys from './ScoreKeys.vue'

const props = defineProps<{
  submissionId: string
  paperId: string
  rows: (ResultRow & { editScore?: number; editComment?: string })[]
  saving?: boolean
}>()

const emit = defineEmits<{
  (e: 'save', row: ResultRow & { editScore?: number; editComment?: string }, next: boolean): void
}>()

const typeNames = ['单选', '多选', '判断', '填空', '简答', '作文']
const index = ref(0)
const current = computed(() => props.rows[index.value])
const editScore = ref<number | undefined>()
const editComment = ref('')
const fullscreen = ref(false)
const panelEl = ref<HTMLElement>()

// 可见记分键数量：面板矮（手机/半屏）时 2 个，常规 4 个
const visibleCount = ref(4)

const stepLabel = '0.5'

watch(current, q => {
  editScore.value = q?.editScore ?? (q?.score as number | undefined) ?? undefined
  editComment.value = q?.editComment ?? q?.comment ?? ''
  void loadCrop()
})
watch(() => props.rows, () => {
  // 行刷新（保存后父组件回填）时同步编辑态
  const q = current.value
  if (q) {
    editScore.value = q.editScore ?? (q.score as number | undefined) ?? undefined
    editComment.value = q.editComment ?? q.comment ?? ''
  }
})

function go(dir: number) {
  const next = index.value + dir
  if (next >= 0 && next < props.rows.length) index.value = next
}

const doubleInfo = computed(() =>
  current.value && (current.value.grader || current.value.needArbitration || current.value.arbiter))

// ── 本题切图 ──
const cropUrl = ref('')
const cropLoading = ref(false)
const cropFailed = ref(false)
const cropRotate = ref(0)
const fullImageUrl = ref('')

const cropStyle = computed(() => ({
  maxHeight: '100%',
  maxWidth: '100%',
  transform: `rotate(${cropRotate.value}deg)`,
}))

async function loadCrop() {
  revokeAll()
  cropFailed.value = false
  cropRotate.value = 0
  if (!current.value) return
  cropLoading.value = true
  try {
    const res: any = await examApi.questionCrop(props.submissionId, current.value.index + 1)
    const blob = res?.data instanceof Blob ? res.data : new Blob([res?.data ?? res], { type: 'image/jpeg' })
    cropUrl.value = URL.createObjectURL(blob)
  } catch {
    cropFailed.value = true
    try {
      const res: any = await examApi.submissionImageSilent(props.submissionId)
      const blob = res?.data instanceof Blob ? res.data : new Blob([res?.data ?? res], { type: 'image/jpeg' })
      fullImageUrl.value = URL.createObjectURL(blob)
    } catch { /* 没有原图：显示"没有扫描原图"占位 */ }
  } finally {
    cropLoading.value = false
  }
}

function rotateCrop() {
  cropRotate.value = (cropRotate.value + 90) % 360
}

function revokeAll() {
  if (cropUrl.value) { URL.revokeObjectURL(cropUrl.value); cropUrl.value = '' }
  if (fullImageUrl.value) { URL.revokeObjectURL(fullImageUrl.value); fullImageUrl.value = '' }
}

function save(next: boolean) {
  const q = current.value
  if (!q) return
  if (editScore.value == null) { ElMessage.warning('请先给分（快捷键或输入框）'); return }
  q.editScore = editScore.value
  q.editComment = editComment.value
  emit('save', q, next)
}

async function toggleFullscreen() {
  try {
    if (!fullscreen.value && panelEl.value?.requestFullscreen) {
      await panelEl.value.requestFullscreen()
      fullscreen.value = true
    } else if (document.fullscreenElement) {
      await document.exitFullscreen()
      fullscreen.value = false
    }
  } catch { /* 浏览器不支持则忽略 */ }
}

function onFsChange() {
  fullscreen.value = !!document.fullscreenElement
}

// 可见记分键数随窗口高度自适应（手机嵌入时 2 个）
function measure() {
  const h = window.innerHeight
  visibleCount.value = h < 640 ? 2 : 4
}

onMounted(() => {
  document.addEventListener('fullscreenchange', onFsChange)
  measure()
  window.addEventListener('resize', measure)
  if (current.value) {
    editScore.value = (current.value.editScore ?? current.value.score) as number | undefined
    editComment.value = current.value.editComment ?? current.value.comment ?? ''
    void loadCrop()
  }
})
onBeforeUnmount(() => {
  document.removeEventListener('fullscreenchange', onFsChange)
  window.removeEventListener('resize', measure)
  revokeAll()
})

function fmt(v: number | null | undefined) {
  return v == null ? '—' : (Math.round(v * 100) / 100).toString()
}
</script>

<style scoped>
.grading-panel {
  display: flex;
  flex-direction: column;
  height: 70vh;
}
.grading-panel.fullscreen { height: 100vh; background: #fff; padding: 8px; }
.nav-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding-bottom: 8px;
  border-bottom: 1px solid #ebeef5;
}
.q-dots {
  display: flex;
  gap: 4px;
  overflow-x: auto;
  max-width: 46vw;
  padding: 2px;
}
.dot {
  min-width: 26px;
  height: 26px;
  border: 1px solid #dcdfe6;
  background: #fff;
  border-radius: 4px;
  font-size: 12px;
  cursor: pointer;
  color: #606266;
}
.dot.done { background: #f0f9eb; border-color: #b3e19d; color: #67c23a; }
.dot.arb { background: #fef0f0; border-color: #fbc4c4; color: #f56c6c; font-weight: 700; }
.dot.active { outline: 2px solid #4285f4; }
.body {
  display: flex;
  gap: 12px;
  flex: 1;
  min-height: 0;
  padding-top: 8px;
}
.left {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-width: 0;
}
.pane {
  border: 1px solid #e4e7ed;
  border-radius: 6px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.pane.ref { flex: 0 0 auto; max-height: 34%; }
.pane.student { flex: 1; min-height: 0; }
.pane-title {
  padding: 5px 10px;
  background: #f5f7fa;
  font-size: 13px;
  color: #606266;
  display: flex;
  align-items: center;
  gap: 8px;
}
.pane-body { padding: 8px 10px; overflow: auto; font-size: 13px; line-height: 1.7; }
.img-body {
  display: flex;
  align-items: center;
  justify-content: center;
  background: #f0f2f5;
  height: 100%;
}
.q-content { margin-bottom: 6px; }
.q-rubric { color: #909399; }
.q-recognized { color: #e6a23c; }
.q-double { margin-top: 6px; display: flex; gap: 6px; align-items: center; font-size: 12px; color: #606266; }
.right {
  flex: 0 0 108px;
  display: flex;
  flex-direction: column;
  align-items: stretch;
}
.score-title { font-size: 12px; color: #909399; margin-bottom: 6px; text-align: center; }
.full-fallback { max-width: 100%; max-height: 100%; object-fit: contain; }
@media (max-width: 720px) {
  .body { flex-direction: column; }
  .right { flex: 0 0 auto; flex-direction: row; flex-wrap: wrap; align-items: flex-start; gap: 10px; }
  .right :deep(.score-keys) { width: 100%; flex-direction: row; justify-content: center; }
  .right :deep(.score-keys .arrow) { height: 36px; width: 40px; }
  .right :deep(.score-keys .key) { width: 52px; height: 36px; }
  .right .score-title { width: 100%; }
  .right :deep(.el-textarea), .right :deep(.el-input-number) { width: 140px !important; }
  .pane.ref { max-height: 30%; }
}
</style>
