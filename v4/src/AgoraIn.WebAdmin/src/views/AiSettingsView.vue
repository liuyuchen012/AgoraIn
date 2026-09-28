<template>
  <el-card shadow="never" class="page-card">
    <template #header>
      <div class="card-header">
        <span class="card-title">AI 批改设置</span>
        <el-button size="small" @click="loadAll">刷新</el-button>
      </div>
    </template>

    <el-form :model="form" label-width="180px" style="max-width:640px" v-loading="loading">
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
      <el-table-column label="场景" width="90">
        <template #default="{ row }">{{ row.endpoint === 'recognize' ? '答题卡识别' : '主观题批改' }}</template>
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
  model: '', visionModel: '', temperature: 0.1, maxTokens: 1024,
  allowImageToCloud: true, humanReviewThreshold: 0.6, retries: 2,
})
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
    form.value = await aiApi.save(form.value)
    ElMessage.success('已保存（即时生效）')
  } finally { saving.value = false }
}

function formatTime(t: string) { return t ? String(t).replace('T', ' ').substring(0, 19) : '—' }
</script>

<style scoped>
.page-card { border-radius: 10px; }
.card-header { display: flex; justify-content: space-between; align-items: center; }
.card-title { font-weight: 600; }
.tip { font-size: 11px; color: #909399; line-height: 1.5; margin-top: 2px; }
</style>
