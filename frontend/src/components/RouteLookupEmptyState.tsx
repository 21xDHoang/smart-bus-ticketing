import { useId } from 'react';
import { CalendarOutlined, EnvironmentOutlined, FlagOutlined } from '@ant-design/icons';
import { Col, Row, Typography, theme } from 'antd';
import { useColorMode } from './ui/colorMode';
import { palette } from './ui/theme';

const { Title, Text } = Typography;

// Bảng màu của khối "Khám phá hành trình của bạn" — lấy đúng theo bản thiết kế được duyệt
// (#1677FF chủ đạo, #174A91 tiêu đề, #F3F7FF nền). Chế độ tối dùng token của theme để không
// đặt một mảng sáng chói giữa nền navy; riêng minh họa giữ nguyên sắc xanh thương hiệu.
const SPEC = {
  primary: '#1677FF',
  primaryDeep: '#0B4FC4',
  primarySoft: '#EAF2FF',
  title: '#174A91',
  background: '#F3F7FF',
  border: '#D9E7FF',
  hintBorder: '#D6E4FF',
  body: '#475569',
} as const;

const HINTS = [
  { icon: <EnvironmentOutlined />, label: 'Chọn điểm đi' },
  { icon: <FlagOutlined />, label: 'Chọn điểm đến' },
  { icon: <CalendarOutlined />, label: 'Chọn ngày đi' },
] as const;

/**
 * Trạng thái ban đầu của trang Tra cứu tuyến — chỉ hiện TRƯỚC lần tìm đầu tiên.
 *
 * Minh họa là SVG nội tuyến (không gọi ảnh từ URL ngoài, không thêm thư viện): xe buýt xanh,
 * tuyến đường nét đứt và hai điểm định vị. Ba gợi ý bên dưới là chỉ dẫn tĩnh, không phải nút —
 * form thật nằm ngay phía trên và vẫn là hành động duy nhất trên màn.
 */
export default function RouteLookupEmptyState() {
  const { token } = theme.useToken();
  const { mode } = useColorMode();
  const dark = mode === 'dark';
  const p = palette(mode);
  const titleId = useId();

  const titleColor = dark ? p.primary.onSoft : SPEC.title;
  const bodyColor = dark ? p.neutral.textMuted : SPEC.body;
  const surface = dark ? p.neutral.surface : token.colorBgContainer;
  const hintBorder = dark ? p.primary.softBorder : SPEC.hintBorder;
  const iconBg = dark ? p.primary.soft : SPEC.primarySoft;
  const iconColor = dark ? p.primary.onSoft : SPEC.primary;

  return (
    <section
      aria-labelledby={titleId}
      style={{
        marginTop: 20,
        padding: 'clamp(20px, 3.5vw, 36px)',
        overflow: 'hidden',
        background: dark ? p.primary.soft : SPEC.background,
        border: `1px solid ${dark ? p.primary.softBorder : SPEC.border}`,
        borderRadius: token.borderRadiusLG,
      }}
    >
      <Row gutter={[32, 24]} align="middle">
        <Col xs={24} lg={13} xl={14}>
          <Title
            id={titleId}
            level={2}
            style={{
              margin: 0,
              color: titleColor,
              fontSize: 'clamp(22px, 2.4vw, 28px)',
              lineHeight: 1.25,
              letterSpacing: '-0.02em',
            }}
          >
            Khám phá hành trình của bạn
          </Title>

          <Text
            style={{
              display: 'block',
              marginTop: 10,
              color: titleColor,
              fontSize: 16,
              fontWeight: 600,
              lineHeight: 1.5,
            }}
          >
            Mỗi hành trình bắt đầu từ một điểm đến.
          </Text>

          <Text
            style={{
              display: 'block',
              marginTop: 8,
              maxWidth: 490,
              color: bodyColor,
              fontSize: 14,
              lineHeight: 1.7,
            }}
          >
            Tìm tuyến xe buýt phù hợp, xem giờ khởi hành và lựa chọn chuyến đi thuận tiện cho bạn.
          </Text>

          <div
            role="list"
            aria-label="Ba bước để tìm tuyến"
            style={{ display: 'flex', flexWrap: 'wrap', gap: 10, marginTop: 24 }}
          >
            {HINTS.map((hint) => (
              <div
                key={hint.label}
                role="listitem"
                style={{
                  display: 'flex',
                  flex: '1 1 150px',
                  alignItems: 'center',
                  gap: 9,
                  minWidth: 0,
                  padding: '9px 12px',
                  background: surface,
                  border: `1px solid ${hintBorder}`,
                  borderRadius: token.borderRadius,
                }}
              >
                <span
                  aria-hidden="true"
                  style={{
                    display: 'inline-flex',
                    flex: '0 0 auto',
                    alignItems: 'center',
                    justifyContent: 'center',
                    width: 30,
                    height: 30,
                    color: iconColor,
                    background: iconBg,
                    borderRadius: 999,
                    fontSize: 15,
                  }}
                >
                  {hint.icon}
                </span>
                <Text
                  style={{
                    color: dark ? p.neutral.text : SPEC.title,
                    fontSize: 13,
                    fontWeight: 600,
                    whiteSpace: 'nowrap',
                  }}
                >
                  {hint.label}
                </Text>
              </div>
            ))}
          </div>
        </Col>

        <Col xs={24} lg={11} xl={10}>
          <BusRouteIllustration dark={dark} />
        </Col>
      </Row>
    </section>
  );
}

/** Minh họa phẳng, gọn — cao khoảng 190–225 px ở cột desktop. */
function BusRouteIllustration({ dark }: { dark: boolean }) {
  const uid = useId().replace(/:/g, '');
  const gradientId = `route-bus-gradient-${uid}`;
  const titleId = `route-bus-title-${uid}`;

  const art = dark
    ? {
        bubble: '#2B4E86',
        route: '#8CB8FF',
        pin: '#4C9AFF',
        pinDeep: '#7FB0FF',
        road: '#2A4A7F',
        roadDash: '#8CB8FF',
        busTop: '#4C9AFF',
        busBottom: SPEC.primary,
        busDeep: '#0B3B8C',
        window: '#EAF3FF',
        stripe: '#8CC0FF',
        shadow: '#0B1B33',
        light: '#FFD166',
      }
    : {
        bubble: '#E1EEFF',
        route: '#9CC5FF',
        pin: SPEC.primary,
        pinDeep: SPEC.primaryDeep,
        road: '#D8E7FF',
        roadDash: '#FFFFFF',
        busTop: '#4C9AFF',
        busBottom: SPEC.primary,
        busDeep: SPEC.primaryDeep,
        window: '#F3F8FF',
        stripe: '#8CC0FF',
        shadow: '#B8D3FF',
        light: '#FFC94D',
      };

  return (
    <div style={{ width: '100%', maxWidth: 430, marginInline: 'auto', lineHeight: 0 }}>
      <svg
        viewBox="0 0 440 230"
        role="img"
        aria-labelledby={titleId}
        style={{ display: 'block', width: '100%', height: 'auto', overflow: 'visible' }}
      >
        <title id={titleId}>
          Xe buýt màu xanh trên tuyến đường, với điểm đi và điểm đến được đánh dấu
        </title>
        <defs>
          <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={art.busTop} />
            <stop offset="100%" stopColor={art.busBottom} />
          </linearGradient>
        </defs>

        {/* Nền trang trí */}
        <circle cx="374" cy="40" r="27" fill={art.bubble} />
        <circle cx="404" cy="72" r="11" fill={art.bubble} opacity="0.65" />
        <circle cx="52" cy="40" r="8" fill={art.bubble} />
        <circle cx="88" cy="30" r="4" fill={art.route} opacity="0.7" />

        {/* Tuyến đường nối hai điểm định vị — nằm sau xe buýt */}
        <path
          d="M68 88 C 136 148, 258 148, 372 84"
          fill="none"
          stroke={art.route}
          strokeWidth="3"
          strokeLinecap="round"
          strokeDasharray="1 12"
        />

        {/* Điểm đi */}
        <g>
          <path
            d="M68 30c-12.7 0-23 10.3-23 23 0 17.3 23 34 23 34s23-16.7 23-34c0-12.7-10.3-23-23-23z"
            fill={art.pin}
          />
          <circle cx="68" cy="53" r="8" fill="#FFFFFF" />
          <circle cx="68" cy="53" r="3.2" fill={art.pin} />
        </g>

        {/* Điểm đến */}
        <g>
          <path
            d="M372 24c-11 0-20 9-20 20 0 15 20 30 20 30s20-15 20-30c0-11-9-20-20-20z"
            fill={art.pinDeep}
          />
          <circle cx="372" cy="44" r="7" fill="#FFFFFF" />
          <circle cx="372" cy="44" r="2.8" fill={art.pinDeep} />
        </g>

        {/* Mặt đường */}
        <path d="M16 188H424" stroke={art.road} strokeWidth="30" strokeLinecap="round" />
        <path
          d="M24 188H416"
          stroke={art.roadDash}
          strokeWidth="2.5"
          strokeLinecap="round"
          strokeDasharray="20 16"
        />

        {/* Xe buýt */}
        <g transform="translate(112 96)">
          <ellipse cx="108" cy="94" rx="104" ry="8" fill={art.shadow} opacity="0.55" />
          <path
            d="M-16 27h26M-23 40h16"
            stroke={art.route}
            strokeWidth="4"
            strokeLinecap="round"
            opacity="0.8"
          />

          <rect x="0" y="0" width="216" height="82" rx="18" fill={`url(#${gradientId})`} />
          <rect x="30" y="-7" width="64" height="10" rx="5" fill={art.busDeep} opacity="0.72" />
          <rect x="103" y="-7" width="34" height="10" rx="5" fill={art.busDeep} opacity="0.5" />

          {/* Dải cửa sổ */}
          <rect x="16" y="13" width="168" height="27" rx="8" fill={art.window} />
          <rect x="56" y="13" width="5" height="27" fill={art.busBottom} />
          <rect x="96" y="13" width="5" height="27" fill={art.busBottom} />
          <rect x="136" y="13" width="5" height="27" fill={art.busBottom} />
          <path d="M24 18h38" stroke="#FFFFFF" strokeWidth="3" strokeLinecap="round" opacity="0.85" />

          {/* Kính chắn gió, đèn và cửa */}
          <rect x="188" y="13" width="18" height="27" rx="6" fill={art.window} />
          <rect x="200" y="54" width="8" height="7" rx="3.5" fill={art.light} />
          <rect x="8" y="54" width="7" height="6" rx="3" fill="#FF8A8A" opacity="0.9" />
          <rect x="14" y="49" width="188" height="5" rx="2.5" fill={art.stripe} opacity="0.85" />

          <rect x="150" y="48" width="25" height="34" rx="7" fill={art.busDeep} />
          <rect x="155" y="53" width="15" height="13" rx="4" fill={art.window} />
          <rect x="164" y="72" width="4" height="5" rx="2" fill={art.window} opacity="0.75" />

          {/* Biển số tuyến */}
          <rect x="24" y="57" width="30" height="16" rx="5" fill="#FFFFFF" opacity="0.95" />
          <text
            x="39"
            y="69"
            textAnchor="middle"
            fontSize="9"
            fontWeight="700"
            fill={art.pinDeep}
            style={{ fontFamily: 'inherit' }}
          >
            01
          </text>

          <rect x="214" y="24" width="7" height="11" rx="3" fill={art.busDeep} opacity="0.85" />

          {/* Bánh xe */}
          <circle cx="52" cy="86" r="19" fill={art.busDeep} />
          <circle cx="52" cy="86" r="9.5" fill={art.window} />
          <circle cx="52" cy="86" r="4.5" fill={art.busDeep} />
          <circle cx="168" cy="86" r="19" fill={art.busDeep} />
          <circle cx="168" cy="86" r="9.5" fill={art.window} />
          <circle cx="168" cy="86" r="4.5" fill={art.busDeep} />
        </g>
      </svg>
    </div>
  );
}
