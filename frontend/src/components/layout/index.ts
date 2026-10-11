// Barrel của bộ khung app. Chỉ export component — giữ quy tắc react/only-export-components
// của oxlint khỏi kêu. Các file nội bộ (TopNav, StaffSider…) import theo đường dẫn riêng
// trong AppLayout.
export { default as AppLayout } from './AppLayout';
