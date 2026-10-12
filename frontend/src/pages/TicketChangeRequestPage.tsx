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
  Select,
  Space,
  Typography,
  message,
  theme,
} from 'antd';
import { CheckCircleFilled } from '@ant-design/icons';
import ticketApi from '../api/ticketApi';
import type { Ticket } from '../api/ticketApi';
import ticketChangeRequestApi, {
  MOCK_NEW_TRIPS,
  mockCancellationFee,
} from '../api/ticketChangeRequestApi';
import type {
  SubmitTicketChangePayload,
  TicketChangeNewTripOption,
} from '../api/ticketChangeRequestApi';
import {
  TICKET_REQUEST_STATUS_META,
  TICKET_REQUEST_TYPE_META,
  getTicketRequestTypeMeta,
} from '../api/ticketRequestApi';
import type { TicketRequestStatus, TicketRequestType } from '../api/ticketRequestApi';
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

/** Giá trị của form gửi yêu cầu huỷ/đổi vé — `newTripId` chỉ có khi chọn loại "đổi chuyến". */
interface TicketChangeFormValues {
  type: TicketRequestType;
  newTripId?: string;
  reason: string;
}

/** Số ký tự tối đa của lý do — chừa đủ chỗ cho hành khách giải thích, không cần quá dài. */
const REASON_MAX_LENGTH = 1000;

/** Hai lựa chọn loại yêu cầu, sinh từ TICKET_REQUEST_TYPE_META để nhãn không lệch màn theo dõi. */
const TYPE_OPTIONS: { value: TicketRequestType; label: string }[] = (
  Object.keys(TICKET_REQUEST_TYPE_META) as TicketRequestType[]
).map((value) => ({ value, label: TICKET_REQUEST_TYPE_META[value].label }));

/** Kết quả một lượt tải vé theo id — bốn trạng thái, không gộp trạng thái nào vào nhau. */
type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'not-found' }
  | { status: 'done'; ticket: Ticket };

/** Tóm tắt yêu cầu vừa gửi — đủ để vẽ màn hình thành công, kèm phí/hoàn đã hiện trước khi gửi. */
interface SubmittedSummary {
  type: TicketRequestType;
  status: TicketRequestStatus;
  reason: string;
  /** Phí huỷ (Cancel) hoặc chênh lệch giá (Change; dương = trả thêm, âm = hoàn lại). */
  fee: number;
  /** Số tiền hoàn lại (Cancel) = giá vé - phí. null với đổi chuyến. */
  refundAmount: number | null;
  /** Chuyến mới — chỉ có khi đổi chuyến. */
  newTrip: TicketChangeNewTripOption | null;
  createdAt: string;
}

/** Câu mô tả chênh lệch giá khi đổi chuyến — dấu dương/âm nói rõ ai trả ai. */
function priceDiffMessage(diff: number): string {
  if (diff > 0) return `Chênh lệch +${formatVnd(diff)} — bạn trả thêm khi nhà xe duyệt.`;
  if (diff < 0) return `Chênh lệch ${formatVnd(diff)} — nhà xe hoàn lại khi duyệt.`;
  return 'Đổi ngang giá — không phát sinh thêm.';
}

// Màn hình "Gửi yêu cầu huỷ / đổi vé" (story 5 "Hủy & Đổi vé", Sprint 4 — task của Dương Thị
// Hạnh): hành khách chọn huỷ vé (hoàn tiền) hay đổi chuyến (sang chuyến khác), xem phí/hoàn
// TRƯỚC khi gửi, viết lý do rồi gửi yêu cầu để nhà xe xem xét. Đây là màn hình cho HÀNH KHÁCH —
// không giới hạn vai trò như các màn hình quản trị. Vào từ nút "Huỷ / Đổi vé" trên màn "Vé của tôi".
//
// Chỉ vé "Còn hiệu lực" (đã thanh toán, chuyến chưa khởi hành) mới gửi được yêu cầu. Hai loại yêu
// cầu khớp hai bảng `CancellationRequests` (huỷ) và `TicketChanges` (đổi chuyến) — xem
// docs/32-csdl-huy-doi-ve.md. Backend yêu cầu huỷ/đổi chưa có nên màn hình đang chạy dữ liệu giả
// (xem ticketChangeRequestApi.ts); type/trạng thái/meta dùng chung ticketRequestApi.ts.
export default function TicketChangeRequestPage() {
  const { ticketId = '' } = useParams();
  const navigate = useNavigate();
  const { token } = theme.useToken();

  const [form] = Form.useForm<TicketChangeFormValues>();

  // Loại yêu cầu và chuyến mới đang chọn — gọi HOOK trước mọi nhánh trả về sớm để thứ tự hook
  // không đổi giữa các lần render (rules-of-hooks). Giá trị chỉ được dùng ở nhánh vẽ form.
  const type = Form.useWatch('type', form) ?? 'Cancel';
  const newTripId = Form.useWatch('newTripId', form);

  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);

  const [submitting, setSubmitting] = useState(false);
  // Yêu cầu vừa gửi thành công — null khi chưa gửi.
  const [submitted, setSubmitted] = useState<SubmittedSummary | null>(null);

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

  const handleSubmit = async (values: TicketChangeFormValues) => {
    setSubmitting(true);
    try {
      const newTrip = MOCK_NEW_TRIPS.find((trip) => trip.id === values.newTripId) ?? null;

      let payload: SubmitTicketChangePayload;
      let fee: number;
      let refundAmount: number | null = null;

      if (values.type === 'Cancel') {
        fee = mockCancellationFee(ticket.departureTime, ticket.price);
        refundAmount = ticket.price - fee;
        payload = { type: 'Cancel', ticketId, reason: values.reason };
      } else if (newTrip) {
        fee = newTrip.price - ticket.price;
        payload = {
          type: 'Change',
          ticketId,
          newTripId: newTrip.id,
          newSeatId: newTrip.seatId,
          reason: values.reason,
        };
      } else {
        // Không thể xảy ra — Form đã chặn thiếu chuyến mới khi loại là "Đổi chuyến".
        message.error('Vui lòng chọn chuyến mới.');
        return;
      }

      const result = await ticketChangeRequestApi.submit(payload);
      setSubmitted({
        type: values.type,
        status: result.status,
        reason: values.reason,
        fee,
        refundAmount,
        newTrip,
        createdAt: result.createdAt,
      });
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
  const usable = isTicketUsable(getTicketStatus(ticket));

  const newTrip = MOCK_NEW_TRIPS.find((trip) => trip.id === newTripId) ?? null;

  const cancelFee = mockCancellationFee(ticket.departureTime, ticket.price);
  const priceDiff = newTrip ? newTrip.price - ticket.price : 0;

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
              initialValues={{ type: 'Cancel' }}
              onFinish={handleSubmit}
              disabled={submitting}
            >
              <Form.Item
                name="type"
                label="Loại yêu cầu"
                rules={[{ required: true, message: 'Vui lòng chọn loại yêu cầu.' }]}
              >
                <Segmented options={TYPE_OPTIONS} />
              </Form.Item>

              {type === 'Change' && (
                <Form.Item
                  name="newTripId"
                  label="Chuyến mới"
                  rules={[{ required: true, message: 'Vui lòng chọn chuyến mới.' }]}
                  extra="Ghế trống được chọn sẵn trong dữ liệu giả — khi nối API thật sẽ cho chọn ghế cụ thể."
                >
                  <Select
                    placeholder="Chọn chuyến muốn đổi sang"
                    options={MOCK_NEW_TRIPS.map((trip) => ({
                      value: trip.id,
                      label: `${trip.routeCode} — ${trip.routeName} · ${formatDateTime(
                        trip.departureTime,
                      )} · Ghế ${trip.seatNumber} · ${formatVnd(trip.price)}`,
                    }))}
                  />
                </Form.Item>
              )}

              {/* Phí/hoàn hiện TRƯỚC khi gửi — con số khách đã thấy là con số nhà xe áp dụng. */}
              {type === 'Cancel' && (
                <Alert
                  type="info"
                  showIcon
                  style={{ marginBottom: 16 }}
                  message={`Phí huỷ ${formatVnd(cancelFee)} · Hoàn lại ${formatVnd(ticket.price - cancelFee)}`}
                />
              )}
              {type === 'Change' && newTrip && (
                <Alert type="info" showIcon style={{ marginBottom: 16 }} message={priceDiffMessage(priceDiff)} />
              )}

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
                    <StatusTag color={getTicketRequestTypeMeta(submitted.type).color}>
                      {getTicketRequestTypeMeta(submitted.type).label}
                    </StatusTag>
                  ),
                },
                {
                  key: 'status',
                  label: 'Trạng thái',
                  children: (
                    <StatusTag color={TICKET_REQUEST_STATUS_META[submitted.status].color}>
                      {TICKET_REQUEST_STATUS_META[submitted.status].label}
                    </StatusTag>
                  ),
                },
                ...(submitted.type === 'Change' && submitted.newTrip
                  ? [
                      {
                        key: 'newTrip',
                        label: 'Chuyến mới',
                        children: `${submitted.newTrip.routeCode} — ${submitted.newTrip.routeName} · ${formatDateTime(
                          submitted.newTrip.departureTime,
                        )}`,
                      },
                    ]
                  : []),
                {
                  key: 'money',
                  label: submitted.type === 'Cancel' ? 'Hoàn lại' : 'Chênh lệch',
                  children:
                    submitted.type === 'Cancel'
                      ? `${formatVnd(submitted.refundAmount ?? 0)} (phí huỷ ${formatVnd(submitted.fee)})`
                      : priceDiffMessage(submitted.fee),
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
