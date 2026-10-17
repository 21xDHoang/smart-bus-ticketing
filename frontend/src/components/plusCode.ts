// -----------------------------------------------------------------------------
// Giải mã Plus Code (Open Location Code) của Google Maps — không cần thư viện ngoài.
//
// Plus Code đầy đủ có 10 ký tự "8 ký + '+' + 2 ký" (VD: 8H4H7346+QC) — giải mã trực tiếp
// ra toạ độ. Plus Code RÚT GỌN (VD: 7346+QC) chỉ có 4/6 ký trước '+', phải đi kèm địa danh
// để khôi phục phần tiền tố vùng bị lược bỏ. Luồng: mã đầy đủ → decode thẳng; mã rút gọn →
// geocode địa danh ra toạ độ tham chiếu → recoverNearest.
//
// Thuật toán là Open Location Code (mã nguồn mở của Google), bảng chữ 20 ký tự
// "23456789CFGHJMPQRVWX" (bỏ 0, 1, I, L, O cho khỏi nhầm). Mỗi cặp ký tự thu hẹp ô toạ độ
// 20 lần; ký tự lẻ là vĩ độ, ký tự chẵn là kinh độ.
// -----------------------------------------------------------------------------

const ALPHABET = '23456789CFGHJMPQRVWX';

const ALPHABET_INDEX: Record<string, number> = Object.fromEntries(
  [...ALPHABET].map((char, index) => [char, index]),
);

/** Bắt một token Plus Code hợp lệ trong chuỗi tự do (VD: "7346+QC Vân Hà, Bắc Giang"). */
const PLUS_CODE_PATTERN = /[23456789cfghjmpqrvwx]{2,8}\+[23456789cfghjmpqrvwx]{2,3}/i;

export interface ResolvedLocation {
  latitude: number;
  longitude: number;
}

function digitIndexOf(char: string): number {
  return ALPHABET_INDEX[char.toUpperCase()] ?? -1;
}

/** Giải mã một mã ĐẦY ĐỦ (≥ 10 ký tự sau khi bỏ dấu '+') ra toạ độ tâm ô. */
function decodeFullCode(code: string): ResolvedLocation {
  const clean = code.toUpperCase().replace('+', '').replace(/0+$/, '');

  let latMin = -90;
  let latMax = 90;
  let lngMin = -180;
  let lngMax = 180;

  for (let i = 0; i < clean.length; i += 2) {
    const latDigit = digitIndexOf(clean[i]);
    const lngDigit = digitIndexOf(clean[i + 1]);
    const latStep = (latMax - latMin) / 20;
    const lngStep = (lngMax - lngMin) / 20;

    latMin += latDigit * latStep;
    latMax = latMin + latStep;
    lngMin += lngDigit * lngStep;
    lngMax = lngMin + lngStep;
  }

  return { latitude: (latMin + latMax) / 2, longitude: (lngMin + lngMax) / 2 };
}

/** Mã hoá toạ độ thành tiền tố `digitCount` ký tự (chẵn) — dùng để khôi phục phần vùng. */
function encodePrefix(latitude: number, longitude: number, digitCount: number): string {
  const lat = Math.max(-90, Math.min(90, latitude));
  const lng = Math.max(-180, Math.min(180, longitude));

  let latMin = -90;
  let latMax = 90;
  let lngMin = -180;
  let lngMax = 180;
  let result = '';

  for (let i = 0; i < digitCount; i += 2) {
    const latStep = (latMax - latMin) / 20;
    const lngStep = (lngMax - lngMin) / 20;
    const latDigit = Math.min(19, Math.max(0, Math.floor((lat - latMin) / latStep)));
    const lngDigit = Math.min(19, Math.max(0, Math.floor((lng - lngMin) / lngStep)));

    result += ALPHABET[latDigit] + ALPHABET[lngDigit];

    latMin += latDigit * latStep;
    latMax = latMin + latStep;
    lngMin += lngDigit * lngStep;
    lngMax = lngMin + lngStep;
  }

  return result;
}

/**
 * Khôi phục toạ độ từ mã RÚT GỌN + toạ độ tham chiếu (địa danh). Phần tiền tố vùng bị lược
 * bỏ được tính lại từ toạ độ tham chiếu: địa danh luôn đủ gần vị trí thật nên tiền tố đúng.
 */
function recoverNearest(shortDigits: string, reference: ResolvedLocation): ResolvedLocation {
  const prefixLength = 10 - shortDigits.length;
  const prefix = encodePrefix(reference.latitude, reference.longitude, prefixLength);
  return decodeFullCode(prefix + shortDigits);
}

/** Geocode một địa danh (VD: "Vân Hà, Bắc Giang, Việt Nam") ra toạ độ tham chiếu. */
async function geocodeLocality(locality: string): Promise<ResolvedLocation> {
  const url = `https://photon.komoot.io/api/?q=${encodeURIComponent(locality)}&limit=1`;

  let response: Response;
  try {
    response = await fetch(url);
  } catch {
    throw new Error('Không thể kết nối dịch vụ tìm địa điểm. Kiểm tra mạng.');
  }

  if (!response.ok) {
    throw new Error('Dịch vụ tìm địa điểm báo lỗi. Vui lòng thử lại.');
  }

  const data = (await response.json()) as {
    features?: { geometry?: { coordinates?: [number, number] } }[];
  };
  const coordinates = data.features?.[0]?.geometry?.coordinates;

  if (!coordinates) {
    throw new Error('Không tìm thấy vị trí từ địa điểm đã nhập.');
  }

  // Photon trả [kinh độ, vĩ độ].
  const [longitude, latitude] = coordinates;
  return { latitude, longitude };
}

/**
 * Phân giải một chuỗi người dùng nhập thành toạ độ. Chấp nhận:
 *   - Mã đầy đủ: "8H4H7346+QC"
 *   - Mã rút gọn + địa danh: "7346+QC Vân Hà, Bắc Giang, Việt Nam"
 * Ném lỗi tiếng Việt rõ ràng khi mã không hợp lệ / thiếu địa danh / không geocode được.
 */
export async function resolvePlusCode(input: string): Promise<ResolvedLocation> {
  const trimmed = input.trim();
  if (!trimmed) {
    throw new Error('Vui lòng nhập Plus Code.');
  }

  const match = trimmed.match(PLUS_CODE_PATTERN);
  if (!match) {
    throw new Error('Không tìm thấy mã Plus Code hợp lệ trong chuỗi đã nhập.');
  }

  const code = match[0].toUpperCase();
  const locality = trimmed.replace(match[0], '').replace(/^[\s,]+|[\s,]+$/g, '');
  const cleanDigits = code.replace('+', '').replace(/0+$/, '');

  if (cleanDigits.length >= 10) {
    // Mã đầy đủ — giải mã trực tiếp, không cần geocode.
    return decodeFullCode(code);
  }

  // Mã rút gọn — bắt buộc có địa danh để khôi phục phần vùng.
  if (!locality) {
    throw new Error('Plus Code rút gọn cần kèm địa điểm. VD: 7346+QC Vân Hà, Bắc Giang, Việt Nam.');
  }

  const reference = await geocodeLocality(locality);
  return recoverNearest(cleanDigits, reference);
}
