import { useEffect, useState } from 'react';
import {
  AutoComplete,
  Button,
  Card,
  Descriptions,
  Empty,
  Radio,
  Select,
  Space,
  Typography,
  message,
} from 'antd';
import { CheckCircleFilled, IdcardOutlined, SearchOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import monthlyPassApi, { PASS_TYPE_OPTIONS, findPassType } from '../api/monthlyPassApi';
import type { MonthlyPass, MonthlyPassRoute, PassTypeCode } from '../api/monthlyPassApi';
import stopSearchApi from '../api/stopSearchApi';
import type { AppError } from '../api/axiosClient';
import MonthlyPassStatusTag from '../components/MonthlyPassStatusTag';

const { Title, Text } = Typography;

/** Định dạng tiền VND — ví dụ 200000 → "200.000 đ". */
function formatVnd(price: number): string {
  return `${price.toLocaleString('vi-VN')} đ`;
}

/** Gợi ý của một ô nhập điểm đi/điểm đến — giá trị phát ra là TÊN trạm. */
interface StopOption {
  value: string;
  label: string;
}

/**
 * Gợi ý trạm cho một ô nhập: gọi GET /stops/search sau khi người dùng ngừng gõ 300ms.
 *
 * Chỉ là lớp cộng thêm — AutoComplete cho gõ tay giá trị bất kỳ, nên gợi ý hỏng (mất mạng,
 * backend lỗi) thì ô nhập vẫn dùng được. Cùng lý do components/StopAutocomplete.tsx chọn
 * AutoComplete thay vì Select.
 */
function useStopSuggestions(keyword: string): StopOption[] {
  const [options, setOptions] = useState<StopOption[]>([]);

  useEffect(() => {
    const trimmed = keyword.trim();
    if (trimmed.length === 0) {
      // oxlint-disable-next-line react/set-state-in-effect
      setOptions([]);
      return;
    }

    let cancelled = false;
    const timer = setTimeout(() => {
      stopSearchApi
        .search(trimmed)
        .then((stops) => {
          if (cancelled) return;
          setOptions(
            stops.map((stop) => ({
              // /routes/search khớp theo TÊN trạm (không nhận id), nên giá trị phát ra là tên.
              value: stop.name,
              // Kèm địa chỉ để phân biệt hai trạm trùng tên — hợp đồng /stops/search ghi rõ.
              label: `${stop.name} — ${stop.address}`,
            })),
          );
        })
        .catch(() => {
          // Gợi ý hỏng không được chặn gõ tay — im lặng bỏ gợi ý, không báo lỗi.
          if (!cancelled) setOptions([]);
        });
    }, 300);

    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [keyword]);

  return options;
}

// Màn hình đăng ký vé tháng (story 16) — dành cho HÀNH KHÁCH.
//
// Luồng: nhập Điểm đi / Điểm đến (gợi ý trạm từ GET /stops/search — công khai) → bấm "Tìm
// tuyến" gọi GET /routes/search (công khai, chỉ trả tuyến Active khớp cặp điểm) → chọn một
// tuyến trong danh sách khớp → chọn loại vé → xem giá + thời hạn → Đăng ký.
//
// Vì sao không phải ô chọn "danh sách tuyến": GET /routes chỉ mở cho Admin/Manager (màn quản
// lý tuyến, story 12) nên hành khách nhận 403 — đúng lỗi từng thấy ở màn này. /routes/search
// là API công khai duy nhất trả về tuyến, nhưng bắt buộc đủ điểm đi + điểm đến (mục
// "/routes/search" của docs/api-contract.md) — chọn cặp điểm chính là luồng tra cứu của hành
// khách mà hợp đồng đã vạch (xem thêm đầu frontend/src/api/monthlyPassApi.ts).
export default function MonthlyPassRegistrationPage() {
  const [origin, setOrigin] = useState('');
  const [destination, setDestination] = useState('');

  // Tuyến khớp cặp điểm đi/điểm đến — rỗng cho tới lần "Tìm tuyến" đầu tiên.
  const [foundRoutes, setFoundRoutes] = useState<MonthlyPassRoute[]>([]);
  const [searching, setSearching] = useState(false);
  // Đã bấm "Tìm tuyến" ít nhất một lần — phân biệt "chưa tìm" với "tìm mà không có kết quả".
  const [hasSearched, setHasSearched] = useState(false);
  const [selectedRouteId, setSelectedRouteId] = useState<string | null>(null);

  const [passTypeCode, setPassTypeCode] = useState<PassTypeCode | undefined>(undefined);
  const [submitting, setSubmitting] = useState(false);
  // Vé tháng vừa đăng ký thành công — null khi chưa đăng ký hoặc đang đăng ký vé mới.
  const [registered, setRegistered] = useState<MonthlyPass | null>(null);

  const originOptions = useStopSuggestions(origin);
  const destinationOptions = useStopSuggestions(destination);

  const selectedRoute = foundRoutes.find((route) => route.id === selectedRouteId);
  const selectedPassType = findPassType(passTypeCode);

  // Ngày hiệu lực dự kiến: bắt đầu hôm nay, kết thúc sau đúng `durationMonths` tháng.
  const validity =
    selectedPassType === undefined
      ? null
      : {
          from: dayjs().startOf('day'),
          to: dayjs().startOf('day').add(selectedPassType.durationMonths, 'month'),
        };

  const handleSearchRoutes = async () => {
    const from = origin.trim();
    const to = destination.trim();

    if (from.length === 0 || to.length === 0) {
      message.warning('Vui lòng nhập đủ điểm đi và điểm đến.');
      return;
    }

    if (from.toLowerCase() === to.toLowerCase()) {
      // Cùng lối chặn của backend (400 errors.destination) và form tra cứu phía client:
      // hai đầu mút trùng nhau thì không có hành trình hợp lệ nào.
      message.warning('Điểm đi và điểm đến không được trùng nhau.');
      return;
    }

    setSearching(true);
    try {
      const routes = await monthlyPassApi.searchRoutes(from, to);
      setFoundRoutes(routes);
      setSelectedRouteId(null);
      setHasSearched(true);
    } catch (error) {
      message.error((error as AppError).customMessage || 'Không tìm được tuyến cho cặp điểm này.');
    } finally {
      setSearching(false);
    }
  };

  const handleSubmit = async () => {
    // Nút đã disabled khi thiếu lựa chọn; đây là chốt chặn thứ hai cho đường gọi bằng mã.
    if (!selectedRoute || !selectedPassType) return;

    setSubmitting(true);
    try {
      const pass = await monthlyPassApi.register({
        routeId: selectedRoute.id,
        passTypeCode: selectedPassType.code,
      });
      setRegistered(pass);
      message.success('Đã đăng ký vé tháng thành công.');
    } catch (error) {
      message.error((error as AppError).customMessage || 'Không thể đăng ký vé tháng.');
    } finally {
      setSubmitting(false);
    }
  };

  // Đăng ký thêm một vé khác: xoá kết quả cũ và bỏ tuyến/loại vé đã chọn. GIỮ điểm đi/điểm đến
  // và danh sách tuyến vừa tìm — khách thường đăng ký tiếp trên cùng cung đường.
  const handleRegisterAnother = () => {
    setRegistered(null);
    setSelectedRouteId(null);
    setPassTypeCode(undefined);
  };

  const registeredRoute = foundRoutes.find((route) => route.id === registered?.routeId);
  const registeredType = findPassType(registered?.passTypeCode);

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          Đăng ký vé tháng
        </Title>
        <Text type="secondary">
          Chọn điểm đi và điểm đến để tìm tuyến, chọn loại vé để xem giá, rồi đăng ký vé tháng
          tiết kiệm chi phí đi lại.
        </Text>
      </div>

      <Card
        variant="borderless"
        style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}
      >
        <Space direction="vertical" size={20} style={{ display: 'flex', width: '100%' }}>
          {/* Ô nhập cặp điểm + nút tìm tuyến. */}
          <Space size="large" wrap align="end" style={{ display: 'flex', width: '100%' }}>
            <div style={{ flex: 1, minWidth: 240 }}>
              <Text type="secondary" style={{ display: 'block', marginBottom: 4 }}>
                Điểm đi
              </Text>
              <AutoComplete
                value={origin}
                onChange={setOrigin}
                options={originOptions}
                placeholder="Ví dụ: Bến Thành"
                allowClear
                style={{ width: '100%' }}
              />
            </div>

            <div style={{ flex: 1, minWidth: 240 }}>
              <Text type="secondary" style={{ display: 'block', marginBottom: 4 }}>
                Điểm đến
              </Text>
              <AutoComplete
                value={destination}
                onChange={setDestination}
                options={destinationOptions}
                placeholder="Ví dụ: Chợ Lớn"
                allowClear
                style={{ width: '100%' }}
              />
            </div>

            <Button
              type="primary"
              icon={<SearchOutlined />}
              loading={searching}
              onClick={handleSearchRoutes}
            >
              Tìm tuyến
            </Button>
          </Space>

          {/* Danh sách tuyến khớp — chọn một tuyến để đăng ký. */}
          {foundRoutes.length > 0 && (
            <div>
              <Text type="secondary">
                Tìm thấy {foundRoutes.length} tuyến khớp — chọn một tuyến:
              </Text>
              <Radio.Group
                value={selectedRouteId}
                onChange={(event) => setSelectedRouteId(event.target.value as string)}
                style={{ display: 'block', marginTop: 8 }}
              >
                <Space direction="vertical" size={8} style={{ display: 'flex' }}>
                  {foundRoutes.map((route) => (
                    <Radio key={route.id} value={route.id}>
                      <Text strong>{route.code}</Text> — {route.name}{' '}
                      <Text type="secondary">
                        ({route.origin} → {route.destination})
                      </Text>
                    </Radio>
                  ))}
                </Space>
              </Radio.Group>
            </div>
          )}

          {/* Tìm mà không có tuyến nào khớp — khác với "chưa tìm" ở trên. */}
          {hasSearched && foundRoutes.length === 0 && (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description="Không có tuyến nào khớp cặp điểm này. Thử đổi chiều điểm đi/điểm đến hoặc chọn tên trạm khác."
            />
          )}

          <div style={{ maxWidth: 320 }}>
            <Text type="secondary" style={{ display: 'block', marginBottom: 4 }}>
              Loại vé
            </Text>
            <Select
              value={passTypeCode}
              onChange={(value: PassTypeCode) => setPassTypeCode(value)}
              placeholder="Chọn loại vé tháng"
              options={PASS_TYPE_OPTIONS}
              suffixIcon={<IdcardOutlined />}
              style={{ width: '100%' }}
            />
          </div>

          {/* Ô "xem giá" — chỉ hiện khi đã chọn đủ cả tuyến lẫn loại vé. */}
          {selectedRoute && selectedPassType && validity && (
            <div
              style={{
                background: '#eef1ff',
                border: '1px solid #dbe1ff',
                borderRadius: 12,
                padding: '16px 20px',
              }}
            >
              <Space direction="vertical" size={6} style={{ width: '100%' }}>
                <Text type="secondary" style={{ fontSize: 13 }}>
                  Giá vé tháng của bạn
                </Text>
                <Text style={{ fontSize: 28, fontWeight: 700, color: '#4361ee', lineHeight: 1.2 }}>
                  {formatVnd(selectedPassType.price)}
                </Text>
                <Text style={{ fontSize: 13 }}>
                  {selectedRoute.code} — {selectedRoute.name} · {selectedRoute.origin} →{' '}
                  {selectedRoute.destination}
                </Text>
                <Text type="secondary" style={{ fontSize: 13 }}>
                  Thời hạn {selectedPassType.durationMonths} tháng · Hiệu lực từ{' '}
                  {validity.from.format('DD/MM/YYYY')} đến {validity.to.format('DD/MM/YYYY')}
                </Text>
              </Space>
            </div>
          )}

          <Button
            type="primary"
            size="large"
            loading={submitting}
            disabled={!selectedRoute || !selectedPassType}
            onClick={handleSubmit}
          >
            Đăng ký vé tháng
          </Button>
        </Space>
      </Card>

      {/* Kết quả đăng ký thành công. */}
      {registered && registeredRoute && registeredType && (
        <Card
          variant="borderless"
          style={{ borderRadius: 16, marginTop: 20, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}
          title={
            <Space>
              <CheckCircleFilled style={{ color: '#52c41a' }} />
              <span>Đăng ký vé tháng thành công</span>
            </Space>
          }
        >
          <Descriptions
            column={1}
            size="middle"
            items={[
              {
                key: 'code',
                label: 'Mã vé tháng',
                children: (
                  <Text code style={{ fontSize: 14 }}>
                    {registered.code}
                  </Text>
                ),
              },
              {
                key: 'route',
                label: 'Tuyến áp dụng',
                children: `${registeredRoute.code} — ${registeredRoute.name} (${registeredRoute.origin} → ${registeredRoute.destination})`,
              },
              {
                key: 'passType',
                label: 'Loại vé',
                children: registeredType.name,
              },
              {
                key: 'validity',
                label: 'Hiệu lực',
                children: `${dayjs(registered.validFrom).format('DD/MM/YYYY')} → ${dayjs(registered.validTo).format('DD/MM/YYYY')}`,
              },
              {
                key: 'price',
                label: 'Giá vé',
                children: <Text strong>{formatVnd(registered.price)}</Text>,
              },
              {
                key: 'status',
                label: 'Trạng thái',
                children: <MonthlyPassStatusTag pass={registered} />,
              },
            ]}
          />

          <Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
            Lưu lại mã vé tháng để soát vé khi lên xe. Vé tháng không đảm bảo có ghế ngồi.
          </Text>

          <Button style={{ marginTop: 16 }} onClick={handleRegisterAnother}>
            Đăng ký vé khác
          </Button>
        </Card>
      )}
    </div>
  );
}
