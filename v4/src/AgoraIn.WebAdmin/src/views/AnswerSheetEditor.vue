<template>
  <div class="editor">
    <!-- 左侧：设置面板 -->
    <div class="panel-left">
      <el-scrollbar height="100%">
        <div class="panel-section">
          <div class="section-title">答题卡基础信息</div>
          <el-form label-width="80px" size="small">
            <el-form-item label="试卷标题">
              <el-input v-model="form.title" placeholder="如：2026年秋季期中考试" />
            </el-form-item>
            <el-form-item label="科目">
              <el-input v-model="form.subject" placeholder="如：数学" />
            </el-form-item>
            <el-form-item label="班级">
              <el-select v-model="form.classId" clearable placeholder="全部班级" style="width:100%">
                <el-option v-for="c in classes" :key="c.id" :label="c.name" :value="c.id" />
              </el-select>
            </el-form-item>
          </el-form>
        </div>

        <div class="panel-section">
          <div class="section-title">答题卡设置</div>
          <el-form label-width="100px" size="small">
            <el-form-item label="密封线">
              <el-switch v-model="form.hasSealLine" />
            </el-form-item>
            <el-form-item label="考号区域">
              <el-radio-group v-model="form.idAreaType">
                <el-radio value="bubble">考号填涂区</el-radio>
                <el-radio value="barcode">条形码</el-radio>
                <el-radio value="handwrite">手写考号</el-radio>
              </el-radio-group>
            </el-form-item>
            <el-form-item label="注意事项">
              <el-switch v-model="form.hasNotes" />
            </el-form-item>
            <el-form-item label="AB卷">
              <el-switch v-model="form.hasAB" />
            </el-form-item>
            <el-form-item label="红色答题卡">
              <el-switch v-model="form.isRed" />
            </el-form-item>
            <el-form-item label="客观题竖排">
              <el-switch v-model="form.verticalObjective" />
            </el-form-item>
            <el-form-item label="分区答题卡">
              <el-switch v-model="form.partitioned" />
            </el-form-item>
          </el-form>
        </div>

        <div class="panel-section">
          <div class="section-title">添加题目</div>
          <div class="btn-group">
            <el-button size="small" @click="addQuestion(0)">+ 客观题</el-button>
            <el-button size="small" @click="addQuestion(3)">+ 填空题</el-button>
            <el-button size="small" @click="addQuestion(4)">+ 解答题</el-button>
          </div>
          <div class="btn-group" style="margin-top:6px">
            <el-button size="small" @click="addQuestion(2)">+ 判断题</el-button>
            <el-button size="small" @click="addQuestion(5)">+ 作文题</el-button>
          </div>
        </div>

        <div class="panel-section">
          <div class="section-title">题目列表（共 {{questions.length}} 题，{{totalScore}} 分）</div>
          <div v-for="(q, idx) in questions" :key="idx" class="q-item">
            <div class="q-header">
              <span class="q-num">{{idx+1}}</span>
              <el-tag size="small" :type="qTypeTag(q.type)">{{qTypeName(q.type)}}</el-tag>
              <el-input-number v-model="q.score" size="small" :min="0.5" :max="100" :step="0.5"
                               style="width:80px;margin-left:auto" />
              <span style="font-size:11px;color:#909399;margin-left:2px">分</span>
              <el-button link type="danger" size="small" @click="removeQuestion(idx)" style="margin-left:4px">✕</el-button>
            </div>
            <el-input v-model="q.content" size="small" :type="q.type>=3?'textarea':'text'"
                      :rows="q.type>=3?2:1" placeholder="题目内容/题干" style="margin-top:4px" />
            <template v-if="q.type===0||q.type===1">
              <div v-for="(opt,oi) in getOpts(q)" :key="oi" class="opt-row">
                <span class="opt-key">{{String.fromCharCode(65+oi)}}</span>
                <el-input v-model="opt.text" size="small" placeholder="选项" style="flex:1" />
              </div>
              <el-button size="small" text @click="addOpt(q)" style="margin-top:2px">+ 选项</el-button>
            </template>
            <el-input v-model="q.standardAnswer" size="small" placeholder="标准答案" style="margin-top:4px" />
          </div>
        </div>
      </el-scrollbar>
    </div>

    <!-- 右侧：实时预览 -->
    <div class="panel-right">
      <div class="preview-toolbar">
        <el-button type="primary" @click="refreshPreview">刷新预览</el-button>
        <el-button type="success" @click="downloadPdf">下载 PDF</el-button>
        <el-button @click="printSheet">打印</el-button>
      </div>
      <div class="preview-frame" ref="previewContainer">
        <iframe ref="previewIframe" style="width:100%;height:100%;border:none" />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
import { ElMessage } from 'element-plus'
import { examApi, classApi, type ClassRow } from '@/api/client'

const classes = ref<ClassRow[]>([])
const previewIframe = ref<HTMLIFrameElement>()

const form = ref({
  title: '答题卡',
  subject: '',
  classId: '',
  hasSealLine: false,
  idAreaType: 'bubble' as 'bubble'|'barcode'|'handwrite',
  hasNotes: true,
  hasAB: false,
  isRed: false,
  verticalObjective: false,
  partitioned: false,
})

interface QuestionItem {
  type: number
  score: number
  content: string
  standardAnswer: string
  optionsJson: string
  rubric: string
}

const questions = ref<QuestionItem[]>([])
const totalScore = computed(() => questions.value.reduce((s,q) => s+(q.score||0), 0))

function qTypeName(t: number) {
  return {0:'单选',1:'多选',2:'判断',3:'填空',4:'简答',5:'作文'}[t]||'未知'
}
function qTypeTag(t: number) {
  return {0:'',1:'warning',2:'success',3:'info',4:'danger',5:'danger'}[t as any]||''
}

function addQuestion(type: number) {
  questions.value.push({ type, score: type===5?40:type>=3?5:1, content:'', standardAnswer:'', optionsJson:'[]', rubric:'' })
}
function removeQuestion(idx: number) { questions.value.splice(idx,1) }

function getOpts(q: QuestionItem): {key:string;text:string}[] {
  try { const a=JSON.parse(q.optionsJson||'[]'); return Array.isArray(a)?a:[] } catch {} return [{key:'A',text:''},{key:'B',text:''}]
}
function addOpt(q: QuestionItem) {
  const opts=getOpts(q); opts.push({key:String.fromCharCode(65+opts.length),text:''}); q.optionsJson=JSON.stringify(opts)
}

function generateHtml(): string {
  const obj = questions.value.filter(q=>q.type<=2)
  const subj = questions.value.filter(q=>q.type>=3)

  let html = `<!DOCTYPE html><html><head><meta charset="utf-8"><style>
@page{size:A4;margin:10mm}*{box-sizing:border-box}
body{font-family:"SimSun","Songti SC",serif;margin:0;color:#000}
.anchor{position:absolute;width:6mm;height:6mm;background:#000}
.anchor.tl{top:4mm;left:4mm}.anchor.tr{top:4mm;right:4mm}
.anchor.bl{bottom:4mm;left:4mm}.anchor.br{bottom:4mm;right:4mm}
.sheet{position:relative;width:190mm;min-height:277mm;padding:14mm 10mm 10mm}
.title{text-align:center;font-size:16pt;font-weight:bold;margin-bottom:2mm}
.subtitle{text-align:center;font-size:10pt;color:#333;margin-bottom:4mm}
.info{border:0.4mm solid #000;padding:3mm;margin-bottom:4mm;display:flex;gap:4mm}
.info-left{flex:1;font-size:10pt;line-height:8mm}
.info-right{width:70mm}
.write-line{border-bottom:0.3mm solid #666;display:inline-block;min-width:30mm}
.id-grid{display:flex;gap:1.5mm}.id-col{text-align:center}
.id-col .col-label{font-size:7pt;margin-bottom:0.5mm}
.bubble{width:4.5mm;height:4.5mm;border:0.3mm solid #000;border-radius:50%;margin:0.6mm auto}
.section-title{font-size:11pt;font-weight:bold;margin:3mm 0 2mm;border-left:1mm solid #000;padding-left:2mm}
.omr-grid{column-count:3;column-gap:4mm}
.omr-item{break-inside:avoid;margin-bottom:1.6mm;font-size:9pt}
.omr-row{display:flex;align-items:center;gap:1mm}
.omr-no{width:7mm;text-align:right;font-weight:bold}
.omr-opts{display:flex;gap:1mm}
.omr-opt{width:4.5mm;height:4.5mm;border:0.3mm solid #000;border-radius:50%;font-size:6pt;text-align:center;line-height:4.5mm}
.answer-box{border:0.4mm solid #000;margin-bottom:3mm}
.answer-head{font-size:9pt;padding:1mm 2mm;border-bottom:0.3mm dashed #888}
.answer-body{min-height:var(--h,30mm)}
.answer-lines{background-image:repeating-linear-gradient(transparent,transparent 7mm,#ccc 7mm,#ccc 7.2mm)}
.footer{position:absolute;bottom:12mm;left:10mm;right:10mm;display:flex;justify-content:space-between;align-items:center;font-size:8pt;color:#444;border-top:0.3mm solid #999;padding-top:2mm}
.qr{width:18mm;height:18mm;border:0.3mm solid #000;display:flex;align-items:center;justify-content:center;font-size:6pt;text-align:center}
.seal{position:absolute;left:0;top:0;bottom:0;width:18mm;border-right:0.3mm dashed #999;display:flex;align-items:center;justify-content:center;writing-mode:vertical-rl;font-size:9pt;letter-spacing:3mm;color:#666}
</style></head><body><div class="sheet">`

  // 四角定位标记
  html += `<div class="anchor tl"></div><div class="anchor tr"></div><div class="anchor bl"></div><div class="anchor br"></div>`

  // 密封线
  if (form.value.hasSealLine) html += `<div class="seal">密 封 线 内 不 要 答 题</div>`

  // 标题
  html += `<div class="title">${esc(form.value.title)}</div>`
  html += `<div class="subtitle">科目：${esc(form.value.subject||'—')}　总分：${totalScore.value}</div>`

  // 学生信息区
  html += `<div class="info"><div class="info-left">`
  html += `班级：<span class="write-line" style="min-width:40mm"></span><br>`
  html += `姓名：<span class="write-line" style="min-width:40mm"></span><br>`
  html += `学号：<span class="write-line" style="min-width:40mm"></span>`
  html += `</div><div class="info-right">`

  if (form.value.idAreaType==='bubble') {
    html += `<div style="font-size:8pt;margin-bottom:1mm">考号填涂区</div><div class="id-grid">`
    for(let c=0;c<8;c++) {
      html += `<div class="id-col"><div class="col-label">-</div>`
      for(let n=0;n<10;n++) html += `<div class="bubble"></div>`
      html += `</div>`
    }
    html += `</div>`
  } else if (form.value.idAreaType==='barcode') {
    html += `<div style="font-size:8pt">条形码区域</div><div style="height:20mm;border:0.3mm solid #000;margin-top:2mm"></div>`
  } else {
    html += `<div style="font-size:8pt">手写考号</div><div style="border-bottom:0.3mm solid #000;margin-top:4mm"></div>`
  }
  html += `</div></div>`

  // 注意事项
  if (form.value.hasNotes) {
    html += `<div style="border:0.4mm solid #000;padding:2mm 3mm;margin-bottom:3mm;font-size:9pt">`
    html += `<div style="font-weight:bold;text-align:center;margin-bottom:1mm">注 意 事 项</div>`
    html += `<div>1.　答题前请将姓名、班级、学号填写清楚。</div>`
    html += `<div>2.　客观题答题,必须使用2B铅笔填涂,修改时用橡皮擦干净。</div>`
    html += `<div>3.　主观题必须使用黑色签字笔书写。</div>`
    html += `<div>4.　必须在题号对应的答题区域内作答,超出答题区域书写无效。</div>`
    html += `<div>5.　保持答卷清洁完整。</div>`
    html += `</div>`
  }

  // 客观题区
  if (obj.length > 0) {
    html += `<div class="section-title">一、客观题（请填涂所选选项）</div><div class="omr-grid">`
    for(const q of obj) {
      html += `<div class="omr-item"><div class="omr-row"><div class="omr-no">${questions.value.indexOf(q)+1}.</div><div class="omr-opts">`
      if (q.type===2) { // 判断
        html += `<div class="omr-opt">√</div><div class="omr-opt">×</div>`
      } else {
        const opts = getOpts(q)
        for(const o of opts) html += `<div class="omr-opt">${esc(o.key)}</div>`
      }
      html += `</div></div></div>`
    }
    html += `</div>`
  }

  // 主观题区
  if (subj.length > 0) {
    html += `<div class="section-title">二、主观题（请在框内作答）</div>`
    for(const q of subj) {
      const h = q.type===5?'80mm':q.type===4?'40mm':'20mm'
      const lines = q.type>=4?' answer-lines':''
      html += `<div class="answer-box" style="--h:${h}"><div class="answer-head">第${questions.value.indexOf(q)+1}题（${q.score}分）</div><div class="answer-body${lines}"></div></div>`
    }
  }

  // 页脚
  html += `<div class="footer"><div>请用2B铅笔填涂，保持卡面整洁</div><div class="qr">二维码</div></div>`
  html += `</div></body></html>`
  return html
}

function esc(s: string) { return (s||'').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;') }

function refreshPreview() {
  const html = generateHtml()
  const iframe = previewIframe.value
  if (!iframe) return
  const doc = iframe.contentDocument || iframe.contentWindow?.document
  if (doc) { doc.open(); doc.write(html); doc.close() }
}

function printSheet() {
  refreshPreview()
  setTimeout(() => {
    const iframe = previewIframe.value
    iframe?.contentWindow?.print()
  }, 300)
}

function downloadPdf() {
  printSheet() // 浏览器打印对话框可选"另存为PDF"
  ElMessage.info('在打印对话框中选择「另存为 PDF」即可导出')
}

onMounted(async () => {
  try { classes.value = await classApi.list() } catch {}
  await nextTick()
  refreshPreview()
})
</script>

<style scoped>
.editor { display: flex; height: calc(100vh - 120px); gap: 0; }
.panel-left { width: 380px; border-right: 1px solid #e4e7ed; background: #fff; flex-shrink: 0; }
.panel-right { flex: 1; display: flex; flex-direction: column; background: #f5f7fa; }
.preview-toolbar { padding: 8px 16px; background: #fff; border-bottom: 1px solid #e4e7ed; display: flex; gap: 8px; }
.preview-frame { flex: 1; padding: 16px; overflow: auto; display: flex; justify-content: center; }
.preview-frame iframe { width: 210mm; min-height: 297mm; background: #fff; box-shadow: 0 2px 12px rgba(0,0,0,0.1); }

.panel-section { padding: 12px 16px; border-bottom: 1px solid #f0f0f0; }
.section-title { font-size: 13px; font-weight: 600; margin-bottom: 10px; color: #303133; }
.btn-group { display: flex; gap: 6px; flex-wrap: wrap; }
.q-item { border: 1px solid #e4e7ed; border-radius: 6px; padding: 8px 10px; margin-bottom: 8px; background: #fafafa; }
.q-header { display: flex; align-items: center; gap: 6px; }
.q-num { font-weight: 700; font-size: 13px; color: #409eff; min-width: 20px; }
.opt-row { display: flex; align-items: center; gap: 4px; margin-top: 2px; }
.opt-key { font-weight: 600; font-size: 12px; color: #606266; min-width: 16px; }
</style>
