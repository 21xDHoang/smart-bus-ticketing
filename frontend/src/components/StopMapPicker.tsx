import { useEffect, useRef, useState } from 'react';
import { Button, Input, Space, Typography, theme } from 'antd';
import { MapContainer, Marker, TileLayer, useMap, useMapEvents } from 'react-leaflet';
import { divIcon } from 'leaflet';
import type { LatLngExpression, LeafletMouseEvent } from 'leaflet';
import 'leaflet/dist/leaflet.css';
import { palette } from './ui/theme';
import { resolvePlusCode } from './plusCode';

// Toạ độ mặc định: trung tâm Hà Nội — dùng khi thêm mới (chưa có toạ độ nào).
const DEFAULT_CENTER: LatLngExpression = [21.0285, 105.8542];
const DEFAULT_ZOOM = 13;

// Marker tự vẽ (SVG) để tránh lỗi "icon vỡ" của Leaflet khi đóng gói bằng Vite:
// ảnh marker mặc định không được bundle nên hiển thị trống. Dùng divIcon + SVG nội tuyến.
//
// Màu ghim lấy từ bảng màu thương hiệu ở chế độ SÁNG: nền bản đồ OSM luôn là gạch sáng, không
// đổi theo chế độ giao diện, nên ghim cũng giữ một sắc ở cả hai chế độ.
const pinColor = palette('light').primary.base; // #0B5FFF — chấm trắng trên nền này đạt 5.13:1
const pinDotColor = palette('light').primary.onBase;

const pinIcon = divIcon({
  className: '', // bỏ class mặc định để không bị khung trắng/viền xung quanh
  iconSize: [36, 36],
  iconAnchor: [18, 36], // mũi kim nằm đúng toạ độ đã chọn
  html: `<svg xmlns="http://www.w3.org/2000/svg" width="36" height="36" viewBox="0 0 24 24">
    <path fill="${pinColor}" d="M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7z"/>
    <circle cx="12" cy="9" r="2.5" fill="${pinDotColor}"/>
  </svg>`,
});

interface StopMapPickerProps {
  /** Toạ độ đang chọn (null = chưa chọn). */
  value?: { latitude: number; longitude: number } | null;
  /** Gọi khi người dùng chọn toạ độ mới (bấm bản đồ hoặc nhập Plus Code). */
  onChange?: (latitude: number, longitude: number) => void;
}

// Component con nằm TRONG <MapContainer> để bắt sự kiện bấm bản đồ.
// (react-leaflet chỉ cho dùng useMapEvents ở bên trong MapContainer.)
function ClickCapture({ onPick }: { onPick: (latitude: number, longitude: number) => void }) {
  useMapEvents({
    click(event: LeafletMouseEvent) {
      onPick(event.latlng.lat, event.latlng.lng);
    },
  });
  return null;
}

// Bay bản đồ tới toạ độ khi `value` đổi (VD: sau khi nhập Plus Code). Không bay ở lần render
// đầu tiên — MapContainer đã tự đặt `center` khởi tạo rồi.
function Recenter({ value }: { value: { latitude: number; longitude: number } | null | undefined }) {
  const map = useMap();
  const previous = useRef<{ latitude: number; longitude: number } | null>(value);

  useEffect(() => {
    if (!value) return;
    const old = previous.current;
    previous.current = value;

    if (old && old.latitude === value.latitude && old.longitude === value.longitude) {
      return;
    }

    map.flyTo([value.latitude, value.longitude], Math.max(map.getZoom(), 16), { duration: 0.8 });
  }, [value, map]);

  return null;
}

export default function StopMapPicker({ value, onChange }: StopMapPickerProps) {
  const { token } = theme.useToken();
  const center: LatLngExpression = value ? [value.latitude, value.longitude] : DEFAULT_CENTER;

  const [plusCode, setPlusCode] = useState('');
  const [plusCodeError, setPlusCodeError] = useState<string | null>(null);
  const [plusCodeResult, setPlusCodeResult] = useState<string | null>(null);
  const [resolving, setResolving] = useState(false);

  const handleResolvePlusCode = async () => {
    const input = plusCode.trim();
    if (!input) {
      setPlusCodeError('Vui lòng nhập Plus Code.');
      setPlusCodeResult(null);
      return;
    }

    setResolving(true);
    setPlusCodeError(null);
    setPlusCodeResult(null);

    try {
      const { latitude, longitude } = await resolvePlusCode(input);
      // Cập nhật toạ độ — Recenter sẽ bay bản đồ tới đó và Marker hiện ngay.
      onChange?.(latitude, longitude);
      setPlusCodeResult(`Đã xác định vị trí: ${latitude.toFixed(6)}, ${longitude.toFixed(6)}.`);
    } catch (error) {
      // Giữ nguyên bản đồ + vị trí đã chọn trước đó; chỉ báo lỗi ở ô nhập.
      setPlusCodeError((error as Error).message || 'Không xác định được vị trí từ Plus Code.');
    } finally {
      setResolving(false);
    }
  };

  return (
    <div>
      <div
        style={{
          height: 320,
          borderRadius: token.borderRadius,
          overflow: 'hidden',
          border: `1px solid ${token.colorBorder}`,
        }}
      >
        <MapContainer center={center} zoom={DEFAULT_ZOOM} style={{ height: '100%', width: '100%' }}>
          <TileLayer
            attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
            url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
          />
          <ClickCapture onPick={(latitude, longitude) => onChange?.(latitude, longitude)} />
          {value && <Marker position={[value.latitude, value.longitude]} icon={pinIcon} />}
          <Recenter value={value} />
        </MapContainer>
      </div>

      {/* Nhập Plus Code — giữ nguyên bản đồ, không chuyển màn hình, không tải lại trang. */}
      <Space.Compact style={{ width: '100%', marginTop: 8 }}>
        <Input
          placeholder="Hoặc nhập Plus Code, VD: 7346+QC Vân Hà, Bắc Giang, Việt Nam"
          value={plusCode}
          status={plusCodeError ? 'error' : undefined}
          onChange={(event) => {
            setPlusCode(event.target.value);
            setPlusCodeError(null);
            setPlusCodeResult(null);
          }}
          onPressEnter={handleResolvePlusCode}
          allowClear
        />
        <Button type="primary" loading={resolving} onClick={handleResolvePlusCode}>
          Xác nhận
        </Button>
      </Space.Compact>

      {plusCodeError && (
        <Typography.Text type="danger" style={{ display: 'block', marginTop: 4 }}>
          {plusCodeError}
        </Typography.Text>
      )}
      {plusCodeResult && (
        <Typography.Text type="success" style={{ display: 'block', marginTop: 4 }}>
          {plusCodeResult}
        </Typography.Text>
      )}
    </div>
  );
}
