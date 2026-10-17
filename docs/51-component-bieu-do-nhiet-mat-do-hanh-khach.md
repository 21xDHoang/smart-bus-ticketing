# Component biểu đồ nhiệt mật độ hành khách theo giờ và trạm

> Task **"Component biểu đồ nhiệt mật độ hành khách theo giờ và trạm"** — **Sprint 5, dòng 56**
> của bảng phân công (6 điểm, ưu tiên Cao). Backlog ghi người làm là **Hoàng Văn Thịnh**;
> phiên này Vàng Thị Dăm làm theo yêu cầu trực tiếp của chủ dự án (làm hộ, không phải phân công
> chính thức). Đây là việc **frontend**, ngoài ranh giới CSDL của Dăm — vì vậy chỉ tạo **file
> component mới**, không sửa `package.json`, `App.tsx`, `src/api/` hay page của người khác.

## 1. File và phạm vi

| Nội dung | Đường dẫn |
|---|---|
| Component | `frontend/src/components/PassengerDensityHeatmap.tsx` |
| Tài liệu này | `docs/51-component-bieu-do-nhiet-mat-do-hanh-khach.md` |

Component là **thuần hiển thị**: nhận mảng đã tổng hợp qua prop, không gọi API, không đọc
context toàn cục. Nhờ vậy nó dùng được ở bất kỳ màn hình báo cáo nào và không phụ thuộc vào
endpoint chưa có.

## 2. Hợp đồng dữ liệu

```ts
interface PassengerDensityCell {
  stationId: string;    // khoá trạm — danh tính hàng
  stationName: string;  // tên hiển thị
  hour: number;         // 0–23, giờ Việt Nam (UTC+7)
  passengers: number;   // số lượt khách lên xe tại trạm trong giờ đó
}

interface PassengerDensityStation {
  id: string;
  name: string;
}

interface PassengerDensityHeatmapProps {
  data: readonly PassengerDensityCell[];
  stations?: readonly PassengerDensityStation[]; // giữ thứ tự trạm trên tuyến
  hours?: readonly number[];                     // mặc định 0–23
  maxPassengers?: number;                        // trần màu dùng chung giữa nhiều biểu đồ
  emptyText?: ReactNode;
}
```

### Ý nghĩa từng trường

- **`passengers` là số lượt khách LÊN XE**, không phải số vé bán. Nguồn đúng là
  `TicketValidations.BoardingStopId` — trạm phụ xe ghi nhận lúc khách bước lên — chứ không phải
  `Ticket.BoardingStopId` (trạm khách chọn lúc mua, chỉ là thông tin dự kiến; xem doc-comment
  của `TicketValidation`).
- **`hour` là giờ khởi hành của chuyến theo giờ Việt Nam**, cùng quy ước với
  `/reports/peak-hours`: chuyến 00:30 giờ VN thuộc giờ 0 của ngày hôm đó. Không dùng giờ bán vé.
- **`stationId` phải ổn định** giữa các lần tải; component gộp các ô trùng
  `(stationId, hour)` bằng phép cộng, nên dữ liệu thô từ nhiều chuyến có thể truyền thẳng vào.
- `stations` bỏ trống → component lấy danh sách trạm từ `data` và sắp theo tên (tiếng Việt).
  Truyền vào khi màn hình cha cần giữ **thứ tự trạm trên tuyến** hoặc muốn hiện cả trạm chưa có
  khách.
- `maxPassengers` bỏ trống → lấy giá trị lớn nhất trong dữ liệu. Truyền vào khi hai biểu đồ
  cạnh nhau phải dùng **cùng thang màu** để so sánh được.

## 3. Cách hoạt động

- Vẽ bằng `<table>` + CSS, **không thêm thư viện biểu đồ**. Lý do: các biểu đồ tự vẽ khác trong
  repo (`RevenueByRouteChart`, `components/ui/viz.ts`) đã chốt lối "không dùng thư viện ngoài để
  không phải đụng `package.json`", và bảng HTML cho ngữ nghĩa hàng/cột thật.
- **Màu là kênh phụ, không phải kênh duy nhất:** mỗi ô có số lượt khách, tooltip đầy đủ và
  `scope="row"/"col"` cho trình đọc màn hình. Ô 0 để trống số nhưng vẫn có tooltip "0 lượt khách".
- Nền ô trộn `colorPrimary` với `colorFillQuaternary` theo tỉ lệ `giá trị / trần`, có sàn 18%
  để ô có khách rất ít vẫn khác ô 0. Màu lấy từ token AntD nên tự đổi theo chế độ sáng/tối.
- Cột tên trạm dính bên trái (`position: sticky`) khi cuộn ngang; bảng có `minWidth` theo số giờ
  nên trên màn hình hẹp sẽ cuộn ngang thay vì bóp méo ô.
- Không có dữ liệu và không truyền `stations` → hiện `Empty` với câu "Chưa có dữ liệu mật độ
  hành khách theo trạm."

## 4. Cách dùng

```tsx
import PassengerDensityHeatmap from '../components/PassengerDensityHeatmap';

<PassengerDensityHeatmap
  data={cells}                       // PassengerDensityCell[]
  stations={routeStops.map((stop) => ({
    id: stop.stopId,
    name: stop.stopName,
  }))}
/>
```

Màn hình cha chịu trách nhiệm tải dữ liệu và xử lý ba trạng thái tải/ lỗi/ xong như các trang
báo cáo khác. Component không tự bật `loading`; khi đang tải, màn hình cha giữ khung hoặc hiện
`LoadingState` của `components/ui`.

## 5. Nguồn dữ liệu — chưa có endpoint

🔴 **Hiện chưa có endpoint nào trả mật độ hành khách theo trạm.** `GET /reports/peak-hours` chỉ
có trục giờ, không có trạm; `GET /reports/fill-rate` là tỷ lệ lấp đầy theo chuyến/tuyến/khung
giờ, cũng không có trạm. Vì vậy component **chưa được nối vào page nào** — đúng phạm vi task
"component", nhưng là điểm cần bàn tiếp:

1. Muốn có dữ liệu thật, phải **sửa `docs/api-contract.md` trước** (quy ước mục 1.5) để thêm
   endpoint, ví dụ `GET /reports/passenger-density?from=&to=&routeId=`, trả
   `{ stations: [...], hours: 0–23, cells: [{ stationId, hour, passengers }] }`.
2. Người chủ API (Trần Trung Hiếu / Nguyễn Duy Kiên) dựng endpoint, đọc
   `TicketValidations` join `Trips` để lấy giờ khởi hành UTC+7 và `BoardingStopId`.
3. Sau đó Dương Thị Hạnh (màn hình thống kê lấp đầy) hoặc Hoàng Văn Thịnh nối component vào page
   — chỉ là một lời gọi component, không phải sửa lại component.

**Không gọi endpoint chưa có trong `api-contract.md`** — quy ước nhóm, và cũng là lý do component
nhận dữ liệu qua prop thay vì tự fetch.

## 6. Đã kiểm

| Phép kiểm | Kết quả |
|---|---|
| `tsc -b` (frontend) | ✅ 0 error |
| `npm run lint` (oxlint) | ✅ exit 0; 4 warning **có sẵn** ở `HomePage`, `TicketDownloadShare`, `StopManagePage`, `SeatMapPage` — không có warning nào ở file mới |
| `npm run build` | ✅ build thành công (1 phút 53 giây); cảnh báo chunk > 500 kB là **có sẵn** của toàn app, không phải do component này |

Frontend chưa có test runner (không có file `*.test.*`, không có script `test` trong
`package.json`), nên không thêm test tự động. Logic gộp ô và sắp xếp nằm trong `buildGrid` — hàm
thuần, có thể tách ra test riêng khi nhóm bổ sung hạ tầng test.

## 7. Giới hạn đã biết

1. **Chưa có dữ liệu thật** — component mới được kiểm bằng build/type-check, chưa render với dữ
   liệu production.
2. **Chưa có consumer** — page tích hợp thuộc task khác; xem §5.
3. **Không có tương tác** — chưa có click/lọc theo ô. Task chỉ yêu cầu hiển thị; nếu màn hình cha
   cần "bấm ô để lọc", thêm prop `onCellClick` là thay đổi nhỏ, nhưng phải giữ được thao tác bàn
   phím (dùng `<button>` trong ô) chứ không gắn `onClick` lên `<td>`.
4. **Ngưỡng màu** là tương đối theo trần dữ liệu, không phải thang tuyệt đối. Muốn so sánh giữa
   các kỳ, truyền `maxPassengers` chung.
