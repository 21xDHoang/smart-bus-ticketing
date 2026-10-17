import type { DiscountedPassengerType } from './fareApi';
import type { FareDiscount } from '../utils/fareTemplateCalc';

// -----------------------------------------------------------------------------
// Bảng giá mẫu theo km — khái niệm CHỈ có ở frontend (backend chưa có entity).
//
// Đây là bảng QUY ƯỚC: giá của 1 km cho 5 đối tượng. Người lớn (Standard) là giá gốc/km;
// 4 đối tượng ưu đãi (Student/Senior/Child/Disabled) nhận ưu đãi (theo % hoặc số tiền) áp lên
// giá người lớn. Khi áp vào một tuyến, giá từng đối tượng = giá/km sau ưu đãi × số km tuyến.
//
// Lưu ở localStorage nên sống qua refresh nhưng KHÔNG chia sẻ giữa các máy. Khi backend bổ sung
// entity + endpoint thì chuyển sang API thật.
// -----------------------------------------------------------------------------

/** Bảng giá mẫu theo km. */
export interface PerKmFareTemplate {
  /** Giá gốc của 1 km cho người lớn (VND). */
  adultPricePerKm: number;
  /** Ưu đãi của 4 đối tượng còn lại, áp lên giá người lớn. */
  discounts: Record<DiscountedPassengerType, FareDiscount>;
}

const STORAGE_KEY = 'sbt-fare-template-per-km';

/** Giá trị mặc định khi chưa cấu hình — ưu đãi 50% cho HSSV/người cao tuổi/khuyết tật, 30% trẻ em. */
export const DEFAULT_FARE_TEMPLATE: PerKmFareTemplate = {
  adultPricePerKm: 1000,
  discounts: {
    Student: { kind: 'percent', value: 50 },
    Senior: { kind: 'percent', value: 50 },
    Child: { kind: 'percent', value: 30 },
    Disabled: { kind: 'percent', value: 50 },
  },
};

/** Đọc bảng giá mẫu đã lưu; chưa có hoặc dữ liệu hỏng thì trả về bảng mặc định. */
export function loadFareTemplate(): PerKmFareTemplate {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as PerKmFareTemplate) : DEFAULT_FARE_TEMPLATE;
  } catch {
    return DEFAULT_FARE_TEMPLATE;
  }
}

/** Ghi bảng giá mẫu xuống localStorage. */
export function saveFareTemplate(template: PerKmFareTemplate): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(template));
  } catch {
    // Không ghi được thì cấu hình chỉ sống trong phiên này.
  }
}
