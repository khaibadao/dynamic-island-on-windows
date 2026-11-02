Bản chỉnh sửa từ [sadeeshasathsara/dynamic-island-on-windows](https://github.com/sadeeshasathsara/dynamic-island-on-windows) (MIT).

### Mới trong v1.7.0
- **Viên "đang phát" mảnh** (cao 22px) khi nghe nhạc: ảnh bìa · tên bài · sóng nhạc, kèm **vạch tiến độ** mảnh ở mép dưới. **Bấm xuyên qua được** xuống tab trình duyệt bên dưới; rê chuột lại gần thì viên **mờ đi**, để chuột nằm yên trên viên **~0,4 giây** thì bảng mới mở. Không có gì đang chạy thì vẫn là vạch \\\_\_\_\_\_/ như cũ
- **Thanh tua dạng sóng**: phần đã phát là làn sóng chạy theo màu bài hát, phần còn lại là vạch thẳng; tạm dừng thì sóng phẳng lại. Kéo để tua
- **Lời bài hát** (nút mới cạnh ⏮ ⏯ ⏭): bảng tách đôi như Spotify, trình phát bên trái, lời chạy theo nhạc bên phải; dòng đang hát sáng lên và tự cuộn; **bấm vào một dòng để tua tới đó**. Lời lấy từ LRCLIB (chỉ gửi tên bài, ca sĩ, độ dài)
- **Mèo Mây** 🐱 ngồi cạnh tai thỏ (và trong bảng): nhảy theo nhạc, ngồi canh cây khi tập trung, nhảy cẫng lên khi xong hết việc cần làm hoặc trồng xong cây, buồn khi bỏ cuộc, ngủ khi máy để yên 10 phút. Mỗi cây trồng xong là một phần ăn, Mây lớn dần. Bấm vào Mây trong bảng để vuốt ve
- **Lịch sử Clipboard** (tab mới): **30 mục gần nhất, cả chữ và ảnh**. Chữ xếp ở trên, **ảnh nằm thành hàng ngang ở dưới** (lăn chuột để cuộn). Bấm một mục để chép lại rồi Ctrl + V. **Ghim** mục quan trọng để không bị xoá. Chỉ lưu trên máy (`%LOCALAPPDATA%\DynamicIslandNotifier\clipboard`); bỏ qua những gì trình quản lý mật khẩu đánh dấu riêng tư
- **Tab chỉ còn biểu tượng**, tab đang chọn mới hiện tên (rê chuột để xem tên)
- **Mở / đóng mượt hơn**: cửa sổ không còn đổi kích thước khi mở, viên biến hình thành bảng; quầng sáng tạm tắt trong lúc chuyển động

### Mới trong v1.6.0
- **Việc cần làm** (tab mới trên tai thỏ): gõ việc rồi Enter; ghi giờ trong câu để đặt hạn (vd. "Nộp lab OSG **17:00**", "họp nhóm **9h30**"). Tick để hoàn thành, rê chuột để xoá. Huy hiệu cam trên tab cho biết còn bao nhiêu việc
- **Nhắc nhở liên tục**: chưa có việc nào → hỏi "Hôm nay bạn sẽ làm những gì?" **mỗi 30 phút**; còn việc chưa xong → nhắc **mỗi 60 phút**, thêm nhắc **trước hạn 15 phút** và **khi quá hạn**. Nhắc vẫn hiện khi bật Không làm phiền. Bấm vào nhắc → mở thẳng tab Việc cần làm. Việc chưa xong tự chuyển sang hôm sau
- **Nút nhanh** chuyển thành **cột dọc bên phải** bảng, thanh tab gọn hơn
- **Hiệu ứng mở/đóng mềm mại**: bảng phóng ra từ tai thỏ với độ nảy nhẹ, thu lại mượt; chuyển tab có hiệu ứng mờ dần
- **Xoá thông báo**: nút **Xoá tất cả**, hoặc rê chuột vào từng thông báo → **×**
- Sửa **pin tai nghe hiện "+18"**: nay chỉ tính tai nghe đang kết nối, mỗi thiết bị đếm một lần

### Mới trong v1.5.0
- **Nút nhanh** trên thanh tab của tai thỏ: **tắt/bật micro** (đỏ khi đang tắt), **Không làm phiền** (thông báo không trượt xuống nhưng vẫn vào lịch sử; vạch tai thỏ có viền tím khi đang bật), **chụp màn hình**, **khoá máy**
- **Pin tai nghe Bluetooth** hiện trên thanh tab; đảo báo khi tai nghe vừa kết nối và khi pin còn ≤ 20%. (Chỉ với tai nghe báo pin cho Windows, ví dụ hiện % trong Settings → Bluetooth; AirPods thường không báo)
- **Thống kê trồng cây theo tuần**: tab Trồng cây → **Thống kê tuần ›**: biểu đồ số phút tập trung 7 ngày qua, tổng số cây và **chuỗi ngày liên tiếp**
- Giờ trên thanh tab gọn lại (rê chuột vào để xem thứ, ngày); bảng rộng thêm một chút cho đủ chỗ

### Sửa trong v1.4.3
- **Vạch tai thỏ màu đen** hoà vào thanh tiêu đề tối, màu bài hát chỉ còn ở **viền mảnh + quầng sáng** (đỡ chói, không lạc quẻ)
- **Chạm vạch là mở bảng ngay** (bỏ bước viên trung gian phải chờ ~1 giây)
- **Thu lại nhanh hơn nhiều** khi rời chuột: chỉ tính đúng phần bảng nhìn thấy, chờ 0,25 giây thay vì có khi gần 2 giây

### Sửa trong v1.4.2
- Vạch tai thỏ dày hơn (6px) và có dạng hình thang **\\\_\_\_\_\_\_/** ôm sát mép trên màn hình, góc dưới bo mềm

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
