import { createApp } from 'vue'
import { ElMessage } from 'element-plus'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import 'element-plus/dist/index.css'
import App from './App.vue'
import router from './router'

const app = createApp(App)
app.use(createPinia())
app.use(router)
app.use(ElementPlus, { locale: zhCn })
app.mount('#app')

// ── 过期版本自愈 ──
// 浏览器缓存旧 index.html 会静默运行旧 JS（修复不生效、上传超时等假象）。
// 启动时对比线上 index.html 的资源 hash 与当前已加载的 hash，不一致即提示并自动刷新（仅一次，防循环）。
async function checkStaleBundle() {
  try {
    const res = await fetch('/index.html', { cache: 'no-store' })
    if (!res.ok) return
    const html = await res.text()
    const live = html.match(/index-([\w-]+)\.js/)?.[1]
    const current = (document.querySelector('script[src*="index-"]')?.getAttribute('src') || '')
      .match(/index-([\w-]+)\.js/)?.[1]
    if (live && current && live !== current && !sessionStorage.getItem('agorain_reloaded')) {
      sessionStorage.setItem('agorain_reloaded', '1')
      ElMessage.warning({ message: '页面已是旧版本，正在自动加载最新版…', duration: 2500 })
      setTimeout(() => location.reload(), 2500)
    }
  } catch { /* 检测失败不影响使用 */ }
}
checkStaleBundle()
