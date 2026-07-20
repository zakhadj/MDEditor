using System.Collections.Generic;
using System.IO;
using System.Text;
using Markdig;
using Markdig.Renderers;

namespace MdEditor.Services;

/// <summary>
/// Renders Markdown to HTML and produces the WebView2 preview document.
/// Each top-level block is wrapped with its source character span (data-md-start/data-md-end)
/// so the host can map raw-text selections/scroll position to rendered blocks and back.
/// </summary>
public class MarkdownRenderService
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private static readonly Dictionary<string, string> LightVars = new()
    {
        ["--md-bg"] = "#ffffff",
        ["--md-fg"] = "#20201d",
        ["--md-muted"] = "#5f5e5a",
        ["--md-border"] = "rgba(0,0,0,0.15)",
        ["--md-code-bg"] = "#f1efe8",
        ["--md-link"] = "#0c447c",
        ["--md-hl-bg"] = "#e6f1fb",
        ["--md-hl-border"] = "#85b7eb",
        ["--md-scrollbar-thumb"] = "rgba(0,0,0,0.35)",
        ["--md-scrollbar-thumb-hover"] = "rgba(0,0,0,0.55)"
    };

    private static readonly Dictionary<string, string> DarkVars = new()
    {
        ["--md-bg"] = "#1f1f1f",
        ["--md-fg"] = "#f3f2f1",
        ["--md-muted"] = "#c5c4c0",
        ["--md-border"] = "rgba(255,255,255,0.12)",
        ["--md-code-bg"] = "#2b2b2b",
        ["--md-link"] = "#7fb8ee",
        ["--md-hl-bg"] = "#16324a",
        ["--md-hl-border"] = "#3a6a98",
        ["--md-scrollbar-thumb"] = "rgba(255,255,255,0.28)",
        ["--md-scrollbar-thumb-hover"] = "rgba(255,255,255,0.45)"
    };

    public static IReadOnlyDictionary<string, string> GetThemeVars(bool isDark) => isDark ? DarkVars : LightVars;

    public string RenderBody(string markdownText)
    {
        var document = Markdig.Markdown.Parse(markdownText ?? string.Empty, _pipeline);
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        _pipeline.Setup(renderer);

        var sb = new StringBuilder();
        var stringBuilder = writer.GetStringBuilder();
        foreach (var block in document)
        {
            var startLen = stringBuilder.Length;
            renderer.Render(block);
            writer.Flush();
            var blockHtml = stringBuilder.ToString(startLen, stringBuilder.Length - startLen);
            sb.Append("<div class=\"md-block\" data-md-start=\"").Append(block.Span.Start)
              .Append("\" data-md-end=\"").Append(block.Span.End).Append("\">")
              .Append(blockHtml)
              .Append("</div>");
        }

        return sb.ToString();
    }

    public string BuildShellHtml(string initialBodyHtml, bool isDark, string? localBaseHref = null)
    {
        var vars = GetThemeVars(isDark);
        var varsCss = new StringBuilder();
        foreach (var kvp in vars)
        {
            varsCss.Append(kvp.Key).Append(':').Append(kvp.Value).Append(';');
        }

        var baseTag = localBaseHref != null ? $"<base href=\"{localBaseHref}\" />" : string.Empty;
        var html = ShellTemplate
            .Replace("__BASE_TAG__", baseTag)
            .Replace("__ROOT_VARS__", varsCss.ToString())
            .Replace("__INITIAL_BODY__", initialBodyHtml);
        return html;
    }

    private const string ShellTemplate = """
<!DOCTYPE html>
<html lang="fr">
<head>
<meta charset="UTF-8" />
__BASE_TAG__
<style>
  :root { __ROOT_VARS__ }
  * { box-sizing: border-box; }
  html, body { margin:0; }
  body {
    background: var(--md-bg);
    color: var(--md-fg);
    font-family: "Segoe UI", system-ui, sans-serif;
    font-size: 14px;
    line-height: 1.7;
    padding: 14px;
  }
  img { max-width: 100%; }
  a { color: var(--md-link); }
  h1, h2, h3, h4 { font-weight: 500; }
  h1 { font-size: 20px; margin: 0 0 12px; }
  h2 { font-size: 16px; margin: 16px 0 8px; }
  h3 { font-size: 14px; margin: 14px 0 6px; }
  p { margin: 0 0 10px; }
  ul, ol { margin: 0 0 10px; padding-left: 22px; }
  blockquote { margin: 0 0 10px; padding: 2px 12px; border-left: 3px solid var(--md-border); color: var(--md-muted); }
  code { background: var(--md-code-bg); border-radius: 4px; padding: 1px 5px; font-family: "Cascadia Code", Consolas, monospace; font-size: 0.9em; }
  pre { background: var(--md-code-bg); border-radius: 8px; padding: 10px 12px; overflow:auto; }
  pre code { background: transparent; padding: 0; }
  table { border-collapse: collapse; margin: 0 0 10px; }
  th, td { border: 1px solid var(--md-border); padding: 5px 10px; }
  hr { border: none; border-top: 1px solid var(--md-border); margin: 14px 0; }
  ::selection { background: var(--md-hl-bg); }
  .md-block { border-radius: 6px; transition: background-color 0.15s ease; }
  .md-block.md-highlight { background: var(--md-hl-bg); outline: 1px solid var(--md-hl-border); }
  ::-webkit-scrollbar { width: 12px; height: 12px; }
  ::-webkit-scrollbar-track { background: transparent; }
  ::-webkit-scrollbar-thumb {
    background-color: var(--md-scrollbar-thumb);
    border-radius: 4px;
    border: 3px solid transparent;
    background-clip: content-box;
  }
  ::-webkit-scrollbar-thumb:hover {
    background-color: var(--md-scrollbar-thumb-hover);
    background-clip: content-box;
  }
</style>
</head>
<body>
<div id="content">__INITIAL_BODY__</div>
<script>
(function () {
  // Suppression des remontees 'scroll' pendant un defilement declenche par l'hote.
  // Un drapeau a un coup ne suffit pas (un seul defilement emet des dizaines d'evenements), et une
  // simple fenetre fixe non plus : un scrollToRatio arrivant pendant un scrollIntoView 'smooth' la
  // raccourcirait, laissant la fin du defilement lisse reboucler vers l'editeur.
  // D'ou deux bornes : une limite dure, et une limite glissante reconduite tant que des evenements
  // arrivent - la suppression s'arrete donc quand le defilement s'est reellement stabilise.
  var suppressHardUntil = 0;
  var suppressSettleUntil = 0;
  var SETTLE_MS = 150;
  var scrollScheduled = false;

  function suppressScrollReports(maxMs) {
    var now = performance.now();
    suppressHardUntil = Math.max(suppressHardUntil, now + maxMs);
    suppressSettleUntil = Math.max(suppressSettleUntil, now + SETTLE_MS);
  }

  function scrollReportSuppressed() {
    var now = performance.now();
    if (now >= suppressHardUntil || now >= suppressSettleUntil) { return false; }
    suppressSettleUntil = now + SETTLE_MS;   // le defilement programmatique est toujours en cours
    return true;
  }

  function postToHost(obj) {
    if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage(obj); }
  }

  function setContent(html) {
    document.getElementById('content').innerHTML = html;
  }

  function scrollToRatio(ratio) {
    var max = document.documentElement.scrollHeight - window.innerHeight;
    suppressScrollReports(400);
    window.scrollTo(0, Math.max(0, max) * ratio);
  }

  function applyTheme(vars) {
    for (var k in vars) { document.documentElement.style.setProperty(k, vars[k]); }
  }

  function clearHighlights() {
    var prev = document.querySelectorAll('.md-highlight');
    for (var i = 0; i < prev.length; i++) { prev[i].classList.remove('md-highlight'); }
  }

  function highlightRange(start, end) {
    clearHighlights();
    var blocks = document.querySelectorAll('.md-block');
    var first = null;
    for (var i = 0; i < blocks.length; i++) {
      var b = blocks[i];
      var bStart = parseInt(b.getAttribute('data-md-start'), 10);
      var bEnd = parseInt(b.getAttribute('data-md-end'), 10);
      if (bEnd >= start && bStart <= end) {
        b.classList.add('md-highlight');
        if (!first) { first = b; }
      }
    }
    if (first) {
      // Le surlignage suit une selection faite dans l'editeur : l'apercu peut se repositionner si le
      // bloc est hors ecran, mais ce defilement ne doit surtout pas etre renvoye a l'hote, sinon la
      // zone d'edition se met a sauter sous le curseur. Marge large : le smooth dure plusieurs centaines
      // de millisecondes.
      suppressScrollReports(1500);
      first.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }
  }

  function getSelectionText() { return window.getSelection().toString(); }

  function selectAllContent() {
    var r = document.createRange();
    r.selectNodeContents(document.body);
    var s = window.getSelection();
    s.removeAllRanges();
    s.addRange(r);
  }

  window.__md = {
    setContent: setContent,
    scrollToRatio: scrollToRatio,
    applyTheme: applyTheme,
    highlightRange: highlightRange,
    clearHighlights: clearHighlights,
    getSelectionText: getSelectionText,
    selectAllContent: selectAllContent
  };

  window.addEventListener('scroll', function () {
    if (scrollReportSuppressed()) { return; }
    if (scrollScheduled) { return; }
    scrollScheduled = true;
    requestAnimationFrame(function () {
      scrollScheduled = false;
      if (scrollReportSuppressed()) { return; }
      var max = document.documentElement.scrollHeight - window.innerHeight;
      var ratio = max > 0 ? window.scrollY / max : 0;
      postToHost({ type: 'scroll', ratio: ratio });
    });
  });

  document.addEventListener('selectionchange', function () {
    var sel = window.getSelection();
    if (!sel || sel.isCollapsed || sel.rangeCount === 0) { return; }
    var node = sel.anchorNode;
    var el = node && node.nodeType === 3 ? node.parentElement : node;
    var block = el ? el.closest('[data-md-start]') : null;
    if (!block) { return; }
    postToHost({
      type: 'select',
      start: parseInt(block.getAttribute('data-md-start'), 10),
      end: parseInt(block.getAttribute('data-md-end'), 10)
    });
  });

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', function (e) {
      var data = e.data;
      if (!data || !data.type) { return; }
      switch (data.type) {
        case 'setContent': setContent(data.html); break;
        case 'scrollTo': scrollToRatio(data.ratio); break;
        case 'highlight': highlightRange(data.start, data.end); break;
        case 'theme': applyTheme(data.vars); break;
      }
    });
  }
})();
</script>
</body>
</html>
""";
}
