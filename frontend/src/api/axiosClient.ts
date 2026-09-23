import axios from 'axios';
import type { AxiosError } from 'axios';

// Cấu trúc lỗi backend trả về — xem docs/01-kien-truc.md:
// { "message": "…", "errors": { "phoneNumber": ["…"] } }
export interface ApiErrorPayload {
  message?: string;
  errors?: Record<string, string[]>;
}

// Lỗi chuẩn hóa để mọi màn hình xử lý giống nhau:
// - customMessage: câu thông báo thân thiện, hiển thị qua toast.
// - errors: lỗi theo từng trường, để form gắn trực tiếp vào ô input tương ứng.
export interface AppError extends Error {
  status?: number;
  errors?: Record<string, string[]>;
  customMessage: string;
}

const axiosClient = axios.create({
  baseURL: import.meta.env.VITE_API_URL || 'http://localhost:5080/api',
  headers: { 'Content-Type': 'application/json' },
  timeout: 10000,
});

// Request interceptor: tự động gắn access token nếu đã đăng nhập.
axiosClient.interceptors.request.use(
  (config) => {
    const token = localStorage.getItem('access_token');
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => Promise.reject(error),
);

// Response interceptor: thành công trả thẳng data; thất bại chuẩn hóa lỗi.
axiosClient.interceptors.response.use(
  (response) => response.data,
  (error: AxiosError<ApiErrorPayload>) => {
    const status = error.response?.status;
    const data = error.response?.data;
    const backendMessage = data?.message;

    let customMessage: string;

    if (status === 400) {
      customMessage = backendMessage || 'Dữ liệu gửi lên không hợp lệ.';
    } else if (status === 401) {
      // Giữ nguyên thông báo backend (sai mật khẩu / tài khoản bị khóa / phiên hết hạn).
      customMessage = backendMessage || 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.';
    } else if (status === 403) {
      customMessage = backendMessage || 'Bạn không có quyền truy cập tính năng này.';
    } else if (status === 404) {
      customMessage = backendMessage || 'Không tìm thấy tài nguyên yêu cầu.';
    } else if (status === 409) {
      customMessage = backendMessage || 'Dữ liệu đã tồn tại.';
    } else if (status && status >= 500) {
      customMessage = 'Lỗi hệ thống máy chủ. Vui lòng thử lại sau.';
    } else if (error.request) {
      customMessage = 'Không thể kết nối đến máy chủ. Kiểm tra mạng hoặc backend.';
    } else {
      customMessage = 'Đã có lỗi xảy ra. Vui lòng thử lại!';
    }

    const appError = new Error(customMessage) as AppError;
    appError.status = status;
    appError.errors = data?.errors;
    appError.customMessage = customMessage;

    return Promise.reject(appError);
  },
);

export default axiosClient;
