# RtlFix

راست‌نویسی (راست‌به‌چپ کردن) متن فارسی **درجا** داخل برنامه‌های ویندوزی — بدون کپی/پیست و بدون پنجره‌ی جداگانه.

## چه کاری انجام می‌دهد

| محیط | روش |
|---|---|
| ترمینال‌ها (Windows Terminal، cmd، PowerShell، هر کنسول bidi-کور) | بازنویسی مستقیم بافر کنسول: شکل‌دهی حروف + ترتیب بصری UAX #9 + آینه‌سازی پرانتزها |
| برنامه‌های Electron/Chromium (Qoder، Antigravity، VS Code، LM Studio، ZCode و…) | تزریق زنده از طریق CDP با **کشف خودکار پورت از پروسه‌ی درحال‌اجرا** — مستقل از اینکه برنامه از کجا اجرا شده باشد |
| VS Code و برنامه‌های HTML (Freebuff، LM Studio) | تزریق مستقیم CSS/HTML |
| Windows Terminal | تنظیم فونت با اتصال درست حروف |
| مرورگرها | اکستنشن `RtlNow` |
| هر برنامه‌ی نیتیو دیگر | کلیدهای میانبر کلیپبورد |

برنامه‌ی هدف اگر **بدون** پورت دیباگ اجرا شده باشد، دکمه‌ی تزریق خودش آن را یکبار با پورت باز می‌کند؛ watcher پس‌زمینه هم هر چند ثانیه command line تمام پروسه‌ها را برای پورت دیباگ اسکن می‌کند و هر برنامه‌ای که با آن اجرا شده باشد — از هر جایی — خودکار تزریق می‌شود.

## نصب

1. `RtlFix-Setup-win-x64.zip` را از [Releases](../../releases) یا همین ریپو (پوشه‌ی `release/`) دانلود و باز کنید.
2. `RtlFix.App.exe` را اجرا کنید — در نوار اعلان (System Tray) می‌نشیند.
3. از منوی راست‌کلیک Tray، گزینه‌ی «اجرای خودکار با ویندوز» را در صورت تمایل فعال کنید.

نسخه‌ی self-contained است و نیازی به نصب .NET ندارد. ویندوز ۱۰/۱۱ ×۶۴.

## کلیدهای میانبر (قابل تغییر از Settings)

| کار | کلید |
|---|---|
| اصلاح کلیپ‌بورد | `Ctrl+Alt+R` |
| کپی، اصلاح و پیست | `Ctrl+Alt+V` |
| پیست بصری (برای برنامه‌های bidi-کور) | `Ctrl+Alt+Shift+V` |
| تغییر مود (Auto/Visual/Repair) | `Ctrl+Alt+T` |
| خواندن انتخاب‌شده | `Ctrl+Alt+X` |
| Quick Writer | `Ctrl+Alt+Space` |

## ساخت از سورس

```powershell
dotnet build RtlFix.slnx -c Release
dotnet test tests/RtlFix.Core.Tests -c Release
dotnet publish src/RtlFix.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/RtlFix
```

## ساختار

```
src/RtlFix.Core        # موتور Bidi (مطابق UAX #9، تست‌شده با داده UCD)، شکل‌دهی فارسی، ترنسفورم‌ها
src/RtlFix.App         # Tray app: موتور بافر کنسول، یکپارچه‌ساز برنامه‌های دسکتاپ، تزریق CDP، UI
tools/RtlFix.GenTables # تولید جدول‌های یونیکد از data/ucd
tools/RtlFix.Probe     # ابزار بررسی برنامه‌ی هدف
tests                  # ۶۷ تست (شامل تست تطابق Bidi با BidiCharacterTest.txt)
extension/RtlNow       # اکستنشن مرورگر
release                # فایل نصبی آماده
```
