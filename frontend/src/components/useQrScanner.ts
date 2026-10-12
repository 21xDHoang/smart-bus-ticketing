import { useCallback, useEffect, useRef, useState } from 'react';
import type { RefObject } from 'react';
import jsQR from 'jsqr';

// -----------------------------------------------------------------------------
// Hook "quét mã QR bằng camera" (US 15 "Soát vé QR", Sprint 4 — task của Nguyễn Đình Băng).
//
// Bật camera sau của thiết bị (facingMode 'environment'), chạy vòng lặp requestAnimationFrame
// chụp từng khung hình vào <canvas> rồi đưa qua jsQR để giải mã. Bắt được một mã thì gọi
// `onResult` ĐÚNG MỘT LẦN rồi dừng quét (để màn hình hiển thị kết quả); màn hình tắt `active`
// để hook dừng camera, bật lại để quét tiếp.
//
// Hook KHÔNG vẽ gì lên màn hình — chỉ trả `videoRef` để màn hình gắn vào <video> của mình.
// Luật quan trọng:
// - Mỗi lượt chạy (đổi `active`/`attempt`) là MỘT phiên camera riêng, tự dọn dẹp khi kết thúc
//   (tắt track + huỷ frame) — React StrictMode gọi effect hai lần trong dev cũng không rò camera.
// - Giải mã được giãn cách ~180ms/khung để khỏi chiếm CPU — quét QR không cần giải mã 60fps.
// -----------------------------------------------------------------------------

/** Trạng thái của camera — `starting`/`scanning` là đang chạy, ba trạng thái sau là các lỗi. */
export type QrScannerStatus = 'starting' | 'scanning' | 'denied' | 'unavailable' | 'error';

export interface UseQrScannerResult {
  /** Gắn vào thuộc tính `ref` của thẻ <video> để hook phát luồng camera vào đó. */
  videoRef: RefObject<HTMLVideoElement | null>;
  status: QrScannerStatus;
  /** Câu lỗi thân thiện khi `status` là denied/unavailable/error; null khi đang chạy bình thường. */
  error: string | null;
  /** Chạy lại một phiên camera mới — dùng cho nút "Thử lại" sau khi bị từ chối quyền hoặc lỗi. */
  restart: () => void;
}

/** Khoảng cách tối thiểu giữa hai lượt giải mã (ms) — giảm tải CPU khi quét liên tục. */
const SCAN_INTERVAL_MS = 180;

export function useQrScanner(onResult: (text: string) => void, active: boolean): UseQrScannerResult {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [status, setStatus] = useState<QrScannerStatus>('starting');
  const [error, setError] = useState<string | null>(null);
  // Tăng giá trị để chạy lại cả phiên camera (nút "Thử lại" khi bị lỗi/đã bị từ chối quyền).
  const [attempt, setAttempt] = useState(0);

  // Giữ callback mới nhất trong ref — tránh khởi động lại camera mỗi khi `onResult` đổi tham
  // chiếu (màn hình thường truyền arrow hàm nội tuyến, đổi identity sau mỗi lần render).
  const onResultRef = useRef(onResult);
  useEffect(() => {
    onResultRef.current = onResult;
  }, [onResult]);

  useEffect(() => {
    if (!active) return;

    // Chụp phần tử <video> một lần ở đầu effect — ref trỏ tới cùng một nút DOM suốt đời
    // component nên không cần đọc lại `videoRef.current` trong vòng lặp hay cleanup.
    const video = videoRef.current;

    let disposed = false;
    let stream: MediaStream | null = null;
    let rafId = 0;
    let lastScanAt = 0;

    // canvas dùng chung cho cả phiên — đặt willReadFrequently vì gọi getImageData liên tục.
    const canvas = document.createElement('canvas');
    const context = canvas.getContext('2d', { willReadFrequently: true });

    function stopCamera() {
      if (stream) {
        stream.getTracks().forEach((track) => track.stop());
        stream = null;
      }
    }

    function tick() {
      if (disposed) return;

      const now = performance.now();
      if (now - lastScanAt < SCAN_INTERVAL_MS) {
        rafId = requestAnimationFrame(tick);
        return;
      }
      lastScanAt = now;

      if (video && video.readyState === video.HAVE_ENOUGH_DATA && context) {
        canvas.width = video.videoWidth;
        canvas.height = video.videoHeight;
        context.drawImage(video, 0, 0, canvas.width, canvas.height);

        const frame = context.getImageData(0, 0, canvas.width, canvas.height);
        const code = jsQR(frame.data, frame.width, frame.height, {
          inversionAttempts: 'dontInvert',
        });

        if (code && code.data) {
          onResultRef.current(code.data);
          return; // dừng quét — màn hình sẽ tắt `active` để đóng camera.
        }
      }

      rafId = requestAnimationFrame(tick);
    }

    async function start() {
      // Về lại trạng thái "đang khởi động" mỗi lần mở camera — kể cả lượt thử lại sau khi lỗi.
      // oxlint-disable-next-line react/set-state-in-effect
      setStatus('starting');
      // oxlint-disable-next-line react/set-state-in-effect
      setError(null);

      if (!navigator.mediaDevices?.getUserMedia) {
        setStatus('unavailable');
        setError('Trình duyệt hoặc thiết bị không hỗ trợ truy cập camera.');
        return;
      }

      try {
        stream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: 'environment' },
          audio: false,
        });
      } catch (err) {
        if (disposed) return;
        const name = err instanceof DOMException ? err.name : null;
        const denied = name === 'NotAllowedError' || name === 'SecurityError';
        setStatus(denied ? 'denied' : 'error');
        setError(
          denied
            ? 'Bạn đã từ chối quyền truy cập camera. Hãy cấp quyền trong trình duyệt rồi thử lại.'
            : 'Không mở được camera. Kiểm tra thiết bị hoặc camera đang bị ứng dụng khác chiếm dụng.',
        );
        return;
      }

      if (disposed) {
        stopCamera();
        return;
      }

      if (!video) {
        stopCamera();
        setStatus('error');
        setError('Không tìm thấy khung hiển thị camera.');
        return;
      }

      video.srcObject = stream;

      try {
        await video.play();
      } catch {
        if (!disposed) {
          stopCamera();
          setStatus('error');
          setError('Không phát được luồng camera.');
        }
        return;
      }

      if (disposed) {
        stopCamera();
        return;
      }

      setStatus('scanning');
      lastScanAt = 0;
      rafId = requestAnimationFrame(tick);
    }

    start();

    return () => {
      disposed = true;
      cancelAnimationFrame(rafId);
      stopCamera();
      if (video) video.srcObject = null;
    };
  }, [active, attempt]);

  const restart = useCallback(() => {
    setAttempt((value) => value + 1);
  }, []);

  return { videoRef, status, error, restart };
}
