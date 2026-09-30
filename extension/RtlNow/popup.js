const page = document.getElementById("page");
const always = document.getElementById("always");
const status = document.getElementById("status");

function say(text) {
  status.textContent = text;
}

/** Ask the page in view: with "toggle" it answers by changing, with "state" it only reports. */
function ask(toggle) {
  chrome.tabs.query({ active: true, currentWindow: true }, ([tab]) => {
    if (!tab?.id) return;
    chrome.tabs.sendMessage(tab.id, { type: toggle ? "toggle" : "state" }, (reply) => {
      if (chrome.runtime.lastError) {
        say("این صفحه از عوض کردن متن خودش نمی‌پذیرد.");
        return;
      }
      page.checked = reply.on;
      chrome.action.setBadgeText({ text: reply.on ? "RTL" : "", tabId: tab.id });
    });
  });
}

page.addEventListener("click", () => ask(!page.checked));

always.addEventListener("click", () => {
  chrome.storage.local.set({ auto: always.checked });
  if (!always.checked) return;
  chrome.tabs.query({}, (tabs) => {
    for (const tab of tabs) {
      if (tab.id) {
        chrome.tabs.sendMessage(tab.id, { type: "set", on: true }, () => chrome.runtime.lastError);
      }
    }
  });
});

chrome.storage.local.get({ auto: true }, ({ auto }) => {
  always.checked = auto;
  ask(false);
});
