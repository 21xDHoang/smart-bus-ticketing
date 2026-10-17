# Xuất file lớn bất đồng bộ + thông báo khi xong

> Task **"Xử lý xuất file lớn bất đồng bộ + thông báo khi xong"** — US 24 *"Xuất dữ liệu"*,
> **Sprint 5 dòng 64**, mức **Trung bình**, **4 điểm**, người làm: **Vàng Thị Dăm**.
>
> 🟢 **Trạng thái trên nhánh:** code API + service + job nền + notifier + test đã xong, build sạch.
> Hợp đồng đã ghi vào `docs/api-contract.md` (mục **"Xuất file bất đồng bộ — /exports"**) và bảng
> thông báo đã mở rộng lên **mười một loại**.
>
> ⚠️ **Còn đúng một bước phải chạy trên máy Dăm:** migration `Sprint5_AsyncExportJobs` chưa được
> sinh trong nhánh này. Lệnh `dotnet ef migrations add` bị `.claude/settings.json` chặn (deny rule
> của repo), nên migration phải do Dăm tự chạy — xem §8. **Không merge/deploy trước khi có
> migration**, nếu không mọi endpoint `/api/exports` sẽ lỗi "relation ExportJobs does not exist".

## 1. Vì sao cần task này

Ba bản xuất đồng bộ của Sprint 4 (`GET /reports/revenue/export`, `GET /reports/fill-rate/export`,
`GET /audit-logs/export`) trả file ngay trong request. Báo cáo lấp đầy và nhật ký vì vậy bị chặn
trần **10.000 dòng Excel / 2.000 dòng PDF**: vượt trần là 400, không cắt bớt file. Người dùng có
nhu cầu xuất cả quý / cả năm — số dòng vượt xa trần đó, và dựng file mất hàng chục giây tới vài
phút, đủ để proxy cắt request hoặc người dùng đóng tab.

Task này thêm **đường thứ hai**, không thay đường cũ:

1. Client đặt job → nhận **202** ngay.
2. Job nền dựng file, trần **50.000 dòng Excel / 10.000 dòng PDF**.
3. Xong thì ghi **thông báo in-app** (`ExportCompleted` / `ExportFailed`) và đẩy SignalR.
4. Client hỏi trạng thái hoặc tải file trong **24 giờ**.

## 2. Endpoint

| Method | Endpoint | Quyền | Trả về |
|---|---|---|---|
| POST | `/api/exports` | `ManagerOrAbove`, riêng `audit-logs` là `Admin` | `202` + `ExportJobResponse`, header `Location` |
| GET | `/api/exports/{id}` | chủ job | `200 ExportJobResponse` |
| GET | `/api/exports/{id}/download` | chủ job | file `.xlsx` / `.pdf` |

Không có endpoint danh sách job — xem §10. Hợp đồng chi tiết (body, từng trường, mã lỗi, ví dụ
JSON) nằm trong `api-contract.md`; file này chỉ ghi **thiết kế và lý do**.

## 3. Bảng `ExportJobs`

Một dòng là một yêu cầu xuất, không phải dữ liệu báo cáo. Số liệu vẫn nằm ở bảng nghiệp vụ; file
chỉ là bản sao tại thời điểm dựng.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | `uuid` | `Guid.NewGuid()` trong entity (A1) |
| `UserId` | `uuid` FK → `Users` | **Restrict** — xoá tài khoản không xoá dấu vết xuất báo cáo |
| `RequestedByName` | `varchar(200)` | Họ tên **đóng băng** lúc đặt job, ghi vào khối đầu file |
| `ReportType` | `varchar(20)` | `Revenue` / `FillRate` / `AuditLogs` qua `HasConversion<string>()` (A3) |
| `Format` | `varchar(10)` | `xlsx` / `pdf`, đã chuẩn hoá chữ thường |
| `Status` | `varchar(20)` | `Pending` / `Processing` / `Succeeded` / `Failed` |
| `ParametersJson` | `text` | Ảnh chụp bộ lọc, **bỏ khoá null** — xem §4 |
| `FileName` / `ContentType` | `varchar(200)` / `varchar(100)` | Chỉ có khi `Succeeded` |
| `Content` | `bytea` | Toàn bộ file. Chỉ có khi `Succeeded` |
| `FileSizeBytes` | `bigint` | Để màn danh sách hiển thị mà không đọc `Content` |
| `RowCount` | `int` | Số dòng dữ liệu; `null` với báo cáo doanh thu |
| `ErrorMessage` | `varchar(1000)` | Lý do cho người dùng đọc, không có stack trace |
| `AttemptCount` | `int` | Số lần job nền đã nhận job |
| `CreatedAt` | `timestamptz` | Bắt buộc ở mọi bảng (A4) |
| `UpdatedAt` | `timestamptz?` | Có sửa thì có |
| `StartedAt` / `CompletedAt` / `ExpiresAt` | `timestamptz?` | Mốc xử lý và hạn tải |

Chỉ mục:

- `(UserId, CreatedAt)` — job của tôi, mới nhất trước; đồng thời phục vụ FK.
- `(Status, CreatedAt)` — job nền nhặt việc; không có thì mỗi nhịp quét là đọc toàn bảng.
- `(ExpiresAt)` — lượt dọn job hết hạn.

**Vì sao file nằm trong CSDL chứ không phải đĩa:** hosting có ổ đĩa tạm không bền và có thể nhiều
instance; file trên đĩa của instance A không tải được từ instance B. `bytea` dùng chung cơ chế sao
lưu đã có, và job tự hết hạn sau 24 giờ nên bảng không phình vô hạn. Đánh đổi: tải file tiêu băng
thông CSDL — chấp nhận được vì đây là thao tác thưa.

## 4. Ảnh chụp bộ lọc

`ParametersJson` chỉ chứa **trường thuộc loại báo cáo đã chọn**:

| Loại | Trường giữ |
|---|---|
| `revenue` | `granularity`, `from`, `to`, `routeId` |
| `fill-rate` | `from`, `to`, `routeId`, `timeSlot` |
| `audit-logs` | `from`, `to`, `auditUserId`, `auditAction` |

Hai lý do:

- Trường thừa của form dùng chung **không bị coi là lỗi** — frontend gửi cả form là chuyện bình
  thường, và trường thừa không làm đổi nội dung file. Nhưng chúng cũng không được lọt vào ảnh chụp.
- Đóng băng tham số ngay lúc đặt job: người dùng sửa bộ lọc trong lúc job chạy không được làm đổi
  file đã hứa.

Serializer dùng `JsonSerializerDefaults.Web` + `DefaultIgnoreCondition = WhenWritingNull`, nên dòng
JSON đọc lên đúng là "job này dùng bộ lọc nào".

## 5. Máy trạng thái và job nền

```
POST /exports
      │
      ▼
   Pending ──(job nền nhận)──► Processing ──(dựng xong)──► Succeeded
      ▲                            │                          │
      │                            └──(lỗi)──► Failed ◄───────┘
      └── RecoverStaleAsync: Processing quá 10 phút
```

- Job nền `ExportJobBackgroundService` quét mỗi **2 giây**, mỗi lượt tối đa 10 job, mỗi job một
  scope `AppDbContext` riêng. Nhịp 2 giây (khác 15 phút của job vé tháng) vì độ trễ là thứ người
  dùng nhìn thấy sau khi bấm "Xuất".
- Job `Pending` sống qua restart. Job `Processing` quá 10 phút được đưa về `Pending` **không tăng**
  `AttemptCount` (tăng ở đây là đếm hai lần cho một lần hỏng).
- Mỗi job được nhận tối đa **3 lần**; lần thứ tư chốt `Failed` — một job độc không được quay vòng
  vô hạn.
- `OperationCanceledException` khi app tắt **không** chốt `Failed`: lần khởi động sau job được thu
  hồi và làm tiếp.
- Lượt dọn chạy mỗi giờ, `ExecuteDeleteAsync` trên `ExpiresAt <= now` — xoá thẳng ở CSDL, không nạp
  cột `bytea` vào bộ nhớ.

## 6. Dựng file

Processor **không gọi ba service đồng bộ**, vì hai trong ba service đó có trần 10.000/2.000 nằm
trong file của người khác — đúng thứ job nền cần vượt. Nó dùng nguồn không trần:

| Loại | Nguồn | Trần bất đồng bộ |
|---|---|---|
| `revenue` | `IRevenueExportService.ExportAsync` (tối đa 366 bucket) | không chạm trần |
| `fill-rate` | `IFillRateReportService.GetAsync` (mọi chuyến trong kỳ) | 50.000 Excel / 10.000 PDF |
| `audit-logs` | Đọc thẳng `AuditLogs`, cùng bộ lọc bản đồng bộ | 50.000 Excel / 10.000 PDF |

Vượt trần → job `Failed` kèm câu nêu số dòng thực tế + trần + gợi ý thu hẹp bộ lọc. **Không cắt
bớt file** — chứng từ thiếu dòng mà không ai biết còn tệ hơn không có chứng từ.

Tên file giữ đúng lối ba bản đồng bộ: `bao-cao-doanh-thu_…`, `bao-cao-ty-le-lap-day_…`,
`nhat-ky-kiem-toan_…` + `YYYYMMDD-HHmmssZ` + `.xlsx|.pdf` (ASCII không dấu).

## 7. Thông báo khi xong

Hai loại mới trong bảng `Notifications`:

| `Type` | `RelatedEntityType` | Người nhận |
|---|---|---|
| `ExportCompleted` | `ExportJob` | **Chủ job** — không phải mọi quản lý |
| `ExportFailed` | `ExportJob` | Chủ job |

- Ghi dòng `Notifications` **trước**, rồi đẩy `NotificationReceived(id)` qua `NotificationHub` —
  cùng khuôn `TicketRequestResultNotifier`. Đẩy hỏng không làm hỏng job.
- Dedupe theo `(UserId, Type, RelatedEntityId)` đọc-trước-khi-ghi: job bị thu hồi rồi chốt kết quả
  lần hai chỉ sinh **một** thông báo.
- Thông báo **không chứa số tiền và không chứa URL tuyệt đối**: số tiền là bản sao dễ lệch khỏi
  file; URL nhét vào CSDL sẽ chết khi đổi domain.
- Notifier nằm trong `try/catch` riêng ở processor: nó vỡ cũng **không** được biến job đã
  `Succeeded` thành `Failed`.

## 8. Migration — bước còn thiếu trên nhánh

Bảng `ExportJobs` + hai giá trị enum mới chỉ cần **một** migration. Trên máy Dăm, chạy:

```powershell
$env:Jwt__Key = "local-design-time-only-key-at-least-32-chars-long"
$env:Jwt__Issuer = "SmartBus"; $env:Jwt__Audience = "SmartBusClient"
$env:ConnectionStrings__Default = "Host=localhost;Port=5432;Database=smartbus;Username=postgres;Password=placeholder"
dotnet ef migrations add Sprint5_AsyncExportJobs --project backend/SmartBus.Api
```

Sau đó kiểm file sinh ra:

- `ExportJobs` có đủ cột ở §3, `UserId` FK **`Restrict`**, ba chỉ mục đúng tên cột.
- `ReportType` / `Status` là `varchar(20)`, không phải `integer`.
- `Content` là `bytea`; `CreatedAt`/`ExpiresAt` là `timestamptz`.
- `NotificationType` / `NotificationTarget` là enum C# — không đổi schema, vì hai cột này lưu
  chuỗi và **không** có ràng buộc CHECK theo danh sách giá trị.

Nếu migration đã lỡ sinh sai: xoá cặp file `<timestamp>_Sprint5_AsyncExportJobs.cs` +
`.Designer.cs`, `git checkout -- backend/SmartBus.Api/Migrations/AppDbContextModelSnapshot.cs`, rồi
chạy lại. (`migrations remove` cần PostgreSQL thật, xem `docs/25`.)

## 9. Test

Ba file mới, **43 ca** cùng với test host:

| File | Nội dung |
|---|---|
| `ExportJobsApiTests.cs` | 202 + `Location`, trường thừa bị bỏ qua, 400 theo đúng trường, 403 audit-logs cho Manager, 404 tuyến, 409 trần 3 job, 404 job người khác/hết hạn, 409 và 200 khi tải file |
| `ExportJobProcessorTests.cs` | Job lấp đầy / doanh thu / nhật ký xong và ghi đúng file + `RowCount`, `ExportCompleted`; định dạng lạ và hết 3 lần thử → `Failed` + `ExportFailed`; chốt lại không ghi trùng thông báo; hàng đợi rỗng |
| `ExportJobServiceTests.cs` | Thu hồi chỉ chạm `Processing` quá hạn, không tăng `AttemptCount`; dọn chỉ xoá job quá `ExpiresAt` (chạy trên SQLite vì `ExecuteDeleteAsync` không có trên InMemory); ảnh chụp tham số chỉ giữ trường đúng loại |

Host test thay job nền thật bằng bản noop (`NoopExportJobJob`) — thêm vào `TestAppFactory`,
`TicketValidationTestFactory`, `NhaMaySqlite` và một ca ghim ở `TestHostKhongChayJobNenTests`.
Lý do: job thật quét **2 giây một lượt**, nhanh nhất trong bảy job, sẽ chen vào giữa lúc test seed
và assert.

Lệnh chạy:

```powershell
$env:TEMP='D:\tmp\sbt-test'; $env:TMP='D:\tmp\sbt-test'
dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj --filter "FullyQualifiedName~ExportJob|FullyQualifiedName~TestHostKhongChayJobNen"
```

## 10. Giới hạn đã biết

| Giới hạn | Lý do / hướng xử lý |
|---|---|
| **Chưa có migration trong nhánh** | Deny rule chặn `dotnet ef migrations add`; Dăm chạy tay — §8 |
| **Chưa có màn hình frontend** | Task dòng 64 chỉ giao backend; component xuất báo cáo thuộc task khác. `api-contract.md` đã đủ để Băng nối |
| **Chưa map hai loại thông báo ở `notificationApi.ts`** | File của Băng; đã ghi ghi chú trong `api-contract.md`. Chưa map thì rơi vào nhánh mặc định, không vỡ |
| **Nhận job không nguyên tử** | Đọc job rồi ghi trạng thái, không dùng `UPDATE … WHERE Status='Pending'`/`SKIP LOCKED`. Đúng với một instance; nếu deploy nhiều instance phải đổi sang claim nguyên tử. Job bị hai instance cùng nhận sẽ dựng file hai lần (vô hại về dữ liệu, tốn CPU) |
| **Không có endpoint danh sách job** | Không có story nào cần "lịch sử xuất"; thêm sau là thay đổi cộng thêm |
| **File chiếm chỗ trong CSDL** | Hạn 24 giờ + lượt dọn mỗi giờ; đổi sang object storage là việc của một task riêng nếu lưu lượng tăng |

## 11. Kiểm chứng đã chạy

- `dotnet build backend/SmartBus.Api/SmartBus.Api.csproj` → **0 warning, 0 error**.
- `dotnet build backend/SmartBus.Tests/SmartBus.Tests.csproj` → thành công; 4 warning `xUnit2031`
  có sẵn ở `SubscriptionApiTests.cs`, không liên quan nhánh này.
- `dotnet test … --filter "FullyQualifiedName~ExportJob|…"` → **43/43 xanh**; chạy cả
  `AuditLogCoverageTests` → **40/40 xanh** sau khi khai `POST /api/exports` vào bảng phủ sóng.
- Chạy toàn bộ test suite (1.911 ca): **1.910 xanh, 1 đỏ** — ca đỏ là
  `ReportPdfServiceTests.GeneratePdf_cung_model_ra_cung_file`, **có sẵn từ trước, không liên quan
  nhánh này**: nó so sánh byte của hai file PDF sinh liên tiếp, mà QuestPDF nhúng mốc thời gian
  tạo file nên hai lượt rơi vào hai giây khác nhau là lệch. Chạy riêng lớp đó → **15/15 xanh**.
  File test và service PDF không nằm trong diff của nhánh.
- ⚠️ **GitNexus không chạy được trên máy này** (không có `.gitnexus/run.cjs`, `analyze` crash — xem
  ghi chú trong bộ nhớ dự án). Bước `impact`/`detect_changes` bắt buộc của `CLAUDE.md` được bù
  bằng: đọc toàn bộ service/controller/job nền liên quan, `dotnet build`, chạy test, và rà
  `git status` trước khi commit. Không có thay đổi nào lên symbol dùng chung ngoài việc **thêm**
  giá trị cuối hai enum và **thêm** một lời gọi `ConfigureExportJob` trong `AppDbContext`.
