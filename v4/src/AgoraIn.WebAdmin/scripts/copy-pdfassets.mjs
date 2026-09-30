import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)))
const dist = path.join(root, 'dist')

// pdf.js 渲染中文 Word/PDF 需要 cMaps（CID 字符表）与标准字体数据，
// 缺失时中文页面会渲染成空白，AI 视觉识别就看到白图
const pairs = [
  // 放到 assets/ 下：nginx 放行清单里有 /assets，/cmaps 独立路径会被宝塔安全规则 404
  ['node_modules/pdfjs-dist/cmaps', 'dist/assets/cmaps'],
  ['node_modules/pdfjs-dist/standard_fonts', 'dist/assets/standard_fonts'],
]
for (const [src, dst] of pairs) {
  const s = path.join(root, src)
  const d = path.join(root, dst)
  if (!fs.existsSync(s)) {
    console.warn('skip (missing):', src)
    continue
  }
  fs.rmSync(d, { recursive: true, force: true })
  fs.cpSync(s, d, { recursive: true })
  console.log('copied:', dst)
}
