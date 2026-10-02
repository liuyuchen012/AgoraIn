<template>
  <!-- 动态扫卡页（App 内嵌 WebView / 浏览器均可）：摄像头实时取流，
       **必须读到卡面右下角的页码二维码**才算认到页，同一页在镜头前保持 2 秒自动上传。
       服务端仍做完整 OMR 识别（四角标记/气泡/考号），此页只负责"取到一张完整、稳定的页面帧"。 -->
  <div class="scan-page">
    <div class="bar">
      <span class="title">动态扫卡</span>
      <el-select v-model="paperId" placeholder="选择试卷" size="small" style="flex:1;min-width:150px"
                 :loading="loadingPapers" @change="resetChain">
        <el-option v-for="p in papers" :key="p.id" :label="`${p.title}（${p.submissionCount || 0} 份）`" :value="p.id" />
      </el-select>
      <el-button size="small" :disabled="!chainId" @click="resetChain">换新一份</el-button>
    </div>

    <div class="stage">
      <video ref="videoEl" playsinline muted autoplay />
      <canvas ref="overlayEl" class="overlay" />
      <div class="badge" :class="state">
        <template v-if="state === 'idle'">{{ paperId ? '启动摄像头…' : '先选择试卷' }}</template>
        <template v-else-if="state === 'starting'">正在打开摄像头…</template>
        <template v-else-if="state === 'camera-error'">摄像头打开失败（需要 https 与相机权限）</template>
        <template v-else-if="state === 'searching'">把答题卡整页放进取景框（需看到右下角二维码）</template>
        <template v-else-if="state === 'locked'">第 {{ lockedPage }} 页 · 保持不动 {{ holdProgress }}%</template>
        <template v-else-if="state === 'uploading'">正在上传识别…</template>
        <template v-else-if="state === 'rejected'">{{ lastError || '未识别到答题卡' }}</template>
      </div>
      <div v-if="state === 'locked' || state === 'uploading'" class="ring"
           :style="{ background: `conic-gradient(#4285f4 ${ringDeg}deg, rgba(255,255,255,.25) 0deg)` }" />
    </div>

    <div v-if="cameraError" class="fallback">
      <input ref="fileEl" type="file" accept="image/*" style="display:none" @change="onPickFile" />
      <el-button size="small" @click="retryCamera">重试摄像头</el-button>
      <el-button v-if="!inApp" size="small" type="primary" @click="fileEl?.click()">拍照 / 选图上传</el-button>
    </div>

    <div class="results">
      <div v-for="(r, i) in uploaded" :key="i" class="card" :class="{ bad: r.failed }">
        <img v-if="r.thumb" :src="r.thumb" />
        <div class="info">
          <b>{{ r.label }}</b>
          <span>{{ r.detail }}</span>
          <span v-if="r.warning" class="warn">{{ r.warning }}</span>
        </div>
        <el-button v-if="r.missingRef" link type="warning" size="small" @click="forceUpload(r)">仍然录入</el-button>
      </div>
      <div v-if="!uploaded.length" class="empty">识别结果会显示在这里（连续扫描自动归并为同一份答卷）</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import jsQR from 'jsqr'
import { examApi, applyToken, type PaperRow } from '@/api/client'

const route = useRoute()
const router = useRouter()

const papers = ref<PaperRow[]>([])
const paperId = ref('')
const loadingPapers = ref(false)
const chainId = ref<string | null>(null)
const uploaded = ref<{
  label: string; detail: string; warning?: string; failed?: boolean; missingRef?: boolean
  thumb?: string; file?: File; pageNo?: number
}[]>([])

const videoEl = ref<HTMLVideoElement>()
const overlayEl = ref<HTMLCanvasElement>()
const fileEl = ref<HTMLInputElement>()

type State = 'idle' | 'starting' | 'searching' | 'locked' | 'uploading' | 'camera-error' | 'rejected'
const state = ref<State>('idle')
const lockedPage = ref(0)
const holdProgress = ref(0)
const lastError = ref('')
const cameraError = ref(false)

/** App 内嵌时隐藏"拍照上传"兜底（WebView 文件选择器不一定可用；摄像头就是主路径） */
const inApp = computed(() => route.query.app === '1')

// ── 二维码 + 稳定计时 ──
const HOLD_MS = 2000          // 同一页保持 2 秒才上传
const QR_LOST_RESET_MS = 500  // 二维码丢失超过 0.5s 视为页面移开，重计
let stream: MediaStream | null = null
let scanTimer: number | undefined
let rafTimer: number | undefined
const qrCanvas = document.createElement('canvas')
const qrCtx = qrCanvas.getContext('2d', { willReadFrequently: true })
const shotCanvas = document.createElement('canvas')
const shotCtx = shotCanvas.getContext('2d')

let holdKey = ''          // 当前锁定的 agorain:sheet:{paper}:p{n}
let holdStart = 0         // 该 key 首次连续出现的时间
let lastSeen = 0          // 最近一次读到二维码的时间
let lastUploadedKey = ''  // 已上传过的页（防止同一页连传）

const ringDeg = computed(() => Math.round(360 * holdProgress.value / 100))

function parsePayload(data: string): { paperId: string; page: number } | null {
  const parts = data.split(':')
  if (parts.length !== 4 || parts[0] !== 'agorain' || parts[1] !== 'sheet') return null
  const page = parts[3].startsWith('p') ? Number(parts[3].slice(1)) : NaN
  if (!Number.isInteger(page) || page < 1) return null
  return { paperId: parts[2], page }
}

async function startCamera() {
  state.value = 'starting'
  cameraError.value = false
  try {
    stream = await navigator.mediaDevices.getUserMedia({
      video: { facingMode: { ideal: 'environment' }, width: { ideal: 1920 }, height: { ideal: 1440 } },
      audio: false,
    })
    if (videoEl.value) {
      videoEl.value.srcObject = stream
      await videoEl.value.play()
    }
    state.value = 'searching'
    scheduleScan()
  } catch (e: any) {
    cameraError.value = true
    state.value = 'camera-error'
    lastError.value = e?.message || ''
  }
}

function stopCamera() {
  stream?.getTracks().forEach(t => t.stop())
  stream = null
  window.clearTimeout(scanTimer)
  cancelAnimationFrame(rafTimer!)
}

/** 每帧抽一帧低分辨率给 jsQR：必须解出本试卷的页码二维码（卡面右下角）才认页 */
function scheduleScan() {
  scanTimer = window.setTimeout(() => {
    const v = videoEl.value
    if (v && v.readyState >= 2 && qrCtx && state.value !== 'uploading') {
      const w = 560
      const h = Math.round(v.videoHeight / Math.max(1, v.videoWidth) * w) || 420
      qrCanvas.width = w; qrCanvas.height = h
      qrCtx.drawImage(v, 0, 0, w, h)
      let page: number | null = null
      try {
        const img = qrCtx.getImageData(0, 0, w, h)
        const code = jsQR(img.data, w, h, { inversionAttempts: 'dontInvert' })
        const hit = code?.data ? parsePayload(code.data) : null
        // 只认当前选中试卷的页码二维码；别的卷 / 其他二维码不算
        if (hit && hit.paperId === paperId.value) page = hit.page
      } catch { /* 单帧失败无所谓，下一帧再来 */ }

      const now = Date.now()
      if (page != null) {
        const key = `${paperId.value}:p${page}`
        if (key !== holdKey) { holdKey = key; holdStart = now }
        lastSeen = now
        lockedPage.value = page
        const held = now - holdStart
        holdProgress.value = Math.min(100, Math.round(held / HOLD_MS * 100))
        if (held >= HOLD_MS && key !== lastUploadedKey) {
          void captureAndUpload(page)
        } else {
          state.value = 'locked'
        }
      } else if (now - lastSeen > QR_LOST_RESET_MS) {
        holdKey = ''
        holdProgress.value = 0
        if (state.value !== 'rejected') state.value = 'searching'
      }
      drawOverlay()
    }
    if (stream) scheduleScan()
  }, 140)
}

/** 取景框辅助线（按检测状态变色） */
function drawOverlay() {
  const c = overlayEl.value, v = videoEl.value
  if (!c || !v) return
  if (c.width !== v.clientWidth) { c.width = v.clientWidth; c.height = v.clientHeight }
  const ctx = c.getContext('2d')
  if (!ctx) return
  ctx.clearRect(0, 0, c.width, c.height)
  ctx.strokeStyle = state.value === 'locked' ? '#34a853' : 'rgba(255,255,255,.85)'
  ctx.lineWidth = 3
  const m = 14
  const w = c.width - m * 2, h = c.height - m * 2, L = 34
  ctx.beginPath()
  for (const [x, y, dx, dy] of [[m, m, 1, 1], [m + w, m, -1, 1], [m, m + h, 1, -1], [m + w, m + h, -1, -1]] as const) {
    ctx.moveTo(x + dx * L, y); ctx.lineTo(x, y); ctx.lineTo(x, y + dy * L)
  }
  ctx.stroke()
  // 二维码预期位置提示（右下角）
  ctx.fillStyle = state.value === 'locked' ? '#34a853' : 'rgba(255,255,255,.7)'
  ctx.font = '12px sans-serif'
  ctx.fillText('▣ 右下角二维码需清晰可见', m, m + h + 16 > c.height ? m + h - 6 : m + h + 14)
}

async function captureAndUpload(page: number) {
  const v = videoEl.value
  if (!v || !paperId.value) return
  state.value = 'uploading'
  holdProgress.value = 100
  try {
    const file = await shoot(v)
    await doUpload(file, page)
  } catch (e: any) {
    state.value = 'rejected'
    lastError.value = e?.response?.data?.error || e?.message || '上传失败'
  } finally {
    holdProgress.value = 0
    holdKey = ''
    if (state.value === 'uploading') state.value = 'searching'
  }
}

function shoot(v: HTMLVideoElement): Promise<File> {
  const maxSide = 1600
  const scale = Math.min(1, maxSide / Math.max(v.videoWidth, v.videoHeight))
  shotCanvas!.width = Math.round(v.videoWidth * scale)
  shotCanvas!.height = Math.round(v.videoHeight * scale)
  shotCtx!.drawImage(v, 0, 0, shotCanvas!.width, shotCanvas!.height)
  return new Promise((resolve, reject) => {
    shotCanvas!.toBlob(
      b => b ? resolve(new File([b], `scan-p${Date.now()}.jpg`, { type: 'image/jpeg' })) : reject(new Error('取帧失败')),
      'image/jpeg', 0.9)
  })
}

async function doUpload(file: File, page: number, allowMissingId = false) {
  const res = await examApi.uploadSubmission(paperId.value, file, { submissionId: chainId.value, allowMissingId })
  chainId.value = res.submissionId || chainId.value
  lastUploadedKey = `${paperId.value}:p${page}`
  const total = res.totalPages || 1
  uploaded.value.unshift({
    label: `第 ${res.pageNo || page} / ${total} 页`,
    detail: `考号 ${res.recognizedStudent || '未识别'} · 识别 ${res.answers?.length || 0} 题`
      + (res.autoScored ? ` · 自动判分 ${res.autoScored} 题` : ''),
    warning: res.warning || undefined,
    thumb: URL.createObjectURL(file),
    file, pageNo: page,
    missingRef: res.pageNo === 1 && !res.recognizedStudent,
  })
  state.value = 'searching'
}

/** 考号没涂：老师确认后强行录入（之后在批改页手动绑定学生） */
async function forceUpload(item: (typeof uploaded.value)[number]) {
  if (!item.file || item.pageNo == null) return
  try {
    item.missingRef = false
    await doUpload(item.file, item.pageNo, true)
    ElMessage.success('已录入（未识别考号，请在批改页手动绑定学生）')
  } catch (e: any) {
    item.missingRef = true
    ElMessage.error(e?.response?.data?.error || '录入失败')
  }
}

async function onPickFile(ev: Event) {
  const f = (ev.target as HTMLInputElement).files?.[0]
  if (!f || !paperId.value) return
  state.value = 'uploading'
  try {
    await doUpload(f, 1)
  } catch (e: any) {
    state.value = 'rejected'
    lastError.value = e?.response?.data?.error || e?.message || '上传失败'
  } finally {
    if (state.value === 'uploading') state.value = 'searching'
    ;(ev.target as HTMLInputElement).value = ''
  }
}

function resetChain() {
  chainId.value = null
  lastUploadedKey = ''
  holdKey = ''
  uploaded.value = []
  state.value = paperId.value ? 'searching' : 'idle'
}

async function retryCamera() {
  stopCamera()
  await startCamera()
}

onMounted(async () => {
  const token = route.query.token as string | undefined
  if (token) {
    applyToken(token)
    await router.replace({ path: route.path, query: inApp.value ? { app: '1' } : {} })
  }
  try {
    papers.value = (await examApi.list<PaperRow>()) || []
  } catch { /* 未登录等：由拦截器提示 */ }
  const pre = route.query.paper as string | undefined
  if (pre && papers.value.some(p => p.id === pre)) paperId.value = pre
  else if (papers.value.length === 1) paperId.value = papers.value[0]!.id
  window.addEventListener('resize', drawOverlay)
  await startCamera()
})

onBeforeUnmount(() => {
  stopCamera()
  window.removeEventListener('resize', drawOverlay)
  uploaded.value.forEach(u => u.thumb && URL.revokeObjectURL(u.thumb))
})
</script>

<style scoped>
.scan-page { display: flex; flex-direction: column; height: 100vh; background: #10141a; color: #e5eaf0; }
.bar { display: flex; gap: 8px; align-items: center; padding: 8px 10px; background: #1a2028; }
.bar .title { font-weight: 700; font-size: 14px; white-space: nowrap; }

.stage { position: relative; flex: 1; min-height: 0; background: #000; overflow: hidden; }
.stage video { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: contain; }
.overlay { position: absolute; inset: 0; width: 100%; height: 100%; pointer-events: none; }
.badge {
  position: absolute; left: 50%; transform: translateX(-50%);
  bottom: 14px; padding: 6px 14px; border-radius: 16px; font-size: 13px; white-space: nowrap;
  background: rgba(16, 20, 26, .78); border: 1px solid rgba(255,255,255,.2);
}
.badge.locked { border-color: #34a853; color: #b8ecbf; }
.badge.uploading { border-color: #4285f4; color: #cfe0ff; }
.badge.rejected, .badge.camera-error { border-color: #f56c6c; color: #ffd6d6; }
.ring {
  position: absolute; right: 16px; bottom: 16px; width: 44px; height: 44px; border-radius: 50%;
  -webkit-mask: radial-gradient(circle at center, transparent 55%, #000 56%);
          mask: radial-gradient(circle at center, transparent 55%, #000 56%);
}

.fallback { display: flex; gap: 8px; padding: 8px 10px; background: #1a2028; }
.results { max-height: 30vh; overflow: auto; padding: 8px 10px; display: flex; flex-direction: column; gap: 6px; }
.card {
  display: flex; gap: 8px; align-items: center; padding: 6px 8px;
  background: #1a2028; border: 1px solid #2a323d; border-radius: 8px;
}
.card.bad { border-color: #f56c6c; }
.card img { width: 44px; height: 58px; object-fit: cover; border-radius: 4px; background: #000; }
.card .info { flex: 1; display: flex; flex-direction: column; font-size: 12px; gap: 2px; min-width: 0; }
.card .warn { color: #e6a23c; font-size: 11px; }
.empty { color: #5b6673; font-size: 12px; text-align: center; padding: 12px; }
</style>
