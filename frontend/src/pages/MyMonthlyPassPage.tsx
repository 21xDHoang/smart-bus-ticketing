import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button, List, Popconfirm, Space, Typography, message, theme } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import myMonthlyPassApi from '../api/myMonthlyPassApi';
import type { MonthlyPass } from '../api/monthlyPassApi';
import type { AppError } from '../api/axiosClient';
import MonthlyPassExpiryReminder from '../components/MonthlyPassExpiryReminder';
import MonthlyPassStatusTag from '../components/MonthlyPassStatusTag';
import { getExpiringSoonPasses } from '../components/monthlyPassReminder';
import { EmptyState, ErrorState, FilterBar, LoadingState, PageCard, PageHeader, RouteBadge } from '../components/ui';
import { formatDate, formatVnd } from '../components/ui/format';

const { Text } = Typography;

/** Mảng rỗng dùng chung — giữ nguyên tham chiếu để không sinh mảng mới mỗi lần render. */
const NO_PASSES: MonthlyPass[] = [];

/**
 * Rút mã tuyến từ mã vé tháng.
 *
 * Hợp đồng chốt khuôn mã vé là `MP-{mã tuyến}-{6 ký tự A–Z/0–9}`, nên đoạn giữa hai dấu gạch
 * chính là mã tuyến. Chỉ là lưới dự phòng khi response thiếu `routeCode` (hợp đồng luôn trả kèm
 * mã + tên tuyến): vé vẫn còn căn cứ để hành khách đối chiếu mình đang đi tuyến nào.
 * Mã không đúng khuôn → trả `null` để chỗ gọi biết là không rút được gì.
 */
function routeCodeFromPassCode(code: string): string | null {
  const match = /^MP-(.+)-[A-Z0-9]{6}$/.exec(code);
  return match ? match[1] : null;
}

/** Kết quả một lượt tải danh sách vé — ba trạng thái, không gộp trạng thái nào vào nhau. */
type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; passes: MonthlyPass[] };

/** Bốn trạng thái hiển thị của màn hình — đúng bốn nhánh, không nhánh nào kiêm nhánh nào. */
type PassView =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'empty' }
  | { kind: 'passes'; passes: MonthlyPass[] };

/**
 * Luật chuyển trạng thái, tách khỏi JSX để bốn nhánh không lồng vào nhau.
 *
 * `done` mà không có vé nào là "chưa có vé đang hoạt động" — im lặng, không phải lỗi. Còn gọi
 * API hỏng là trạng thái RIÊNG: mảng rỗng và lượt gọi hỏng là hai chuyện khác nhau, gộp lại
 * thì màn hình nói sai sự thật (lỗi mạng bị đọc thành "bạn không có vé tháng nào").
 */
function viewOf(state: LoadState): PassView {
  if (state.status === 'loading') return { kind: 'loading' };
  if (state.status === 'error') return { kind: 'error', message: state.message };
  return state.passes.length === 0
    ? { kind: 'empty' }
    : { kind: 'passes', passes: state.passes };
}

/**
 * Dòng tuyến của một vé — mã + tên lấy thẳng từ `GET /monthly-passes/me` (backend trả kèm
 * `routeCode`/`routeName` đọc từ bảng Routes). Không gọi `GET /routes`: endpoint đó sau policy
 * ManagerOrAbove nên hành khách nhận 403. Mã vé vẫn là lưới dự phòng nếu response thiếu mã tuyến.
 * Không hiện `routeId` thô: một GUID không nói gì với hành khách.
 */
function RouteLine({ pass }: { pass: MonthlyPass }) {
  const routeCode = pass.routeCode || routeCodeFromPassCode(pass.code);

  if (!routeCode) return <Text strong>Vé tháng</Text>;

  return (
    <Space size={8} wrap>
      <RouteBadge code={routeCode} />
      <Text strong>{pass.routeName || `Tuyến ${routeCode}`}</Text>
    </Space>
  );
}

// Màn hình quản lý vé tháng của tôi + nút gia hạn — story 16 "Đăng ký và gia hạn vé tháng
// trực tuyến", task Sprint 2 dòng 45 của Hoàng Văn Thịnh.
//
// Hai endpoint đều đã có thật ở backend (xem myMonthlyPassApi.ts): GET /monthly-passes/me trả
// về các vé ĐANG có hiệu lực, POST /monthly-passes/{id}/renew ghi thêm một dòng mới cho kỳ kế
// tiếp. Hai component dùng lại của Nguyễn Đình Băng — MonthlyPassStatusTag (nhãn còn hạn / sắp
// hết hạn) và MonthlyPassExpiryReminder (nhắc ở đầu trang) — được NHÚNG nguyên vẹn, không sửa:
// doc của cả hai đều ghi rõ chúng dành cho màn hình này.
//
// Màn hình này cần một dòng route trong App.tsx (file dùng chung §E1, của Băng) mới vào được
// từ menu — xem báo cáo kèm PR.
export default function MyMonthlyPassPage() {
  const navigate = useNavigate();
  // Màu lấy từ token của theme chung — không viết hex tay trong màn hình.
  const { token } = theme.useToken();

  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [reloadToken, setReloadToken] = useState(0);

  // Vé đang gia hạn (một lượt ghi tại một thời điểm) và kỳ kế tiếp đã tạo, khoá theo id vé cũ.
  const [renewingId, setRenewingId] = useState<string | null>(null);
  const [nextPeriods, setNextPeriods] = useState<Record<string, MonthlyPass>>({});

  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API. Đây là lần tải thực sự (không phải "đồng
    // bộ state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setState({ status: 'loading' });

    myMonthlyPassApi
      .list()
      .then((passes) => {
        if (cancelled) return;
        setState({ status: 'done', passes });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setState({
          status: 'error',
          message: appError.customMessage || 'Không thể tải danh sách vé tháng.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadToken]);

  const view = viewOf(state);
  const passes = view.kind === 'passes' ? view.passes : NO_PASSES;

  // Gọi trước khi render để biết component nhắc nhở CÓ hiện gì không — nhờ vậy không đặt một
  // khối rỗng có margin ở đầu trang khi không có vé nào sắp hết hạn.
  const expiring = getExpiringSoonPasses(passes);

  const handleRenew = (pass: MonthlyPass) => {
    setRenewingId(pass.id);

    myMonthlyPassApi
      .renew(pass.id)
      .then((renewed) => {
        // Kỳ mới có validFrom ở TƯƠNG LAI nên KHÔNG nằm trong GET /monthly-passes/me (hợp
        // đồng: chỉ trả vé đang có hiệu lực) — vì vậy KHÔNG gọi lại danh sách, mà ghi nhớ kỳ
        // mới để hiện ngay trên thẻ của vé vừa gia hạn. Thiếu dòng này thì bấm "Gia hạn" xong
        // màn hình không đổi gì và người dùng tưởng đã hỏng.
        setNextPeriods((current) => ({ ...current, [pass.id]: renewed }));
      })
      .catch((err: unknown) => {
        // Lỗi hiện bằng toast vì trong thẻ không có chỗ đặt câu lỗi theo từng vé (409 chồng
        // lấn, 404 vé không còn…). Thành công thì KHÔNG toast: đã có dòng "kỳ kế tiếp" nói
        // ngay tại chỗ, báo hai lần là thừa.
        const appError = err as AppError;
        message.error(appError.customMessage || 'Không gia hạn được vé tháng.');
      })
      .finally(() => setRenewingId(null));
  };

  return (
    <div>
      <PageHeader
        title="Vé tháng của tôi"
        subtitle={
          <>
            Vé tháng đang có hiệu lực của bạn. Vé đã hết hạn và kỳ gia hạn chưa tới ngày không
            hiện ở đây.
          </>
        }
      />

      {expiring.length > 0 && (
        <div style={{ marginBottom: 16 }}>
          <MonthlyPassExpiryReminder passes={passes} />
        </div>
      )}

      <PageCard
        toolbar={
          <FilterBar
            actions={
              <Button icon={<PlusOutlined />} onClick={() => navigate('/monthly-passes')}>
                Đăng ký vé tháng
              </Button>
            }
          >
            <Text type="secondary">
              {view.kind === 'passes' && (
                <>
                  Đang có <Text strong>{view.passes.length}</Text> vé tháng hoạt động
                </>
              )}
            </Text>
          </FilterBar>
        }
      >

        {/* Đang tải — spinner riêng, không mượn danh sách của lượt trước và không hiện số đếm. */}
        {view.kind === 'loading' && <LoadingState description="Đang tải danh sách vé tháng…" />}

        {/* Gọi API hỏng — trạng thái riêng, có lối thoát, KHÔNG mạo nhận là "chưa có vé nào". */}
        {view.kind === 'error' && (
          <ErrorState
            title="Không tải được danh sách vé tháng"
            description={view.message}
            onRetry={() => setReloadToken((n) => n + 1)}
          />
        )}

        {/* Tải xong nhưng không có vé nào đang hoạt động — mảng rỗng là câu trả lời hợp lệ. */}
        {view.kind === 'empty' && (
          <EmptyState
            description="Bạn chưa có vé tháng nào đang hoạt động. Vé đã hết hạn hoặc kỳ gia hạn chưa tới ngày cũng không hiện ở đây."
            action={
              <Button
                type="primary"
                icon={<PlusOutlined />}
                onClick={() => navigate('/monthly-passes')}
              >
                Đăng ký vé tháng
              </Button>
            }
          />
        )}

        {view.kind === 'passes' && (
          <List<MonthlyPass>
            dataSource={view.passes}
            renderItem={(pass) => {
              const nextPeriod = nextPeriods[pass.id];

              return (
                <List.Item style={{ padding: '14px 0', borderBlockEnd: `1px solid ${token.colorSplit}` }}>
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'flex-start',
                      gap: 16,
                      width: '100%',
                      flexWrap: 'wrap',
                    }}
                  >
                    {/* Tuyến + trạng thái + loại vé + khoảng hiệu lực + mã vé */}
                    <div style={{ flex: 1, minWidth: 240 }}>
                      <Space size={8} wrap>
                        <RouteLine pass={pass} />
                        <MonthlyPassStatusTag pass={pass} />
                      </Space>

                      <div style={{ marginTop: 4 }}>
                        <Text type="secondary" style={{ fontSize: 13 }}>
                          {pass.passTypeName || pass.passTypeCode} · Hiệu lực{' '}
                          {formatDate(pass.validFrom)} → {formatDate(pass.validTo)}
                        </Text>
                      </div>

                      <div>
                        <Text type="secondary" style={{ fontSize: 13 }}>
                          Mã vé: <Text code>{pass.code}</Text>
                        </Text>
                      </div>

                      {/* Kỳ kế tiếp vừa tạo — nằm ngoài GET /me nên phải nói tại đây. */}
                      {nextPeriod && (
                        <div style={{ marginTop: 4 }}>
                          <Text type="success" style={{ fontSize: 13 }}>
                            Đã gia hạn — kỳ kế tiếp {formatDate(nextPeriod.validFrom)} →{' '}
                            {formatDate(nextPeriod.validTo)} (mã {nextPeriod.code})
                          </Text>
                        </div>
                      )}
                    </div>

                    {/* Giá đã trả + nút gia hạn */}
                    <div style={{ textAlign: 'right', minWidth: 150 }}>
                      {/* colorPrimaryText (không phải colorPrimary) — màu primary làm CHỮ trên nền
                          tối chỉ đạt ~2.85:1; token *Text là biến thể đã tính đủ tương phản hai chế độ. */}
                      <div
                        className="tabular-nums"
                        style={{ fontSize: 16, fontWeight: 700, color: token.colorPrimaryText }}
                      >
                        {formatVnd(pass.price)}
                      </div>
                      <Popconfirm
                        title="Gia hạn vé tháng này?"
                        // Chỉ hứa điều chắc chắn: ngày BẮT ĐẦU của kỳ mới là validTo của vé cũ
                        // (hợp đồng — vé còn hạn thì kỳ mới nối đuôi liền mạch). Ngày kết thúc
                        // do server tính theo tháng lịch nên không đoán trước ở đây.
                        description={`Kỳ mới cùng loại vé, hiệu lực từ ${formatDate(pass.validTo)}. Vé hiện tại được giữ nguyên làm lịch sử.`}
                        okText="Gia hạn"
                        cancelText="Huỷ"
                        onConfirm={() => handleRenew(pass)}
                      >
                        <Button
                          type="link"
                          size="small"
                          style={{ paddingInline: 0 }}
                          loading={renewingId === pass.id}
                          // Đã gia hạn rồi thì khoá nút: bấm lần nữa sẽ tính đúng khoảng hiệu
                          // lực vừa tạo nên chắc chắn nhận 409 chồng lấn. Còn đang gia hạn một
                          // vé khác thì khoá tất cả để không có hai lượt ghi cùng lúc.
                          disabled={nextPeriod !== undefined || renewingId !== null}
                        >
                          {nextPeriod ? 'Đã gia hạn' : 'Gia hạn'}
                        </Button>
                      </Popconfirm>
                    </div>
                  </div>
                </List.Item>
              );
            }}
          />
        )}
      </PageCard>
    </div>
  );
}
