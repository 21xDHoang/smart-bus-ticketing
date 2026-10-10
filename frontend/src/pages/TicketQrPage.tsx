import { useEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Alert, Button, Result, Space, Tag, Typography } from 'antd';
import { CompressOutlined, ExpandOutlined } from '@ant-design/icons';
import ticketApi from '../api/ticketApi';
import type { Ticket } from '../api/ticketApi';
import type { AppError } from '../api/axiosClient';
import TicketQr from '../components/TicketQr';
import TicketStatusTag from '../components/TicketStatusTag';
import { getTicketStatus, isTicketUsable } from '../components/ticketStatus';
import { ErrorState, LoadingState, PageCard, PageHeader } from '../components/ui';
import { formatDateTime, formatVnd } from '../components/ui/format';

const { Text } = Typography;

/** Kết quả một lượt tải vé theo id — bốn trạng thái, không gộp trạng thái nào vào nhau. */
type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'not-found' }
  | { status: 'done'; ticket: Ticket };

/** Tay cầm giữ màn hình sáng trả về từ Wake Lock API — chỉ cần `release` khi rời chế độ quét. */
interface WakeLockHandle {
  release: () => Promise<void>;
}

/**
 * Xin giữ màn hình sáng (Screen Wake Lock) trong lúc quét. Trình duyệt không hỗ trợ → trả
 * `null`, không phải lỗi. Ép kiểu qua `unknown` để không phụ thuộc kiểu `wakeLock` trong lib.dom.
 */
function requestWakeLock(): Promise<WakeLockHandle | null> {
  const wakeLock = (navigator as unknown as {
    wakeLock?: { request: (type: 'screen') => Promise<WakeLockHandle> };
  }).wakeLock;

  if (!wakeLock) return Promise.resolve(null);
  return wakeLock.request('screen');
}

// Màn hình hiển thị mã QR vé + tăng sáng màn hình khi quét (User Story 4, Sprint 3 — task của
// Nguyễn Đình Băng): phóng to mã QR của một vé trên nền trắng để nhân viên soát vé quét khi
// khách lên xe, kèm "chế độ quét" bật toàn màn hình + giữ màn hình sáng (không tự mờ/tắt) trong
// lúc quét. Vào từ nút "Xem mã QR" trên màn hình "Vé của tôi".
//
// Web không có API chỉnh độ sáng phần cứng chuẩn — "tăng sáng" ở đây làm bằng ba thứ đáng tin
// cậy: (1) nền trắng tuyệt đối để đầu đọc thấy mã rõ nhất, (2) toàn màn hình để bỏ mọi thành
// phần giao diện gây tối, (3) Wake Lock giữ màn hình không tự mờ/tắt giữa lúc quét.
export default function TicketQrPage() {
  const { ticketId = '' } = useParams();
  const navigate = useNavigate();

  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);
  const [scanning, setScanning] = useState(false);
  const wakeLockRef = useRef<WakeLockHandle | null>(null);

  // Kích thước mã QR ở chế độ quét: phóng to hết mức nhưng chừa lề để không tràn màn hình hẹp.
  const scanQrSize = Math.max(160, Math.min(300, window.innerWidth - 96));

  // Tải vé theo id trên URL. `cancelled` chặn setState sau khi đã rời màn hình.
  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API. Đây là lần tải thực sự (không phải "đồng
    // bộ state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setState({ status: 'loading' });

    ticketApi
      .get(ticketId)
      .then((ticket) => {
        if (cancelled) return;
        setState(ticket ? { status: 'done', ticket } : { status: 'not-found' });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setState({
          status: 'error',
          message: appError.customMessage || 'Không thể tải vé.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [ticketId, reloadKey]);

  // Người dùng thoát toàn màn hình bằng phím Esc (hoặc nút của trình duyệt) → tắt chế độ quét
  // và nhả wake lock, nếu không trạng thái trong app lệch với màn hình thật.
  useEffect(() => {
    const onFullscreenChange = () => {
      if (document.fullscreenElement) return;
      setScanning(false);
      const handle = wakeLockRef.current;
      wakeLockRef.current = null;
      if (handle) void handle.release().catch(() => {});
    };

    document.addEventListener('fullscreenchange', onFullscreenChange);
    return () => document.removeEventListener('fullscreenchange', onFullscreenChange);
  }, []);

  // Rời màn hình thì nhả wake lock, tránh giữ màn hình sáng mãi sau khi đã đi nơi khác.
  useEffect(() => {
    return () => {
      const handle = wakeLockRef.current;
      wakeLockRef.current = null;
      if (handle) void handle.release().catch(() => {});
    };
  }, []);

  const enterScanMode = () => {
    setScanning(true);

    // Toàn màn hình: phải gọi ngay trong thao tác người dùng thì trình duyệt mới chấp nhận.
    try {
      void document.documentElement.requestFullscreen().catch(() => {});
    } catch {
      // Không vào được toàn màn hình — overlay nền trắng vẫn đủ để quét.
    }

    // Giữ màn hình sáng: best-effort, thiếu thì chế độ quét vẫn chạy bình thường.
    void requestWakeLock()
      .then((handle) => {
        wakeLockRef.current = handle;
      })
      .catch(() => {});
  };

  const exitScanMode = () => {
    setScanning(false);

    const handle = wakeLockRef.current;
    wakeLockRef.current = null;
    if (handle) void handle.release().catch(() => {});

    if (document.fullscreenElement) {
      try {
        void document.exitFullscreen().catch(() => {});
      } catch {
        // Không thoát được toàn màn hình — không ảnh hưởng gì.
      }
    }
  };

  if (state.status === 'loading') {
    return (
      <div>
        <PageHeader title="Mã QR vé" onBack={() => navigate('/my-tickets')} />
        <PageCard>
          <LoadingState description="Đang tải vé…" />
        </PageCard>
      </div>
    );
  }

  if (state.status === 'error') {
    return (
      <div>
        <PageHeader title="Mã QR vé" onBack={() => navigate('/my-tickets')} />
        <PageCard>
          <ErrorState
            title="Không tải được vé"
            description={state.message}
            onRetry={() => setReloadKey((n) => n + 1)}
          />
        </PageCard>
      </div>
    );
  }

  if (state.status === 'not-found') {
    return (
      <div>
        <PageHeader title="Mã QR vé" onBack={() => navigate('/my-tickets')} />
        <PageCard>
          <Result
            status="404"
            title="Không tìm thấy vé"
            subTitle="Vé này không tồn tại hoặc không thuộc tài khoản của bạn."
            extra={
              <Button type="primary" onClick={() => navigate('/my-tickets')}>
                Về danh sách vé
              </Button>
            }
          />
        </PageCard>
      </div>
    );
  }

  const ticket = state.ticket;
  const usable = isTicketUsable(getTicketStatus(ticket));

  return (
    <div>
      <PageHeader
        title="Mã QR vé"
        subtitle="Đưa mã này cho nhân viên soát vé khi lên xe."
        onBack={() => navigate('/my-tickets')}
      />

      <PageCard>
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            gap: 16,
            padding: '16px 0',
          }}
        >
          <Space size={8} wrap>
            <Tag color="blue">{ticket.routeCode}</Tag>
            <Text strong>{ticket.routeName}</Text>
            <TicketStatusTag ticket={ticket} />
          </Space>

          <Text type="secondary">
            {ticket.origin} → {ticket.destination}
          </Text>

          <Text type="secondary">
            Khởi hành {formatDateTime(ticket.departureTime)} · Ghế {ticket.seatNumber}
          </Text>

          <Text type="secondary">
            Lên tại {ticket.boardingStopName} · Xuống tại {ticket.alightingStopName}
          </Text>

          <TicketQr code={ticket.code} size={280} disabled={!usable} />

          <Text type="secondary">
            Mã vé: <Text code>{ticket.code}</Text>
          </Text>

          <Text strong style={{ fontSize: 18 }}>
            {formatVnd(ticket.price)}
          </Text>

          {usable ? (
            <Button type="primary" size="large" icon={<ExpandOutlined />} onClick={enterScanMode}>
              Tăng sáng & toàn màn hình để quét
            </Button>
          ) : (
            <Alert
              type="warning"
              showIcon
              message="Vé này không còn quét được"
              description="Vé đã sử dụng, đã hết hạn hoặc đã bị huỷ nên không thể dùng để lên xe."
            />
          )}
        </div>
      </PageCard>

      {scanning && (
        <div
          style={{
            position: 'fixed',
            inset: 0,
            zIndex: 2000,
            background: '#ffffff',
            color: '#000000',
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            gap: 24,
            padding: 24,
          }}
        >
          <TicketQr code={ticket.code} size={scanQrSize} />

          <div style={{ textAlign: 'center' }}>
            <div style={{ fontSize: 20, fontWeight: 700 }}>{ticket.routeName}</div>
            <div style={{ marginTop: 4 }}>
              Ghế {ticket.seatNumber} · Khởi hành {formatDateTime(ticket.departureTime)}
            </div>
            <div style={{ marginTop: 4 }}>Mã vé: {ticket.code}</div>
          </div>

          <Button size="large" icon={<CompressOutlined />} onClick={exitScanMode}>
            Thoát chế độ quét
          </Button>
        </div>
      )}
    </div>
  );
}
