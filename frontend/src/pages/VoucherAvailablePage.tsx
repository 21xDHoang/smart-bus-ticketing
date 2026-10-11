import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button, List, Space, Tag, Typography, theme } from 'antd';
import { SearchOutlined } from '@ant-design/icons';
import voucherApi from '../api/voucherApi';
import type { Voucher } from '../api/voucherApi';
import type { AppError } from '../api/axiosClient';
import { EmptyState, ErrorState, FilterBar, LoadingState, PageCard, PageHeader } from '../components/ui';
import { formatDate, formatVnd } from '../components/ui/format';

const { Text } = Typography;

/** Giá trị giảm cho khách nhìn — "Giảm 10%" hoặc "Giảm 20.000 đ". */
function discountText(voucher: Voucher): string {
  if (voucher.discountType === 'Percent') return `Giảm ${voucher.discountValue}%`;
  return `Giảm ${formatVnd(voucher.discountValue)}`;
}

/** Dòng điều kiện kèm theo — đơn tối thiểu + mức giảm tối đa (nếu có). */
function conditionsText(voucher: Voucher): string {
  const parts: string[] = [];
  if (voucher.minOrderValue > 0) parts.push(`Đơn từ ${formatVnd(voucher.minOrderValue)}`);
  if (voucher.discountType === 'Percent' && voucher.maxDiscount != null) {
    parts.push(`giảm tối đa ${formatVnd(voucher.maxDiscount)}`);
  }
  return parts.length > 0 ? parts.join(' · ') : 'Không kèm điều kiện';
}

/** Số lượt còn lại — luôn ≥ 0 vì danh sách đã lọc bỏ voucher hết lượt. */
function remainingOf(voucher: Voucher): number {
  return voucher.quantity - voucher.usedCount;
}

/** Kết quả một lượt tải danh sách voucher khả dụng — ba trạng thái, không gộp. */
type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; vouchers: Voucher[] };

/** Bốn trạng thái hiển thị — đúng bốn nhánh, không nhánh nào kiêm nhánh nào. */
type VoucherView =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'empty' }
  | { kind: 'vouchers'; vouchers: Voucher[] };

/**
 * Luật chuyển trạng thái, tách khỏi JSX để bốn nhánh không lồng vào nhau. `done` mà không có
 * voucher nào là "chưa có mã khả dụng" — im lặng, không phải lỗi. Còn gọi API hỏng là trạng
 * thái RIÊNG: mảng rỗng và lượt gọi hỏng là hai chuyện khác nhau, gộp lại thì màn hình nói sai
 * sự thật (lỗi mạng bị đọc thành "không có khuyến mãi nào").
 */
function viewOf(state: LoadState): VoucherView {
  if (state.status === 'loading') return { kind: 'loading' };
  if (state.status === 'error') return { kind: 'error', message: state.message };
  return state.vouchers.length === 0
    ? { kind: 'empty' }
    : { kind: 'vouchers', vouchers: state.vouchers };
}

// Màn hình danh sách voucher đang khả dụng cho hành khách (US 18 "Quản lý Voucher", Sprint 3 —
// task của Nguyễn Đình Băng): liệt kê các mã giảm giá mà khách CÓ THỂ dùng ngay lúc này (đang
// bật + trong khoảng hiệu lực + còn lượt), kèm giá trị giảm và điều kiện áp dụng để khách chủ
// động chọn mã trước khi vào bước thanh toán.
//
// Backend chưa có endpoint danh sách voucher khả dụng (dòng 51 CRUD chưa làm) nên màn hình chạy
// dữ liệu giả qua voucherApi.listAvailable() — cùng cờ USE_MOCK_DATA với màn quản lý voucher.
// Khách không tự dùng mã ở đây: nhập mã là việc của ô nhập mã ở màn thanh toán (component của
// Hoàng Văn Thịnh); màn này chỉ GIỚI THIỆU mã đang có.
export default function VoucherAvailablePage() {
  const navigate = useNavigate();
  const { token } = theme.useToken();

  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [reloadToken, setReloadToken] = useState(0);

  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API. Đây là lần tải thực sự (không phải "đồng
    // bộ state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setState({ status: 'loading' });

    voucherApi
      .listAvailable()
      .then((vouchers) => {
        if (cancelled) return;
        setState({ status: 'done', vouchers });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setState({
          status: 'error',
          message: appError.customMessage || 'Không thể tải danh sách voucher khả dụng.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadToken]);

  const view = viewOf(state);

  return (
    <div>
      <PageHeader
        title="Khuyến mãi"
        subtitle="Các mã giảm giá đang áp dụng cho vé. Ghi nhớ mã và nhập ở bước thanh toán để được giảm giá."
      />

      <PageCard
        toolbar={
          <FilterBar
            actions={
              <Button icon={<SearchOutlined />} onClick={() => navigate('/route-lookup')}>
                Tra cứu tuyến
              </Button>
            }
          >
            <Text type="secondary">
              {view.kind === 'vouchers' && (
                <>
                  Đang có <Text strong>{view.vouchers.length}</Text> voucher khả dụng
                </>
              )}
            </Text>
          </FilterBar>
        }
      >
        {/* Đang tải — spinner riêng, không mượn danh sách của lượt trước và không hiện số đếm. */}
        {view.kind === 'loading' && <LoadingState description="Đang tải danh sách voucher…" />}

        {/* Gọi API hỏng — trạng thái riêng, có lối thoát, KHÔNG mạo nhận là "chưa có voucher nào". */}
        {view.kind === 'error' && (
          <ErrorState
            title="Không tải được danh sách voucher"
            description={view.message}
            onRetry={() => setReloadToken((n) => n + 1)}
          />
        )}

        {/* Tải xong nhưng không có mã nào khả dụng — mảng rỗng là câu trả lời hợp lệ. */}
        {view.kind === 'empty' && (
          <EmptyState
            description="Hiện chưa có voucher nào đang khả dụng. Bạn vẫn có thể tra cứu tuyến và đặt vé bình thường."
            action={
              <Button type="primary" icon={<SearchOutlined />} onClick={() => navigate('/route-lookup')}>
                Tra cứu tuyến
              </Button>
            }
          />
        )}

        {view.kind === 'vouchers' && (
          <List<Voucher>
            dataSource={view.vouchers}
            renderItem={(voucher) => (
              <List.Item style={{ padding: '16px 0', borderBlockEnd: `1px solid ${token.colorSplit}` }}>
                <div
                  style={{
                    display: 'flex',
                    alignItems: 'flex-start',
                    gap: 16,
                    width: '100%',
                    flexWrap: 'wrap',
                  }}
                >
                  {/* Mã + tên + điều kiện + hiệu lực + số lượt còn lại */}
                  <div style={{ flex: 1, minWidth: 240 }}>
                    <Space size={8} wrap>
                      <Tag color="blue">{voucher.code}</Tag>
                      <Text strong>{voucher.name}</Text>
                    </Space>

                    <div style={{ marginTop: 4 }}>
                      <Text type="secondary" style={{ fontSize: 13 }}>
                        {conditionsText(voucher)}
                      </Text>
                    </div>

                    <div>
                      <Text type="secondary" style={{ fontSize: 13 }}>
                        Hiệu lực {formatDate(voucher.validFrom)} → {formatDate(voucher.validUntil)} · Còn{' '}
                        {remainingOf(voucher)} lượt
                      </Text>
                    </div>
                  </div>

                  {/* Giá trị giảm nổi bật */}
                  <div style={{ textAlign: 'right', minWidth: 140 }}>
                    <div style={{ fontSize: 18, fontWeight: 700, color: token.colorPrimary }}>
                      {discountText(voucher)}
                    </div>
                  </div>
                </div>
              </List.Item>
            )}
          />
        )}
      </PageCard>
    </div>
  );
}
