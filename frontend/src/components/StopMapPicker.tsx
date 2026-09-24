import { MapContainer, Marker, TileLayer, useMapEvents } from 'react-leaflet';
import { divIcon } from 'leaflet';
import type { LatLngExpression, LeafletMouseEvent } from 'leaflet';
import 'leaflet/dist/leaflet.css';

// Toạ độ mặc định: trung tâm Hà Nội — dùng khi thêm mới (chưa có toạ độ nào).
const DEFAULT_CENTER: LatLngExpression = [21.0285, 105.8542];
const DEFAULT_ZOOM = 13;

// Marker tự vẽ (SVG) để tránh lỗi "icon vỡ" của Leaflet khi đóng gói bằng Vite:
// ảnh marker mặc định không được bundle nên hiển thị trống. Dùng divIcon + SVG nội tuyến.
const pinIcon = divIcon({
  className: '', // bỏ class mặc định để không bị khung trắng/viền xung quanh
  iconSize: [36, 36],
  iconAnchor: [18, 36], // mũi kim nằm đúng toạ độ đã chọn
  html: `<svg xmlns="http://www.w3.org/2000/svg" width="36" height="36" viewBox="0 0 24 24">
    <path fill="#4361ee" d="M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7z"/>
    <circle cx="12" cy="9" r="2.5" fill="#fff"/>
  </svg>`,
});

interface StopMapPickerProps {
  /** Toạ độ đang chọn (null = chưa chọn). */
  value?: { latitude: number; longitude: number } | null;
  /** Gọi khi người dùng bấm vào bản đồ để chọn toạ độ mới. */
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

export default function StopMapPicker({ value, onChange }: StopMapPickerProps) {
  const center: LatLngExpression = value ? [value.latitude, value.longitude] : DEFAULT_CENTER;

  return (
    <div
      style={{
        height: 320,
        borderRadius: 12,
        overflow: 'hidden',
        border: '1px solid #e5e7eb',
      }}
    >
      <MapContainer center={center} zoom={DEFAULT_ZOOM} style={{ height: '100%', width: '100%' }}>
        <TileLayer
          attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
          url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
        />
        <ClickCapture onPick={(latitude, longitude) => onChange?.(latitude, longitude)} />
        {value && <Marker position={[value.latitude, value.longitude]} icon={pinIcon} />}
      </MapContainer>
    </div>
  );
}
