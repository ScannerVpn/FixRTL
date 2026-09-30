/**
 * RtlNow — Direction Engine
 * Accurately detects and applies Right-to-Left (RTL) direction to Persian/Arabic
 * text blocks in Gmail, web pages, and web applications in-place.
 */

const RTL_REGEX = /[\u0600-\u06FF\u0750-\u077F\u08A0-\u08FF\uFB50-\uFDFF\uFE70-\uFEFF]/;
const STRONG_REGEX = /[\u0600-\u06FF\u0750-\u077F\u08A0-\u08FF\uFB50-\uFDFF\uFE70-\uFEFF]|[A-Za-z\u00C0-\u024F\u1E00-\u1EFF]/;

const SKIPPED_TAGS = new Set([
  "SCRIPT", "STYLE", "CODE", "PRE", "KBD", "SAMP", "VAR", "NOSCRIPT", "CANVAS", "SVG", "MATH"
]);

function startsWithRtl(text) {
  const match = text.match(STRONG_REGEX);
  return match !== null && RTL_REGEX.test(match[0]);
}

/**
 * Applies RTL direction to all Persian/Arabic text blocks within the given root element.
 * @param {Element} root
 * @returns {Element[]} Array of modified elements for undo capability
 */
function giveRightToLeft(root) {
  const modified = [];
  if (!root || root.nodeType !== Node.ELEMENT_NODE) return modified;

  const selector = "p, li, h1, h2, h3, h4, h5, h6, blockquote, td, th, div, textarea, input, span[style*='font'], .a3s, .gmail_default";
  const elements = [
    ...(root.matches?.(selector) ? [root] : []),
    ...root.querySelectorAll(selector)
  ];

  for (const el of elements) {
    if (SKIPPED_TAGS.has(el.tagName)) continue;

    // Handle form inputs and textareas
    if (el.tagName === "TEXTAREA" || (el.tagName === "INPUT" && (!el.type || el.type === "text" || el.type === "search"))) {
      if (!el.hasAttribute("data-rtlnow")) {
        el.setAttribute("data-rtlnow", "true");
        el.setAttribute("dir", "auto");
        modified.push(el);
      }
      continue;
    }

    if (el.hasAttribute("data-rtlnow")) continue;

    // Ignore container divs that contain other block elements, unless it's a known email/message block
    const hasBlockChild = el.querySelector("p, li, h1, h2, h3, h4, h5, h6, blockquote, td, th, table") !== null;
    if (hasBlockChild && (el.tagName === "DIV" || el.tagName === "SECTION" || el.tagName === "ARTICLE") && !el.classList.contains("gmail_default")) {
      continue;
    }

    const text = (el.innerText || el.textContent || "").trim();
    if (!text || !RTL_REGEX.test(text)) continue;

    if (!startsWithRtl(text)) continue;

    // Save previous attribute to allow clean undo
    const prevDir = el.getAttribute("dir");
    if (prevDir !== null) el.setAttribute("data-rtlnow-prev-dir", prevDir);

    el.setAttribute("dir", "rtl");
    el.setAttribute("data-rtlnow", "true");
    el.style.setProperty("direction", "rtl", "important");
    el.style.setProperty("text-align", "right", "important");

    // Fix list parent styling
    if (el.tagName === "LI" && el.parentElement) {
      const parent = el.parentElement;
      if (!parent.hasAttribute("data-rtlnow")) {
        parent.setAttribute("dir", "rtl");
        parent.setAttribute("data-rtlnow", "true");
        parent.style.setProperty("direction", "rtl", "important");
        parent.style.setProperty("text-align", "right", "important");
        parent.style.setProperty("padding-right", "2rem", "important");
        parent.style.setProperty("padding-left", "0", "important");
        modified.push(parent);
      }
    }

    modified.push(el);
  }

  return modified;
}

/**
 * Reverts all RTL changes applied by this extension.
 * @param {Element[]} changed
 */
function takeBackRightToLeft(changed) {
  for (const el of changed) {
    if (!el || !el.isConnected) continue;

    if (el.hasAttribute("data-rtlnow-prev-dir")) {
      el.setAttribute("dir", el.getAttribute("data-rtlnow-prev-dir"));
      el.removeAttribute("data-rtlnow-prev-dir");
    } else {
      el.removeAttribute("dir");
    }

    el.removeAttribute("data-rtlnow");
    el.style.removeProperty("direction");
    el.style.removeProperty("text-align");
    el.style.removeProperty("padding-right");
    el.style.removeProperty("padding-left");
  }
  changed.length = 0;
}
