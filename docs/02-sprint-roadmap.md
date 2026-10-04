# Lộ trình Sprint — bản bổ sung

> **Trạng thái:** đề xuất, chờ nhóm chốt
> **Phạm vi:** chỉ xếp 4 User Story còn thiếu vào **Sprint 2–5**.
> **Sprint 1 KHÔNG thay đổi** — Sprint 1 đang chạy, mọi task giữ nguyên.
>
> **Ràng buộc:** làm đúng **24 User Story** có trong Product Backlog. **Không thêm story mới, không sửa nội dung story nào, không mở Sprint 6.** Tài liệu này chỉ trả lời một câu hỏi: 4 story đang thiếu thì xếp vào sprint nào.

## 1. Vì sao có tài liệu này

Lộ trình 5 Sprint (trong `Huong-dan-thanh-vien-BusTicketing.docx`, mục 4) phủ **20/24 User Story**. Bốn story không nằm trong sprint nào:

| US | Nội dung | Epic |
|---|---|---|
| 10 | Thông báo khi xe sắp đến trạm đón/trạm xuống | Định vị & Theo dõi thời gian thực |
| 11 | Tài xế báo cáo sự cố / trễ chuyến để hệ thống cập nhật đến hành khách | Định vị & Theo dõi thời gian thực |
| 17 | Duyệt tài khoản HSSV / người cao tuổi để hưởng giá ưu đãi | Vé tháng & Khuyến mãi |
| 24 | Gửi khiếu nại hoặc đánh giá chất lượng chuyến đi | Quản trị hệ thống & Hỗ trợ |

Điểm đáng lưu ý: ERD tham khảo **đã có bảng cho cả 3 story 11/17/24** (`BAO_CAO_SU_CO`, `loai_uu_dai` + `trang_thai_duyet_uu_dai`, `PHAN_ANH`). Tức là ERD được vẽ theo đủ 24 story, nhưng kế hoạch sprint bỏ sót — hai tài liệu lệch nhau.

**US 17 là trường hợp gấp nhất về mặt kỹ thuật:** Sprint 1 có task *"API cấu hình bảng giá vé theo tuyến và **theo đối tượng ưu đãi**"*. Nghĩa là cấu hình được giá ưu đãi, nhưng không có story nào gán được ưu đãi cho ai → phần giá ưu đãi sẽ là code chết nếu US 17 để quá muộn.

## 2. Lộ trình sau khi bổ sung

Story in **đậm** là phần thêm vào. Các story còn lại giữ **nguyên vị trí cũ**, không xáo trộn.

| Sprint | Mục tiêu | User Story | Số story |
|---|---|---|---|
| 1 | Nền tảng: Tài khoản, Phân quyền & Tuyến đường | 12, 22, 23 *(không đổi)* | 3 |
| 2 | Chuyến xe, Tìm kiếm & Nhật ký | 1, 13, 14, **17** | 4 |
| 3 | Sơ đồ ghế, Giữ chỗ & Định vị | 2, 3, 9, **10**, **11** | 5 |
| 4 | Thanh toán & Vé điện tử | 4, 6, 7, 8 | 4 |
| 5 | Hủy/Đổi vé, Soát vé QR, Vé tháng & Báo cáo | 5, 15, 16, 18, 19, 20, 21, **24** | 8 |

Tổng: **24/24 User Story** đã có sprint. Không story nào bị thêm, sửa, hay bỏ.

## 3. Chi tiết từng story bổ sung

### US 17 — Duyệt đối tượng ưu đãi → **Sprint 2**

| | |
|---|---|
| **Vì sao Sprint 2** | Sprint 1 tạo bảng giá theo đối tượng ưu đãi; để US 17 ở sprint muộn hơn thì phần giá ưu đãi không có dữ liệu thật để kiểm thử suốt 2–3 sprint. Sprint 2 hiện chỉ có 3 story — còn chỗ. |
| **Phụ thuộc** | Tài khoản (S1), bảng giá theo đối tượng ưu đãi (S1) |
| **Cần bổ sung vào CSDL** | **Khuyến nghị bảng riêng `DiscountRequest`** thay vì thêm cột vào `Users`. Lý do: một người có thể gửi → bị từ chối → gửi lại; cần lưu vết phục vụ kiểm toán. Thêm cột vào `Users` sẽ mất lịch sử. |
| **API** | `POST /api/discount-requests` (hành khách gửi kèm minh chứng) · `GET /api/admin/discount-requests?status=` · `PATCH /api/admin/discount-requests/{id}` (duyệt/từ chối) |
| **UI** | Form gửi minh chứng HSSV (upload ảnh thẻ) · Màn hình duyệt cho Quản lý (bảng + lọc trạng thái) |
| ⚠️ **Cảnh báo** | Migration này đụng bảng `Users` do Sprint 1 tạo. Theo `docs/01-kien-truc.md`, `Migrations/*` và `ModelSnapshot.cs` là **của riêng Vàng Thị Dăm** — không ai tự chạy `dotnet ef migrations add`. |

### US 10 — Thông báo trạm → **Sprint 3**

| | |
|---|---|
| **Vì sao Sprint 3** | Ăn trực tiếp vào US 9 (định vị realtime) cùng sprint — dùng chung hạ tầng SignalR, cùng domain. Tách sang sprint khác thì phải dựng lại kênh thông báo. |
| **Phụ thuộc** | US 9 (vị trí xe realtime), US 12 (trạm dừng có toạ độ — S1) |
| **Cần bổ sung vào CSDL** | **ERD hiện chưa đủ để làm story này.** `XE_BUYT` chỉ có `kinh_do_hien_tai`/`vi_do_hien_tai` (toạ độ hiện tại) → **không biết xe đang ở trạm nào / trạm kế tiếp là trạm nào**, mà đó chính là điều kiện để bắn thông báo. Cần tiến độ theo chuyến: `ChuyenXe.ma_tram_hien_tai` hoặc bảng lịch sử vị trí. Ngoài ra cần bảng `Notification` nếu muốn lưu lịch sử thông báo. |
| **Cạm bẫy** | Xe đi và xe về trên cùng tuyến **trùng toạ độ** → chỉ so khoảng cách tới trạm sẽ báo sai chiều. Phải xác định chiều bằng thứ tự trạm (`thu_tu_tram`), không phải bằng toạ độ. |
| **Kênh thông báo** | Cần chốt ngay từ đầu sprint: in-app (SignalR) · email · SMS · hay đẩy thông báo trình duyệt (Web Push). Ảnh hưởng tới hạ tầng và chi phí. |

### US 11 — Báo cáo sự cố → **Sprint 3**

| | |
|---|---|
| **Vì sao Sprint 3** | Nửa sau của story là *"hệ thống cập nhật đến hành khách"* — dùng đúng kênh thông báo của US 10. Tách khỏi US 10 sẽ phải đụng lại kênh đó lần nữa. Nửa đầu (tài xế báo cáo) dùng chung màn hình tài xế đã dựng cho US 9. |
| **Phụ thuộc** | US 13/14 (chuyến xe + phân công tài xế — S2), US 10 (kênh thông báo — cùng sprint) |
| **Cần bổ sung vào CSDL** | Bảng `BaoCaoSuCo` (ERD đã có `BAO_CAO_SU_CO` nhưng **thiếu trạng thái xử lý** — báo rồi thì ai xử lý, đã xử lý chưa). |
| **API** | `POST /api/trips/{id}/incidents` (tài xế báo) · `GET /api/incidents?status=` (quản lý xem) · `PATCH /api/incidents/{id}` (đánh dấu đã xử lý) |
| **UI** | Nút "Báo sự cố" trên màn hình tài xế · Màn hình danh sách sự cố cho Quản lý |
| ⚠️ **Cảnh báo tải** | Sprint 3 thành **5 story** và đây là sprint nặng nhất về kỹ thuật (sơ đồ ghế + giữ chỗ 10 phút có tranh chấp đồng thời + realtime + 2 story thông báo). Nên chia thành 2 luồng song song cho 2 nhóm người, đừng dồn tuần tự. |

### US 24 — Gửi phản ánh → **Sprint 5**

| | |
|---|---|
| **Vì sao Sprint 5** | Story nói về *"chất lượng chuyến đi"* → cần chuyến đã hoàn thành và vé đã thanh toán mới đánh giá được. Sớm nhất là sau Sprint 4 (thanh toán). Đặt ở Sprint 5 cũng khớp chủ đề "khép kín vòng đời vé" của sprint này, và dữ liệu phản ánh là đầu vào cho báo cáo. |
| **Phụ thuộc** | US 15 (xác thực khách đã đi chuyến đó), US 4/6 (vé + thanh toán — S4) |
| **Cần bổ sung vào CSDL** | Bảng `PhanAnh` — ERD đã có `PHAN_ANH`, nhưng **`ma_chuyen` phải cho phép NULL**, nếu không khách không gửi được khiếu nại về tuyến hoặc về app, chỉ gửi được về một chuyến cụ thể. Thiếu cả trạng thái xử lý và phản hồi của nhà xe. |
| **Nên có** | Ràng buộc: chỉ người đã mua vé của chuyến đó mới được đánh giá → tránh spam và tránh đánh giá khống. |

## 4. Sprint 5 sẽ nặng — cần biết trước

Sau khi thêm US 24, Sprint 5 có **8 story**, gấp đôi các sprint khác:

| Sprint | Số story |
|---|---|
| 1 | 3 |
| 2 | 4 |
| 3 | 5 |
| 4 | 4 |
| **5** | **8** |

Vì không mở Sprint 6, đây là hệ quả bắt buộc — **không phải lỗi của việc bổ sung**, mà do Sprint 5 vốn đã gánh 7 story từ đầu. Trong 5 sprint thì dồn vào sprint cuối là hợp lý nhất về mặt phụ thuộc, vì US 5, 15, 16, 18, 19, 20, 21, 24 **đều cần dữ liệu thanh toán của Sprint 4** — không thể kéo lên sớm hơn.

Giảm tải cho Sprint 5 mà **không thêm sprint và không thêm story**: chuyển **US 18 (Quản lý Voucher)** sang Sprint 4, vì voucher là cơ chế giảm giá gắn với thanh toán, và US 6 (cổng thanh toán) đã ở Sprint 4. Khi đó Sprint 4 = 5 story, Sprint 5 = 7 story — cân hơn. Việc này thay đổi mục tiêu Sprint 5 (bỏ chữ "Khuyến mãi") nên cần nhóm đồng ý; nếu không thì giữ nguyên 8 story.

## 5. Mục tiêu số 5 của đề tài

Product Vision ghi:

> *"Phân tích mật độ hành khách theo khung giờ và trạm dừng để đề xuất điều chỉnh tần suất chạy xe. **Dự báo nhu cầu di chuyển** để tối ưu số lượng chuyến xe vào các dịp cao điểm."*

Trong 24 story, phần này **chỉ được phủ một phần** bởi US 20 (tỷ lệ lấp đầy) và US 19 (doanh thu). **"Dự báo nhu cầu" không có story nào.**

Theo ràng buộc "chỉ làm đúng số story trong backlog, không thêm story", phần dự báo nhu cầu **không được thêm story** — nên xử lý bằng cách ghi vào mục **"Hướng phát triển"** của báo cáo đồ án, kèm giải thích dữ liệu đã sẵn sàng (`VE_DIEN_TU` + `CHUYEN_XE`) để làm tiếp sau. Đây là cách xử lý trung thực: không hứa tính năng không có story, cũng không bỏ im lặng một mục tiêu đã ghi trong Product Vision.

## 6. Việc cần chốt

| # | Việc | Ai quyết |
|---|---|---|
| 1 | Chấp nhận lộ trình bổ sung (US 17→S2, US 10+11→S3, US 24→S5) | Cả nhóm |
| 2 | Giữ Sprint 5 = 8 story, hay chuyển US 18 sang Sprint 4 (mục 4) | Cả nhóm |
| 3 | Kênh thông báo cho US 10/11 (in-app / email / SMS / Web Push) | Cả nhóm — ảnh hưởng hạ tầng |
| 4 | Phần "dự báo nhu cầu" ghi vào mục Hướng phát triển của báo cáo (mục 5) | Cả nhóm |

## 7. Ba xung đột tài liệu cần xử lý

Không thuộc phạm vi bổ sung story, nhưng gây lệch giữa các thành viên nếu không chốt:

| Vấn đề | `Huong-dan-thanh-vien-BusTicketing.docx` | Thực tế repo |
|---|---|---|
| **CSDL** | "SQL Server", "SSMS" | Code dùng **PostgreSQL** (`UseNpgsql` + `Npgsql.EntityFrameworkCore.PostgreSQL`); `README.md` và `docs/01-kien-truc.md` cũng ghi PostgreSQL (Supabase) |
| **Vai trò** | 4 vai trò; ghi rõ **Tài xế làm luôn việc soát vé** | Code có 4 vai trò ✅ khớp — nhưng ERD ghi **5** (thêm `PhuXe`/Phụ xe), thừa so với cả hai |
| **Nhật ký** | Sprint 2 có "Nhật ký" | US 23 (nhật ký) **đã nằm trọn trong Sprint 1** → Sprint 2 bị trùng, nên sửa mục tiêu Sprint 2 |

Xung đột CSDL là nghiêm trọng nhất: thành viên đọc hướng dẫn rồi cài SQL Server sẽ lệch hẳn với code đang chạy.
