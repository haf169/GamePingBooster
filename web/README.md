# Game Ping Booster — Web Landing & Facebook Tracking

Website Frontend giới thiệu và phân phối file cài đặt **Game Ping Booster**, được thiết kế theo chuẩn phong cách **Minimalist Monochrome** (Đơn sắc tối giản, typography Serif đối lập mạnh, 0px border-radius, pure black & white).

---

## 1. Triển khai lên Vercel (Deploy to Vercel)

Bạn có thể deploy dự án lên Vercel bằng 1 trong 2 cách sau:

### Cách 1: Liên kết qua GitHub (Đơn giản nhất, khuyên dùng)
1. Push mã nguồn lên GitHub của bạn.
2. Đăng nhập [vercel.com](https://vercel.com) ➔ Chọn **Add New Project**.
3. Chọn Repository của bạn.
4. Ở phần **Root Directory**, bấm **Edit** và chọn thư mục `web`.
5. Bấm **Deploy**. Vercel sẽ tự động tạo domain miễn phí (ví dụ: `game-ping-booster.vercel.app`).

### Cách 2: Deploy bằng Vercel CLI
Mở terminal trong thư mục `web` và chạy:
```bash
cd "e:\ĐỒ ÁN\GamePingBooster-Custom\web"
npx vercel
```
Làm theo hướng dẫn trên màn hình để deploy ngay trong 30 giây.

---

## 2. Cách chia sẻ link lên Facebook để theo dõi khách (Tracking)

Khi đăng bài lên Facebook (Fanpage, Group, trang cá nhân hoặc chạy Ads), hãy dùng định dạng link sau:

```
https://<domain-vercel-cua-ban>/?utm_source=facebook&utm_medium=post&utm_campaign=gpb_share
```

* **Facebook tự động đính kèm `fbclid`**: Khi người dùng click vào link của bạn trên Facebook, Facebook sẽ tự gắn mã định danh `fbclid` (ví dụ: `?fbclid=IwAR...`).
* **Hệ thống tự động bắt**:
  - Địa chỉ IP & vị trí địa lý (Quốc gia, Thành phố qua Vercel Geo-Headers).
  - Loại thiết bị (Điện thoại hay Máy tính), Hệ điều hành (Windows, iOS, Android).
  - Tỷ lệ người vào web có bấm tải file setup hay không.

---

## 3. Xem bảng thống kê (Admin Dashboard)

Truy cập đường dẫn:
```
https://<domain-vercel-cua-ban>/admin.html
```
(Hoặc bấm vào liên kết **Bảng theo dõi Traffic (Admin)** ở chân trang web).

Tại đây bạn sẽ thấy:
* **Tổng lượt xem trang** (Total Visits).
* **Khách đến từ Facebook** (Facebook Visits).
* **Số lượt bấm tải file Setup** (Total Downloads).
* **Tỷ lệ chuyển đổi** (Conversion Rate).
* **Bảng lịch sử chi tiết từng lượt vào** kèm thời gian, thiết bị, vị trí và mã `fbclid`.

---

## 4. (Tùy chọn) Bật thông báo tức thì về Telegram khi có người click

Nếu bạn muốn điện thoại nhận thông báo "Ting ting" ngay lập tức khi có người từ Facebook bấm vào link hoặc tải file:
1. Tạo 1 con bot Telegram qua `@BotFather` ➔ lấy `BOT_TOKEN`.
2. Lấy Chat ID của bạn qua `@userinfobot` ➔ lấy `CHAT_ID`.
3. Vào Vercel Dashboard ➔ Settings ➔ **Environment Variables** ➔ Thêm 2 biến:
   - `TELEGRAM_BOT_TOKEN`
   - `TELEGRAM_CHAT_ID`
4. Xong! Mỗi khi có khách click từ Facebook hoặc tải file, bot sẽ tự động báo ngay về điện thoại cho bạn.
