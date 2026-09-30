/**
 * RtlNow — Background Service Worker
 * Manages context menus and global browser commands.
 */

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({
    id: "rtlnow-toggle",
    title: "راست‌چین‌سازی این صفحه (RTL)",
    contexts: ["page", "selection", "editable"],
  });

  // Ensure default auto-RTL is enabled
  chrome.storage.local.get({ auto: true }, (items) => {
    if (items.auto === undefined) {
      chrome.storage.local.set({ auto: true });
    }
  });
});

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId === "rtlnow-toggle" && tab?.id) {
    chrome.tabs.sendMessage(tab.id, { type: "toggle" }, () => chrome.runtime.lastError);
  }
});

chrome.commands.onCommand.addListener((command, tab) => {
  if (command === "toggle-rtl" && tab?.id) {
    chrome.tabs.sendMessage(tab.id, { type: "toggle" }, () => chrome.runtime.lastError);
  }
});
