# Báo cáo kiểm thử chéo — luồng phản ánh (gửi · phản hồi · thống kê)

| | |
|---|---|
| **Người thực hiện** | Giàng A Vàng (Kiểm thử) |
| **Task** | Sprint 2, story 24 — *Kiểm thử chéo luồng phản ánh trên môi trường online* (dòng r60) |
| **Ngày** | 04/10/2026 |
| **Mốc so sánh** | `main` @ `3ecc099` |
| **Trạng thái** | ⚠️ Chỉ hoàn thành phần kiểm thử **không cần môi trường online** — xem mục 1 |

> Mã lỗi trong báo cáo này (**F1**…) là cục bộ cho báo cáo này, **khác** bộ `C1`–`C6` của
> `docs/bao-cao-kiem-thu-cheo-lich-trinh-sinh-chuyen.md`, bộ `P1`–`P9` của
> `docs/bao-cao-kiem-thu-cheo-phan-cong.md`, bộ `L1`–`L20` của `docs/bao-cao-kiem-thu-sprint-1.md`
> và bộ `V1`–`V7` của `docs/bao-cao-kiem-thu-cheo-ve-thang.md`. Năm bộ không liên tục và không thay
> thế nhau.
>
> **F9 lặp lại ở báo cáo thứ năm** (`L20` → `P9` → (báo cáo tra cứu) `N9` → `V7` → `F9`). Ghi lại là
> bằng chứng về việc nó vẫn chưa được xử lý, không phải lỗi mới.

---

## 1. Phạm vi — phần KHÔNG làm được và vì sao

Task yêu cầu kiểm thử **trên môi trường online**. Đây là **báo cáo thứ năm liên tiếp** gặp đúng một
rào chắn, và tình hình không đổi so với báo cáo vé tháng: nhóm đã có **một nửa** — CSDL PostgreSQL
dùng chung trên Supabase từ 03/10/2026 — nhưng nửa còn lại, **API chạy ngoài máy cá nhân**, vẫn chưa
có. "Online" hiện tại nghĩa là *dữ liệu dùng chung*, không phải *hệ thống chạy chung*.

| Kiểm tra | Story 13 | Story 14 | Story 16 (vé tháng) | Nay |
|---|---|---|---|---|
| CSDL dùng chung cho cả nhóm | ❌ mỗi máy một bản | ❌ vẫn vậy | ✅ Supabase | ✅ vẫn có |
| API chạy ngoài máy cá nhân | ❌ không | ❌ không | ❌ không | ❌ **vẫn không** — tài liệu vẫn ghi `http://localhost:5080` |
| `render.yaml` / `fly.toml` / `Procfile` / `vercel.json` / `docker-compose.yml` | ❌ không | ❌ không | ❌ không | ❌ **vẫn không** |
| Workflow deploy | ❌ chỉ `ci.yml` | ❌ chỉ `ci.yml` | ❌ chỉ `ci.yml` | ❌ **vẫn chỉ `ci.yml`** |
| URL online ở đâu đó trong repo | ❌ không | ❌ không | ❌ không | ❌ **không** — grep `onrender\|vercel.app\|netlify.app\|fly.io\|azurewebsites\|railway.app\|herokuapp` toàn repo (trừ `node_modules`, `dist`): **0 kết quả thật** |
| `appsettings.Production.json` | ❌ không | ❌ không | ❌ không | ❌ **vẫn không** |
| Dockerfile backend | ✅ có | ✅ có | ✅ có | ✅ vẫn có — **chưa từng được deploy** |
| Task deploy trong backlog | ❌ không | ❌ không | ❌ không | ❌ **không có dòng nào** |

**Rào chắn trên chính máy này vẫn nguyên:** `backend/SmartBus.Api/appsettings.Development.json`
**không tồn tại**, và chuỗi kết nối Supabase nằm trong tin nhắn ghim của chat nhóm (cố ý — repo
public). Nên ngay cả việc trỏ API ở máy vào CSDL chung cũng không làm được từ đây.

**Hệ quả:** không chạy được một request HTTP thật nào *qua mạng*. Phần đã làm thay thế — như bốn báo
cáo trước — là **kiểm thử chéo có bằng chứng chạy lại được**:

1. **Đo trên pipeline HTTP thật** bằng `TestAppFactory` (app thật + EF InMemory), dựng đủ bốn vai trò
   và một phản ánh thật, rồi chạy trọn luồng: gửi → hành khách đọc → quản trị đọc → phản hồi → đổi
   trạng thái → hành khách đọc lại → thống kê. Số liệu ở mục 2.1.
2. Đối chiếu bốn phía: mã nguồn frontend ↔ controller/service backend ↔ entity/migration ↔
   `docs/api-contract.md` và `docs/25-huong-dan-csdl-chung.md`.

---

## 2. Mốc chuẩn trên `main` @ `3ecc099`

| Hạng mục | Lệnh | Kết quả |
|---|---|---|
| Test backend | `dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj` | ✅ **599/599 pass**, 0 fail, 0 skip, 1 m 16 s (không có test nào của luồng phản ánh chạy ở đây — xem ghi chú dưới) |
| Build frontend | `npm --prefix frontend run build` | ✅ exit 0 — chỉ còn cảnh báo chunk > 500 kB |
| Lint frontend | `npm --prefix frontend run lint` | ⚠️ 1 cảnh báo — đúng cảnh báo cũ `StopManagePage.tsx:34`, **ngoài** luồng phản ánh |
| Rác debug | grep `console.log` / `debugger` / `TODO` / `FIXME` trong `frontend/src` + `backend/SmartBus.Api` | ⚠️ **1 dòng `TODO`** — `tripAssignmentApi.ts:90`, **ngoài** luồng phản ánh |
| Bí mật bị commit | `git ls-files` lọc `.env`, `appsettings.Development.json`, `*.pfx`, `*.pem`, `id_rsa` | ✅ sạch (chỉ có `appsettings.Development.json.example`) |

> **Ghi chú về 599 test:** ba bộ test của chính tác giả luồng phản ánh
> (`FeedbackAdminApiTests`, `FeedbackLookupApiTests`, `FeedbackStatisticsApiTests`) **đã nằm trên
> `main`** và nằm trong 599 ca đó. Bộ test **kiểm thử chéo nhiều bề mặt** do tôi viết (r59,
> `feedback-flow-api`) nằm trên nhánh `feature/24-test-api-phan-anh`, **chưa merge** — nên nó không
> góp vào con số trên. Đây là hai việc khác nhau: r59 kiểm *API*, r60 (báo cáo này) kiểm *luồng*.

### 2.1 Đo trên pipeline HTTP thật — bảng số liệu

Dựng bốn vai trò (Admin / Manager / Driver / Passenger) và một phản ánh `New` của hành khách trong
CSDL test, rồi gọi đúng những endpoint mà **ba module frontend sẽ gọi khi bỏ cờ dữ liệu giả**
(`feedbackApi.ts`, `feedbackSubmitApi.ts`, và màn quản trị còn thiếu — r58):

| Endpoint | Manager | Passenger | Driver | Không token |
|---|---|---|---|---|
| `POST /api/feedbacks` (**gửi phản ánh** — FE gọi khi bấm nút) | — | **404** | — | **404** |
| `GET /api/feedbacks/me` (danh sách của tôi) | — | **200** ✅ | — | 401 |
| `GET /api/feedbacks/me/{id}` (chi tiết + luồng phản hồi) | — | **200** ✅ | — | 401 |
| `GET /api/admin/feedbacks` (hàng đợi quản trị) | **200** ✅ | 403 | 403 | 401 |
| `POST /api/admin/feedbacks/{id}/replies` (phản hồi) | **200** ✅ | 403 | — | 401 |
| `PATCH /api/admin/feedbacks/{id}` (đổi trạng thái) | **200** ✅ | 403 | — | 401 |
| `GET /api/admin/feedbacks/statistics` (thống kê) | **200** ✅ | 403 | — | 401 |
| `GET /api/trips/search` (ô "chọn chuyến" — FE dự kiến gọi) | — | **400** (thiếu `routeId`) | — | **400** |

Nội dung 403: `{"message":"Bạn không có quyền truy cập tính năng này."}`.

Ba chi tiết đáng chú ý trong bảng:

- `POST /api/feedbacks` trả **404 kể cả khi không gửi token**. Endpoint có `[Authorize]` sẽ trả
  **401** trước. Ra 404 nghĩa là **routing không khớp được route nào** — đây là bằng chứng cơ học
  rằng endpoint gửi phản ánh *không tồn tại*, không phải "tồn tại nhưng chặn quyền". Cùng một kết
  luận với `POST /api/monthly-passes` ở báo cáo vé tháng.
- `GET /api/trips/search` trả **400 chứ không phải 401/403** khi không có token — xác nhận controller
  này **công khai thật** (`TripSearchController.cs:12` ghi rõ đây là API công khai duy nhất của bề
  mặt `/trips`), nhưng nó **đòi `routeId`** và chỉ trả chuyến theo *tuyến + ngày*, **không** trả
  "chuyến của tôi". Đây là mấu chốt của F2.
- Bốn endpoint quản trị (`/api/admin/feedbacks*`) **chạy đúng và phân quyền đúng**: Manager và Admin
  qua được, Passenger và Driver bị 403, không token bị 401. Tầng backend của story 24 **không có lỗi
  nào trong bảng này** — vấn đề nằm ở chỗ khác (F1, F2).

---

## 3. Danh sách lỗi

Mức độ: **Cao** = sai chức năng hoặc gây hiểu nhầm nghiêm trọng · **TB** = ảnh hưởng trải nghiệm
hoặc rủi ro · **Thấp** = tài liệu, vệ sinh mã.

| # | Lỗi | Mức | Người xử lý |
|---|---|---|---|
| F1 | Luồng phản ánh **không khép kín được trong ứng dụng** — 4 endpoint quản trị chạy tốt nhưng **không màn hình nào gọi chúng** | 🔴 Cao | Hoàng Văn Thịnh |
| F2 | Màn gửi phản ánh **thiếu hẳn nguồn dữ liệu chuyến**: `listMyRecentTrips()` ném lỗi, hợp đồng không có endpoint, **không có entity Booking/Ticket** | 🔴 Cao | Trần Trung Hiếu + nhóm |
| F3 | `attachmentUrl` gửi **tên file trần** vào cột tên là URL; không có endpoint upload ảnh | 🟠 TB | Dương Thị Hạnh + Trần Trung Hiếu |
| F4 | `CONTENT_MAX_LENGTH = 2000` **bịa**; comment nói "khớp ràng buộc cột `text`" — sai cả hai vế | 🟠 TB | Dương Thị Hạnh |
| F5 | **Năm chỗ nói "CHƯA có migration"** trong khi migration đã tồn tại; chỗ thứ sáu nói đúng | 🟠 TB | Phùng Duy Hoàng + Nguyễn Duy Kiên |
| F6 | Dữ liệu giả của màn gửi phản ánh dùng tuyến **Hà Nội**, CSDL chung seed tuyến **TP.HCM** | 🟡 Thấp | Dương Thị Hạnh |
| F7 | "Không kèm chuyến" quyết định bằng `!routeName` thay vì `tripId` — **sai với chính hợp đồng** | 🟡 Thấp | Nguyễn Đình Băng |
| F8 | Component chọn mức độ hài lòng (r56) **không tồn tại** — hai màn tự nhúng `<Rate>` của antd | 🟡 Thấp | Hoàng Văn Thịnh |
| F9 | `package-lock.json` lạc ở gốc repo (**lần thứ năm**) | 🟡 Thấp | Phùng Duy Hoàng |

> **Về việc gán người xử lý:** các file frontend ở đây không ghi tên người sở hữu trong đầu file.
> Tên người được suy từ bảng phân công Sprint 2 (`Product_Backlog_Smart_Bus.xlsx`): r55 màn gửi phản
> ánh = Dương Thị Hạnh, r56 component rating = Hoàng Văn Thịnh, r57 màn "Phản ánh của tôi" = Nguyễn
> Đình Băng, r58 màn Admin xử lý phản ánh = Hoàng Văn Thịnh; `App.tsx` thuộc Nguyễn Đình Băng (mục
> E1). Cần đối chiếu lại nếu phân công đã đổi.

---

### F1 — Luồng phản ánh không khép kín được trong ứng dụng 🔴 Cao

**Đây là phát hiện quan trọng nhất của báo cáo, và nó thuộc loại "thiếu màn hình", không phải "sai
màn hình".**

Backend của story 24 **đã xong và chạy đúng**: bốn endpoint quản trị (danh sách, chi tiết, phản hồi,
đổi trạng thái) cộng endpoint thống kê đều trả 200 cho Manager/Admin và 403 cho hành khách (mục 2.1).
Nhưng:

```bash
grep -rn "admin/feedbacks" frontend/src
# => 0 kết quả
```

**Không một dòng frontend nào gọi nhóm `/api/admin/feedbacks`.** `App.tsx` chỉ có đúng hai route của
story 24, cả hai đều là màn **hành khách** và đều `roles: []`:

```tsx
// App.tsx:73-74
{ to: '/feedback-submit', label: 'Gửi phản ánh', roles: [] as string[] },
{ to: '/my-feedback',  label: 'Phản ánh của tôi', roles: [] as string[] },
```

Hệ quả trên luồng nghiệp vụ: hành khách bấm gửi (khi API có) → phản ánh nằm ở trạng thái `New` →
**không có cách nào để nhà xe đọc hoặc trả lời nó qua giao diện**. Task r58 "Màn hình Admin xử lý
phản ánh + phản hồi" (Hoàng Văn Thịnh) chưa làm, và đây chính là mắt xích còn thiếu.

Điều đáng nói là **kịch bản demo cũng không hề nhắc tới màn này**. `docs/25-huong-dan-csdl-chung.md`
mục 2 bước 3 chỉ liệt kê "hai màn riêng của Admin: **Người dùng**, **Nhật ký**", và bảng "Màn nào
gọi API thật" ở `:151-158` **không có dòng nào** cho phản ánh phía quản trị. Nói cách khác: tài liệu
hướng dẫn demo đang mô tả một luồng phản ánh **chỉ có một chiều**.

**Cách tái hiện:**

```bash
grep -rn "admin/feedbacks" frontend/src            # 0 kết quả
grep -n "feedback" frontend/src/App.tsx            # chỉ 2 route hành khách
sed -n '143,158p' docs/25-huong-dan-csdl-chung.md  # demo bỏ qua phản ánh quản trị
```

---

### F2 — Màn gửi phản ánh thiếu hẳn nguồn dữ liệu chuyến 🔴 Cao

Backlog r51 mô tả task gửi phản ánh là *"chọn chuyến, loại phản ánh, nội dung, đính kèm ảnh"*. Ba
mục sau đã có đường; mục **"chọn chuyến" thì không**, và vấn đề sâu hơn một endpoint còn thiếu.

`feedbackSubmitApi.ts:128-130` — nhánh API thật:

```ts
async listMyRecentTrips() {
  throw new Error('Chưa có API danh sách chuyến của hành khách. Xem docs/api-contract.md.');
},
```

Comment ngay trên đó (`:124-127`) giải thích đúng: `GET /trips` nằm sau policy `ManagerOrAbove`, nên
hành khách không tự liệt kê được chuyến. Tôi đã kiểm ba tầng, và cả ba đều trống:

| Tầng | Kiểm | Kết quả |
|---|---|---|
| Hợp đồng | `grep -n "chuyến đã đi\|chuyến của tôi\|/trips/me\|my-trips" docs/api-contract.md` | **0 kết quả** — không có mục nào cho endpoint này |
| Backend | bề mặt `/trips` công khai duy nhất là `GET /api/trips/search` | đòi `routeId` (mục 2.1, trả **400**), trả chuyến theo *tuyến + ngày*, **không** theo *người* |
| Mô hình dữ liệu | `grep -rn "class Booking\|class Ticket\|class Order\|DbSet<Booking" backend/SmartBus.Api` | **0 kết quả** — **không có entity nào** ghi "hành khách nào đã đi chuyến nào" |

Điểm thứ ba là điểm nặng nhất: đây **không phải một endpoint bị bỏ sót**, mà là **một khái niệm chưa
tồn tại trong mô hình dữ liệu**. Muốn làm đúng "chọn chuyến đã đi" thì phải có bảng vé/đặt chỗ trước
— không có nó thì không cách nào trả lời câu hỏi đó, kể cả khi viết thêm một endpoint mới.

Vì vậy ghi chú trong `docs/25-huong-dan-csdl-chung.md:145-146` — *"backend chưa có `POST /feedbacks`"*
— là **đúng nhưng thiếu**: màn gửi phản ánh đang thiếu **hai** thứ, không phải một, và thứ thứ hai
(`nguồn chuyến của hành khách`) chưa được ghi ở bất kỳ đâu trong tài liệu hay backlog.

**Ghi nhận công bằng:** tác giả `feedbackSubmitApi.ts` **đã không bịa tên endpoint**. Comment `:126-127`
nói rõ lý do: *"Chưa có contract nên không dựng lời gọi để tránh đặt tên endpoint bịa (quy ước D)"*.
Đây là cách xử lý đúng khi bị chặn — chỗ này làm đúng, chỉ là việc bị chặn thì chưa ai gỡ.

**Cách tái hiện:**

```bash
grep -n "listMyRecentTrips" -A3 frontend/src/api/feedbackSubmitApi.ts
grep -rn "my-trips\|/trips/me" docs/api-contract.md        # 0 kết quả
ls backend/SmartBus.Api/Entities/ | grep -i "booking\|ticket\|order"   # 0 kết quả
```

---

### F3 — `attachmentUrl` gửi tên file trần vào cột tên là URL 🟠 TB

`FeedbackSubmitPage.tsx:87`:

```tsx
attachmentUrl: fileList[0]?.name ?? null,
```

`fileList[0].name` là **tên file trên máy hành khách**, ví dụ `hoa-don.jpg`. Hợp đồng định nghĩa cột
này là `attachmentUrl` — *"Ảnh đính kèm do hành khách gửi"* (`api-contract.md:2093`), tức một URL.
Gửi tên file trần nghĩa là cột sẽ chứa một chuỗi **trông như URL nhưng không trỏ tới đâu**, và màn
"Phản ánh của tôi" hay màn quản trị về sau nếu render nó thành link/ảnh sẽ hỏng hoặc trỏ sai chỗ.

Tác giả có biết: comment `FeedbackSubmitPage.tsx:58-59` ghi *"chỉ giữ trên client để xem trước, **chưa
upload** (backend chưa có endpoint upload ảnh). Khi gửi, tạm gửi tên file"*. Nên đây là **giải pháp
tạm có ý thức**, không phải sơ suất. Vấn đề là nó sẽ **lọt vào CSDL thật** nếu `USE_MOCK_DATA` được
lật sang `false` mà chưa ai làm luồng upload — và khi đó dữ liệu bẩn đã nằm trong bảng rồi mới phát
hiện.

Đề nghị: hoặc bỏ hẳn `attachmentUrl` khỏi payload cho tới khi có endpoint upload, hoặc chốt trước
trong `api-contract.md` rằng trường này **chỉ nhận URL do endpoint upload trả về** (và bỏ trống nếu
chưa upload) — để lúc lật cờ không âm thầm ghi tên file vào.

**Cách tái hiện:**

```bash
sed -n '55,60p;85,90p' frontend/src/pages/FeedbackSubmitPage.tsx
grep -n "attachmentUrl" docs/api-contract.md       # :2093 — "Ảnh đính kèm do hành khách gửi"
```

---

### F4 — `CONTENT_MAX_LENGTH = 2000` bịa, và comment giải thích nó cũng sai 🟠 TB

`FeedbackSubmitPage.tsx:40-41`:

```tsx
/** Số ký tự tối đa của nội dung phản ánh — khớp ràng buộc cột text của backend. */
const CONTENT_MAX_LENGTH = 2000;
```

Comment này sai ở **cả hai vế**:

1. **Cột `text` của backend không có trần độ dài.** `docs/24-huong-dan-migrate-feedbacks.md` ghi rõ
   trong checklist: *"`Content` và `AttachmentUrl` là `text` (không có trần độ dài ở cột)"*, và
   `api-contract.md:2086` chỉ ghi `content` là *"Nội dung phản ánh — cột `text`"*, không kèm giới hạn.
   Nên "khớp ràng buộc cột text" là mô tả một ràng buộc **không tồn tại**.
2. **2000 là con số của bảng khác.** Nó là giới hạn của **nội dung phản hồi** (nhà xe trả lời):
   `FeedbackAdminService.cs:22 MaxReplyLength = 2000`,
   `CreateFeedbackReplyRequest.cs:13 [StringLength(2000, MinimumLength = 1, …)]`,
   `api-contract.md:2187` — *"`content` ✅ 1–2000 ký tự"* trong mục `POST /admin/feedbacks/{id}/replies`.
   Con số bị **sao chép từ luật của phản hồi sang nội dung phản ánh**, hai thứ khác nhau.

Vì `POST /feedbacks` **chưa tồn tại trong hợp đồng** (F2), không ai có thể nói 2000 là đúng hay sai —
nó chỉ là con số frontend tự chọn. Rủi ro: khi Trần Trung Hiếu viết API thật, nếu giới hạn chốt khác
(ví dụ 1000), màn hình sẽ cho gõ 2000 ký tự rồi bị 400 ở phút cuối; còn nếu **không** chốt giới hạn,
thì con số 2000 của frontend thành luật trên thực tế mà không ai quyết.

**Cách tái hiện:**

```bash
sed -n '40,41p' frontend/src/pages/FeedbackSubmitPage.tsx
grep -n "MaxReplyLength\|StringLength(2000" backend/SmartBus.Api/Services/FeedbackAdminService.cs \
  backend/SmartBus.Api/Dtos/Feedbacks/CreateFeedbackReplyRequest.cs
grep -n "không có trần độ dài" docs/24-huong-dan-migrate-feedbacks.md
```

---

### F5 — Năm chỗ nói "CHƯA có migration", nhưng migration đã có 🟠 TB

Migration **tồn tại thật**:

```bash
ls backend/SmartBus.Api/Migrations/ | grep -i feedback
# 20261003124532_Sprint2_Feedbacks_FeedbackReplies.cs
# 20261003124532_Sprint2_Feedbacks_FeedbackReplies.Designer.cs
```

Vậy mà **năm chỗ** trong mã nguồn backend vẫn khẳng định ngược lại:

| File:dòng | Câu |
|---|---|
| `Entities/Feedback.cs:10` | *"⚠️ Bảng **CHƯA có migration**."* |
| `Program.cs:175` | *"nhưng **CHƯA có migration** — việc sinh migration là của Vàng Thị Dăm"* |
| `Program.cs:186` | *"Cũng nằm trên hai bảng **CHƯA có migration** đó"* |
| `Services/FeedbackAdminService.cs:12` | *"Hai bảng đằng sau service này **CHƯA có migration**"* |
| `Services/FeedbackLookupService.cs:24` | *"Hai bảng đằng sau service này **CHƯA có migration**"* |

Và **đúng một chỗ** nói đúng:

| `Services/FeedbackStatisticsService.cs:29` | *"Bảng `Feedbacks` **đã có migration** (Vàng Thị Dăm) nên service này chạy được trên CSDL thật."* |

Năm chọi một, và **năm là phía sai**. Hợp đồng `api-contract.md:2039-2041` cũng đã cập nhật đúng:
*"Hai bảng `Feedbacks`/`FeedbackReplies` **đã có migration** … **còn thiếu bước chạy
`dotnet ef database update` lên CSDL**"*. Việc còn lại là **apply** migration lên CSDL, không phải
**sinh** nó — hai việc khác nhau, và `docs/24-huong-dan-migrate-feedbacks.md` (do Hoàng viết) hiện
vẫn đang hướng dẫn sinh migration, tức tài liệu đó cũng lạc hậu theo cùng một kiểu.

Đây là hậu quả trực tiếp của cơ chế đã thành nếp: comment đúng ở **thời điểm viết**, nhưng khi Dăm
sinh migration xong thì không ai quay lại sửa. Với F5, hậu quả là **hai tài liệu và năm comment cùng
chỉ sai một hướng**, đủ để người sau tưởng mình phải chạy `migrations add` — mà lệnh đó sẽ **sinh
migration trùng** cho hai bảng đã có.

**Cách tái hiện:**

```bash
grep -rn "CHƯA có migration\|chưa có migration" backend/SmartBus.Api --include="*.cs"   # 5 dòng
grep -rn "đã có migration" backend/SmartBus.Api --include="*.cs"                        # 1 dòng
ls backend/SmartBus.Api/Migrations/ | grep -i feedback                                  # có thật
```

---

### F6 — Dữ liệu giả của màn gửi phản ánh dùng tuyến Hà Nội, CSDL chung seed tuyến TP.HCM 🟡 Thấp

`feedbackSubmitApi.ts:145-146` ghi *"tên tuyến khớp monthlyPassApi và feedbackApi để nhất quán khi
demo"*, và bốn chuyến giả ở `:147-172` đều là tuyến **Hà Nội**: `Bến xe Mỹ Đình — Bến xe Gia Lâm`,
`Cầu Giấy — Bờ Hồ Hoàn Kiếm`, `Bến xe Yên Nghĩa — Bến xe Mỹ Đình`.

Nhưng seeder chung lại dựng TP.HCM — `Seed/SampleDataSeeder.cs:91-93`:

```csharp
Name: "Bến Thành — Chợ Lớn",
Origin: "Bến Thành",
Destination: "Chợ Lớn",
```

Nên trong **cùng một buổi demo**, màn "Gửi phản ánh" (đang chạy mock — `USE_MOCK_DATA = true`) hiện
tuyến Hà Nội, còn màn "Phản ánh của tôi" (dữ liệu thật từ CSDL chung) hiện tuyến TP.HCM. Hai màn của
cùng một story nói hai thành phố khác nhau.

Comment "để nhất quán khi demo" vì vậy **không còn đúng**: nó chỉ nhất quán giữa ba file mock với
nhau (`monthlyPassApi`, `feedbackApi`, `feedbackSubmitApi`), còn CSDL chung thì đã đi hướng khác từ
`SampleDataSeeder`. Lưu ý `feedbackApi.ts` (`USE_MOCK_DATA = false`) **không còn chạy** nhánh mock của
nó nữa, nên chỉ dữ liệu giả của màn gửi phản ánh là còn thấy được trên giao diện.

**Cách tái hiện:**

```bash
grep -n "routeName" frontend/src/api/feedbackSubmitApi.ts | head -5   # Hà Nội
grep -n "Bến Thành\|Chợ Lớn" backend/SmartBus.Api/Seed/SampleDataSeeder.cs | head -3
grep -n "USE_MOCK_DATA" frontend/src/api/feedbackSubmitApi.ts frontend/src/api/feedbackApi.ts
```

---

### F7 — "Không kèm chuyến" quyết định bằng `!routeName` thay vì `tripId` 🟡 Thấp

`MyFeedbackPage.tsx:160`:

```tsx
if (!record.routeName) return <Text type="secondary">Không kèm chuyến</Text>;
```

Hợp đồng nói `routeCode`/`routeName`/`departureTime` là **null trong hai trường hợp khác nhau**
(`api-contract.md:2335-2338`):

> *"Cả ba đều `null` khi `tripId` là `null`, **và cũng `null` khi không tìm thấy chuyến**."*

Vậy khi một phản ánh **có** `tripId` nhưng chuyến đó không tra được (chuyến bị xoá, hoặc chuyến nằm
ngoài phạm vi join), màn hình sẽ hiện "Không kèm chuyến" — trong khi phản ánh đó **có** kèm chuyến.
Câu chữ hiển thị nói sai sự thật của bản ghi.

`tripId` nằm ngay trong DTO (`feedbackApi.ts:55`) và phân biệt được chính xác hai ca, nên sửa chỉ là
đổi `!record.routeName` thành `!record.tripId`. Đây là ca "hai tầng hiểu lệch nhau" đúng nghĩa: hợp
đồng đã cảnh báo trước đúng cái bẫy này, frontend vẫn mắc.

**Cách tái hiện:**

```bash
sed -n '156,161p' frontend/src/pages/MyFeedbackPage.tsx
sed -n '2335,2338p' docs/api-contract.md
```

---

### F8 — Component chọn mức độ hài lòng (r56) không tồn tại 🟡 Thấp

Backlog r56 — *"Component chọn mức độ hài lòng (rating sao)"*, Hoàng Văn Thịnh — không có artifact
nào trong repo:

```bash
ls frontend/src/components/ | grep -i "rate\|rating\|star"    # 0 kết quả
```

Thay vào đó **hai màn tự nhúng thẳng `<Rate>` của antd**, mỗi bên một kiểu:

| Nơi | Cách dùng |
|---|---|
| `FeedbackSubmitPage.tsx:153` | `<Rate allowClear />` — cho nhập, cố ý không đặt `count` |
| `FeedbackSubmitPage.tsx:228` | `<Rate disabled value={submitted.rating} />` — màn xác nhận |
| `MyFeedbackPage.tsx:182` | `rating === null ? '—' : <Rate disabled value={rating} />` |

Ba chỗ nhúng, hai quy ước hiển thị "chưa chấm sao" khác nhau (`'—'` ở bảng, để trống ở màn xác nhận),
và không có chỗ nào là nguồn duy nhất cho ngưỡng/ý nghĩa của thang 1–5. Đây là **trùng lặp** hơn là
lỗi chức năng — nên chỉ ở mức Thấp — nhưng nó là lý do r56 tồn tại, và bỏ qua thì hai màn sẽ tiếp tục
lệch nhau khi có người sửa một bên.

**Cách tái hiện:**

```bash
ls frontend/src/components/ | grep -i "rate\|rating\|star"
grep -rn "<Rate" frontend/src --include="*.tsx"
```

---

### F9 — `package-lock.json` lạc ở gốc repo (lần thứ năm) 🟡 Thấp

```bash
git status --short
# ?? package-lock.json
```

File `package-lock.json` **không được theo dõi** nhưng **liên tục xuất hiện** ở gốc repo, không phải
trong `frontend/`. Đây là **lần thứ năm** nó được ghi nhận (`L20` → `P9` → `N9` → `V7` → `F9`), qua
năm báo cáo khác nhau — nghĩa là chưa lần nào được xử lý.

Gốc rễ vẫn là `.gitignore` ở gốc chưa chặn `package-lock.json` (thuộc mục E1, Phùng Duy Hoàng). Vì
file không được track nên nó **không** vào commit của nhóm — rủi ro không phải "rác trong repo" mà là
mỗi thành viên lại thấy một file lạ khi `git status` và phải tự đoán nó là gì.

**Cách tái hiện:**

```bash
git status --short | grep package-lock
grep -n "package-lock" .gitignore || echo "(không có dòng nào chặn)"
```

---

## 4. Đã kiểm và ĐẠT

### Endpoint phản ánh của backend — đúng hợp đồng, phân quyền đúng

Đo ở mục 2.1 và đối chiếu `docs/api-contract.md` mục "Phản ánh — /feedbacks":

| Endpoint | Kiểm | Kết quả |
|---|---|---|
| `GET /api/feedbacks/me` | Hành khách có 3 phản ánh seed | ✅ 200, trả **mảng trần** đúng `api-contract.md:2300`; mỗi phần tử có `id/tripId/routeCode/routeName/departureTime/type/content/attachmentUrl/rating/status/createdAt/updatedAt/replyCount` — **có `replyCount`, không có `replies`** đúng luật "danh sách chỉ có con đếm" |
| `GET /api/feedbacks/me` | Không token | ✅ **401** — chặn đúng |
| `GET /api/feedbacks/me/{id}` | Hành khách đọc phản ánh của mình | ✅ 200, lần này **có `replies`** và **không có `replyCount`** — đúng luật hai hình dạng ngược nhau của hợp đồng |
| `GET /api/feedbacks/me/{id}` | Phản ánh của **người khác** | ✅ **404**, không phải 403 — `FeedbackLookupController` lọc theo `userId` ngay trong truy vấn nên không lộ là bản ghi có tồn tại |
| `GET /api/admin/feedbacks` | Manager / Admin · Passenger / Driver · không token | ✅ **200 / 403 / 401** — đúng `ManagerOrAbove` |
| `POST /api/admin/feedbacks/{id}/replies` | Manager / Passenger | ✅ **200 / 403**. Hành khách tự phản hồi phản ánh của mình bị chặn — đúng: phản hồi là kênh của nhà xe |
| `PATCH /api/admin/feedbacks/{id}` | Manager / Passenger | ✅ **200 / 403** |
| `GET /api/admin/feedbacks/statistics` | Manager / Passenger | ✅ **200 / 403**; `byType` luôn đủ **3 dòng** theo thứ tự khai báo enum |

Về `GET /api/feedbacks/me/{id}` sau khi quản lý và quản trị cùng phản hồi (mục 2.1, dòng cuối cùng):
hành khách đọc lại được đúng `status=InProgress` **và** `replies` có **2** phần tử — trong khi phản
hồi của chính hành khách đã bị 403 và **không** lọt vào. Đây là bằng chứng end-to-end rằng **chiều
đọc của hành khách phản ánh trung thực thao tác của nhà xe**, đúng như thiết kế.

### Seeder dựng đúng dữ liệu mẫu cho màn "Phản ánh của tôi"

`Seed/SampleDataSeeder.cs:456-572` seed **đúng 3 phản ánh** cho hành khách mẫu, và cố ý phủ đủ các
nhánh hiển thị chỉ trong ba dòng — đúng như `docs/25-huong-dan-csdl-chung.md:143-144` mô tả:

| Phản ánh | Loại | Trạng thái | Chuyến | Phản hồi |
|---|---|---|---|---|
| Khiếu nại (xe trễ 30 phút) | `Complaint` | `Resolved` | có (`TripId` = chuyến sớm nhất tuyến `01`) | **2** |
| Khen ngợi (xe sạch, tài xế thân thiện) | `Compliment` | `InProgress` | có (tuyến `02`) | **1** |
| Góp ý (thêm chuyến sau 21 giờ) | `Suggestion` | `New` | **`null`** — đúng nhánh "Không kèm chuyến" | 0 |

Ba trạng thái, ba loại, `2/1/0` phản hồi, và **đủ cả nhánh `tripId` null lẫn có chuyến** — bộ dữ liệu
mẫu này khớp chính xác với những gì màn hình cần để tự kiểm tra, và cũng khớp với hai ca mà bộ test
`FeedbackLookupApiTests` khoá lại. Hàm còn có luật **idempotent** (`:481-484`): hành khách mẫu đã có
phản ánh nào thì bỏ qua trọn cụm, **không chép đè lên phản ánh thật** người dùng đã gửi qua API. Đây
là chỗ làm đúng.

### `feedbackApi.ts` — chính xác, và đã tắt dữ liệu giả

`feedbackApi.ts` (màn "Phản ánh của tôi") là module duy nhất của story 24 đã chuyển sang **API thật**:
`USE_MOCK_DATA = false`. Hai lời gọi thật của nó khớp hợp đồng từng chữ:

```ts
listMyFeedbacks: (status) => axiosClient.get('/feedbacks/me', { params: { status } }),
getMine: (id) => axiosClient.get(`/feedbacks/me/${id}`),
```

Kiểu `MyFeedbackDetail = Omit<MyFeedback, 'replyCount'> & { replies: FeedbackReply[] }` mô hình hoá
**đúng** luật "hai hình dạng ngược nhau" của hợp đồng — dùng kiểu để chặn nhầm lẫn, thay vì để hai
interface song song rồi quên. Và `MyFeedbackPage` nạp `replies` **lười** (chỉ khi mở rộng dòng,
`FeedbackReplies` ở `:26-89`), đúng như hợp đồng hướng dẫn: danh sách không mang `replies`, chi tiết
mới có — nên mở 20 dòng không bắn 20 request chi tiết.

### `FeedbackStatusTag` — không crash với mã trạng thái lạ

`components/FeedbackStatusTag.tsx:16` dùng `STATUS_ICONS: Record<FeedbackStatus, ReactNode>` nhưng
tra cứu có phòng vệ, nên một `status` lạ (giá trị hợp lệ trong tương lai) hiển thị được thay vì
`undefined` làm vỡ render — đúng lối "mã lạ không lỗi" mà `GET /admin/feedbacks?status=` cũng theo
(hợp đồng: `status` gõ sai trả `[]`, **không** 400).

---

## 5. Việc chuyển cho ai

| Việc | Người | Vì sao là của họ |
|---|---|---|
| Làm màn "Admin xử lý phản ánh + phản hồi" (r58) để khép kín luồng (F1) | **Hoàng Văn Thịnh** | r58; và là người viết controller/service quản trị — API đã sẵn sàng, chỉ thiếu giao diện |
| Quyết định + đặc tả `POST /feedbacks` **và** nguồn "chuyến đã đi của hành khách"; chốt giới hạn độ dài `content` (F2, F4) | **Trần Trung Hiếu** | Chủ `api-contract.md`; `25-huong-dan-csdl-chung.md:145-158` đã ghi việc này đang chờ |
| Quyết định có cần bảng vé/đặt chỗ để trả lời "chuyến khách đã đi" hay không (F2) | **nhóm** (chốt phạm vi) + **Vàng Thị Dăm** (migration) | Đây là quyết định mô hình dữ liệu, không phải sửa lỗi — vượt phạm vi một task API |
| `attachmentUrl`: bỏ khỏi payload tạm, hoặc chốt luật "chỉ nhận URL từ endpoint upload" (F3) | **Dương Thị Hạnh** + **Trần Trung Hiếu** | r55 + `feedbackSubmitApi.ts` (Hạnh); luật trường thuộc hợp đồng (Hiếu) |
| Bỏ `CONTENT_MAX_LENGTH` bịa và sửa comment sai (F4); đổi tuyến mock sang TP.HCM (F6) | **Dương Thị Hạnh** | r55 — `FeedbackSubmitPage.tsx` + `feedbackSubmitApi.ts` |
| Sửa 5 comment nói sai về migration (F5) | **Phùng Duy Hoàng** (4 chỗ: `Entities/Feedback.cs`, `Program.cs` ×2, `FeedbackAdminService.cs`) + **Nguyễn Duy Kiên** (1 chỗ: `FeedbackLookupService.cs:24`) | Chủ hai file service tương ứng; chỗ đúng ở `FeedbackStatisticsService.cs:29` của Kiên **không cần sửa** |
| Cập nhật `docs/24-huong-dan-migrate-feedbacks.md` từ "sinh migration" sang "apply migration" (F5) | **Phùng Duy Hoàng** | Người viết tài liệu đó |
| Đổi `!record.routeName` thành `!record.tripId` (F7) | **Nguyễn Đình Băng** | r57 — `MyFeedbackPage.tsx` |
| Làm component rating dùng chung rồi thay 3 chỗ nhúng (F8) | **Hoàng Văn Thịnh** | r56; `components/` + `App.tsx` thuộc E1 |
| `package-lock.json` ở gốc repo (F9) | **Phùng Duy Hoàng** | `.gitignore` thuộc E1 |

---

## 6. Còn lại — chưa kiểm được

| Hạng mục | Vì sao chưa kiểm được |
|---|---|
| Bất kỳ request HTTP thật nào qua mạng | Không có môi trường online (mục 1) |
| API trỏ vào CSDL Supabase chung | Máy này không có `appsettings.Development.json`; chuỗi kết nối nằm ngoài repo |
| Migration `20261003124532` đã **apply** lên CSDL chung chưa | Không kết nối được để xác nhận; hợp đồng `:2040` nói còn thiếu bước `dotnet ef database update` |
| `POST /feedbacks` (gửi phản ánh) | Endpoint **không tồn tại** — đo được 404 (mục 2.1). Task r51 của Trần Trung Hiếu còn "Chưa làm" |
| Nguồn "chuyến đã đi của hành khách" | Không có endpoint (hợp đồng) **và** không có entity (F2) — không có gì để gọi |
| Luồng gửi phản ánh từ giao diện | Chạy trên dữ liệu giả (`USE_MOCK_DATA = true`); nút Gửi chưa từng chạm backend |
| Đính kèm ảnh | Không có endpoint upload (F3); hiện chỉ gửi tên file |
| Màn "Admin xử lý phản ánh + phản hồi" | Chưa tồn tại (r58) — F1 |
| Thống kê phản ánh **theo tuyến** trên giao diện | Endpoint trả dữ liệu đúng, nhưng không màn nào hiển thị nó (F1) |
| Đa trình duyệt / đa thiết bị | Không có môi trường để mở |
| Nhật ký kiểm toán cho `POST /{id}/replies` và `PATCH /{id}` | Đã có `AuditLogCoverageTests` phủ ở tầng test — ngoài phạm vi báo cáo này |

---

## 7. Phụ lục — lệnh chạy lại mọi bằng chứng trong báo cáo

```bash
# Mốc chuẩn
/c/Users/ASUS/.dotnet/dotnet.exe test backend/SmartBus.Tests/SmartBus.Tests.csproj   # 599/599
npm --prefix frontend run build                                                      # exit 0
npm --prefix frontend run lint                                                       # 1 cảnh báo cũ

# Mục 2.1 — bảng HTTP: dựng bốn vai trò + một phản ánh rồi gọi từng endpoint.
#   Dùng TestAppFactory như các lớp test khác; đây là file ĐO TẠM, không commit.
#   (Đã xoá sau khi lấy số — xem lịch sử phiên làm việc; các ca tương đương được khoá
#    vĩnh viễn ở nhánh feature/24-test-api-phan-anh, file FeedbackFlowApiTests.cs.)

# F1 — không màn hình nào gọi API quản trị
grep -rn "admin/feedbacks" frontend/src            # 0 kết quả
grep -n "feedback" frontend/src/App.tsx            # chỉ 2 route hành khách
grep -rn "Admin xử lý phản ánh" docs/25-huong-dan-csdl-chung.md   # 0 kết quả

# F2 — thiếu nguồn chuyến của hành khách (cả 3 tầng đều trống)
grep -n "listMyRecentTrips" -A3 frontend/src/api/feedbackSubmitApi.ts
grep -rn "my-trips\|/trips/me\|chuyến đã đi" docs/api-contract.md
ls backend/SmartBus.Api/Entities/ | grep -i "booking\|ticket\|order"
grep -n "Authorize" backend/SmartBus.Api/Controllers/TripSearchController.cs   # không có => công khai

# F3 — attachmentUrl là tên file
sed -n '58,59p;87p' frontend/src/pages/FeedbackSubmitPage.tsx
grep -n "attachmentUrl" docs/api-contract.md

# F4 — 2000 là luật của PHẢN HỒI, không phải của cột text
grep -n "CONTENT_MAX_LENGTH" frontend/src/pages/FeedbackSubmitPage.tsx
grep -n "MaxReplyLength" backend/SmartBus.Api/Services/FeedbackAdminService.cs
grep -n "StringLength(2000" backend/SmartBus.Api/Dtos/Feedbacks/CreateFeedbackReplyRequest.cs
grep -n "không có trần độ dài" docs/24-huong-dan-migrate-feedbacks.md

# F5 — 5 chọi 1 về migration
grep -rn "CHƯA có migration" backend/SmartBus.Api --include="*.cs"    # 5 dòng
grep -rn "đã có migration" backend/SmartBus.Api --include="*.cs"     # 1 dòng
ls backend/SmartBus.Api/Migrations/ | grep -i feedback                # migration có thật

# F6 — mock Hà Nội vs seeder TP.HCM
grep -n "Bến xe Mỹ Đình" frontend/src/api/feedbackSubmitApi.ts
grep -n "Bến Thành\|Chợ Lớn" backend/SmartBus.Api/Seed/SampleDataSeeder.cs

# F7 — routeName null ở HAI ca khác nhau
sed -n '156,161p' frontend/src/pages/MyFeedbackPage.tsx
sed -n '2335,2338p' docs/api-contract.md

# F8 — r56 không có artifact
ls frontend/src/components/ | grep -i "rate\|rating\|star"    # 0 kết quả
grep -rn "<Rate" frontend/src --include="*.tsx"

# F9
git status --short | grep package-lock
grep -n "package-lock" .gitignore || echo "(không có dòng nào chặn)"
```

---

*Báo cáo này **không** sửa một dòng code nghiệp vụ nào — đúng ranh giới "Người kiểm thử — Giàng A
Vàng" ở `docs/03-quy-uoc.md` mục 2.4. Mọi mục trong mục 3 kèm các bước tái hiện để người sở hữu file
tự sửa; theo luật cứng của nhóm, người kiểm thử **không** tự sửa code nghiệp vụ cho test xanh.*
