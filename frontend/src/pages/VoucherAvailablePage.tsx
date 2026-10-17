import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button, Input, List, Space, Typography, theme } from 'antd';
import { SearchOutlined } from '@ant-design/icons';
import voucherApi from '../api/voucherApi';
import type { Voucher } from '../api/voucherApi';
import type { AppError } from '../api/axiosClient';
import {
  EmptyState,
  ErrorState,
  FilterBar,
  LoadingState,
  PageCard,
  PageHeader,
  StatusTag,
} from '../components/ui';
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

/** Nhãn điều kiện tuyến — "Mọi tuyến" hoặc "Tuyến 01 · Bến xe Mỹ Đình — Gia Lâm". */
function routeText(voucher: Voucher): string {
  if (voucher.routeId === null) return 'Mọi tuyến';
  const code = voucher.routeCode ? `Tuyến ${voucher.routeCode}` : 'Tuyến riêng';
  return voucher.routeName ? `${code} · ${voucher.routeName}` : code;
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

/**
 * Lọc voucher theo tuyến cho ô tìm kiếm. Truy vấn rỗng = giữ nguyên toàn bộ. Có truy vấn thì
 * khớp theo mã hoặc tên tuyến; voucher áp dụng MỌI tuyến (`routeId` null) luôn được giữ vì nó
 * dùng được trên mọi tuyến.
 */
function filterByRoute(vouchers: Voucher[], query: string): Voucher[] {
  const q = query.trim().toLowerCase();
  if (!q) return vouchers;
  return vouchers.filter((voucher) => {
    if (voucher.routeId === null) return true;
    const routeCode = voucher.routeCode?.toLowerCase() ?? '';
    const routeName = voucher.routeName?.toLowerCase() ?? '';
    return routeCode.includes(q) || routeName.includes(q);
  });
}

// Màn hình danh sách voucher đang khả dụng cho hành khách (US 18 "Quản lý Voucher", Sprint 3 —
// task của Nguyễn Đình Băng): liệt kê các mã giảm giá mà khách CÓ THỂ dùng ngay lúc này (đang
// bật + trong khoảng hiệu lực + còn lượt), kèm giá trị giảm và điều kiện áp dụng để khách chủ
// động chọn mã trước khi vào bước thanh toán.
//
// Hợp đồng chưa có endpoint công khai "voucher đang khả dụng" (mọi endpoint voucher đều là quản
// trị ManagerOrAbove hoặc kiểm-tra-mã), nên voucherApi.listAvailable() chạy dữ liệu giả
// (USE_MOCK_AVAILABLE) để màn hình có mã để hiển thị — nợ hợp đồng: bổ sung endpoint trước.
// Khách không tự dùng mã ở đây: nhập mã là việc của ô nhập mã ở màn thanh toán (component của
// Hoàng Văn Thịnh); màn này chỉ GIỚI THIỆU mã đang có. Ô tìm kiếm lọc mã theo mã/tên tuyến.
export default function VoucherAvailablePage() {
  const navigate = useNavigate();
  const { token } = theme.useToken();

  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [reloadToken, setReloadToken] = useState(0);
  const [searchText, setSearchText] = useState('');

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
  const filteredVouchers = view.kind === 'vouchers' ? filterByRoute(view.vouchers, searchText) : [];
  const hasSearch = searchText.trim() !== '';
  const noSearchResult = view.kind === 'vouchers' && filteredVouchers.length === 0 && hasSearch;

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
            <Input
              allowClear
              prefix={<SearchOutlined />}
              placeholder="Tìm khuyến mãi theo tuyến đường…"
              style={{ width: 300 }}
              value={searchText}
              onChange={(event) => setSearchText(event.target.value)}
            />
            <Text type="secondary">
              {view.kind === 'vouchers' && (
                <>
                  {hasSearch ? 'Tìm thấy ' : 'Đang có '}
                  <Text strong>{filteredVouchers.length}</Text>
                  {hasSearch ? ' voucher khớp tuyến' : ' voucher khả dụng'}
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

        {/* Có voucher nhưng ô tìm kiếm theo tuyến không khớp mã nào — phân biệt với "chưa có mã". */}
        {view.kind === 'vouchers' && noSearchResult && (
          <EmptyState
            description="Không tìm thấy voucher nào cho tuyến đường này. Thử từ khoá khác hoặc xem toàn bộ voucher."
            action={<Button onClick={() => setSearchText('')}>Xem toàn bộ voucher</Button>}
          />
        )}

        {view.kind === 'vouchers' && !noSearchResult && (
          <List<Voucher>
            dataSource={filteredVouchers}
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
                      <StatusTag color="blue">{voucher.code}</StatusTag>
                      <Text strong>{voucher.name}</Text>
                    </Space>

                    <div style={{ marginTop: 4 }}>
                      <Text type="secondary" style={{ fontSize: 13 }}>
                        {routeText(voucher)} · {conditionsText(voucher)}
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
                    <div style={{ fontSize: 18, fontWeight: 700, color: token.colorPrimaryText }}>
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
