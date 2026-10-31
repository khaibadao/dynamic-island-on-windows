Bản chỉnh sửa từ [sadeeshasathsara/dynamic-island-on-windows](https://github.com/sadeeshasathsara/dynamic-island-on-windows) (MIT).

### Sửa trong v1.4.1
- **Tai thỏ không còn che tab trình duyệt**: lúc bình thường chỉ là một **vạch mỏng 4px** ở mép trên, mang màu chủ đạo của bài hát kèm quầng sáng (xanh lá khi đang trồng cây). Đưa chuột chạm vạch → bung thành viên tai thỏ (vẫn giữ viền + quầng sáng theo màu nhạc); di tiếp vào viên → mở bảng tab. Đổi bài thì viên hiện vài giây rồi thu lại thành vạch
- **Sửa màu bị trắng** với ảnh bìa màu nhạt (xám xanh, be…): nay vẫn lấy được màu và làm rực lên

### Mới trong v1.4.0
- **Tai thỏ chia tab**: **Nhạc · Trồng cây · Thông báo**, giờ & pin ở góc phải. Bảng rộng hơn, thấp hơn; mở lại đúng tab dùng lần cuối (đang trồng cây thì mở thẳng tab Trồng cây)
- **Viền và quầng sáng nhẹ theo màu ảnh bìa** cho tai thỏ nổi bật trên nền tối; thanh tua cũng đổi theo màu nhạc
- **Sóng nhạc dài hơn, màu rực hơn**, nằm bên phải tai thỏ (ảnh bìa bên trái) cho cân đối
- **Tên bài hát dài tự chạy chữ** từ phải qua trái
- **6 loại cây**: Cây xanh, Thông, Anh đào; mở khoá **Xương rồng** (3 cây), **Cây cam** (6 cây), **Tre** (10 cây). Khu rừng mỗi ngày hiện đúng loại cây đã trồng
- **Lịch sử thông báo được lưu lại** sau khi khởi động lại (chỉ lưu trên máy, trong `%LOCALAPPDATA%\DynamicIslandNotifier\history.json`; xoá bằng menu khay → Recent notifications → Xoá lịch sử)

### Mới trong v1.3.0
- **Tai thỏ** ở giữa mép trên màn hình (giống iPhone): lúc bình thường là một viên đen nhỏ, có ảnh bìa + sóng nhạc khi đang phát nhạc và cây + thời gian còn lại khi đang trồng cây. **Di chuột vào** để mở bảng điều khiển: giờ & pin, trình phát nhạc, trồng cây, 3 thông báo gần đây. Tự ẩn khi xem phim / chơi game toàn màn hình. Bật/tắt ở menu khay hệ thống → **Notch bar**
- **Trồng cây tập trung** (kiểu Forest): chọn 10 / 25 / 45 / 60 / 90 phút, cây lớn dần theo thời gian; bỏ cuộc thì cây chết. Mỗi ngày có một khu rừng nhỏ và thống kê số cây, số phút
- **Trình phát nhạc đổi màu theo ảnh bìa** bài đang phát, và nay nằm gọn trong tai thỏ (thay cho viên kéo thả)
- **Thanh âm lượng** hiện trên tai thỏ khi bạn chỉnh âm lượng

### Sửa lỗi trong v1.2.1
- Sửa lỗi **thông báo lúc hiện lúc không**: khi thông báo mới đến đúng lúc đảo đang thu lại, hiệu ứng thu lại cũ kéo luôn thông báo mới đi, và đảo có thể bị kẹt không hiện nữa cho tới khi khởi động lại app
- Tin nhắn mới trong cùng một cuộc trò chuyện (Messenger…) không còn bị bỏ qua
- Một thông báo lỗi không còn làm mất các thông báo đến cùng lúc
- Không còn tắt toàn bộ thông báo Windows khi app chạy (trước đây nếu app bị tắt ngang, thông báo Windows có thể bị tắt luôn)

### Có từ v1.2.0
- **Nút Mute hoạt động thật**: tắt thông báo theo từng app / từng trang web. Bật lại ở menu khay hệ thống → **Muted**
- **Đảo báo pin**: hiện khi cắm / rút sạc, cảnh báo khi pin còn 20% và 10% (bấm vào để mở cài đặt pin)
- **Lịch sử thông báo**: menu khay hệ thống → **Recent notifications** (15 thông báo gần nhất, bấm để mở lại)
- File tải về nhẹ hơn nhiều (đã nén)

### Đã có từ v1.1.0
- Thông báo web (Messenger, Claude, Gmail, Zalo…) hiện đúng tên + logo của trang
- Di chuột vào đảo để xem toàn bộ nội dung; bấm vào để mở đúng tab đã gửi thông báo
- Trình phát nhạc (Spotify, YouTube…): viên nhỏ có sóng nhạc, kéo đặt ở đâu cũng được; di chuột vào để có thanh tua, ⏮ ⏯ ⏭ và âm lượng riêng cho app đang phát
- Tự chạy khi bật máy (tắt được ở menu khay hệ thống)

### Tải về
- **DynamicIslandNotifier-x64.exe**: hầu hết máy tính (Intel / AMD)
- **DynamicIslandNotifier-arm64.exe**: máy chip Snapdragon

### Cài đặt
1. Tải file `.exe` phù hợp và chạy (không cần cài .NET)
2. Nếu Windows báo *"Windows protected your PC"* → **More info** → **Run anyway**
3. Cho phép đọc thông báo: **Settings → Privacy & Security → Notifications**
4. Để thông báo web lên đảo: **Settings → System → Notifications → Microsoft Edge** → tick **Show notifications in notification center**

Yêu cầu Windows 11 (build 22621 trở lên).
