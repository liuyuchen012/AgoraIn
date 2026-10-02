<template>
  <el-dialog v-model="visible" :title="`可视化排版 — ${paperTitle}`" width="94%" top="3vh"
             destroy-on-close @opened="load">
    <div class="wrap">
      <div class="toolbar">
        <span class="hint">
          拖动题目框移动、拖右下角改大小；自动吸附 1mm，重叠会标红。
          <b>松手即保存</b>，右侧预览就是打印出来的样子。
        </span>
        <span class="spacer"></span>
        <el-tag v-if="overlaps.size" type="danger" size="small">重叠 {{ overlaps.size }} 处</el-tag>
        <el-tag v-if="modified.size" type="warning" size="small">已改 {{ modified.size }} 题</el-tag>
        <span class="saved">{{ savedHint }}</span>
        <el-button size="small" @click="reloadSheet">刷新预览</el-button>
        <el-button size="small" :disabled="!modified.size" @click="resetAll">全部恢复自动</el-button>
      </div>

      <div class="canvas" v-loading="loading">
        <iframe ref="frameEl" class="frame" :src="iframeSrc" :style="{ height: docHeight + 'px' }"></iframe>
        <div class="overlay" :style="{ height: docHeight + 'px' }">
          <div v-for="it in items" :key="it.questionId"
               class="box" :class="{ pinned: modified.has(it.questionId), bad: overlaps.has(it.questionId) }"
               :style="boxStyle(it)"
               @pointerdown="startDrag($event, it, 'move')">
            <span class="tag">{{ it.questionNo }}</span>
            <div class="handle" @pointerdown.stop="startDrag($event, it, 'resize')"></div>
          </div>
        </div>
      </div>
    </div>
  </el-dialog>
</template>

<script setup lang="ts">
import { computed, nextTick, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { examApi, type SheetPlacement, type SheetQuery } from '@/api/client'

const props = defineProps<{ paperId: string; paperTitle: string; query: SheetQuery }>()

/** CSS 里 1mm = 96/25.4 px，iframe 内的渲染就是这个比例，覆盖层必须用同一个换算 */
const K = 96 / 25.4

const visible = defineModel<boolean>({ required: true })
const frameEl = ref<HTMLIFrameElement>()
const loading = ref(false)
const savedHint = ref('')
const items = ref<SheetPlacement[]>([])
const modified = ref(new Set<string>())
const paperH = ref(297)
const pageCount = ref(1)
const sheetVersion = ref(0)

const iframeSrc = computed(() => examApi.sheetUrl(props.paperId, props.query) + `&_v=${sheetVersion.value}`)
const docHeight = computed(() => Math.round(pageCount.value * (paperH.value - 6) * K))
/** 第 N 页的顶边（mm→px）：每页高 = 纸高 - 6mm（与 .page 容器一致） */
const pageTop = (page: number) => (page - 1) * (paperH.value - 6) * K

const boxStyle = (it: SheetPlacement) => ({
  left: `${it.x * K}px`,
  top: `${pageTop(it.page) + it.y * K}px`,
  width: `${it.w * K}px`,
  height: `${it.h * K}px`,
})

/** 重叠检测：同页内两两相交（老师自己摆的位置，只提示不拦） */
const overlaps = computed(() => {
  const bad = new Set<string>()
  for (let i = 0; i < items.value.length; i++) {
    for (let j = i + 1; j < items.value.length; j++) {
      const a = items.value[i], b = items.value[j]
      if (a.page !== b.page) continue
      if (a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h) {
        bad.add(a.questionId)
        bad.add(b.questionId)
      }
    }
  }
  return bad
})

async function load() {
  loading.value = true
  try {
    const data = await examApi.sheetLayout(props.paperId, props.query)
    items.value = data.items
    paperH.value = data.paperHeightMm
    pageCount.value = data.pageCount
    modified.value = new Set(data.items.filter(i => i.pinned).map(i => i.questionId))
    savedHint.value = ''
  } catch {
    ElMessage.error('读取版面失败')
  } finally {
    loading.value = false
  }
}

function reloadSheet() {
  sheetVersion.value++
}

/** 拖动/缩放：move 改位置，resize 改大小；都吸附 1mm，松手保存。
 *  用 Pointer Events 而不是 mouse：平板/触屏（老师常见）也能拖，且触屏拖框不会变成滚动画布。 */
function startDrag(e: PointerEvent, it: SheetPlacement, mode: 'move' | 'resize') {
  e.preventDefault()
  const startX = e.clientX, startY = e.clientY
  const x0 = it.x, y0 = it.y, w0 = it.w, h0 = it.h
  const snap = (v: number) => Math.round(v)
  const target = e.target as HTMLElement

  const onMove = (ev: PointerEvent) => {
    const dx = (ev.clientX - startX) / K
    const dy = (ev.clientY - startY) / K
    if (mode === 'move') {
      it.x = Math.max(0, snap(x0 + dx))
      it.y = Math.max(0, snap(y0 + dy))
    } else {
      it.w = Math.max(20, snap(w0 + dx))
      it.h = Math.max(8, snap(h0 + dy))
    }
  }
  const onUp = () => {
    window.removeEventListener('pointermove', onMove)
    window.removeEventListener('pointerup', onUp)
    window.removeEventListener('pointercancel', onUp)
    target.classList.remove('dragging')
    // 与初始位置相同（只是点了一下）就不必写库
    if (it.x === x0 && it.y === y0 && it.w === w0 && it.h === h0) return
    modified.value = new Set(modified.value).add(it.questionId)
    save()
  }
  window.addEventListener('pointermove', onMove)
  window.addEventListener('pointerup', onUp)
  window.addEventListener('pointercancel', onUp)
  target.classList.add('dragging')
}

let saveTimer: number | undefined
function save() {
  window.clearTimeout(saveTimer)
  savedHint.value = '保存中…'
  saveTimer = window.setTimeout(async () => {
    try {
      // 只写拖动过的题：没动过的继续走自动排版（否则整卷都被钉死，加题后不会自动重排）
      const payload = items.value.filter(i => modified.value.has(i.questionId))
        .map(i => ({ questionId: i.questionId, pageNo: i.page, xMm: i.x, yMm: i.y, wMm: i.w, hMm: i.h }))
      await examApi.saveSheetLayout(props.paperId, payload as never)
      savedHint.value = `已保存 ${new Date().toLocaleTimeString()}`
      await nextTick()
      reloadSheet()
    } catch {
      savedHint.value = '保存失败'
    }
  }, 500)
}

async function resetAll() {
  modified.value = new Set()
  savedHint.value = '恢复中…'
  try {
    await examApi.saveSheetLayout(props.paperId, [])
    ElMessage.success('已恢复全自动排版')
    await load()
    reloadSheet()
  } catch {
    savedHint.value = '恢复失败'
  }
}

defineExpose({ load })
</script>

<style scoped>
.wrap { display: flex; flex-direction: column; gap: 8px; }
.toolbar { display: flex; align-items: center; gap: 8px; font-size: 12px; color: #606266; }
.toolbar .hint { line-height: 1.6; }
.toolbar .spacer { flex: 1; }
.toolbar .saved { color: #909399; min-width: 120px; text-align: right; }
.canvas { position: relative; height: 68vh; overflow: auto; background: #f0f2f5; }
.frame { display: block; width: 100%; border: 0; background: #fff; }
.overlay { position: absolute; inset: 0; pointer-events: none; }
.box {
  position: absolute; box-sizing: border-box;
  border: 1px dashed rgba(64, 158, 255, .75); background: rgba(64, 158, 255, .06);
  pointer-events: auto; cursor: move; touch-action: none;
}
.box.pinned { border: 1.5px solid #e6a23c; background: rgba(230, 162, 60, .1); }
.box.bad { border-color: #f56c6c; background: rgba(245, 108, 108, .18); }
.box .tag {
  position: absolute; left: 0; top: 0; padding: 0 3px; font-size: 10px; color: #fff;
  background: rgba(64, 158, 255, .9); border-bottom-right-radius: 3px;
}
.box.pinned .tag { background: #e6a23c; }
.box.bad .tag { background: #f56c6c; }
.box .handle {
  position: absolute; right: -4px; bottom: -4px; width: 10px; height: 10px;
  background: #fff; border: 1.5px solid #409eff; border-radius: 2px; cursor: nwse-resize;
  pointer-events: auto;
}
.box.pinned .handle { border-color: #e6a23c; }
</style>
