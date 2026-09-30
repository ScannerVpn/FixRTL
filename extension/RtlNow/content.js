/**
 * RtlNow — Content Script
 * Monitors page content and applies in-place RTL without copying.
 */

let isActive = false;
let modifiedElements = [];
let observer = null;

function injectStyles() {
  if (document.getElementById("rtlnow-injected-styles")) return;
  const style = document.createElement("style");
  style.id = "rtlnow-injected-styles";
  style.textContent = `
    [data-rtlnow] code,
    [data-rtlnow] pre {
      direction: ltr !important;
      text-align: left !important;
      display: inline-block !important;
      unicode-bidi: isolate !important;
    }
    ol[data-rtlnow],
    ul[data-rtlnow] {
      direction: rtl !important;
      text-align: right !important;
      padding-right: 2rem !important;
      padding-left: 0 !important;
    }
  `;
  (document.head || document.documentElement).appendChild(style);
}

function enable() {
  isActive = true;
  injectStyles();
  modifiedElements = giveRightToLeft(document.body || document.documentElement);

  if (!observer) {
    observer = new MutationObserver((mutations) => {
      if (!isActive) return;
      for (const mutation of mutations) {
        for (const node of mutation.addedNodes) {
          if (node.nodeType === Node.ELEMENT_NODE) {
            modifiedElements.push(...giveRightToLeft(node));
          }
        }
      }
    });
    observer.observe(document.body || document.documentElement, {
      childList: true,
      subtree: true,
    });
  }
}

function disable() {
  isActive = false;
  if (observer) {
    observer.disconnect();
    observer = null;
  }
  takeBackRightToLeft(modifiedElements);
}

function toggle(nextState) {
  const target = typeof nextState === "boolean" ? nextState : !isActive;
  if (target) enable();
  else disable();
  return isActive;
}

// Global shortcut: Alt + Shift + X directly inside the web page
window.addEventListener("keydown", (e) => {
  if (e.altKey && e.shiftKey && (e.key === "X" || e.key === "x" || e.code === "KeyX")) {
    toggle();
  }
});

// Communication with popup and background service worker
chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message.type === "toggle") {
    sendResponse({ on: toggle() });
  } else if (message.type === "state") {
    sendResponse({ on: isActive });
  } else if (message.type === "set") {
    sendResponse({ on: toggle(message.on) });
  }
  return false;
});

// Load saved preferences: default to auto=true so pages are automatically RTL in-place
chrome.storage.local.get({ auto: true }, ({ auto }) => {
  if (auto) enable();
});
