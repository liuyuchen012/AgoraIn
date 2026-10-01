<template>
  <div class="register-page">
    <el-card class="register-card">
      <div class="logo">
        <div class="logo-circle">A</div>
        <h1>注册 AgoraIn 账户</h1>
        <p class="subtitle">机构注册创建独立区域 · 家长凭老师颁发的邀请码注册并绑定孩子</p>
      </div>

      <el-radio-group v-model="mode" class="mode-switch">
        <el-radio-button value="join">家长注册（加入区域）</el-radio-button>
        <el-radio-button value="region">机构注册（创建区域）</el-radio-button>
      </el-radio-group>

      <el-form :model="form" label-width="90px" @submit.prevent="onSubmit">
        <el-form-item label="邮箱" required>
          <el-input v-model="form.email" placeholder="用于接收验证码与找回密码" />
        </el-form-item>
        <el-form-item label="验证码" required>
          <div style="display:flex;gap:8px;width:100%">
            <el-input v-model="form.code" placeholder="邮箱验证码" />
            <el-button :disabled="codeCooldown > 0" @click="sendCode">
              {{ codeCooldown > 0 ? `${codeCooldown}s` : '获取验证码' }}
            </el-button>
          </div>
        </el-form-item>
        <el-form-item label="用户名" required>
          <el-input v-model="form.username" placeholder="3–32 位字母/数字/下划线/短横线（不含 @）" />
        </el-form-item>
        <el-form-item label="密码" required>
          <el-input v-model="form.password" type="password" show-password placeholder="至少 6 位" />
        </el-form-item>

        <template v-if="mode === 'join'">
          <el-form-item label="绑定邀请码" required>
            <el-input v-model="form.inviteCode" placeholder="向老师索取 6 位邀请码，注册即自动绑定孩子"
                      @change="loadRegionAgreement" />
          </el-form-item>
          <el-alert v-if="regionAgreement.hasCustom" type="warning" :closable="false" style="margin-bottom:12px"
                    :title="`「${regionAgreement.regionName}」已发布机构隐私政策，注册须额外同意`" />
          <div v-if="regionAgreement.hasCustom" class="region-agreement">
            <div class="ra-title">「{{ regionAgreement.regionName }}」机构隐私政策
              <span v-if="regionAgreement.updatedAt" class="ra-time">更新于 {{ formatTime(regionAgreement.updatedAt) }}</span>
            </div>
            <div class="ra-body">{{ regionAgreement.content }}</div>
          </div>
        </template>
        <template v-else>
          <el-form-item label="区域名称" required>
            <el-input v-model="form.regionName" placeholder="如机构/学校名称（全局唯一，不可重复）" />
          </el-form-item>
          <el-form-item label="区域代号">
            <el-input v-model="form.regionId" placeholder="留空自动生成（3–32 位小写字母/数字）" />
          </el-form-item>
        </template>

        <el-form-item label="">
          <el-checkbox v-model="form.agreeTerms">
            我已阅读并同意<a href="/terms.html" target="_blank" @click.stop>《服务条款》</a>与<a href="/privacy.html" target="_blank" @click.stop>《隐私政策》</a>
          </el-checkbox>
          <el-checkbox v-if="regionAgreement.hasCustom" v-model="agreeRegionTerms">
            我已阅读并同意「{{ regionAgreement.regionName }}」的<b>机构隐私政策</b>
          </el-checkbox>
        </el-form-item>

        <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon style="margin-bottom:12px" />

        <el-button type="primary" size="large" class="submit-btn" :loading="loading" @click="onSubmit">
          注册
        </el-button>
      </el-form>

      <div class="footer">
        已有账户？<router-link to="/login" class="link">返回登录</router-link>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { registerApi, parentApi } from '@/api/client'

const router = useRouter()

const mode = ref<'join' | 'region'>('join')
const form = reactive({
  email: '', code: '', username: '', password: '',
  regionId: '', regionName: '', inviteCode: '', agreeTerms: false, agreeRegionTerms: false,
})
// 机构自定义隐私协议（随邀请码自动加载）
const regionAgreement = ref<{ regionId: string; regionName: string; hasCustom: boolean; content?: string; updatedAt?: string }>(
  { regionId: '', regionName: '', hasCustom: false, content: '', updatedAt: '' })

async function loadRegionAgreement() {
  const code = (form.inviteCode || '').trim().toUpperCase()
  if (!code) { regionAgreement.value = { regionId: '', regionName: '', hasCustom: false, content: '', updatedAt: '' }; return }
  try {
    regionAgreement.value = await parentApi.inviteRegion(code)
    agreeRegionTerms.value = false
  } catch {
    regionAgreement.value = { regionId: '', regionName: '', hasCustom: false, content: '', updatedAt: '' }
  }
}

function formatTime(t?: string) { return t ? String(t).replace('T', ' ').substring(0, 16) : '' }
const loading = ref(false)
const error = ref('')
const agreeRegionTerms = ref(false)
const codeCooldown = ref(0)
let timer: ReturnType<typeof setInterval> | null = null

onUnmounted(() => { if (timer) clearInterval(timer) })

async function sendCode() {
  if (!form.email) { error.value = '请先填写邮箱'; return }
  try {
    await registerApi.sendCode(form.email)
    ElMessage.success('验证码已发送，请查收邮件')
    codeCooldown.value = 60
    timer = setInterval(() => {
      codeCooldown.value--
      if (codeCooldown.value <= 0 && timer) { clearInterval(timer); timer = null }
    }, 1000)
  } catch { /* 错误提示由拦截器统一处理 */ }
}

async function onSubmit() {
  error.value = ''
  if (!form.email || !form.code || !form.username || !form.password) {
    error.value = '请填写完整信息'; return
  }
  if (!form.agreeTerms) { error.value = '请先同意服务条款与隐私政策'; return }
  if (mode.value === 'join' && !form.inviteCode) { error.value = '请填写绑定邀请码'; return }
  if (mode.value === 'join' && regionAgreement.value.hasCustom && !agreeRegionTerms.value) {
    error.value = '请先阅读并同意该机构的隐私政策'; return
  }
  if (mode.value === 'region' && !form.regionName) { error.value = '请填写区域名称'; return }

  loading.value = true
  try {
    const res = await registerApi.register({
      username: form.username, password: form.password,
      agreeTerms: form.agreeTerms,
      mode: mode.value,
      regionId: form.regionId || undefined,
      regionName: form.regionName || undefined,
      inviteCode: form.inviteCode || undefined,
      agreeRegionTerms: agreeRegionTerms.value,
      ...(mode.value === 'region' ? { email: form.email, code: form.code } : {}),
    })
    ElMessage.success(res.message || '注册成功')
    router.push('/login')
  } catch (e: any) {
    error.value = e.response?.data?.error || '注册失败'
  } finally { loading.value = false }
}
</script>

<style scoped>
.region-agreement {
  border: 1px solid #e4e7ed; border-radius: 8px; background: #fafbfc;
  margin-bottom: 14px; overflow: hidden;
}
.ra-title {
  padding: 8px 12px; background: #f1f5fe; font-weight: 600; font-size: 13px; color: #174ea6;
}
.ra-time { font-weight: 400; color: #909399; font-size: 11px; margin-left: 8px; }
.ra-body {
  padding: 12px; max-height: 180px; overflow-y: auto; white-space: pre-wrap;
  font-size: 13px; color: #333; line-height: 1.7;
}
.register-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #f5f7fa;
}

.register-card {
  width: 520px;
  border-radius: 12px;
}

.logo { text-align: center; margin-bottom: 16px; }

.logo-circle {
  width: 44px; height: 44px;
  border-radius: 50%;
  background: #4285f4;
  color: #fff;
  font-size: 22px;
  font-weight: 700;
  line-height: 44px;
  margin: 0 auto 10px;
}

h1 { font-size: 20px; margin-bottom: 6px; }

.subtitle {
  font-size: 13px;
  color: #909399;
}

.mode-switch { display: flex; justify-content: center; width: 100%; margin-bottom: 18px; }

.submit-btn { width: 100%; }

.footer {
  text-align: center;
  font-size: 13px;
  color: #909399;
  margin-top: 18px;
}

.footer .link { color: #4285f4; text-decoration: none; }
</style>
