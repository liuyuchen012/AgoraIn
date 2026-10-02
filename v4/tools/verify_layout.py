"""版面模型 vs 浏览器实际渲染：逐气泡/逐作答框比对（mm）。
双栏排版必须让"渲染出来的位置"与 AnswerSheetLayout 算出的坐标一致，
否则切卡会整体偏移——这是本项目最容易翻车的地方，所以每次动 CSS/布局都要跑一遍。

用法：python verify_layout.py <html路径> <layout.json路径>
"""
import json
import os
import re
import subprocess
import sys

EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
PROFILE = r"C:\Users\liuyu\AppData\Local\Temp\sheetgen\edgeprofile"

html_path = os.path.abspath(sys.argv[1])
model_path = os.path.abspath(sys.argv[2])
probe = html_path.replace(".html", ".probe.html")

JS = """
<pre id="measure-out"></pre>
<script>
(function () {
  const MM = px => px * 25.4 / 96;
  const pages = [...document.querySelectorAll('.page')];
  const out = pages.map((pg, i) => {
    const pr = pg.getBoundingClientRect();
    const rel = el => {
      const r = el.getBoundingClientRect();
      return [MM(r.left - pr.left), MM(r.top - pr.top), MM(r.width), MM(r.height)];
    };
    return {
      page: i + 1,
      paper: [MM(pr.width), MM(pr.height)],
      opts: [...pg.querySelectorAll('.omr-opt')].map(el => ({ q: +el.dataset.q, o: el.dataset.opt, r: rel(el) })),
      frames: [...pg.querySelectorAll('.answer-box')].map(el => {
        const h = el.querySelector('.answer-head');
        const m = h ? h.textContent.match(/第\\s*(\\d+)\\s*题/) : null;
        return { q: m ? +m[1] : -1, r: rel(el) };
      }).concat([...pg.querySelectorAll('.blank-row')].map(el => {
        // 填空题行：题号印在 .blank-no（"第 N 题（x 分）"）
        const n = el.querySelector('.blank-no');
        const m = n ? n.textContent.match(/第\\s*(\\d+)\\s*题/) : null;
        return { q: m ? +m[1] : -1, r: rel(el) };
      })),
      anchors: [...pg.querySelectorAll('.anchor')].map(el => ({ cls: el.className.replace('anchor ', ''), r: rel(el) })),
      cols: [...pg.querySelectorAll('.cols > .col')].map(el => rel(el))
    };
  });
  document.getElementById('measure-out').textContent = JSON.stringify(out);
})();
</script>
"""

src = open(html_path, encoding="utf-8").read()
open(probe, "w", encoding="utf-8").write(src.replace("</body>", JS + "</body>"))

r = subprocess.run([EDGE, "--headless=new", "--disable-gpu", "--no-sandbox",
                    f"--user-data-dir={PROFILE}", "--virtual-time-budget=4000",
                    "--dump-dom", "file:///" + probe.replace("\\", "/")],
                   capture_output=True, text=True, encoding="utf-8", errors="ignore")
m = re.search(r'<pre id="measure-out">(.*?)</pre>', r.stdout, re.S)
if not m:
    print("没有取到测量结果；Edge 输出片段：")
    print(r.stdout[:400])
    sys.exit(1)
dom = json.loads(m.group(1).replace("&quot;", '"').replace("&amp;", "&"))
model = json.load(open(model_path, encoding="utf-8"))

print(f"DOM 页数 {len(dom)} / 模型页数 {len(model)}")
bad = 0
for i, dp in enumerate(dom):
    mp = model[i]
    print(f"第 {dp['page']} 页  纸面 {dp['paper'][0]:.1f}×{dp['paper'][1]:.1f}mm  "
          f"气泡 {len(dp['opts'])}  作答框 {len(dp['frames'])}  栏 {len(dp['cols'])}")
    for c, col in enumerate(dp["cols"]):
        print(f"    栏{c+1} x={col[0]:.1f} 宽={col[2]:.1f}mm")

    mo = {(o["QuestionIndex"] + 1, o["Option"]): o["Bubble"] for o in mp["Options"]}
    worst = 0.0
    for o in dp["opts"]:
        key = (o["q"], o["o"])
        if key not in mo:
            print(f"    ⚠ DOM 多出气泡 {key}")
            bad += 1
            continue
        b = mo[key]
        dx = abs(b["Xmm"] - o["r"][0])
        dy = abs(b["Ymm"] - o["r"][1])
        dw = abs(b["Wmm"] - o["r"][2])
        worst = max(worst, dx, dy)
        if dx > 0.3 or dy > 0.3:
            print(f"    ✗ 气泡 题{o['q']}{o['o']}: 模型({b['Xmm']:.2f},{b['Ymm']:.2f}) DOM({o['r'][0]:.2f},{o['r'][1]:.2f}) Δ({dx:.2f},{dy:.2f})")
            bad += 1
    if len(mo) != len(dp["opts"]):
        print(f"    ⚠ 气泡数量不一致：模型 {len(mo)} / DOM {len(dp['opts'])}")
        bad += 1
    print(f"    客观题气泡最大偏差 {worst:.2f}mm")

    mf = {f["QuestionIndex"] + 1: f["Box"] for f in mp["Frames"]}
    worst_f = 0.0
    for f in dp["frames"]:
        if f["q"] not in mf:
            print(f"    ⚠ DOM 多出作答框 题{f['q']}")
            bad += 1
            continue
        b = mf[f["q"]]
        dx, dy = abs(b["Xmm"] - f["r"][0]), abs(b["Ymm"] - f["r"][1])
        dw = abs(b["Wmm"] - f["r"][2])
        worst_f = max(worst_f, dx, dy)
        if dx > 0.3 or dy > 0.3 or dw > 0.3:
            print(f"    ✗ 作答框 题{f['q']}: 模型({b['Xmm']:.2f},{b['Ymm']:.2f},{b['Wmm']:.2f}) "
                  f"DOM({f['r'][0]:.2f},{f['r'][1]:.2f},{f['r'][2]:.2f}) Δ({dx:.2f},{dy:.2f},{dw:.2f})")
            bad += 1
    if len(mf) != len(dp["frames"]):
        print(f"    ⚠ 作答框数量不一致：模型 {len(mf)} / DOM {len(dp['frames'])}")
        bad += 1
    print(f"    作答框最大偏差 {worst_f:.2f}mm")

print("\n结果：" + ("全部一致 ✓" if bad == 0 else f"{bad} 处不一致 ✗"))
sys.exit(0 if bad == 0 else 2)
