# 🐝 BEE DPI

<p align="center">
  <strong>A simpler way to connect to Steam on Windows.</strong>
  <br>
  Công cụ hỗ trợ kết nối Steam dành cho Windows — đơn giản, trực quan và chỉ với một thao tác.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%20x64-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="Windows x64">
  <img src="https://img.shields.io/badge/Developer-BirenDev-DAA520?style=for-the-badge" alt="Developed by BirenDev">
  <img src="https://img.shields.io/badge/Category-Network%20Utility-555555?style=for-the-badge" alt="Network Utility">
</p>

---

## 🐝 Giới thiệu

**BEE DPI** là tiện ích mạng dành cho Windows, được phát triển bởi **BirenDev**, hướng đến người dùng gặp khó khăn khi truy cập hoặc kết nối với Steam do một số cơ chế kiểm tra và xử lý lưu lượng mạng DPI (*Deep Packet Inspection*).

Thay vì phải thao tác với những dòng lệnh phức tạp, BEE DPI cung cấp giao diện trực quan, giúp người dùng khởi chạy hoặc dừng tiến trình xử lý lưu lượng mạng chỉ bằng một nút bấm.

**Không cần cấu hình dòng lệnh. Không cần máy chủ VPN trung gian. Không cần thao tác phức tạp.**

## ✨ Tính năng nổi bật

| Tính năng | Mô tả |
|---|---|
| One-click Connection | Khởi chạy và dừng kết nối nhanh chóng chỉ với một thao tác |
| DNS Manager | Quản lý và thay đổi DNS thông qua giao diện riêng |
| System Tray | Hỗ trợ thu nhỏ ứng dụng xuống khay hệ thống |
| Minimal Interface | Giao diện tối giản, tập trung vào trải nghiệm sử dụng |
| Self-contained | Gói Windows x64 đi kèm môi trường chạy cần thiết |

## 🚀 Tải xuống

<p align="center">
  <a href="https://github.com/phatptgaming/BEE-DPI/releases">
    <img src="https://img.shields.io/badge/⬇%20DOWNLOAD-BEE%20DPI-DAA520?style=for-the-badge" alt="Download BEE DPI">
  </a>
</p>

Truy cập trang **Releases** để tải phiên bản Windows x64 mới nhất.

**Hướng dẫn cài đặt:**

1. Tải gói `BEE-DPI-Windows-x64.zip` tại mục Releases.
2. Giải nén toàn bộ nội dung vào một thư mục trên máy tính.
3. Mở ứng dụng BEE DPI.
4. Cấp quyền Administrator khi Windows yêu cầu.
5. Nhấn **Connect** để khởi chạy hoặc **Disconnect** để dừng.

> Không chạy trực tiếp ứng dụng bên trong file ZIP. Hãy giải nén đầy đủ các thành phần trước khi sử dụng.

## 🖥️ Yêu cầu hệ thống

- Hệ điều hành Windows 64-bit.
- Quyền Administrator khi cần khởi chạy thành phần xử lý mạng.
- Kết nối Internet.

Gói phát hành được đóng gói theo dạng self-contained, vì vậy người dùng không cần tự cài đặt riêng .NET Runtime.

## 🔍 BEE DPI có phải VPN không?

**Không. BEE DPI không phải VPN.**

BEE DPI xử lý lưu lượng mạng trực tiếp trên thiết bị Windows thông qua các thành phần mạng đi kèm. Ứng dụng không tạo đường hầm VPN đến máy chủ từ xa, không cung cấp dịch vụ proxy từ xa và không có chức năng ẩn hoặc thay đổi địa chỉ IP công cộng.

Hiệu quả hoạt động phụ thuộc vào nhà mạng, cấu hình mạng, khu vực và tình trạng dịch vụ tại thời điểm sử dụng. BEE DPI không đảm bảo khắc phục được mọi trường hợp mất kết nối hoặc bị hạn chế truy cập.

## 🔐 Quyền riêng tư và dữ liệu

BEE DPI được thiết kế để hoạt động trực tiếp trên thiết bị của người dùng.

Một số thông tin cần biết:

- Khi engine đang hoạt động, ứng dụng thực hiện các yêu cầu HTTPS GET ẩn danh đến một số trang Steam công khai để kiểm tra khả năng kết nối.
- Các yêu cầu kiểm tra này không sử dụng thông tin đăng nhập, cookie hoặc dữ liệu tài khoản Steam của người dùng.
- Ứng dụng không tự ý thay đổi DNS của card mạng. Việc áp dụng DNS thông qua DNS Manager chỉ diễn ra khi người dùng chủ động thực hiện.
- Nhật ký hoạt động cục bộ được lưu tại `%LOCALAPPDATA%\BeeDPI\logs`.

## ⚙️ Thành phần bên thứ ba

BEE DPI tích hợp các thành phần mã nguồn mở phục vụ xử lý lưu lượng mạng, bao gồm:

- **GoodbyeDPI** — công cụ xử lý các cơ chế Deep Packet Inspection.
- **WinDivert** — thư viện hỗ trợ tương tác và xử lý lưu lượng mạng trên Windows.

BEE DPI là sản phẩm độc lập được thiết kế và phát triển bởi **BirenDev**. Các thành phần bên thứ ba vẫn thuộc quyền tác giả và giấy phép tương ứng của dự án gốc.

- [GoodbyeDPI – GitHub](https://github.com/ValdikSS/GoodbyeDPI)
- [WinDivert – GitHub](https://github.com/basil00/Divert)

Vui lòng giữ nguyên các thông báo bản quyền và giấy phép đi kèm khi phân phối lại những thành phần này.

## 🛠️ Mã nguồn

Mã nguồn của dự án được công khai tại repository này:

[**BEE DPI – Source Code**](https://github.com/phatptgaming/BEE-DPI/tree/main/BEE_DPI_Source_1.2.1_BIRENDEV)

## 💬 Báo lỗi và đóng góp

Trong quá trình sử dụng, nếu gặp lỗi hoặc có đề xuất cải thiện, bạn có thể tạo Issue trực tiếp tại GitHub:

[**Report an Issue**](https://github.com/phatptgaming/BEE-DPI/issues)

Mọi phản hồi đều là động lực để BirenDev tiếp tục hoàn thiện sản phẩm.

---

<p align="center">
  <strong>🐝 BEE DPI</strong>
  <br>
  Designed & Developed by <strong>BirenDev</strong>
  <br>
  <sub>A small tool. A simpler connection.</sub>
</p>
