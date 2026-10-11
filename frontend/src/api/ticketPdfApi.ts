import dayjs from 'dayjs';
import axiosClient from './axiosClient';
import type { Ticket } from './ticketApi';

// -----------------------------------------------------------------------------
// API "Tải vé PDF" — vé điện tử (story 4 "Vé điện tử QR", Sprint 3), phục vụ task
// "Component tải vé PDF và chia sẻ vé" (Hoàng Văn Thịnh).
//
// Backend ĐÃ có sẵn dịch vụ vẽ vé PDF (`ITicketPdfService` — docs/31-pdf-ve-email.md)
// nhưng CHƯA có endpoint nào trả file cho trình duyệt, và docs/api-contract.md cũng chưa
// có dòng nào, nên file này chạy dữ liệu giả (USE_MOCK_DATA = true) — cùng lối
// ticketApi.ts. Khi endpoint có, chỉ cần viết thật nhánh `api` rồi lật cờ.
//
// Hình dạng endpoint DỰ KIẾN (để Hiếu chốt vào api-contract.md — CHƯA cam kết; docs/31
// §5 giao việc này cho Hiếu + Thịnh):
//   GET /tickets/{id}/pdf
//     → 200 application/pdf, Content-Disposition: attachment
//     → 404 khi vé không tồn tại hoặc không thuộc người gọi
//   Máy chủ dùng lại đúng ITicketPdfService.GeneratePdf với TicketPdfModel —
//   KHÔNG dựng đường vẽ thứ hai (docs/31 §3.3; hai đường vẽ là hai tấm vé lệch nhau).
// -----------------------------------------------------------------------------

/** Một file PDF tải về: nội dung nhị phân + tên file gợi ý cho trình duyệt. */
export interface TicketPdfFile {
  blob: Blob;
  /** Không dấu, khớp lối đặt tên tệp đính kèm email (docs/31 §3.2): ve-dien-tu-….pdf */
  fileName: string;
}

export interface TicketPdfApi {
  /**
   * Tải file PDF của một vé. Nhận cả `ticket` chứ không chỉ `id` vì tên file suy từ mã
   * vé, và bản giả lập cần vài trường để ghi vào stub. Hỏng thì ném AppError
   * (axiosClient đã chuẩn hoá) — màn gọi chỉ việc hiện `customMessage`.
   */
  download: (ticket: Ticket) => Promise<TicketPdfFile>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — backend chưa có endpoint.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** Tên file dự phòng khi máy chủ không kèm Content-Disposition (mã vé là duy nhất toàn hệ thống). */
function fallbackFileName(code: string): string {
  return `ve-dien-tu-${code}.pdf`;
}

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: TicketPdfApi = {
  async download(ticket) {
    // Khai hai generic theo lối chung của tầng api: interceptor của axiosClient trả thẳng
    // response.data. Tên file lấy từ mã vé vì interceptor không giữ header
    // Content-Disposition; lỗi 4xx tuy trả JSON nhưng responseType 'blob' nên phần đọc
    // `message` của axiosClient sẽ không có — vẫn ra thông báo theo status, đủ dùng.
    const blob = await axiosClient.get<Blob, Blob>(`/tickets/${ticket.id}/pdf`, {
      responseType: 'blob',
    });

    return { blob, fileName: fallbackFileName(ticket.code) };
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------

/**
 * Sinh một file PDF tối giản nhưng HỢP LỆ, cố ý chỉ có chữ ASCII (font Helvetica dựng
 * sẵn của PDF không có dấu tiếng Việt), để bấm "Tải vé PDF" là mở lên xem được ngay và
 * thấy rõ đây là bản giả lập.
 *
 * KHÔNG phải đường vẽ vé thứ hai mà docs/31 §3.3 cấm: stub này không bố cục, không QR,
 * không font nhúng — chỉ đủ để component chạy đầu-cuối. Vé thật do máy chủ vẽ bằng
 * ITicketPdfService rồi trả về qua GET /tickets/{id}/pdf.
 */
function buildMockPdf(title: string, lines: string[]): Blob {
  const encoder = new TextEncoder();

  // Thoát ba ký tự đặc biệt của chuỗi văn bản trong PDF: \ ( ).
  const escape = (text: string) => text.replace(/[\\()]/g, (char) => `\\${char}`);

  const content = [
    `BT /F1 16 Tf 40 545 Td (${escape(title)}) Tj ET`,
    ...lines.map(
      (line, index) => `BT /F1 12 Tf 40 ${505 - index * 22} Td (${escape(line)}) Tj ET`,
    ),
  ].join('\n');

  // A5 dọc [0 0 420 595] — cùng khổ với vé thật (docs/31 §3.1). /Length của stream tính
  // theo BYTE (TextEncoder), không theo ký tự.
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 420 595]' +
      ' /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
    `<< /Length ${encoder.encode(content).length} >>\nstream\n${content}\nendstream`,
  ];

  // Bảng xref phải ghi offset BYTE của từng object. Nội dung ở đây cố ý toàn ASCII
  // (1 ký tự = 1 byte) nhưng vẫn đếm bằng TextEncoder cho khớp đúng thứ Blob mã hoá.
  const header = '%PDF-1.4\n';
  const offsets: number[] = [];
  let offset = encoder.encode(header).length;
  let body = header;

  objects.forEach((objectBody, index) => {
    const object = `${index + 1} 0 obj\n${objectBody}\nendobj\n`;
    offsets.push(offset);
    offset += encoder.encode(object).length;
    body += object;
  });

  // Mỗi dòng xref đúng 20 byte: "nnnnnnnnnn ggggg n \n" (10 số offset + 5 số thế hệ + cờ).
  const entries = [
    '0000000000 65535 f ',
    ...offsets.map((value) => `${String(value).padStart(10, '0')} 00000 n `),
  ];
  const xref = `xref\n0 ${objects.length + 1}\n${entries.join('\n')}\n`;
  const trailer = `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${offset}\n%%EOF\n`;

  return new Blob([body, xref, trailer], { type: 'application/pdf' });
}

const mock: TicketPdfApi = {
  async download(ticket) {
    await delay(500);

    // Chỉ dùng các trường chắc chắn ASCII: mã vé, mã tuyến, số ghế, ngày giờ, giá.
    // Tên tuyến có dấu tiếng Việt nên cố ý không đưa vào stub (Helvetica không có dấu).
    const lines = [
      `Ma ve: ${ticket.code}`,
      `Tuyen: ${ticket.routeCode}`,
      `Ghe: ${ticket.seatNumber}`,
      `Khoi hanh: ${dayjs(ticket.departureTime).format('DD/MM/YYYY HH:mm')}`,
      `Gia: ${ticket.price.toLocaleString('vi-VN')} VND`,
      '',
      'Tep gia lap - chua noi may chu that.',
      'Ve that do may chu ve bang ITicketPdfService (docs/31).',
    ];

    return {
      blob: buildMockPdf('VE DIEN TU (BAN GIA LAP)', lines),
      fileName: fallbackFileName(ticket.code),
    };
  },
};

const ticketPdfApi: TicketPdfApi = USE_MOCK_DATA ? mock : api;

export default ticketPdfApi;
