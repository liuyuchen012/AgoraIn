# 从 markdown 生成 Web 端法律条款页面（与 v4/docs 同源，保证内容一致）
import io, re, html

ROOT = r'C:\Users\liuyu\Desktop\数学打卡\check-in-net\v4'

def inline(text: str) -> str:
    t = html.escape(text, quote=False)
    t = re.sub(r'\*\*(.+?)\*\*', r'<strong>\1</strong>', t)
    t = re.sub(r'\*(.+?)\*', r'<em>\1</em>', t)
    return t

def md_to_html(md: str) -> str:
    lines = md.splitlines()
    out = []
    i = 0
    while i < len(lines):
        line = lines[i]
        s = line.strip()

        if not s:
            i += 1
            continue

        if s == '---':
            out.append('<hr>')
            i += 1
            continue

        if s.startswith('# '):
            out.append(f'<h1>{inline(s[2:])}</h1>')
            i += 1
            continue

        if s.startswith('## '):
            out.append(f'<h2>{inline(s[3:])}</h2>')
            i += 1
            continue

        # 表格
        if s.startswith('|') and i + 1 < len(lines) and re.match(r'^\|[\s\-:|]+\|$', lines[i + 1].strip()):
            headers = [c.strip() for c in s.strip('|').split('|')]
            i += 2
            rows = []
            while i < len(lines) and lines[i].strip().startswith('|'):
                rows.append([c.strip() for c in lines[i].strip().strip('|').split('|')])
                i += 1
            th = ''.join(f'<th>{inline(h)}</th>' for h in headers)
            body = ''.join('<tr>' + ''.join(f'<td>{inline(c)}</td>' for c in r) + '</tr>' for r in rows)
            out.append(f'<div class="tblwrap"><table><thead><tr>{th}</tr></thead><tbody>{body}</tbody></table></div>')
            continue

        # 有序列表
        if re.match(r'^\d+\.\s', s):
            items = []
            while i < len(lines) and re.match(r'^\d+\.\s', lines[i].strip()):
                items.append(f'<li>{inline(re.sub(r"^\d+\.\s", "", lines[i].strip()))}</li>')
                i += 1
            out.append('<ol>' + ''.join(items) + '</ol>')
            continue

        # 无序列表
        if s.startswith('- ') or s.startswith('* '):
            items = []
            while i < len(lines) and (lines[i].strip().startswith('- ') or lines[i].strip().startswith('* ')):
                items.append(f'<li>{inline(lines[i].strip()[2:])}</li>')
                i += 1
            out.append('<ul>' + ''.join(items) + '</ul>')
            continue

        # 段落（连续非空行合并）
        paras = []
        while i < len(lines) and lines[i].strip() and not lines[i].strip().startswith(('#', '|', '- ', '* ')) \
                and not re.match(r'^\d+\.\s', lines[i].strip()) and lines[i].strip() != '---':
            paras.append(lines[i].strip())
            i += 1
        if paras:
            out.append('<p>' + inline(' '.join(paras)) + '</p>')
        else:
            i += 1
    return '\n'.join(out)

PAGE_TMPL = '''<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>{title} - AgoraIn</title>
<style>
  * {{ box-sizing: border-box; }}
  body {{
    margin: 0; background: #f5f7fa; color: #202124;
    font-family: -apple-system, BlinkMacSystemFont, "PingFang SC", "Microsoft YaHei", "Helvetica Neue", sans-serif;
    line-height: 1.85; font-size: 15px;
  }}
  .topbar {{
    position: sticky; top: 0; z-index: 10; background: #4285f4; color: #fff;
    display: flex; align-items: center; justify-content: space-between;
    padding: 12px 16px; padding-top: calc(12px + env(safe-area-inset-top));
    box-shadow: 0 1px 4px rgba(0,0,0,.12);
  }}
  .topbar .brand {{ font-weight: 600; font-size: 15px; }}
  .topbar a {{ color: #fff; text-decoration: none; font-size: 13px; opacity: .92; }}
  .wrap {{
    max-width: 760px; margin: 0 auto; padding: 20px 18px calc(48px + env(safe-area-inset-bottom));
    background: #fff; min-height: 100vh;
  }}
  h1 {{ font-size: 22px; margin: 8px 0 4px; text-align: center; }}
  h2 {{
    font-size: 17px; margin: 28px 0 10px; padding-left: 10px;
    border-left: 4px solid #4285f4; color: #1a3a6b;
  }}
  p {{ margin: 10px 0; text-align: justify; }}
  strong {{ color: #174ea6; font-weight: 600; }}
  ol, ul {{ margin: 10px 0; padding-left: 24px; }}
  li {{ margin: 6px 0; }}
  hr {{ border: none; border-top: 1px solid #e8eaed; margin: 24px 0; }}
  .tblwrap {{ overflow-x: auto; margin: 12px 0; -webkit-overflow-scrolling: touch; }}
  table {{ border-collapse: collapse; width: 100%; min-width: 480px; font-size: 13.5px; }}
  th, td {{ border: 1px solid #dadce0; padding: 8px 10px; text-align: left; vertical-align: top; }}
  th {{ background: #f1f5fe; color: #174ea6; white-space: nowrap; }}
  tr:nth-child(even) td {{ background: #fafbfc; }}
  em {{ color: #5f6368; }}
  .foot {{ text-align: center; color: #9aa0a6; font-size: 12px; margin-top: 36px; }}
</style>
</head>
<body>
<div class="topbar">
  <span class="brand">AgoraIn · {title}</span>
  <a href="/register">← 返回注册</a>
</div>
<div class="wrap">
{body}
<div class="foot">运营主体：天津自贸试验区创晨平台网络科技工作室（个体工商户）<br>AgoraIn v4.0 · 闭源商业软件</div>
</div>
</body>
</html>
'''

for src, dst, title in [
    ('docs/terms-of-service.md', 'src/AgoraIn.WebAdmin/public/terms.html', '服务条款'),
    ('docs/privacy-policy.md', 'src/AgoraIn.WebAdmin/public/privacy.html', '隐私政策'),
]:
    md = io.open(os_path := ROOT + '\\' + src, encoding='utf-8').read()
    body = md_to_html(md)
    page = PAGE_TMPL.format(title=title, body=body)
    outp = ROOT + '\\' + dst
    io.open(outp, 'w', encoding='utf-8', newline='').write(page)
    print('written:', dst, f'({len(page)//1024}KB)')
