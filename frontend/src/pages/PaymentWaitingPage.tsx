import { useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Alert, Button, Result, Space, Spin, Typography } from 'antd';
import { HomeOutlined, ReloadOutlined } from '@ant-design/icons';
import paymentApi from '../api/paymentApi';
import type { PaymentMethod, PaymentStatusResult } from '../api/paymentApi';
import type { AppError } from '../api/axiosClient';
import {
  PAYMENT_POLL_INTERVAL_SECONDS,
  formatWaitCountdown,
  getPaymentWaitInfo,
} from '../components/paymentWaiting';
import type { PaymentWaitPhase } from '../components/paymentWaiting';
import { EmptyState, ErrorState, PageCard, PageHeader } from '../components/ui';
import { formatVnd } from '../components/ui/format';

const { Text } = Typography;

/** Nhãn tiếng Việt cho từng cổng — `BankCard` đổi tên để khách đọc hiểu, các cổng còn lại giữ nguyên. */
const METHOD_LABELS: Record<PaymentMethod, string> = {
  MoMo: 'MoMo',
  VNPay: 'VNPay',
  ZaloPay: 'ZaloPay',
  BankCard: 'Thẻ ngân hàng',
};

// Màn hình chờ kết quả thanh toán + xử lý timeout (User Story 6, Sprint 3 — task của Nguyễn
// Đình Băng): hiện ra sau khi khách quay về từ cổng thanh toán, poll liên tục trạng thái giao
// dịch cho tới khi Success/Failed hoặc hết hạn chờ (timeout).
//
// Nhận `paymentCode` qua query string (bắt buộc); `amount`/`seats` là ngữ cảnh do bước chọn ghế
// truyền sang để hiển thị đầu trang, không gọi thêm endpoint nào. Cổng (`method`) lấy từ kết quả
// poll — đó là dữ liệu do backend trả về khi kiểm tra giao dịch.
//
// Timeout là khái niệm phía frontend: hết PAYMENT_TIMEOUT_SECONDS mà giao dịch vẫn Pending thì
// ngừng poll và mời khách kiểm tra lại, không tự đoán kết quả. Bản thân việc poll là task của
// màn này; "màn hình chọn phương thức thanh toán" là task của Dương Thị Hạnh, không nằm ở đây.
export default function PaymentWaitingPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const paymentCode = searchParams.get('paymentCode')?.trim() ?? '';
  const contextAmount = searchParams.get('amount')?.trim() ?? '';
  const contextSeats = searchParams.get('seats')?.trim() ?? '';

  // Kết quả poll gần nhất + đồng hồ đếm giờ chờ. `hardError` là lỗi DỨT KHOÁT (backend trả 4xx/5xx
  // — ví dụ mã thanh toán không tồn tại) thì ngừng poll; `lastPollError` là lỗi thoáng qua (mất
  // mạng) — vẫn poll tiếp và chỉ hiển thị để khách biết đang mất kết nối.
  const [result, setResult] = useState<PaymentStatusResult | null>(null);
  const [secondsElapsed, setSecondsElapsed] = useState(0);
  const [hardError, setHardError] = useState<string | null>(null);
  const [lastPollError, setLastPollError] = useState<string | null>(null);

  const waitInfo = getPaymentWaitInfo(result, secondsElapsed);
  const phase: PaymentWaitPhase | 'Error' = hardError ? 'Error' : waitInfo.phase;
  const isTerminal =
    phase === 'Success' || phase === 'Failed' || phase === 'Timeout' || phase === 'Error';

  // Đồng hồ đếm ngược: tick mỗi giây để biết còn bao nhiêu giây trước khi hết hạn chờ. Chỉ chạy
  // khi đang chờ thật — tới pha cuối là ngừng.
  useEffect(() => {
    if (!paymentCode || isTerminal) return;

    const timer = window.setInterval(() => setSecondsElapsed((s) => s + 1), 1000);
    return () => window.clearInterval(timer);
  }, [paymentCode, isTerminal]);

  // Poll trạng thái giao dịch theo chu kỳ PAYMENT_POLL_INTERVAL_SECONDS. Lỗi mạng (không có
  // status) là thoáng qua nên giữ kết quả cũ và tiếp tục; lỗi có status (4xx/5xx) là dứt khoát
  // nên dừng lại và hiển thị riêng — không mạo nhận là "đang chờ".
  useEffect(() => {
    if (!paymentCode || isTerminal) return;

    let cancelled = false;
    let timer: number | undefined;

    const poll = () => {
      paymentApi
        .getStatus(paymentCode)
        .then((res) => {
          if (cancelled) return;
          setResult(res);
          setLastPollError(null);
        })
        .catch((err: unknown) => {
          if (cancelled) return;
          const appError = err as AppError;

          if (appError.status) {
            setHardError(appError.customMessage || 'Không thể kiểm tra trạng thái thanh toán.');
          } else {
            setLastPollError(appError.customMessage || 'Không thể kết nối đến máy chủ.');
          }
        });
    };

    poll();
    timer = window.setInterval(poll, PAYMENT_POLL_INTERVAL_SECONDS * 1000);

    return () => {
      cancelled = true;
      if (timer !== undefined) window.clearInterval(timer);
    };
  }, [paymentCode, isTerminal]);

  // "Kiểm tra lại" / "Thử lại": xoá kết quả cũ và chạy lại từ đầu — đồng hồ chờ về 0, poll lại.
  // Vì `isTerminal` trở về false nên hai effect trên tự chạy lại, không cần gọi lại thủ công.
  const restart = () => {
    setResult(null);
    setSecondsElapsed(0);
    setHardError(null);
    setLastPollError(null);
  };

  // Chưa có mã thanh toán → không có giao dịch nào để chờ. Mời quay lại luồng chọn ghế.
  if (!paymentCode) {
    return (
      <EmptyState
        description="Chưa có giao dịch thanh toán. Hãy chọn ghế rồi tiến hành thanh toán để tới bước này."
        action={
          <Button type="primary" onClick={() => navigate('/route-lookup')}>
            Quay lại tra cứu tuyến
          </Button>
        }
      />
    );
  }

  // Ngữ cảnh hiển thị: số tiền ưu tiên lấy từ bước chọn ghế (query string), thiếu mới dùng kết
  // quả poll; cổng thì lấy từ kết quả poll (backend là nơi biết khách chọn cổng nào).
  const parsedAmount = Number(contextAmount);
  const amount =
    contextAmount !== '' && Number.isFinite(parsedAmount) ? parsedAmount : (result?.amount ?? null);
  const method = result?.method ?? null;
  const methodLabel = method ? (METHOD_LABELS[method] ?? method) : null;

  const subtitle = [
    methodLabel,
    amount !== null ? formatVnd(amount) : null,
    contextSeats !== '' ? `Ghế ${contextSeats}` : null,
  ]
    .filter(Boolean)
    .join(' · ');

  return (
    <div>
      <PageHeader title="Chờ kết quả thanh toán" subtitle={subtitle || undefined} />

      <PageCard>
        {phase === 'Pending' && (
          <div style={{ textAlign: 'center', padding: '48px 0' }}>
            <Spin size="large" />
            <div style={{ marginTop: 16 }}>
              <Text strong style={{ fontSize: 18 }}>
                Đang chờ kết quả thanh toán…
              </Text>
            </div>
            <div style={{ marginTop: 8 }}>
              <Text type="secondary">
                Hết thời gian chờ sau {formatWaitCountdown(waitInfo.secondsLeft)}
              </Text>
            </div>
            <div style={{ marginTop: 16 }}>
              <Text type="secondary">
                Vui lòng không đóng trang này cho tới khi có kết quả. Giao dịch đang được cổng
                thanh toán xử lý.
              </Text>
            </div>
            {lastPollError && (
              <Alert
                style={{ marginTop: 24, textAlign: 'left' }}
                type="warning"
                showIcon
                message="Đang mất kết nối"
                description="Sẽ tự thử lại. Nếu kéo dài, hãy kiểm tra kết nối mạng."
              />
            )}
          </div>
        )}

        {phase === 'Success' && (
          <Result
            status="success"
            title="Thanh toán thành công"
            subTitle={
              <Space direction="vertical" size={4}>
                {subtitle && <Text>{subtitle}</Text>}
                {result?.paidAt && (
                  <Text type="secondary">
                    Thời điểm thanh toán:{' '}
                    {new Date(result.paidAt).toLocaleTimeString('vi-VN', {
                      hour: '2-digit',
                      minute: '2-digit',
                      second: '2-digit',
                    })}
                  </Text>
                )}
              </Space>
            }
            extra={
              <Button type="primary" icon={<HomeOutlined />} onClick={() => navigate('/')}>
                Về trang chủ
              </Button>
            }
          />
        )}

        {phase === 'Failed' && (
          <Result
            status="error"
            title="Thanh toán thất bại"
            subTitle={
              <Space direction="vertical" size={4}>
                {result?.message ? <Text>{result.message}</Text> : null}
                {subtitle && <Text type="secondary">{subtitle}</Text>}
              </Space>
            }
            extra={
              <Space wrap>
                <Button onClick={() => navigate('/route-lookup')}>Quay lại tra cứu tuyến</Button>
                <Button type="primary" icon={<HomeOutlined />} onClick={() => navigate('/')}>
                  Về trang chủ
                </Button>
              </Space>
            }
          />
        )}

        {phase === 'Timeout' && (
          <Result
            status="warning"
            title="Hết thời gian chờ kết quả"
            subTitle={
              <Space direction="vertical" size={4}>
                <Text>
                  Giao dịch có thể vẫn đang được xử lý phía cổng thanh toán. Hãy kiểm tra lại
                  trạng thái, hoặc xem kết quả trong "Vé của tôi" sau vài phút.
                </Text>
                {subtitle && <Text type="secondary">{subtitle}</Text>}
                {lastPollError && <Text type="secondary">{lastPollError}</Text>}
              </Space>
            }
            extra={
              <Space wrap>
                <Button icon={<ReloadOutlined />} onClick={restart}>
                  Kiểm tra lại
                </Button>
                <Button type="primary" icon={<HomeOutlined />} onClick={() => navigate('/')}>
                  Về trang chủ
                </Button>
              </Space>
            }
          />
        )}

        {phase === 'Error' && (
          <ErrorState
            title="Không kiểm tra được trạng thái thanh toán"
            description={hardError ?? undefined}
            onRetry={restart}
          />
        )}
      </PageCard>
    </div>
  );
}
