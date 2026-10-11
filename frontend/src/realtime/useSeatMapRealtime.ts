import { useEffect, useRef } from 'react';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

// -----------------------------------------------------------------------------
// Đồng bộ trạng thái ghế realtime giữa các phiên (SignalR) — task cùng tên, dòng 11 Sprint 3
// (Hoàng Văn Thịnh), phục vụ US 2 "Chọn vị trí ghế". Hợp đồng đầy đủ ở docs/api-contract.md,
// mục "Đồng bộ ghế realtime — /hubs/seat-map".
//
// Kiến trúc: hub KHÔNG gửi sơ đồ ghế. Sự kiện SeatMapChanged chỉ báo "sơ đồ ghế của chuyến vừa
// đổi" (payload { tripId }), client nhận rồi tự gọi lại GET /trips/{id}/seats và tự diff. Nhờ vậy
// trạng thái ghế chỉ có MỘT nguồn sự thật duy nhất là endpoint đó — không có định nghĩa thứ hai
// để lệch nhau, cái giá chỉ là một request thừa mỗi lần có sự kiện.
// -----------------------------------------------------------------------------

// URL hub suy từ chính VITE_API_URL mà axiosClient dùng (axiosClient.ts dòng 21 — biến ở đó
// không export nên chép lại đúng một dòng còn hơn nới quyền cho tầng api). Hub sống ở GỐC máy
// chủ (/hubs/seat-map) chứ không nằm dưới tiền tố /api, nên cắt đuôi /api trước khi ghép.
const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5080/api';
const SEAT_MAP_HUB_URL = `${API_BASE_URL.replace(/\/api\/?$/, '')}/hubs/seat-map`;

/**
 * Mở kênh SignalR tới hub sơ đồ ghế cho MỘT chuyến và gọi `onChanged` mỗi khi máy chủ báo sơ đồ
 * ghế của chuyến đó vừa đổi.
 *
 * - `onChanged` được giữ ở ref nên truyền hàm mới mỗi lần render cũng không làm kênh dựng lại —
 *   chỉ `tripId` đổi mới có kênh mới.
 * - Mọi lỗi (bắt tay, vào nhóm) đều được NUỐT: lời nhắc realtime là phụ trợ, màn hình vẫn tải
 *   được sơ đồ ghế bằng request thường — không để một kênh phụ hỏng làm phiền người dùng.
 * - Không gọi `LeaveTrip` lúc dọn: ngắt kết nối đã tự xoá mọi nhóm phía máy chủ (hợp đồng), thêm
 *   một invoke trong lúc teardown chỉ tạo thêm một lời gọi có thể hỏng mà không thêm gì.
 */
export function useSeatMapRealtime(tripId: string, onChanged: () => void): void {
  // Luôn trỏ tới bản onChanged mới nhất — cập nhật sau mỗi render, không phải trong effect dựng
  // kênh, để kênh chỉ phụ thuộc tripId.
  const onChangedRef = useRef(onChanged);

  useEffect(() => {
    onChangedRef.current = onChanged;
  });

  useEffect(() => {
    if (!tripId) return;

    const connection = new HubConnectionBuilder()
      .withUrl(SEAT_MAP_HUB_URL)
      .withAutomaticReconnect({
        // Thử lại vô hạn, cách nhau 15 giây. Mặc định của thư viện chỉ thử 4 lượt trong ~42 giây
        // rồi bỏ hẳn — mất mạng dài (wifi xe khách, 4G chập chờn) là kênh chết im lặng tới khi
        // khách tải lại trang. Kênh này phụ trợ và rẻ, cứ để nó tự sống lại khi có mạng.
        nextRetryDelayInMilliseconds: () => 15_000,
      })
      .configureLogging(import.meta.env.DEV ? LogLevel.Information : LogLevel.Warning)
      .build();

    let cancelled = false;

    connection.on('SeatMapChanged', () => {
      // Nhóm là theo chuyến và mỗi hook chỉ vào MỘT nhóm, nên sự kiện tới đây chắc chắn là của
      // chuyến này — không phải lọc lại theo payload.
      onChangedRef.current();
    });

    // Mất kết nối là MẤT NHÓM (trạng thái nhóm nằm ở máy chủ, không đi theo kênh). Vào lại nhóm
    // sau mỗi lần tự kết nối lại, rồi gọi lại sơ đồ ghế một lượt: sự kiện xảy ra trong lúc đứt
    // kênh không được đệm lại, lượt gọi này bù đúng khoảng đó.
    connection.onreconnected(() => {
      void connection
        .invoke('JoinTrip', tripId)
        .then(() => onChangedRef.current())
        .catch(() => {});
    });

    connection
      .start()
      .then(() => {
        if (cancelled) {
          // Effect đã bị dọn trong lúc đang bắt tay (StrictMode gọi hai lượt ở dev) — dừng ngay
          // kênh vừa dựng, không vào nhóm nữa.
          void connection.stop();
          return;
        }

        return connection.invoke('JoinTrip', tripId);
      })
      .catch(() => {
        // Bắt tay hỏng (máy chủ không chạy, mạng chặn…) → không reo lỗi: sơ đồ ghế vẫn tải bình
        // thường, chỉ mất phần nhắc realtime.
      });

    return () => {
      cancelled = true;
      connection.off('SeatMapChanged');
      void connection.stop();
    };
  }, [tripId]);
}
