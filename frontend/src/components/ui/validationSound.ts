// -----------------------------------------------------------------------------
// Âm thanh phản hồi kết quả soát vé (US 15 "Soát vé QR", Sprint 4 — task "Hiển thị
// kết quả soát vé rõ ràng (xanh/đỏ) + âm thanh phản hồi").
//
// Không dùng file .mp3/.wav — âm tổng hợp trực tiếp bằng Web Audio API nên không thêm
// asset vào repo và vẫn chạy được khi tắt mạng (soát vé ngoại tuyến vẫn phải có phản
// hồi). Kèm rung (navigator.vibrate) vì tài xế quét bằng điện thoại, có thể để máy trên
// giá đỡ không nhìn màn hình — một tín hiệu nghe + rung đủ để biết kết quả mà không cần
// nhìn vào chữ xanh/đỏ.
//
// Tại sao tạo AudioContext TRỄ + gọi resume(): trình duyệt chặn âm thanh khi chưa có
// tương tác người dùng. Lượt soát luôn đi sau một thao tác (cấp quyền camera / gõ mã bấm
// "Kiểm tra") nên resume thành công, nhưng context vẫn cần được "đánh thức" lại mỗi khi
// bị treo ở trạng thái suspended.
// -----------------------------------------------------------------------------

/** Rung thiết bị — mẫu dài/ngắn theo hợp lệ/không; bỏ qua khi trình duyệt không hỗ trợ. */
function vibrate(pattern: number | number[]) {
  try {
    if (typeof navigator !== 'undefined' && 'vibrate' in navigator) {
      navigator.vibrate(pattern);
    }
  } catch {
    // Rung chỉ là phản hồi phụ — một số trình duyệt ném dù có hàm, bỏ qua là đủ.
  }
}

/** AudioContext dùng chung — tạo trễ để không đụng chính sách chặn âm thanh của trình duyệt. */
let audioContext: AudioContext | null = null;

function getAudioContext(): AudioContext | null {
  if (typeof window === 'undefined') return null;

  const ctor: typeof AudioContext | undefined =
    window.AudioContext ??
    (window as Window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;

  if (!ctor) return null;
  if (!audioContext) audioContext = new ctor();
  return audioContext;
}

/** Đánh thức context nếu đang treo — không await nổi cũng không sao, chỉ mất tiếng lượt đó. */
async function resume(context: AudioContext): Promise<void> {
  if (context.state !== 'suspended') return;
  try {
    await context.resume();
  } catch {
    // Bị trình duyệt chặn (chưa đủ tương tác) — bỏ qua, lần soát sau sẽ thử lại.
  }
}

interface ToneOptions {
  frequency: number;
  /** Lệch giây so với `currentTime` — nốt sau xếp chồng lên nốt trước bằng cách cộng dồn. */
  startOffset: number;
  duration: number;
  /** Dạng sóng: sine cho "ding" êm, sawtooth cho "buzz" gắt hơn. */
  type?: OscillatorType;
  /** Âm lượng đỉnh (0..1) — để nốt thấp không chói. */
  peak?: number;
  /** Tần số kết thúc — để nốt hạ/tăng dần; bỏ trống = giữ nguyên tần số. */
  endFrequency?: number;
}

/** Phát một nốt ngắn với bao âm thanh (lên nhanh, xuống dần) để không nghe "cộp". */
function tone(context: AudioContext, options: ToneOptions): void {
  const start = context.currentTime + options.startOffset;
  const oscillator = context.createOscillator();
  const gain = context.createGain();

  oscillator.type = options.type ?? 'sine';
  oscillator.frequency.setValueAtTime(options.frequency, start);
  if (options.endFrequency && options.endFrequency !== options.frequency) {
    oscillator.frequency.exponentialRampToValueAtTime(options.endFrequency, start + options.duration);
  }

  const peak = options.peak ?? 0.2;
  gain.gain.setValueAtTime(0.0001, start);
  gain.gain.exponentialRampToValueAtTime(peak, start + 0.015);
  gain.gain.exponentialRampToValueAtTime(0.0001, start + options.duration);

  oscillator.connect(gain);
  gain.connect(context.destination);

  oscillator.start(start);
  oscillator.stop(start + options.duration + 0.05);
}

/** Vé hợp lệ — "ding-ding" hai nốt tăng dần, rung ngắn: tín hiệu "được lên xe". */
export async function playValidationSuccessSound(): Promise<void> {
  vibrate([60, 80, 60]);

  const context = getAudioContext();
  if (!context) return;
  await resume(context);

  tone(context, { frequency: 659.25, startOffset: 0, duration: 0.14 });
  tone(context, { frequency: 880, startOffset: 0.14, duration: 0.22 });
}

/** Vé không hợp lệ — "buzz" trầm hạ dần, rung lâu hơn: tín hiệu "không cho lên". */
export async function playValidationErrorSound(): Promise<void> {
  vibrate([180, 120, 180]);

  const context = getAudioContext();
  if (!context) return;
  await resume(context);

  tone(context, { frequency: 300, endFrequency: 160, startOffset: 0, duration: 0.32, type: 'sawtooth', peak: 0.16 });
}
