# Benchmark مربوط به ذخیره‌سازی Weather در SQL Server

این پروژه با SQL Server واقعی کار می‌کند و پنج سناریوی مستقل را با `BenchmarkDotNet` مقایسه می‌کند:

- `AppendOnly`
- `Update`
- `Upsert`
- `ReadLatestSnapshot`
- `BatchCleanup`

## Setup / Run

1. یک database خالی روی SQL Server بسازید و فایل `.env` را در ریشهٔ solution ایجاد کنید. فایل `.env.example` نمونهٔ قابل کپی است:

```powershell
Copy-Item .env.example .env
```

مقدار `SQLSERVER_CONNECTION_STRING` را در `.env` قرار دهید. متغیر process با همین نام نیز پشتیبانی می‌شود و بر مقدار فایل `.env` اولویت دارد.

2. از ریشهٔ solution اجرا کنید:

```powershell
dotnet run --project .\Weather.Performance -c Release
```

در صورت نداشتن مجوز `CREATE TABLE`، ابتدا `Weather.Performance/schema.sql` را روی database اجرا کنید. `GlobalSetup` نیز جدول‌های benchmark و snapshot آزمایشی موردنیاز `ReadLatestSnapshot` را در صورت داشتن مجوز ایجاد می‌کند.

## بخش Persistence Strategy

در production فعلی، `WeatherService` پس از دریافت موفق پاسخ Weather API، آن را با `SqlWeatherRepository.UpsertAsync` در `dbo.WeatherSnapshots` ذخیره می‌کند. بنابراین implementation فعلی production append-only نیست؛ برای هر `LocationKey`، snapshot قبلی update می‌شود و در صورت نبودن رکورد، یک رکورد جدید insert می‌شود. `GetLatestAsync` payload مربوط به همان `LocationKey` را برمی‌گرداند.

`LocationKey` از پارامترهای canonicalized درخواست، یعنی latitude، longitude و مقدار نرمال‌شدهٔ `hourly`، ساخته می‌شود تا درخواست‌های معادل از کلید یکسان استفاده کنند. سناریوی `AppendOnly` در benchmark مسیر جداگانه‌ای برای درج رکورد جدید در جدول benchmark است و به‌تنهایی نشان‌دهندهٔ persistence strategy فعلی production نیست. `Update` و `Upsert` نیز سناریوهای مقایسه‌ای benchmark هستند.

## بخش Cleanup و ملاحظات Production

در production، snapshotهای قدیمی باید بر اساس retention policy به‌صورت دوره‌ای حذف شوند. این cleanup باید خارج از request/response path و به‌عنوان یک background maintenance operation اجرا شود. cleanup بهتر است با batchهای کوچک، مانند `DELETE TOP (1000)`، انجام شود و یک delete بزرگ در یک transaction اجرا نشود.

در implementation فعلی، ستون زمانی جدول production `ReceivedAtUtc` است و index مربوط به cleanup در repository ایجاد می‌شود. این index پیدا کردن efficient رکوردهای منقضی‌شده را پشتیبانی می‌کند:

```sql
CREATE INDEX IX_WeatherSnapshots_ReceivedAtUtc
ON WeatherSnapshots(ReceivedAtUtc);
```

cleanup همچنان می‌تواند روی row یا keyهایی که حذف می‌شوند lock ایجاد کند و در شرایط خاص باعث contention با فعالیت هم‌زمان database شود. بنابراین frequency و batch size باید بر اساس workload و retention requirements محیط production تنظیم شوند.

اگر جدول در production بسیار بزرگ شود، می‌توان time-based partitioning و partition switching یا partition-level retention را بررسی کرد. partitioning عمداً در این assignment پیاده‌سازی نشده است، زیرا برای scope فعلی ضروری نیست و پیچیدگی غیرضروری ایجاد می‌کند.

## بخش Benchmark Interpretation

هر پنج سناریوی `AppendOnly`، `Update`، `Upsert`، `ReadLatestSnapshot` و `BatchCleanup` به‌صورت جداگانه benchmark می‌شوند. `BatchCleanup` عمداً جدا benchmark شده است، چون یک background maintenance operation است و بخشی از request path نیست. بنابراین زمان ثبت‌شده برای `AppendOnly` شامل هزینهٔ cleanup دوره‌ای نیست و هزینهٔ cleanup باید در production به‌صورت amortized maintenance cost در نظر گرفته شود.

benchmark فعلی با تنظیمات زیر اجرا شده است:

- `Workers = 2`
- `KeySpace = 100`
- `OperationsPerWorker = 20`
- `InProcessToolchain`

کار benchmark با مقدار ثابت `OperationsPerWorker` انجام می‌شود و پارامتر `DurationSeconds` وجود ندارد. خروجی اجرای فعلی علاوه بر measurementهای `BenchmarkDotNet`، در `RunConcurrent` مقدارهای `RPS` و latencyهای `p50`، `p95` و `p99` را نیز برای هر سناریو چاپ می‌کند.

نتایج benchmark برای مقایسهٔ رفتار روش‌ها در محیط development هستند و نباید به‌عنوان production capacity یا throughput قطعی تفسیر شوند. performance validation نهایی باید در staging environment و با سخت‌افزار، SQL Server configuration، concurrency، network، connection pool و workload نزدیک به production انجام شود.

## Monitoring اختیاری

در صورت نیاز production می‌توان ابزارهایی مانند `Prometheus` و `Grafana` را برای جمع‌آوری و نمایش metrics به پروژه اضافه کرد. همچنین health check سبک برای بررسی اتصال `SQL Server` قابل افزودن است. این قابلیت‌ها در نسخهٔ فعلی assignment پیاده‌سازی نشده‌اند.

با توجه به timeout پنج‌ثانیه‌ای درخواست Weather API و نسبتاً زیاد بودن سرعت موردنیاز، رویکرد `AppendOnly` انتخاب مناسب‌تری برای مسیر اصلی persistence است؛ با این حال نتایج benchmark صرفاً مقایسه‌ای هستند و ظرفیت قطعی production را تعیین نمی‌کنند.

## Deployment

برای حفظ سادگی پروژه، `Dockerfile` و تنظیمات Docker در این assignment نگهداری نمی‌شوند. استقرار production می‌تواند با `IIS` روی Windows Server انجام شود. مقادیر محیطی مانند connection string و آدرس Weather API باید از environment configuration سرور تأمین شوند و در repository قرار نگیرند.

## OpenAPI

در محیط `Development`، سند OpenAPI در مسیر زیر در دسترس است:

```text
/openapi/v1.json
```

مسیر `/` در API routeای ندارد و بازکردن آن به‌صورت طبیعی `404 Not Found` برمی‌گرداند. `AddOpenApi()` سند JSON را فعال می‌کند و به‌تنهایی Swagger UI ایجاد نمی‌کند؛ برای مشاهدهٔ سند، از آدرس کامل مانند `https://localhost:44379/openapi/v1.json` استفاده کنید.
