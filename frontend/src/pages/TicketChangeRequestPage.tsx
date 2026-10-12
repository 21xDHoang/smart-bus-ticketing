import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import {
  Alert,
  Button,
  Descriptions,
  Form,
  Input,
  Result,
  Segmented,
  Space,
  Typography,
  message,
  theme,
} from 'antd';
import { CheckCircleFilled } from '@ant-design/icons';
import ticketApi from '../api/ticketApi';
import type { Ticket } from '../api/ticketApi';
import ticketChangeRequestApi from '../api/ticketChangeRequestApi';
import type {
  SubmittedTicketChange,
  SubmitTicketChangePayload,
  TicketChangeType,
} from '../api/ticketChangeRequestApi';
import {
  TICKET_CHANGE_STATUS_META,
  TICKET_CHANGE_TYPE_OPTIONS,
  getTicketChangeTypeMeta,
} from '../api/ticketChangeRequestApi';
import type { AppError } from '../api/axiosClient';
import TicketStatusTag from '../components/TicketStatusTag';
import { getTicketStatus, isTicketUsable } from '../components/ticketStatus';
import {
  ErrorState,
  LoadingState,
  PageCard,
  PageHeader,
  RouteBadge,
  RouteLine,
  StatusTag,
} from '../components/ui';
import { formatDateTime, formatVnd } from '../components/ui/format';

const { Text, Paragraph } = Typography;

/** Giá trị của form gửi yêu cầu huỷ/đổi vé — hai trường đều bắt buộc. */
interface TicketChangeFormValues {
  type: TicketChangeType;
  reason: string;
}

/** Số ký tự tối đa của lý do — chừa đủ chỗ cho hành khách giải thích, không cần quá dài. */
const REASON_MAX_LENGTH = 1000;

/** Kết quả một lượt tải vé theo id — bốn trạng thái, không gộp trạng thái nào vào nhau. */
type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'not-found' }
  | { status: 'done'; ticket: Ticket };

// Màn hình "Gửi yêu cầu huỷ / đổi vé" (story 5 "Hủy & đổi vé", Sprint 4 — task của Dương Thị
// Hạnh): hành khách chọn huỷ vé (hoàn tiền) hay đổi vé (chuyển sang chuyến khác), viết lý do rồi
// gửi yêu cầu để nhà xe xem xét. Đây là màn hình cho HÀNH KHÁCH — không giới hạn vai trò như
// các màn hình quản trị. Vào từ nút "Huỷ/Đổi vé" trên màn "Vé của tôi".
//
// Chỉ vé "Còn hiệu lực" (đã thanh toán, chuyến chưa khởi hành) mới gửi được yêu cầu — vé đã
// dùng / hết hạn / đã huỷ thì hiện cảnh báo thay vì form. Backend yêu cầu huỷ/đổi chưa có nên
// màn hình đang chạy dữ liệu giả (xem ticketChangeRequestApi.ts).
export default function TicketChangeRequestPage() {
  const { ticketId = '' } = useParams();
  const navigate = useNavigate();
  const { token } = theme.useToken();

  const [form] = Form.useForm<TicketChangeFormValues>();

  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);

  const [submitting, setSubmitting] = useState(false);
  // Yêu cầu vừa gửi thành công — null khi chưa gửi.
  const [submitted, setSubmitted] = useState<SubmittedTicketChange | null>(null);

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

  const handleSubmit = async ({ type, reason }: TicketChangeFormValues) => {
    setSubmitting(true);
    try {
      const payload: SubmitTicketChangePayload = { ticketId, type, reason };
      setSubmitted(await ticketChangeRequestApi.submit(payload));
      message.success('Đã gửi yêu cầu huỷ/đổi vé thành công.');
    } catch (error) {
      message.error((error as AppError).customMessage || 'Không thể gửi yêu cầu.');
    } finally {
      setSubmitting(false);
    }
  };

  if (state.status === 'loading') {
    return (
      <div>
        <PageHeader title="Huỷ / Đổi vé" onBack={() => navigate('/my-tickets')} />
        <PageCard>
          <LoadingState description="Đang tải vé…" />
        </PageCard>
      </div>
    );
  }

  if (state.status === 'error') {
    return (
      <div>
        <PageHeader title="Huỷ / Đổi vé" onBack={() => navigate('/my-tickets')} />
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
        <PageHeader title="Huỷ / Đổi vé" onBack={() => navigate('/my-tickets')} />
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
  const displayStatus = getTicketStatus(ticket);
  const usable = isTicketUsable(displayStatus);

  return (
    <div>
      <PageHeader
        title="Huỷ / Đổi vé"
        subtitle="Gửi yêu cầu huỷ vé hoặc đổi sang chuyến khác. Nhà xe sẽ xem xét và phản hồi."
        onBack={() => navigate('/my-tickets')}
      />

      {/* Tóm tắt vé đang chọn — để hành khách chắc chắn đang gửi yêu cầu cho đúng vé. */}
      <PageCard>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <RouteBadge code={ticket.routeCode} />
          <Text strong style={{ fontSize: 16 }}>
            {ticket.routeName}
          </Text>
          <span style={{ marginLeft: 'auto' }}>
            <TicketStatusTag ticket={ticket} />
          </span>
        </div>

        <div style={{ marginTop: 12 }}>
          <RouteLine from={ticket.origin} to={ticket.destination} />
        </div>

        <div style={{ marginTop: 12 }}>
          <Text type="secondary" style={{ fontSize: 13 }}>
            Khởi hành {formatDateTime(ticket.departureTime)} · Ghế {ticket.seatNumber} ·{' '}
            <Text strong>{formatVnd(ticket.price)}</Text>
          </Text>
        </div>
      </PageCard>

      {!usable ? (
        // Vé không còn hiệu lực — không có form, chỉ giải thích vì sao không gửi được.
        <div style={{ marginTop: 20 }}>
          <PageCard>
            <Alert
              type="warning"
              showIcon
              message="Vé này không thể gửi yêu cầu huỷ/đổi"
              description="Chỉ vé còn hiệu lực (đã thanh toán, chuyến chưa khởi hành) mới gửi được yêu cầu huỷ hoặc đổi."
            />
          </PageCard>
        </div>
      ) : (
        <div style={{ marginTop: 20 }}>
          <PageCard>
            <Form<TicketChangeFormValues>
              form={form}
              layout="vertical"
              onFinish={handleSubmit}
              disabled={submitting}
            >
              <Form.Item
                name="type"
                label="Loại yêu cầu"
                rules={[{ required: true, message: 'Vui lòng chọn loại yêu cầu.' }]}
              >
                <Segmented options={TICKET_CHANGE_TYPE_OPTIONS} />
              </Form.Item>

              <Form.Item
                name="reason"
                label="Lý do"
                rules={[
                  { required: true, message: 'Vui lòng nhập lý do.' },
                  { max: REASON_MAX_LENGTH, message: `Lý do tối đa ${REASON_MAX_LENGTH} ký tự.` },
                ]}
              >
                <Input.TextArea
                  rows={4}
                  maxLength={REASON_MAX_LENGTH}
                  showCount
                  placeholder="Ví dụ: đổi lịch trình, không đi được nữa, muốn chuyển sang chuyến khác…"
                />
              </Form.Item>

              <Button type="primary" htmlType="submit" size="large" loading={submitting}>
                Gửi yêu cầu
              </Button>
            </Form>
          </PageCard>
        </div>
      )}

      {/* Kết quả gửi thành công. */}
      {submitted && (
        <div style={{ marginTop: 20 }}>
          <PageCard
            title={
              <Space>
                {/* colorSuccessText — bản dành cho chữ/nét mảnh của màu success; colorSuccess
                    gốc chỉ đạt ~2.5–3.3:1 khi làm icon nhỏ. */}
                <CheckCircleFilled style={{ color: token.colorSuccessText }} />
                <span>Gửi yêu cầu thành công</span>
              </Space>
            }
          >
            <Descriptions
              column={1}
              size="middle"
              items={[
                {
                  key: 'type',
                  label: 'Loại yêu cầu',
                  children: (
                    <StatusTag color={getTicketChangeTypeMeta(submitted.type).color}>
                      {getTicketChangeTypeMeta(submitted.type).label}
                    </StatusTag>
                  ),
                },
                {
                  key: 'status',
                  label: 'Trạng thái',
                  children: (
                    <StatusTag color={TICKET_CHANGE_STATUS_META[submitted.status].color}>
                      {TICKET_CHANGE_STATUS_META[submitted.status].label}
                    </StatusTag>
                  ),
                },
              ]}
            />

            <Paragraph style={{ marginTop: 12, whiteSpace: 'pre-wrap' }}>
              {submitted.reason}
            </Paragraph>

            <Text type="secondary" style={{ fontSize: 12 }}>
              Gửi lúc {formatDateTime(submitted.createdAt)}. Nhà xe sẽ phản hồi trong thời gian
              sớm nhất.
            </Text>

            <Button
              type="primary"
              style={{ marginTop: 16 }}
              onClick={() => navigate('/my-tickets')}
            >
              Về vé của tôi
            </Button>
          </PageCard>
        </div>
      )}
    </div>
  );
}
