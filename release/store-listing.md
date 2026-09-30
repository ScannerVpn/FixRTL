# متن‌های داشبورد Chrome Web Store — RtlNow v1.0.1

فایل آپلود: `release/RtlNow-Chrome-v1.0.1.zip`

---

## ۱. Single purpose (یک جمله، انگلیسی)

```
Detect Persian text on the page the user is viewing and apply right-to-left text direction to it, in place.
```

## ۲. Permission justifications (انگلیسی — در تب Privacy practice)

**Host permission — `<all_urls>`:**
```
The content script runs on every page the user opens to detect Persian/Arabic
text blocks and set their CSS direction to RTL. It reads nothing, stores
nothing, and sends nothing anywhere; it only toggles the `dir` attribute of
elements on the page the user is already viewing.
```

**`storage`:**
```
Stores a single on/off toggle (auto-RTL mode) in chrome.storage.local so the
user's preference survives browser restarts. Never synced, never transmitted.
```

**`contextMenus`:**
```
Adds one right-click item ("راست‌چین‌سازی این صفحه") so the user can toggle
RTL for the current page without opening the popup.
```

**`activeTab`:**
```
Allows the popup and context menu to message the content script of the tab
the user is currently interacting with.
```

## ۳. توضیحات لیستینگ (فیلد Description فروشگاه)

```
راست‌چین‌سازی خودکار و فوری متن‌های فارسی در جیمیل و تمام سایت‌ها — درجا، بدون کپی-پیست.

RtlNow پاراگراف‌های فارسی/عربی هر صفحه را تشخیص می‌دهد و جهت آن‌ها را راست‌به‌چپ می‌کند؛
کدها و بلوک‌های برنامه‌نویسی دست‌نخورده چپ می‌مانند تا خراب نشوند.

ویژگی‌ها:
• تشخیص خودکار متن فارسی در همه‌ی سایت‌ها (شامل جیمیل، توییتر/X، تله‌گرام وب و...)
• کلید صفحه و کلید «همیشه فعال» در پاپ‌آپ
• راست‌کلیک → «راست‌چین‌سازی این صفحه»
• میانبر Alt+Shift+X
• بدون جمع‌آوری هیچ داده‌ای — همه‌چیز محلی (سیاست حریم خصوصی در ریپو)

سورس باز و مستندات: https://github.com/ScannerVpn/FixRTL
```

## ۴. مشخصات پیشنهادی

| فیلد | مقدار |
|---|---|
| Category | **Accessibility** (یا Tools) |
| Language | Persian (فارسی) |
| Privacy policy URL | `https://github.com/ScannerVpn/FixRTL/blob/main/PRIVACY.md` |
| Distribution | Public |
| Regions | همه |

## ۵. اسکرین‌شات‌ها (حداقل ۱، اندازه ۱۲۸۰×۸۰۰ یا ۶۴۰×۴۰۰)

بهترین نمونه: یک ایمیل فارسی در جیمیل **قبل و بعد** از فعال‌شدن. (تب اول «قبل»، تب دوم همان صفحه راست‌چین‌شده — دو اسکرین‌شات جدا بگیر.)

## ۶. نکات ریویو

- `<all_urls>` ریویو طولانی‌تری می‌گیرد (چند روز تا یک‌هفته). اگر اولین سابمیت را رد کردند، متن توجیه بالا را دقیقاً همان‌جا پیست کن و دوباره بفرست.
- هیچ remote code در کار نیست (الزام MV3) ✓ — همه‌ی اسکریپت‌ها داخل پکیج‌اند.
