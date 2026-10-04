import dayjs from 'dayjs';
import type { Dayjs } from 'dayjs';
import type { DriverTrip } from '../api/driverApi';
import { toVietnamIso } from './departureDateTime';

// -----------------------------------------------------------------------------
// Trạng thái ca làm việc của tài xế — logic thuần, tách khỏi màn hình để chạy thử được.
//
// "Ca làm việc" KHÔNG phải một bảng riêng: hệ thống không có thực thể CaLam nào. Ca của một
// tài xế trong ngày CHÍNH LÀ tập chuyến được phân công cho họ trong ngày đó
// (GET /drivers/{id}/trips). Trạng thái vì thế phải SUY RA từ danh sách chuyến, chứ không
// đọc được từ một trường nào có sẵn.
// -----------------------------------------------------------------------------

/** Trạng thái ca làm việc trong ngày. */
export type ShiftState =
  /** Đang có chuyến chạy. */
  | 'OnDuty'
  /** Còn chuyến phía trước, chưa tới giờ khởi hành đầu tiên. */
  | 'Upcoming'
  /** Đã chạy hết chuyến trong ngày. */
  | 'Finished'
  /** Hôm nay không được phân công chuyến nào. */
  | 'Off';

interface ShiftStateMeta {
  label: string;
  /** Tên màu preset của Tag AntD — dùng cùng bộ từ vựng với TRIP_STATUS_META. */
  color: string;
}

export const SHIFT_STATE_META: Record<ShiftState, ShiftStateMeta> = {
  OnDuty: { label: 'Đang trong ca', color: 'processing' },
  Upcoming: { label: 'Chưa vào ca', color: 'blue' },
  Finished: { label: 'Đã hết ca', color: 'success' },
  Off: { label: 'Hôm nay nghỉ', color: 'default' },
};

export interface ShiftSummary {
  state: ShiftState;
  /** Số chuyến trong ngày, KHÔNG tính chuyến đã huỷ. */
  tripCount: number;
  /** Giờ khởi hành chuyến đầu — null khi hôm nay nghỉ. */
  firstDeparture: Dayjs | null;
  /**
   * Giờ tới bến dự kiến của chuyến CUỐI có chốt giờ tới — null khi chưa chuyến nào chốt.
   *
   * Cố ý bỏ qua `arrivalTime` null thay vì lấy tạm giờ khởi hành: lấy tạm sẽ biến một
   * chuyến đang chạy dở thành "đã xong" ngay sau lúc khởi hành.
   */
  lastArrival: Dayjs | null;
  /** Chuyến kế tiếp chưa khởi hành — null khi không còn chuyến nào phía trước. */
  nextDeparture: Dayjs | null;
  /**
   * Có chuyến backend đang đánh dấu `Running` hay không.
   *
   * Khác với `state === 'OnDuty'`: giữa hai chuyến trong ngày tài xế vẫn TRONG CA nhưng không
   * ĐANG CHẠY. Ô mô tả cần phân biệt hai tình huống này.
   */
  hasRunningTrip: boolean;
}

const TIME_FORMAT = 'HH:mm';

function minOf(values: Dayjs[]): Dayjs | null {
  if (values.length === 0) return null;
  return values.reduce((a, b) => (b.isBefore(a) ? b : a));
}

/**
 * Suy ra trạng thái ca làm việc từ danh sách chuyến trong ngày của một tài xế.
 *
 * Thứ tự xét quan trọng — trạng thái do backend báo phải xét TRƯỚC đồng hồ:
 *  1. Bỏ chuyến đã huỷ — chuyến huỷ không khiến ai phải vào ca.
 *  2. Còn chuyến nào backend đánh dấu `Running` thì chắc chắn đang trong ca. Đây là nguồn
 *     tin cậy nhất vì backend biết chuyến nào đang chạy, còn giờ trên máy người xem có thể
 *     lệch. Bước này cũng gỡ đúng ca đang chạy mà chuyến chưa chốt giờ tới.
 *  3. Mọi chuyến đều `Completed` → đã hết ca. Bước này PHẢI đứng trước các phép so đồng hồ:
 *     xét đồng hồ trước thì một tài xế đã chạy xong hết vẫn bị báo "chưa vào ca" mỗi khi
 *     đồng hồ máy người xem chậm hơn giờ khởi hành.
 *  4. Chưa tới giờ khởi hành đầu tiên → chưa vào ca.
 *  5. Đã qua giờ tới bến cuối cùng (khi biết) → đã hết ca.
 *  6. Còn lại: trong ca. Gồm cả khoảng nghỉ giữa hai chuyến — tài xế vẫn trong ca, chỉ là
 *     không đang chạy; ô mô tả phân biệt hai việc đó bằng `hasRunningTrip`.
 */
export function deriveShiftSummary(trips: DriverTrip[], now: Dayjs): ShiftSummary {
  const active = trips.filter((trip) => trip.status !== 'Cancelled');

  if (active.length === 0) {
    return {
      state: 'Off',
      tripCount: 0,
      firstDeparture: null,
      lastArrival: null,
      nextDeparture: null,
      hasRunningTrip: false,
    };
  }

  const firstDeparture = minOf(active.map((trip) => dayjs(trip.departureTime)));

  const lastArrival = (() => {
    const arrivals = active
      .map((trip) => trip.arrivalTime)
      .filter((value): value is string => value !== null)
      .map((value) => dayjs(value));
    if (arrivals.length === 0) return null;
    return arrivals.reduce((a, b) => (b.isAfter(a) ? b : a));
  })();

  const nextDeparture = minOf(
    active
      .filter((trip) => trip.status === 'Scheduled' && !dayjs(trip.departureTime).isBefore(now))
      .map((trip) => dayjs(trip.departureTime)),
  );

  const hasRunningTrip = active.some((trip) => trip.status === 'Running');
  const allCompleted = active.every((trip) => trip.status === 'Completed');

  let state: ShiftState;
  if (hasRunningTrip) {
    state = 'OnDuty';
  } else if (allCompleted) {
    state = 'Finished';
  } else if (firstDeparture && now.isBefore(firstDeparture)) {
    state = 'Upcoming';
  } else if (lastArrival && now.isAfter(lastArrival)) {
    state = 'Finished';
  } else {
    state = 'OnDuty';
  }

  return {
    state,
    tripCount: active.length,
    firstDeparture,
    lastArrival,
    nextDeparture,
    hasRunningTrip,
  };
}

/**
 * Câu mô tả ngắn cho ô "Ca hôm nay", đặt dưới nhãn trạng thái.
 *
 * Trả về số liệu cụ thể (mấy chuyến, mốc giờ nào) thay vì nhắc lại nhãn trạng thái — nhãn
 * đã nằm ngay trên đó rồi.
 */
export function describeShift(summary: ShiftSummary): string {
  switch (summary.state) {
    case 'Off':
      return 'Không có chuyến nào hôm nay';
    case 'Upcoming':
      return summary.firstDeparture
        ? `Vào ca lúc ${summary.firstDeparture.format(TIME_FORMAT)}`
        : 'Chưa xếp chuyến';
    case 'Finished':
      return summary.lastArrival
        ? `${summary.tripCount} chuyến, xong lúc ${summary.lastArrival.format(TIME_FORMAT)}`
        : `${summary.tripCount} chuyến, đã chạy xong`;
    case 'OnDuty':
      // Đang có chuyến chạy là thông tin mạnh nhất, ưu tiên hơn cả chuyến kế tiếp.
      if (summary.hasRunningTrip) return `${summary.tripCount} chuyến, đang chạy`;
      // Nghỉ giữa hai chuyến: điều độ viên cần biết mấy giờ tài xế chạy tiếp, chứ không phải
      // mấy giờ xong cả ngày.
      if (summary.nextDeparture) {
        return `${summary.tripCount} chuyến, chuyến kế tiếp ${summary.nextDeparture.format(TIME_FORMAT)}`;
      }
      return summary.lastArrival
        ? `${summary.tripCount} chuyến, dự kiến xong ${summary.lastArrival.format(TIME_FORMAT)}`
        : `${summary.tripCount} chuyến, đang trong ca`;
  }
}

/**
 * Khoảng thời gian của một ngày theo giờ Việt Nam, dạng chuỗi gửi lên API.
 *
 * Nối hẳn offset +07:00 thay vì nhờ `toISOString()`: hợp đồng `from`/`to` của
 * GET /drivers/{id}/trips nhận ISO 8601 CÓ KÈM múi giờ, và "hôm nay" phải là hôm nay theo
 * giờ bến xe, không phải theo múi giờ của trình duyệt người xem.
 */
export function vietnamDayRange(reference: Dayjs): { from: string; to: string } {
  return {
    from: toVietnamIso(reference.startOf('day')),
    to: toVietnamIso(reference.endOf('day')),
  };
}
