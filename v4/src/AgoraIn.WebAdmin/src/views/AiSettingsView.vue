<template>
  <el-card shadow="never" class="page-card">
    <template #header>
      <div class="card-header">
        <span class="card-title">AI 批改设置</span>
        <el-button size="small" @click="loadAll">刷新</el-button>
      </div>
    </template>

    <el-form :model="form" label-width="180px" style="max-width:640px" v-loading="loading">
      <el-form-item label="API 地址">
        <el-input v-model="form.baseUrl" placeholder="https://api.deepseek.com" style="max-width:420px" />
        <div class="tip">OpenAI 兼容服务地址（保存到服务器数据库，优先于 appsettings.json）。常见地址：
          DeepSeek https://api.deepseek.com ·
          智谱 https://open.bigmodel.cn/api/paas/v4 ·
          通义 https://dashscope.aliyuncs.com/compatible-mode/v1 ·
          自建中转填其 BaseUrl。地址末尾已含 /v1 /v4 等版本段时自动适配，无需再补 /v1。</div>
      </el-form-item>
      <el-form-item label="API 密钥">
        <el-input v-model="apiKeyInput" type="password" show-password
                  :placeholder="form.hasApiKey ? `已配置（${form.apiKeyMasked}），输入新值覆盖，留空不变` : '尚未配置，请输入 API 密钥'"
                  style="max-width:420px" />
        <div style="width:100%">
          <el-button v-if="form.hasApiKey" link type="danger" size="small" @click="clearApiKey">清除密钥（回落服务器配置）</el-button>
        </div>
        <div class="tip">密钥保存在服务端数据库（读取时脱敏），优先于 appsettings.json 的 DeepSeek:ApiKey；OpenAI 兼容协议</div>
      </el-form-item>
      <el-form-item label="批改模型（文本）">
        <el-input v-model="form.model" placeholder="如：deepseek-chat / glm-4-flash" />
        <div class="tip">OpenAI 兼容协议；模型名、密钥由服务器 appsettings.json 的 DeepSeek:ApiKey/BaseUrl 决定</div>
      </el-form-item>
      <el-form-item label="识别模型（视觉）">
        <el-input v-model="form.visionModel" placeholder="如：glm-4v-flash / qwen-vl-plus（留空 = 使用批改模型）" />
        <div class="tip">答题卡图片识别（考号涂卡 + 客观题 OMR）必须使用支持图像输入的多模态模型</div>
      </el-form-item>
      <el-form-item label="采样温度">
        <el-input-number v-model="form.temperature" :min="0" :max="2" :step="0.1" />
        <div class="tip">批改一致性要求低温（推荐 0~0.2）：同一题多次批改波动应可接受</div>
      </el-form-item>
      <el-form-item label="单次调用 max_tokens">
        <el-input-number v-model="form.maxTokens" :min="128" :max="32768" :step="128" />
      </el-form-item>
      <el-form-item label="低置信度阈值">
        <el-input-number v-model="form.humanReviewThreshold" :min="0" :max="1" :step="0.05" />
        <div class="tip">AI 置信度低于该值时，提交自动进入"待人工"队列</div>
      </el-form-item>
      <el-form-item label="失败重试次数">
        <el-input-number v-model="form.retries" :min="0" :max="5" />
      </el-form-item>
      <el-form-item label="允许发送作答图像给大模型">
        <el-switch v-model="form.allowImageToCloud" />
        <div class="tip">隐私开关（默认开启）：关闭后学生作答图像不发送给第三方模型，主观题只支持教师手判</div>
      </el-form-item>
      <el-form-item label="主观题阅卷提示词模板">
        <el-input v-model="form.gradingPromptTemplate" type="textarea" :rows="7"
                  placeholder="留空使用内置默认模板。占位符：{Type} 题型 / {Question} 题干 / {StudentAnswer} 学生答案 / {StandardAnswer} 标准答案 / {Rubric} 评分要点 / {MaxScore} 满分" />
        <div class="tip">模板会与每题的评分要点（rubric）组合生成 AI 阅卷提示词；修改后对所有主观题 AI 批改即时生效</div>
      </el-form-item>
      <el-form-item>
        <el-button type="primary" :loading="saving" @click="save">保存设置</el-button>
      </el-form-item>
    </el-form>

    <el-divider content-position="left">调用日志与 Token 消耗</el-divider>
    <div style="margin-bottom:10px">
      累计消耗 Token：<el-tag type="warning">{{ logs?.totalTokens ?? 0 }}</el-tag>
    </div>
    <el-table :data="logs?.logs || []" size="small" stripe max-height="380" border>
      <el-table-column prop="createdAt" label="时间" width="170">
        <template #default="{ row }">{{ formatTime(row.createdAt) }}</template>
      </el-table-column>
      <el-table-column label="场景" width="110">
        <template #default="{ row }">{{ scenarioNames[row.endpoint] || row.endpoint }}</template>
      </el-table-column>
      <el-table-column prop="model" label="模型" min-width="140" />
      <el-table-column prop="promptTokens" label="输入 Token" width="100" />
      <el-table-column prop="completionTokens" label="输出 Token" width="100" />
      <el-table-column prop="totalTokens" label="合计" width="80" />
      <el-table-column prop="durationMs" label="耗时(ms)" width="90" />
      <el-table-column label="结果" width="80">
        <template #default="{ row }">
          <el-tag size="small" :type="row.success ? 'success' : 'danger'">{{ row.success ? '成功' : '失败' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="error" label="失败原因" min-width="160" show-overflow-tooltip />
    </el-table>
  </el-card>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { aiApi, type AiSettings, type AiLogRow } from '@/api/client'

const loading = ref(false)
const saving = ref(false)
const form = ref<AiSettings>({
  baseUrl: 'https://api.deepseek.com', model: '', visionModel: '', temperature: 0.1, maxTokens: 1024,
  allowImageToCloud: true, humanReviewThreshold: 0.6, retries: 2,
  gradingPromptTemplate: null, hasApiKey: false, apiKeyMasked: '',
})
const apiKeyInput = ref('')
const logs = ref<{ totalTokens: number; logs: AiLogRow[] } | null>(null)

onMounted(loadAll)

async function loadAll() {
  loading.value = true
  try {
    form.value = await aiApi.get()
    logs.value = await aiApi.logs()
  } finally { loading.value = false }
}

async function save() {
  saving.value = true
  try {
    // apiKey 仅在输入了新值时提交（undefined = 服务端保持不变）
    const payload: Record<string, unknown> = { ...form.value }
    delete payload.hasApiKey
    delete payload.apiKeyMasked
    payload.baseUrl = (form.value.baseUrl || '').trim().replace(/\/+$/, '')
    if (apiKeyInput.value.trim()) payload.apiKey = apiKeyInput.value.trim()
    else delete payload.apiKey
    form.value = await aiApi.save(payload)
    apiKeyInput.value = ''
    ElMessage.success('已保存（即时生效）')
  } finally { saving.value = false }
}

async function clearApiKey() {
  saving.value = true
  try {
    form.value = await aiApi.save({ apiKey: '' })
    apiKeyInput.value = ''
    ElMessage.success('已清除在线密钥，回落服务器配置')
  } finally { saving.value = false }
}

// AI 调用场景名（与服务端 endpoint 字段对应）
const scenarioNames: Record<string, string> = {
  recognize: '答题卡识别',
  grade: '主观题批改',
  extract: '文本导题',
  extract_images: '图片导题',
  fill_answers: '答案回填',
  generate_answer: '生成答案',
}

function formatTime(t: string) { return t ? String(t).replace('T', ' ').substring(0, 19) : '—' }
</script>

<style scoped>
.page-card { border-radius: 10px; }
.card-header { display: flex; justify-content: space-between; align-items: center; }
.card-title { font-weight: 600; }
.tip { font-size: 11px; color: #909399; line-height: 1.5; margin-top: 2px; }
</style>
