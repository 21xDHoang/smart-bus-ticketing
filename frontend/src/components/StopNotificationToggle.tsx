import { useEffect, useState } from 'react';
import { Button, Switch, Tooltip, message } from 'antd';
import subscriptionApi from '../api/subscriptionApi';
import type { AppError } from '../api/axiosClient';
import { useAuth } from '../contexts';

interface StopNotificationToggleProps {
  /** Tuyến chứa trạm — khoá nghiệp vụ của lượt theo dõi. */
  routeId: string;

  /** Trạm muốn bật/tắt thông báo "xe sắp đến". */
  stopId: string;

  /** Nhãn cạnh công tắc; bỏ trống dùng câu mặc định. */
  label?: string;

  /** Trang cha khoá công tắc (ví dụ đang tải dữ liệu khác). */
  disabled?: boolean;

  /** Gọi sau khi bật/tắt thành công — trang cha cập nhật danh sách theo dõi của mình. */
  onChanged?: (subscribed: boolean) => void;
}

/** Ba trạng thái của lần tải đầu — không gộp trạng thái nào vào nhau. */
type LoadState = 'loading' | 'ready' | 'error';

/**
 * Công tắc "Nhận thông báo khi xe sắp đến trạm này" — task *"Component bật/tắt nhận thông báo
 * theo trạm"* (US 10 "Thông báo trạm", Sprint 5 dòng 20 — Hoàng Văn Thịnh).
 *
 * Tự khép kín ba bước của một công tắc:
 *   1. tải `GET /subscriptions` để biết trạm này đang được theo dõi hay chưa;
 *   2. bật  → `POST /subscriptions` với đúng `routeId` + `stopId`;
 *   3. tắt  → `DELETE /subscriptions/{id}` — XOÁ dòng, không gửi cờ `IsActive` (docs/37 §3.2).
 *
 * ⚠️ Hai trường hợp cần phân biệt (docs/37 §3.1):
 *   - Có dòng riêng cho ĐÚNG trạm này → công tắc bật/tắt được.
 *   - Chỉ có dòng `stopId = null` (theo dõi CẢ TUYẾN) → trạm này đã được phủ; công tắc hiện
 *     "bật" và KHOÁ, vì tắt ở đây sẽ xoá lượt theo dõi của mọi trạm trên tuyến. Muốn tắt thì
 *     tắt ở mục theo dõi cả tuyến.
 *
 * Bấm bật khi dòng đã tồn tại (ví dụ tab khác vừa tạo) → backend trả 409; component tải lại
 * danh sách thay vì hiện lỗi, vì kết quả cuối cùng vẫn là "đang bật".
 *
 * ⚠️ Mỗi instance gọi `GET /subscriptions` một lần. Trang hiển thị CẢ danh sách trạm cùng lúc
 * nên chỉ dựng công tắc cho trạm đang chọn, hoặc nâng việc tải danh sách lên trang cha ở lần
 * tích hợp sau (xem docs/45 §5).
 */
export default function StopNotificationToggle({
  routeId,
  stopId,
  label = 'Nhận thông báo khi xe sắp đến trạm này',
  disabled = false,
  onChanged,
}: StopNotificationToggleProps) {
  const { isAuthenticated } = useAuth();

  const [loadState, setLoadState] = useState<LoadState>('loading');
  const [subscriptionId, setSubscriptionId] = useState<string | null>(null);
  const [routeSubscribed, setRouteSubscribed] = useState(false);
  const [pending, setPending] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  // Tải trạng thái theo dõi của đúng trạm này (và của cả tuyến) mỗi khi đổi người đăng nhập,
  // đổi trạm, hoặc bấm "Thử lại".
  useEffect(() => {
    if (!isAuthenticated) return;

    let cancelled = false;

    // Đồng bộ state với lần tải mới — không phải state dẫn xuất trong render.
    // oxlint-disable-next-line react/set-state-in-effect
    setLoadState('loading');

    subscriptionApi
      .listMine()
      .then((list) => {
        if (cancelled) return;

        const mine = list.find((item) => item.routeId === routeId && item.stopId === stopId);
        const wholeRoute = list.find((item) => item.routeId === routeId && item.stopId === null);

        setSubscriptionId(mine?.id ?? null);
        setRouteSubscribed(Boolean(wholeRoute));
        setLoadState('ready');
      })
      .catch(() => {
        if (cancelled) return;
        setLoadState('error');
      });

    return () => {
      cancelled = true;
    };
  }, [isAuthenticated, routeId, stopId, reloadKey]);

  const handleToggle = async (next: boolean) => {
    if (pending) return;

    setPending(true);

    try {
      if (next) {
        const created = await subscriptionApi.subscribe({ routeId, stopId });
        setSubscriptionId(created.id);
        message.success('Đã bật nhận thông báo cho trạm này.');
        onChanged?.(true);
      } else if (subscriptionId) {
        await subscriptionApi.unsubscribe(subscriptionId);
        setSubscriptionId(null);
        message.success('Đã tắt nhận thông báo cho trạm này.');
        onChanged?.(false);
      }
    } catch (error) {
      const appError = error as AppError;

      if (appError.status === 409) {
        // Dòng đã tồn tại (tab khác hoặc lần bấm trước): tải lại để lấy đúng id, không báo lỗi
        // — kết quả cuối cùng vẫn là "đang bật".
        message.info('Bạn đã theo dõi trạm này rồi.');
        onChanged?.(true);
        setReloadKey((key) => key + 1);
      } else if (appError.status === 404 && !next) {
        // Dòng đã bị xoá ở nơi khác — coi như đã tắt, không phải lỗi của người dùng.
        setSubscriptionId(null);
        message.info('Trạm này đã được tắt theo dõi từ trước.');
        onChanged?.(false);
      } else {
        message.error(
          appError.customMessage ||
            (next ? 'Không bật được nhận thông báo.' : 'Không tắt được nhận thông báo.'),
        );
      }
    } finally {
      setPending(false);
    }
  };

  // Chưa đăng nhập thì không có lượt theo dõi nào để đọc/ghi — khoá công tắc và nói lý do.
  if (!isAuthenticated) {
    return (
      <Tooltip title="Đăng nhập để nhận thông báo khi xe sắp đến trạm.">
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8, opacity: 0.65 }}>
          <Switch disabled checked={false} aria-label={label} />
          <span>{label}</span>
        </span>
      </Tooltip>
    );
  }

  if (loadState === 'error') {
    return (
      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
        <Switch disabled checked={false} aria-label={label} />
        <span>Không tải được trạng thái theo dõi.</span>
        <Button type="link" size="small" onClick={() => setReloadKey((key) => key + 1)}>
          Thử lại
        </Button>
      </span>
    );
  }

  if (routeSubscribed) {
    return (
      <Tooltip title="Bạn đang theo dõi cả tuyến này nên đã nhận thông báo cho mọi trạm. Tắt ở mục theo dõi tuyến.">
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
          <Switch checked disabled aria-label={label} />
          <span>{label}</span>
        </span>
      </Tooltip>
    );
  }

  const loading = loadState === 'loading' || pending;

  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
      <Switch
        checked={subscriptionId !== null}
        loading={loading}
        disabled={disabled || loadState !== 'ready' || pending}
        onChange={handleToggle}
        aria-label={label}
      />
      <span>{label}</span>
    </span>
  );
}
