"""Generate the two static project pages. Python standard library only."""
from html import escape
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]
BASE = 'https://bob-the-heater.github.io/corsair-memory-takeover/'
REPO = 'https://github.com/BoB-The-Heater/corsair-memory-takeover'
VERSION = '0.3.1'
RELEASE = REPO + '/releases/tag/v' + VERSION

LANGUAGES = {
    'zh-CN': {
        'path': '', 'other': 'en/', 'switch': 'English',
        'title': '海盗船内存接管助手 | Corsair Memory Takeover',
        'description': '检测海盗船 RGB 内存与主板软件的接入状态。提供华硕 Aura Sync、微星 Mystic Light 服务维护预览，以及技嘉 RGB Fusion 检测与接入指引。',
        'heading': '海盗船内存接管助手',
        'intro': '检查官方软件的接入状态，维护已确认的服务。灯效由主板厂商软件控制。',
        'download': '下载 Windows 版', 'source': '查看源码',
        'platform': '.NET Framework 4.8 · 简体中文界面 · MIT',
        'nav': ['支持范围', '使用步骤', '常见问题'],
        'support': '支持范围',
        'columns': ['主板软件', '当前功能'],
        'brands': [
            ('ASUS Aura Sync', '组件检测、已确认服务基线的维护（预览）'),
            ('MSI Mystic Light', '组件检测、已确认服务基线的维护（预览）'),
            ('GIGABYTE RGB Fusion', '检测与官方接入指引'),
            ('其他品牌', '检测与接入指引'),
        ],
        'limit': '适配仍为预览，不能保证所有硬件兼容。服务运行不代表实际同步，需在官方软件中确认内存可控。',
        'start': '使用步骤',
        'steps': [
            ('下载并检测', '解压发行包，运行 CorsairTakeover.exe。首次检测无需管理员权限。'),
            ('完成官方接入', '按品牌指引安装组件、完成控制授权，并确认实体灯效跟随。'),
            ('启用服务维护', '华硕、微星用户检测通过后可启用维护，再检查重启和唤醒结果。'),
        ],
        'guide': '查看完整使用指南',
        'faq': '常见问题',
        'questions': [
            ('服务正常，内存仍不显示怎么办？', '检查官方插件、控制授权和厂商软件的设备扫描。助手不能补齐官方软件不支持的设备。'),
            ('如何关闭维护？', '使用工具中的「撤销」。只关闭窗口不会取消登录任务。'),
            ('检测信息会上传吗？', '程序没有报告上传功能。提交问题时请自行删除个人路径、序列号和设备标识。'),
        ],
        'issues': '问题反馈', 'license': 'MIT 许可证',
        'footer': '独立项目，与各硬件厂商无隶属关系。', 'skip': '跳到正文',
    },
    'en': {
        'path': 'en/', 'other': '../', 'switch': '简体中文',
        'title': 'Corsair Memory Takeover | RAM RGB Detection & Service Maintenance',
        'description': 'Check Corsair RGB RAM integration with motherboard software. Preview ASUS Aura Sync and MSI Mystic Light service maintenance, plus GIGABYTE RGB Fusion detection and setup guidance.',
        'heading': 'Corsair Memory Takeover',
        'intro': 'Check official software integration and maintain confirmed services. Lighting stays in your motherboard software.',
        'download': 'Download for Windows', 'source': 'View source',
        'platform': '.NET Framework 4.8 · Chinese UI · MIT',
        'nav': ['Support', 'Get started', 'FAQ'],
        'support': 'Support', 'columns': ['Motherboard software', 'Features'],
        'brands': [
            ('ASUS Aura Sync', 'Component detection and confirmed service maintenance (preview)'),
            ('MSI Mystic Light', 'Component detection and confirmed service maintenance (preview)'),
            ('GIGABYTE RGB Fusion', 'Detection and official setup guidance'),
            ('Other brands', 'Detection and setup guidance'),
        ],
        'limit': 'Adapters are previews. Compatibility is not guaranteed; running services do not prove physical RGB synchronization.',
        'start': 'Get started',
        'steps': [
            ('Download and scan', 'Extract the release and run CorsairTakeover.exe. The first scan needs no administrator rights.'),
            ('Complete official setup', 'Install required components, approve control and confirm physical lighting in vendor software.'),
            ('Enable maintenance', 'Eligible ASUS/MSI users can enable service maintenance, then test restart and resume.'),
        ],
        'guide': 'Read the setup guide (Chinese)',
        'faq': 'FAQ',
        'questions': [
            ('Services run, but RAM is still missing?', 'Check official plugins, control approval and device scanning. The assistant cannot add unsupported devices to vendor software.'),
            ('How do I disable maintenance?', 'Use Undo in the application. Closing the window does not remove its sign-in task.'),
            ('Does it upload machine data?', 'The application has no report upload feature. Redact personal paths, serial numbers and identifiers before sharing an issue.'),
        ],
        'issues': 'Report an issue', 'license': 'MIT License',
        'footer': 'Independent project, not affiliated with hardware vendors.', 'skip': 'Skip to content',
    },
}

for language, text in LANGUAGES.items():
    prefix = '../' if text['path'] else ''
    url = BASE + text['path']
    title, description = escape(text['title'], quote=True), escape(text['description'], quote=True)
    schema = {
        '@context': 'https://schema.org', '@type': 'SoftwareApplication',
        'name': 'Corsair Memory Takeover', 'operatingSystem': 'Windows x64',
        'applicationCategory': 'UtilitiesApplication', 'softwareVersion': VERSION,
        'inLanguage': 'zh-CN', 'url': url, 'downloadUrl': RELEASE,
        'description': text['description'], 'license': REPO + '/blob/main/LICENSE',
    }
    nav = ''.join(f'<a href="#{anchor}">{label}</a>' for anchor, label in zip(['support', 'start', 'faq'], text['nav']))
    rows = ''.join(f'<tr><th scope="row">{brand}</th><td>{feature}</td></tr>' for brand, feature in text['brands'])
    steps = ''.join(f'<li><h3>{heading}</h3><p>{body}</p></li>' for heading, body in text['steps'])
    questions = ''.join(f'<details><summary>{question}</summary><p>{answer}</p></details>' for question, answer in text['questions'])
    html = f'''<!doctype html>
<html lang="{language}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>{title}</title>
  <meta name="description" content="{description}">
  <meta name="robots" content="index,follow">
  <meta name="theme-color" content="#14243d">
  <link rel="canonical" href="{url}">
  <link rel="alternate" hreflang="zh-CN" href="{BASE}">
  <link rel="alternate" hreflang="en" href="{BASE}en/">
  <link rel="alternate" hreflang="x-default" href="{BASE}">
  <meta property="og:type" content="website">
  <meta property="og:site_name" content="Corsair Memory Takeover">
  <meta property="og:title" content="{title}">
  <meta property="og:description" content="{description}">
  <meta property="og:url" content="{url}">
  <meta name="twitter:card" content="summary">
  <meta name="twitter:title" content="{title}">
  <meta name="twitter:description" content="{description}">
  <link rel="stylesheet" href="{prefix}assets/site.css">
  <link rel="sitemap" type="application/xml" href="{BASE}sitemap.xml">
  <script type="application/ld+json">{json.dumps(schema, ensure_ascii=False)}</script>
</head>
<body>
  <a class="skip" href="#main">{text['skip']}</a>
  <header><div class="wrap top">
    <a class="wordmark" href="{prefix or './'}">Corsair Memory Takeover</a>
    <nav aria-label="{'主导航' if language == 'zh-CN' else 'Main navigation'}">{nav}<a class="language" href="{text['other']}">{text['switch']}</a></nav>
  </div></header>
  <main id="main" class="wrap">
    <section class="hero">
      <p class="eyebrow">Windows x64 · v{VERSION} · Preview</p>
      <h1>{text['heading']}</h1>
      <p class="lead">{text['intro']}</p>
      <div class="actions"><a class="button primary" href="{RELEASE}">{text['download']}</a><a class="button secondary" href="{REPO}">{text['source']}</a></div>
      <p class="fine">{text['platform']}</p>
    </section>
    <section id="support">
      <h2>{text['support']}</h2>
      <table><thead><tr><th scope="col">{text['columns'][0]}</th><th scope="col">{text['columns'][1]}</th></tr></thead><tbody>{rows}</tbody></table>
      <p class="notice">{text['limit']}</p>
    </section>
    <section id="start"><h2>{text['start']}</h2><ol class="steps">{steps}</ol><a href="{REPO}/blob/main/GUIDE.md">{text['guide']} →</a></section>
    <section id="faq"><h2>{text['faq']}</h2>{questions}</section>
  </main>
  <footer><div class="wrap"><div class="footerlinks"><a href="{REPO}/issues">{text['issues']}</a><a href="{REPO}/blob/main/LICENSE">{text['license']}</a><a href="{prefix}sitemap.xml">Sitemap</a></div><p>{text['footer']}</p></div></footer>
</body>
</html>
'''
    (ROOT / 'docs' / text['path'] / 'index.html').write_text(html, encoding='utf-8')

(ROOT / 'docs/.nojekyll').write_text('', encoding='utf-8')
(ROOT / 'docs/sitemap.xml').write_text(f'''<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url><loc>{BASE}</loc></url>
  <url><loc>{BASE}en/</loc></url>
</urlset>
''', encoding='utf-8')
print('Generated project pages.')
