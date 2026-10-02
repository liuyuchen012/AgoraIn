# AgoraIn v4 答题卡模板规范

> 本文定义 v4 电子答题卡的版面要素、渲染参数与识别约定，是
> `Server/Services/AnswerSheetRenderer.cs`（渲染）与识别流水线（视觉模型/OMR）的共同契约。

## 1. 纸张与坐标系

- 默认 **A4 纵向**（210mm × 297mm），可选 A3/B4/8K、单栏/双栏；
- **栏数由纸宽自动决定**（`AnswerSheetOptions.Columns` 推导，不是用户开关）：
  内容宽 ≥ 200mm（8K 260 / A3 297 / B4 250）排 **双栏**——表头（标题/注意事项/信息区+考号区）通栏，
  其下内容先排满左栏再接右栏，每栏宽 =（内容宽 − 8mm 栏距）/ 2，块内客观题气泡列数按栏宽重算；
  A4（210）、16K（185）仍是单栏。同样题目在 8K 双栏下页数约为 A4 单栏的一半。
  三处必须算出同一套坐标：渲染（`.cols/.col` 用固定 mm 宽）、版面模型（`AnswerSheetLayout`）、
  识别与切图；改动后须做「模型 vs 浏览器实测」的逐气泡比对（脚本 `verify_layout.py`，当前最大偏差 0.21mm）。
- 版面以 CSS 打印盒模型排版，`@media print` 下隐藏屏幕装饰元素，`page-break-after: always` 分页；
- 所有绝对定位元素（定位标记、二维码）相对页面四边留 ≥ 8mm 安全边距，防止打印机裁边。

## 2. 必备版面要素

### 2.1 四角定位标记

- 页面四角各一个 **实心黑色方块**（约 6mm × 6mm）；
- 用途：拍照后透视校正（检测四个方块中心 → 单应性变换归正）；
- 不可被密封线、贴纸遮挡。

### 2.2 学生信息区

- 姓名：手写区（横线）；
- 考号：三种模式（`idAreaType`）：
  - `bubble`：OMR 涂卡区，8 列 × 数字 0-9，列间距 ≥ 4mm（识别按投影分析定位气泡）；
  - `barcode`：条码/考号文本区（按学生批量生成时预填学号）；
  - `handwrite`：手写考号方格。

### 2.3 客观题涂卡区

- 按题号排列，每题一行（或竖排模式一列），选项气泡 `A B C D…` / 判断 `√ ×`；
- 气泡直径 ≥ 3mm，行距 ≥ 5mm（保证 OMR 投影可分）；
- 多选题气泡行内加注“多选”提示。

### 2.4 主观题作答区

- 每题一个**边界框**（细实线矩形），框内左上角标注题号与分值；
- 边界框用于拍照后按模板切分题块图，切分图交给视觉模型/教师批改；
- 作文题提供 300-400 字方格或横线区。

### 2.5 页脚二维码

- 内容 payload：`agorain:sheet:{paperId}:p{pageIndex}`；
- 服务端用 QRCoder 生成 PNG 内嵌；识别时用于校验试卷与页码（无 QRCoder 环境回退为文本标记）。

## 3. 渲染端点（AnswerSheetController，匿名只读）

| 端点 | 说明 |
| --- | --- |
| `GET /api/v4/sheet/{paperId}` | 空白通用答题卡 HTML（浏览器打印） |
| `GET /api/v4/sheet/{paperId}/student/{studentId}` | 单个学生（预填学号条码区） |
| `GET /api/v4/sheet/{paperId}/batch?classId={classId}` | 全班一人一页（`page-break-after` 分页） |

批量生成输出为连续 HTML 页面，浏览器 `Ctrl+P` 即可导出 PDF 或直接打印。

## 4. 识别约定

1. 预处理：四角定位标记检测 → 透视校正 → 按模板切分题块；
2. 客观题：OMR 涂卡判定与标准答案比对自动判分（`Source=Ai`），识别置信度低于阈值（默认 0.6，`ai.humanReviewThreshold` 可调）转待人工；
3. 考号绑定：条码/考号 → 学号辅助匹配，**结果必须经教师确认**（批改工作台「绑定学生」）；
4. 主观题：题块图 + 评分要点（rubric）交多模态模型按 JSON 协议返回 `{score, comment, confidence}`；
5. 状态机：`未批 → AI已批 →（低置信度→）待人工 → 已确认`；只有已确认计入成绩。

## 5. 版面参数（Web「答题卡配置」）

| 参数 | 取值 | 说明 |
| --- | --- | --- |
| pageSize | A4 / A3 | 纸张 |
| columns | 1 / 2 | 栏数 |
| idAreaType | bubble / barcode / handwrite | 考号区形态 |
| hasSealLine | bool | 密封线 |
| hasNotes | bool | 考生注意事项 |
| hasAB | bool | AB 卷标记 |
| isRed | bool | 红色（统考）版式 |
| verticalObj | bool | 客观题竖排 |
| partitioned | bool | 分区版式 |

## 6. 模板版本

`AnswerSheetTemplate.Version` 随试卷改版递增；`AnswerSheetSubmission.TemplateVersion`
在提交时锁定，历史提交按其锁定版本切分，避免改版后错位。
