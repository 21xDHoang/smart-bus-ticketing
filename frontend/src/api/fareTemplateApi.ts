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

/** Chuẩn hoá một ưu đãi đọc từ localStorage — sai kiểu/thiếu trường thì rơi về `fallback`. */
function normalizeDiscount(raw: unknown, fallback: FareDiscount): FareDiscount {
  if (
    typeof raw === 'object' &&
    raw !== null &&
    ((raw as FareDiscount).kind === 'percent' || (raw as FareDiscount).kind === 'amount') &&
    typeof (raw as FareDiscount).value === 'number'
  ) {
    return { kind: (raw as FareDiscount).kind, value: (raw as FareDiscount).value };
  }
  return fallback;
}

/** Đọc bảng giá mẫu đã lưu; chưa có, JSON hỏng hoặc thiếu trường thì gộp về bảng mặc định. */
export function loadFareTemplate(): PerKmFareTemplate {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return DEFAULT_FARE_TEMPLATE;

    const parsed = JSON.parse(raw) as Partial<PerKmFareTemplate>;
    const discounts = (
      typeof parsed.discounts === 'object' && parsed.discounts !== null ? parsed.discounts : {}
    ) as Partial<Record<DiscountedPassengerType, FareDiscount>>;

    return {
      adultPricePerKm:
        typeof parsed.adultPricePerKm === 'number' && Number.isFinite(parsed.adultPricePerKm)
          ? parsed.adultPricePerKm
          : DEFAULT_FARE_TEMPLATE.adultPricePerKm,
      discounts: {
        Student: normalizeDiscount(discounts.Student, DEFAULT_FARE_TEMPLATE.discounts.Student),
        Senior: normalizeDiscount(discounts.Senior, DEFAULT_FARE_TEMPLATE.discounts.Senior),
        Child: normalizeDiscount(discounts.Child, DEFAULT_FARE_TEMPLATE.discounts.Child),
        Disabled: normalizeDiscount(discounts.Disabled, DEFAULT_FARE_TEMPLATE.discounts.Disabled),
      },
    };
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
