import { useState } from 'react';
import { Alert, Button, Descriptions, Result, Spin, Tag, Typography, theme } from 'antd';
import { ReloadOutlined, ScanOutlined } from '@ant-design/icons';
import ticketValidationApi from '../api/ticketValidationApi';
import type { TicketValidationResult } from '../api/ticketValidationApi';
import { getTicketValidationReasonMeta } from '../api/ticketValidationApi';
import type { AppError } from '../api/axiosClient';
import ManualTicketCodeInput from '../components/ManualTicketCodeInput';
import { useQrScanner } from '../components/useQrScanner';
import type { QrScannerStatus } from '../components/useQrScanner';
import { PageCard, PageHeader } from '../components/ui';
import { formatDateTime } from '../components/ui/format';

const { Text } = Typography;

/** Trạng thái một lượt quét — khác với trạng thái camera (useQrScanner). */
type ScanPhase = 'scanning' | 'validating' | 'result';

/** Câu lỗi cho từng trạng thái camera hỏng — gom để màn hình không lặp đoạn rẽ nhánh. */
const STATUS_MESSAGE: Record<Exclude<QrScannerStatus, 'starting' | 'scanning'>, string> = {
  denied: 'Camera chưa được cấp quyền.',
  unavailable: 'Thiết bị không hỗ trợ camera.',
  error: 'Không mở được camera.',
};

/** Kết quả soát vé — xanh/đỏ rõ ràng kèm thông tin vé khi hợp lệ. */
function ScanResult({
  result,
  onRescan,
}: {
  result: TicketValidationResult;
  onRescan: () => void;
}) {
  const rescanButton = (
    <Button type="primary" icon={<ReloadOutlined />} onClick={onRescan}>
      Quét tiếp
    </Button>
  );

  if (!result.valid || !result.ticket) {
    const meta = result.reason ? getTicketValidationReasonMeta(result.reason) : null;

    return (
      <Result
        status="error"
        title="Vé không hợp lệ"
        subTitle={meta ? meta.label : 'Không xác định được lý do.'}
        extra={rescanButton}
      />
    );
  }

  const ticket = result.ticket;

  return (
    <Result
      status="success"
      title="Vé hợp lệ"
      subTitle={`Cho hành khách lên xe · Ghế ${ticket.seatNumber}`}
      extra={rescanButton}
    >
      <Descriptions
        bordered
        size="small"
        column={1}
        style={{ maxWidth: 480, margin: '0 auto', textAlign: 'left' }}
        items={[
          {
            key: 'passenger',
            label: 'Hành khách',
            children: ticket.passengerName ?? <Text type="secondary">—</Text>,
          },
          {
            key: 'route',
            label: 'Tuyến',
            children: (
              <span>
                <Tag style={{ marginInlineEnd: 6 }}>{ticket.routeCode}</Tag>
                {ticket.routeName}
              </span>
            ),
          },
          { key: 'departure', label: 'Giờ khởi hành', children: formatDateTime(ticket.departureTime) },
          {
            key: 'stops',
            label: 'Lộ trình',
            children: `${ticket.boardingStopName} → ${ticket.alightingStopName}`,
          },
          { key: 'code', label: 'Mã vé', children: <Text code>{ticket.code}</Text> },
        ]}
      />
    </Result>
  );
}

// Màn hình "Soát vé" (story 15, Sprint 4 — task "Màn hình quét QR bằng camera thiết bị di động"
// của Nguyễn Đình Băng): dành cho TÀI XẾ, bật camera sau quét mã QR trên vé điện tử của khách,
// gửi mã lên backend soát rồi hiện kết quả hợp lệ/không. Backend soát vé chưa có nên phần kiểm
// tra chạy dữ liệu giả (ticketValidationApi.ts); phần QUÉT CAMERA là thật (useQrScanner + jsQR).
// Kèm đường lùi NHẬP MÃ THỦ CÔNG (task "Component nhập mã vé thủ công khi camera không quét
// được"): camera hỏng hoặc mã khó quét thì tài xế gõ/dán mã vé — đi CHUNG luồng soát với quét.
export default function TicketScanPage() {
  const [phase, setPhase] = useState<ScanPhase>('scanning');
  const [result, setResult] = useState<TicketValidationResult | null>(null);
  const [validateError, setValidateError] = useState<string | null>(null);
  // Tài xế tự mở ô nhập thủ công trong lúc camera còn chạy (mã mờ, màn khách nứt…).
  const [manualOpen, setManualOpen] = useState(false);
  const { token } = theme.useToken();

  // Camera chỉ chạy khi đang ở pha quét — bắt được mã hoặc vào pha kết quả thì tắt để nhả camera.
  const { videoRef, status, error, restart } = useQrScanner(onScanned, phase === 'scanning');

  function onScanned(code: string) {
    setPhase('validating');
    setValidateError(null);

    ticketValidationApi
      .validate(code)
      .then((validation) => {
        setResult(validation);
        setPhase('result');
      })
      .catch((err: unknown) => {
        // Soát hỏng (mạng/backend) không phải "vé không hợp lệ" — báo lỗi và quay lại quét.
        const appError = err as AppError;
        setValidateError(appError.customMessage || 'Không soát được mã này, thử lại.');
        setPhase('scanning');
      });
  }

  function handleRescan() {
    setResult(null);
    setValidateError(null);
    setPhase('scanning');
  }

  if (phase === 'result' && result) {
    return (
      <div>
        <PageHeader title="Soát vé" subtitle="Kết quả kiểm tra mã QR trên vé điện tử." />
        <PageCard>
          <ScanResult result={result} onRescan={handleRescan} />
        </PageCard>
      </div>
    );
  }

  const cameraFailed = status === 'denied' || status === 'unavailable' || status === 'error';
  // Camera hỏng hẳn thì ô nhập thủ công là đường chính (hiện sẵn); camera còn chạy thì nằm sau
  // nút gợi ý để khung quét vẫn là đường chính.
  const manualVisible = cameraFailed || (status === 'scanning' && manualOpen);

  return (
    <div>
      <PageHeader
        title="Soát vé"
        subtitle="Hướng camera sau vào mã QR trên vé của hành khách để kiểm tra."
      />

      <PageCard>
        {/* Khung camera — nền đen, bo góc; video phủ kín, khung quét nằm giữa. */}
        <div
          style={{
            position: 'relative',
            width: '100%',
            maxWidth: 560,
            margin: '0 auto',
            aspectRatio: '4 / 3',
            borderRadius: 16,
            overflow: 'hidden',
            background: '#0b0b0f',
          }}
        >
          <video
            ref={videoRef}
            muted
            playsInline
            aria-label="Khung hình camera soát vé"
            style={{ width: '100%', height: '100%', objectFit: 'cover' }}
          />

          {/* Khung quét — ô vuông giữa màn hình để người dùng căn mã QR vào. */}
          {status === 'scanning' && (
            <div
              style={{
                position: 'absolute',
                inset: 0,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                pointerEvents: 'none',
              }}
            >
              <div
                style={{
                  width: '58%',
                  aspectRatio: '1',
                  border: '2px solid rgba(255,255,255,0.85)',
                  borderRadius: 12,
                  boxShadow: '0 0 0 9999px rgba(0,0,0,0.35)',
                }}
              />
            </div>
          )}

          {/* Lớp phủ trạng thái: đang mở camera / đang soát / camera hỏng.
              Vì sao phải thêm `|| phase === 'validating'`: khi pha quét tắt, useQrScanner
              dọn dẹp mà không đổi `status`, nên nó GIỮ NGUYÊN 'scanning' suốt lúc đang soát —
              chỉ nhìn `status` thì lớp phủ "Đang kiểm tra mã vé…" không bao giờ hiện. */}
          {(status !== 'scanning' || phase === 'validating') && (
            <div
              style={{
                position: 'absolute',
                inset: 0,
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                justifyContent: 'center',
                gap: 12,
                padding: 24,
                textAlign: 'center',
                background: 'rgba(11,11,15,0.88)',
              }}
            >
              {status === 'starting' || phase === 'validating' ? (
                <Spin size="large" />
              ) : (
                <ScanOutlined style={{ fontSize: 40, color: 'rgba(255,255,255,0.5)' }} />
              )}
              <Text style={{ color: 'rgba(255,255,255,0.85)', maxWidth: 380 }}>
                {phase === 'validating'
                  ? 'Đang kiểm tra mã vé…'
                  : cameraFailed
                    ? error ?? STATUS_MESSAGE[status]
                    : 'Đang mở camera…'}
              </Text>
            </div>
          )}
        </div>

        {/* Dòng gợi ý khi đang quét + nút thử lại khi camera hỏng. */}
        <div style={{ textAlign: 'center', marginTop: 16 }}>
          {status === 'scanning' ? (
            <>
              <Text type="secondary">Đưa mã QR vào giữa khung — hệ thống tự nhận diện.</Text>
              <div style={{ marginTop: 4 }}>
                <Button type="link" size="small" onClick={() => setManualOpen((open) => !open)}>
                  {manualOpen ? 'Ẩn nhập mã thủ công' : 'Không quét được? Nhập mã thủ công'}
                </Button>
              </div>
            </>
          ) : cameraFailed ? (
            <Button icon={<ReloadOutlined />} onClick={restart}>
              Thử lại
            </Button>
          ) : null}
        </div>

        {/* Ô nhập mã thủ công — hiện sẵn khi camera hỏng hẳn, hoặc khi tài xế tự mở lúc camera còn chạy.
            Mã nhập tay đi về CHÍNH `onScanned` của luồng quét: một đường soát duy nhất. */}
        {manualVisible && (
          <div
            style={{
              marginTop: 16,
              padding: 16,
              border: `1px dashed ${token.colorBorder}`,
              borderRadius: token.borderRadiusLG,
            }}
          >
            <Text strong>Nhập mã vé thủ công</Text>
            <div style={{ margin: '2px 0 8px' }}>
              <Text type="secondary">Mã in ngay dưới mã QR trên vé của hành khách.</Text>
            </div>
            <ManualTicketCodeInput onSubmit={onScanned} disabled={phase === 'validating'} />
          </div>
        )}

        {validateError && (
          <Alert
            type="error"
            showIcon
            message={validateError}
            style={{ marginTop: 16 }}
            action={
              <Button size="small" onClick={handleRescan}>
                Quét lại
              </Button>
            }
          />
        )}
      </PageCard>
    </div>
  );
}
