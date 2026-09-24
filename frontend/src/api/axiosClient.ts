import axios from 'axios';
import type { AxiosError, InternalAxiosRequestConfig } from 'axios';
import tokenStorage from './tokenStorage';

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

const BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5080/api';

const axiosClient = axios.create({
  baseURL: BASE_URL,
  headers: { 'Content-Type': 'application/json' },
  timeout: 10000,
});

// Client riêng để gọi /auth/refresh-token, KHÔNG dùng axiosClient.
// Nếu dùng chung, request refresh lúc gặp 401 sẽ lại đi qua interceptor bên dưới
// và tự gọi refresh tiếp → vòng lặp vô hạn.
const refreshClient = axios.create({
  baseURL: BASE_URL,
  headers: { 'Content-Type': 'application/json' },
  timeout: 10000,
});

// Endpoint không bao giờ thử refresh:
// - /auth/login, /auth/register: chưa có phiên để làm mới.
// - /auth/refresh-token: thất bại là hết phiên thật, thử lại cũng vô ích.
const NO_REFRESH_PATHS = ['/auth/login', '/auth/register', '/auth/refresh-token'];

interface RetriableConfig extends InternalAxiosRequestConfig {
  /** Đánh dấu request đã được chạy lại một lần — chặn vòng lặp refresh vô hạn. */
  _retry?: boolean;
}

interface RefreshedTokens {
  accessToken: string;
  refreshToken: string;
}

// ---------------------------------------------------------------------------
// Refresh token dùng MỘT LẦN: backend thu hồi token cũ ngay khi đổi (AuthService.
// RefreshTokenAsync). Nếu 5 request cùng nhận 401 mà mỗi request tự gọi refresh,
// request đầu đổi được token nhưng 4 request sau cầm token đã bị thu hồi → 401 hàng loạt.
// Vì vậy cả nhóm dùng CHUNG một promise: chỉ refresh một lần, ai cũng nhận cùng token mới.
let refreshPromise: Promise<string> | null = null;

function refreshAccessToken(): Promise<string> {
  refreshPromise ??= doRefresh().finally(() => {
    // Xong (thành công hay thất bại) thì mở lại cho lần refresh sau.
    refreshPromise = null;
  });

  return refreshPromise;
}

async function doRefresh(): Promise<string> {
  const refreshToken = tokenStorage.getRefreshToken();
  if (!refreshToken) {
    throw new Error('Không có refresh token để làm mới phiên.');
  }

  const { data } = await refreshClient.post<RefreshedTokens>('/auth/refresh-token', {
    refreshToken,
  });

  tokenStorage.save(data.accessToken, data.refreshToken);
  return data.accessToken;
}

// ---------------------------------------------------------------------------
// Phiên hết hạn hẳn (refresh token cũng hỏng) → axiosClient xoá token rồi báo ra ngoài
// để AuthContext hạ trạng thái đăng nhập. Không import trực tiếp AuthContext ở đây:
// tầng api không nên biết React, và như vậy sẽ thành import vòng.
export type SessionExpiredListener = () => void;

const sessionExpiredListeners = new Set<SessionExpiredListener>();

/** Đăng ký nhận thông báo khi phiên hết hạn. Trả về hàm huỷ đăng ký. */
export function onSessionExpired(listener: SessionExpiredListener): () => void {
  sessionExpiredListeners.add(listener);
  return () => {
    sessionExpiredListeners.delete(listener);
  };
}

function notifySessionExpired(): void {
  sessionExpiredListeners.forEach((listener) => listener());
}

// ---------------------------------------------------------------------------
// Chuẩn hoá mọi lỗi về AppError để màn hình không phải tự đoán status code.
function toAppError(error: AxiosError<ApiErrorPayload>, overrideMessage?: string): AppError {
  const status = error.response?.status;
  const data = error.response?.data;
  const backendMessage = data?.message;

  let customMessage: string;

  if (overrideMessage) {
    customMessage = overrideMessage;
  } else if (status === 400) {
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

  return appError;
}

function shouldTryRefresh(config: RetriableConfig): boolean {
  const url = config.url ?? '';

  if (NO_REFRESH_PATHS.some((path) => url.includes(path))) {
    return false;
  }

  // Không có refresh token thì thử cũng chỉ tốn một vòng request.
  return tokenStorage.getRefreshToken() !== null;
}

// Request interceptor: tự động gắn access token nếu đã đăng nhập.
axiosClient.interceptors.request.use(
  (config) => {
    const token = tokenStorage.getAccessToken();
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => Promise.reject(error),
);

// Response interceptor: thành công trả thẳng data; 401 thì thử refresh rồi chạy lại;
// thất bại chuẩn hóa lỗi.
axiosClient.interceptors.response.use(
  (response) => response.data,
  async (error: AxiosError<ApiErrorPayload>) => {
    const config = error.config as RetriableConfig | undefined;

    // Access token hết hạn (401) → đổi refresh token lấy token mới rồi chạy lại request
    // đúng MỘT lần. Người dùng không thấy gián đoạn nào.
    if (error.response?.status === 401 && config && !config._retry && shouldTryRefresh(config)) {
      config._retry = true;

      try {
        const accessToken = await refreshAccessToken();
        config.headers.Authorization = `Bearer ${accessToken}`;

        // Chạy lại qua axiosClient để response vẫn được trả thẳng data như bình thường.
        return await axiosClient.request(config);
      } catch {
        // Refresh thất bại → phiên hết hạn thật: xoá token và báo cho AuthContext.
        tokenStorage.clear();
        notifySessionExpired();

        return Promise.reject(
          toAppError(error, 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.'),
        );
      }
    }

    return Promise.reject(toAppError(error));
  },
);

export default axiosClient;
