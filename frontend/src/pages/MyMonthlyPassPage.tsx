import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Alert,
  Button,
  Card,
  Empty,
  List,
  Popconfirm,
  Result,
  Space,
  Spin,
  Tag,
  Typography,
  message,
} from 'antd';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import myMonthlyPassApi from '../api/myMonthlyPassApi';
import { findPassType } from '../api/monthlyPassApi';
import type { MonthlyPass } from '../api/monthlyPassApi';
import { fetchRoutes } from '../api/routeApi';
import type { AppError } from '../api/axiosClient';
import MonthlyPassExpiryReminder from '../components/MonthlyPassExpiryReminder';
import MonthlyPassStatusTag from '../components/MonthlyPassStatusTag';
import { getExpiringSoonPasses } from '../components/monthlyPassReminder';

const { Title, Text } = Typography;

/** Trần `pageSize` của GET /routes là 100 — cùng con số monthlyPassApi.ts đang dùng. */
const ROUTE_PAGE_SIZE = 100;

/** Tên + mã tuyến đã tra được, khoá theo `routeId` của vé. */
type RouteLookup = Record<string, { code: string; name: string }>;

/** Mảng rỗng dùng chung — giữ nguyên tham chiếu để không sinh mảng mới mỗi lần render. */
const NO_PASSES: MonthlyPass[] = [];

/** Định dạng tiền VND — ví dụ 200000 → "200.000 đ". */
function formatVnd(price: number): string {
  return `${price.toLocaleString('vi-VN')} đ`;
}

/** Ngày hiển thị cho hành khách — hợp đồng trả ISO 8601 UTC, chỉ cần phần ngày. */
function formatDate(iso: string): string {
  return dayjs(iso).format('DD/MM/YYYY');
}

/**
 * Rút mã tuyến từ mã vé tháng.
 *
 * Hợp đồng chốt khuôn mã vé là `MP-{mã tuyến}-{6 ký tự A–Z/0–9}`, nên đoạn giữa hai dấu gạch
 * chính là mã tuyến. Dùng khi chưa tra được tên tuyến (xem ghi chú ở effect tra tuyến): vé
 * vẫn còn căn cứ để hành khách đối chiếu mình đang đi tuyến nào, thay vì một dòng trống.
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
 * Dòng tuyến của một vé — có tên tuyến thì hiện mã + tên, chưa tra được thì lùi về mã tuyến
 * rút từ mã vé. Không hiện `routeId` thô: một GUID không nói gì với hành khách.
 */
function RouteLine({ pass, route }: { pass: MonthlyPass; route?: { code: string; name: string } }) {
  if (route) {
    return (
      <Space size={8} wrap>
        <Tag color="blue">{route.code}</Tag>
        <Text strong>{route.name}</Text>
      </Space>
    );
  }

  const routeCode = routeCodeFromPassCode(pass.code);

  return <Text strong>{routeCode ? `Tuyến ${routeCode}` : 'Vé tháng'}</Text>;
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

  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [reloadToken, setReloadToken] = useState(0);

  // Tên tuyến chỉ để hiển thị thêm — xem effect bên dưới.
  const [routes, setRoutes] = useState<RouteLookup>({});
  const [routesFailed, setRoutesFailed] = useState(false);

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

  useEffect(() => {
    let cancelled = false;

    // Tra tên tuyến là bước "có thì tốt", chạy SONG SONG và KHÔNG chặn danh sách vé: vé phải
    // hiện được kể cả khi bước này hỏng. Hợp đồng (docs/api-contract.md, mục
    // GET /monthly-passes/me) chỉ định ghép routeId → tên tuyến bằng GET /routes, nhưng
    // endpoint đó chỉ dành cho Admin/Manager — hành khách nhận 403 (đã đo trong
    // docs/bao-cao-kiem-thu-cheo-ve-thang.md). Nên với hành khách, tên tuyến sẽ không có và
    // màn hình lùi về mã tuyến rút từ mã vé, kèm một dòng giải thích ở đầu danh sách.
    //
    // Không lọc status=Active: đây là tra TÊN, tuyến tạm ngưng vẫn cần tên để hiển thị.
    fetchRoutes({ page: 1, pageSize: ROUTE_PAGE_SIZE })
      .then(({ items }) => {
        if (cancelled) return;
        setRoutes(
          Object.fromEntries(items.map((route) => [route.id, { code: route.code, name: route.name }])),
        );
      })
      .catch(() => {
        if (cancelled) return;
        // 403 với hành khách là chuyện đã biết, không phải lỗi của màn hình — đánh dấu để
        // hiện một dòng giải thích, không báo lỗi.
        setRoutesFailed(true);
      });

    return () => {
      cancelled = true;
    };
  }, []);

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
      <div style={{ marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          Vé tháng của tôi
        </Title>
        <Text type="secondary">
          Vé tháng đang có hiệu lực của bạn. Vé đã hết hạn và kỳ gia hạn chưa tới ngày không
          hiện ở đây.
        </Text>
      </div>

      {expiring.length > 0 && (
        <div style={{ marginBottom: 16 }}>
          <MonthlyPassExpiryReminder passes={passes} />
        </div>
      )}

      <Card variant="borderless" style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}>
        <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
          <Text type="secondary">
            {view.kind === 'passes' && (
              <>
                Đang có <Text strong>{view.passes.length}</Text> vé tháng hoạt động
              </>
            )}
          </Text>
          <Button icon={<PlusOutlined />} onClick={() => navigate('/monthly-passes')}>
            Đăng ký vé tháng
          </Button>
        </Space>

        {/* Nói một lần ở đầu danh sách thay vì lặp ở từng dòng vé. */}
        {routesFailed && view.kind === 'passes' && (
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 16 }}
            message="Chưa tra được tên tuyến"
            description="Danh sách chỉ hiện được mã tuyến. Vé tháng của bạn vẫn dùng bình thường."
          />
        )}

        {/* Đang tải — spinner riêng, không mượn danh sách của lượt trước và không hiện số đếm. */}
        {view.kind === 'loading' && (
          <div style={{ padding: '56px 0', textAlign: 'center' }}>
            <Spin size="large" />
            <div style={{ marginTop: 12 }}>
              <Text type="secondary">Đang tải danh sách vé tháng…</Text>
            </div>
          </div>
        )}

        {/* Gọi API hỏng — trạng thái riêng, có lối thoát, KHÔNG mạo nhận là "chưa có vé nào". */}
        {view.kind === 'error' && (
          <Result
            status="warning"
            title="Không tải được danh sách vé tháng"
            subTitle={view.message}
            extra={
              <Button icon={<ReloadOutlined />} onClick={() => setReloadToken((n) => n + 1)}>
                Thử lại
              </Button>
            }
          />
        )}

        {/* Tải xong nhưng không có vé nào đang hoạt động — mảng rỗng là câu trả lời hợp lệ. */}
        {view.kind === 'empty' && (
          <Empty description="Bạn chưa có vé tháng nào đang hoạt động. Vé đã hết hạn hoặc kỳ gia hạn chưa tới ngày cũng không hiện ở đây.">
            <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/monthly-passes')}>
              Đăng ký vé tháng
            </Button>
          </Empty>
        )}

        {view.kind === 'passes' && (
          <List<MonthlyPass>
            dataSource={view.passes}
            renderItem={(pass) => {
              const passType = findPassType(pass.passTypeCode);
              const nextPeriod = nextPeriods[pass.id];

              return (
                <List.Item style={{ padding: '14px 0', borderBlockEnd: '1px solid #f1f5f9' }}>
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
                        <RouteLine pass={pass} route={routes[pass.routeId]} />
                        <MonthlyPassStatusTag pass={pass} />
                      </Space>

                      <div style={{ marginTop: 4 }}>
                        <Text type="secondary" style={{ fontSize: 13 }}>
                          {passType?.name ?? pass.passTypeCode} · Hiệu lực{' '}
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
                      <div style={{ fontSize: 16, fontWeight: 700, color: '#4361ee' }}>
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
      </Card>
    </div>
  );
}
