Bản chỉnh sửa từ [sadeeshasathsara/dynamic-island-on-windows](https://github.com/sadeeshasathsara/dynamic-island-on-windows) (MIT).

### Mới trong v2.7.4
- **Kênh thử nghiệm (Beta)**: Cài đặt & quyền riêng tư → "Nhận bản thử nghiệm (Beta)". Bản đang thử được đăng dạng Pre-release trên GitHub; ai không bật mục này sẽ chỉ nhận bản chính thức như trước
- Dọn trang Releases: chỉ giữ bản v2.6.2 và bản mới nhất; các bản beta cũ tự được dọn, chỉ giữ 3 bản gần nhất
- Gỡ phần code không còn dùng

### Mới trong v2.7.3
- **Lịch học kiểu Calendar**: giờ bên trái, mỗi buổi một khối màu theo loại (xanh dương: trên lớp · cam: học thêm có thầy / cô · tím: tự học, Luyện…, BTVN… · xanh lá: đi dạy), vạch đỏ "bây giờ", buổi đã qua mờ đi, buổi đang học ghi "còn … phút", buổi kế tiếp ghi "sau …". Dòng đầu tóm tắt: số buổi, còn mấy buổi, tiếp theo là gì. Mở tab là tự cuộn tới chỗ "bây giờ"

### Mới trong v2.7.2
- **Nhẹ khi gọi video**: lúc camera đang bật (Meet, Zoom, Teams, Messenger…), nốt nhạc dừng trôi, mèo và mắt đứng yên để cuộc gọi mượt; tắt camera là chạy lại như cũ. App chỉ xem Windows ghi là camera có đang được dùng không (như đèn báo camera), không mở camera. Tắt được trong Cài đặt & quyền riêng tư

### Sửa trong v2.7.2
- **Đọc được thời khoá biểu dạng lưới** trên Google Sheet: cột giờ ở bất kỳ đâu ("7h-7h45", "6h45", "19h-20h30 (1h30)", "22h30-0h"), các thứ nằm ngang, môn ghi bằng chữ (Văn, Toán…); ô tự ghi giờ ("17h30-19h: Toán thầy Lư") lấy giờ của ô; bỏ qua "Giải lao", "Vào lớp", "Dậy"; hai tiết liền nhau cùng môn gộp làm một

### Sửa trong v2.7.1
- **Hết giật khi mở bảng**: nút đổi loa không còn hỏi Windows danh sách loa / tai nghe mỗi lần mở bảng (chỉ hỏi lúc bấm, và hỏi ngầm); trạng thái micro cũng được hỏi ngầm. Với tai nghe Bluetooth, các câu hỏi này có thể làm đứng giao diện cả trăm mili giây
- Tab Trồng cây không vẽ lại các nút Tự do / Pomodoro mỗi lần chuyển tab
- **Máy đo khựng**: mỗi lần giao diện bị đứng quá 150 ms, startup.log ghi lại bao lâu và lúc đó đang làm gì (không ghi nội dung gì của bạn), để tìm đúng chỗ còn lag

### Mới trong v2.7.0
- **📌 Giữ im Dynamic**: nút ghim ở hàng nút trên cùng của bảng (hoặc menu khay). Ghim rồi thì bảng không tự đóng khi chuột rời đi, chép rồi dán bao nhiêu lần cũng được. Bấm ghim lần nữa (hoặc Esc khi chuột trên bảng) để thả
- **Kéo thả từ tab Copy**: kéo chữ hoặc ảnh ra Messenger, Word, thư mục… (thả vào thư mục ra file .png)
- **Pomodoro trong Trồng cây**: chọn Tự do hoặc Pomodoro với 4 mốc (25·5, 45·10, 50·10, 90·20) hoặc tự chỉnh; 4 phiên, nghỉ dài sau phiên cuối, mỗi phiên học xong là một cây và được tính vào giờ mở khoá cây
- **Đếm ngược ngày thi**: hạn chót bắt đầu bằng "Thi…" (vd *Thi PE PRO192 20/11 7h30*) được đếm theo ngày, mỗi sáng báo một lần; từ 3 ngày trước chuyển cam, ngày thi chuyển đỏ
- **Ghi chú nhanh Ctrl + Alt + N** từ bất cứ đâu: có giờ thì thành hạn chót, không có giờ thì lưu ghi chú (tab Hạn chót & ghi chú)
- **Lịch học** (tab mới): dán link Google Sheet (cột Thứ · Bắt đầu · Kết thúc · Môn · Phòng), chọn file .xlsx / .csv / .ics, hoặc dán / kéo ảnh chụp lịch vào (đọc chữ ngay trên máy). Nhắc trước 15 phút, viên hiện môn đang học và số phút còn lại, 20h báo lịch ngày mai
- **Đổi loa ↔ tai nghe** một chạm ở hàng nút trên cùng của bảng
- **Mèo Mây có cảm xúc**: phấn khích khi mở khoá cây mới, tò mò khi có tin nhắn, lo lắng khi sắp tới hạn / ngày thi, đọc sách trong phiên Pomodoro, vươn vai giờ nghỉ, ngáp khi đã khuya
- **Tổng kết tuần** tối Chủ nhật: giờ tập trung, số cây, chuỗi ngày, ngày chăm nhất
- Mắt trên viên nhìn theo chuột từ xa hơn (thêm khoảng 1/5 màn hình)

### Sửa trong v2.6.2
- **Banner Windows không còn hiện trùng với tai thỏ** sau mỗi lần khởi động lại hay cập nhật: banner được giữ ẩn suốt khi app còn cài. Trả lại banner: bỏ tích **Ẩn banner Windows trùng tai thỏ** ở menu khay (hoặc trong Cài đặt), hoặc gỡ app bằng bộ cài. Cài bằng lệnh PowerShell thì nhớ bỏ tích mục này trước khi xoá app

### Sửa trong v2.6.1
- Bỏ dải sáng lướt ngang qua bảng nhạc

### Mới trong v2.6.0
- **60 khung hình/giây cho mọi thứ** (mượt như trước), vẫn giữ các sửa chữa chống khựng của v2.5.0
- **Loang màu đẹp hơn**: màu bài hát rực và trong hơn; 9 nốt nhạc nhiều kiểu (♪ ♫ ♬ ♩ ♯ ♭, khoá sol, khoá fa) cùng vài đốm sáng lấp lánh

### Sửa trong v2.5.0
- **Mượt hơn nhiều**: bỏ các chỗ bắt cả khung tai thỏ vẽ lại liên tục (thanh tua nhạc, mắt, việc đếm khung hình của nốt nhạc trôi chạy theo từng khung hình màn hình); mọi hoạt ảnh giới hạn 30 khung hình/giây; sóng nhạc và nốt nhạc trôi nhẹ hơn; đọc thông báo nhẹ hơn
- **Tự mở ngay khi đăng nhập**: thêm một tác vụ "khi đăng nhập" trong Task Scheduler (không cần quyền admin), không phải xếp hàng sau các app khởi động khác như trước (có khi hơn 10 phút). Tắt "Start with Windows" hoặc tắt trong Task Manager là bỏ cả hai
- **Bỏ 5 thú cưng mới**, chỉ giữ mèo; menu khay có **Hiện mèo** để bật / tắt hẳn mèo (tắt thì không tốn gì)

### Mới trong v2.4.0
- **12 cây vẽ lại** cho bớt "nhựa": tán nhiều chùm lá có sáng tối, thân có cành và vân gỗ, thông tuyết dáng thon nhiều tầng phủ sương
- **Mở khoá cây theo giờ tập trung** (3 cây có sẵn, rồi 4 · 10 · 20 · 30 · 50 · 80 · 120 · 180 · 240 giờ)
- **Tai nghe Bluetooth hiện ngay trên viên**: viên giãn ra vài giây với tên, số tai đang kết nối (nếu Windows cho biết) và pin
- **Hạn chót**: bấm vào một hạn để **sửa**; khoảng mỗi giờ (8h–22h) có một thông báo tóm tắt 2–3 hạn gần nhất
- **Bộ cài** `DynamicIslandSetup-x64.exe` (không cần quyền admin, gỡ trong Settings → Apps; gỡ là trả lại banner thông báo Windows)
- Mảnh đất trồng cây màu tối hơn để cây nổi bật

### Sửa trong v2.4.0
- **Viên không còn che tab trình duyệt**: chỉ khi đưa chuột lên **sát mép trên màn hình** mới mở bảng; chuột ở phần dưới của viên thì viên gần như trong suốt và **bấm xuyên qua** xuống tab bên dưới
- **Thông báo đến dồn dập không còn bị mất**: mỗi thông báo được hiện đủ 2 giây, lần lượt; tin mới cùng người gửi thì cập nhật luôn. Messenger gửi tin mới thay tin cũ cũng được nhận ra; thông báo đọc lỗi chi tiết vẫn được hiện
- **Banner Windows không còn hiện trùng** sau khi khởi động lại máy
- **Tên hạn chót gọn**: "Sáng mai đi cà phê 7h" → "Đi cà phê"; "chiều/tối" cũng tính đúng giờ chiều
- **Đặt đúng giữa mép trên** ở mọi mức Scale (125%, 150%…) và khi có nhiều màn hình
- Nốt nhạc vẫn trôi khi Tiết kiệm pin hoặc khi Windows tắt hiệu ứng hoạt ảnh (có công tắc riêng nếu muốn dừng)
- Không còn hộp thoại chặn màn hình khi chưa có quyền đọc thông báo

### Mới trong v2.3.0
- **Cài đặt & quyền riêng tư** (menu khay): ghi rõ app xem gì trên máy và gửi gì ra mạng, mỗi thứ có công tắc riêng: đọc tên cửa sổ (Deep focus, thuyết trình), nghe âm thanh loa cho sóng nhạc, ẩn banner Windows trùng tai thỏ, nhắc hạn chót cả khi Không làm phiền, biểu tượng trang web (Google), lời bài hát từ NetEase
- **Cập nhật an toàn hơn**: bấm thông báo có bản mới sẽ hiện **danh sách thay đổi** trước, rồi mới cài. File tải về được **kiểm tra mã SHA-256** (kèm theo mỗi bản phát hành) trong một thư mục tạm riêng; sai mã là huỷ
- Lần đầu mở app sẽ báo rằng tai thỏ tự mở cùng Windows và chỉ chỗ tắt

### Sửa trong v2.3.0
- **Không để lại thay đổi trên Windows**: banner thông báo của các app được trả lại như cũ khi thoát app, khi đăng xuất / tắt máy, hoặc khi tắt mục "Ẩn banner Windows"
- Không còn ép bật lại thông báo Windows mỗi lần mở (chỉ sửa một lần cho máy từng dùng bản rất cũ)
- Tắt khởi động trong Task Manager thì app **tôn trọng lựa chọn đó** (trước đây tự bật lại)
- **Lịch sử copy và lịch sử thông báo được mã hoá** (Windows DPAPI): chỉ tài khoản Windows của bạn đọc được. Dữ liệu cũ tự chuyển sang dạng mã hoá
- Kiểm tra cập nhật **nhận đúng bản mới** cả trên mạng trường / công ty dùng chung, và báo "Không kiểm tra được" khi mất mạng thay vì báo nhầm là đã mới nhất

### Mới trong v2.2.0
- **Nốt nhạc trôi**: thay cho ảnh bìa xoay, phía sau bảng giờ là 7 nốt nhạc (♪ ♫ ♬ 𝄞) mang màu chủ đạo của bài hát, nhòe như khói màu, trôi ngang thật chậm, nhấp nhô và nghiêng nhẹ. Nền giữ tông màu sáng, rõ như v2.1.1
- **Bỏ ánh loang ra ngoài viền bảng**

### Sửa trong v2.2.0
- **Hết khựng**: mỗi nốt và mảng màu chỉ vẽ (và làm nhòe) một lần rồi chỉ dịch chuyển, nhịp đều 30 khung hình/giây; bỏ lớp ánh ngoài viền vốn bị làm nhòe lại liên tục; sóng nhạc ngừng phân tích âm thanh khi bảng đang mở

### Sửa trong v2.1.1
- Sửa lỗi **bảng bị phóng to hết khung** khi bật Loang màu (bản v2.1.0 đã gỡ). Lớp loang màu giờ chỉ phủ phía sau, không làm bảng to ra

### Mới trong v2.1.1 (loang màu)
- **Loang màu theo bài hát**: nền bảng là chính ảnh bìa được làm nhòe, các mảng màu trôi chậm, đổi bài thì màu chuyển dần. **Ánh màu tràn ra ngoài** ôm theo đúng hình bảng, bo tròn ở hai góc dưới. Viên tai thỏ vẫn giữ LED trắng
- Menu khay → **Loang màu**: *Đầy đủ*, *Nhẹ* (ảnh nhòe đứng yên) hoặc *Tắt*
- Tự giảm cho máy yếu: không có tăng tốc đồ họa → tắt; đang Tiết kiệm pin hoặc Windows tắt hiệu ứng hoạt ảnh → chuyển sang Nhẹ; chạy dưới 20 khung hình/giây vài giây liền → chuyển sang Nhẹ và báo một lần. Chỉ chạy khi bảng đang mở

### Sửa trong v2.0.1
- **Sóng nhạc nhảy theo nhạc thật**: âm thanh được tách thành 7 dải, trái là tiếng bass / trống, giữa là giọng, phải là tiếng cao. Mỗi thanh tự cân độ nhạy theo bài, bật lên khi trống đánh rồi rơi xuống từ từ. Chỉ phân tích ngay trên máy, không ghi âm, không lưu
- **Copy ảnh bằng Snipping Tool không còn lưu 2 lần**
- **Chiếc lá khi trồng cây** vẽ lại: lá xanh có cuống và gân, **lay nhẹ** như có gió

### Mới trong v2.0.0
**Trồng cây làm lại**
- **Thước kéo** chọn thời gian từ **5 phút đến 3 giờ** (kéo ngang hoặc lăn chuột, mỗi nấc 5 phút)
- **12 loại cây có hoạt ảnh** (lá đung đưa, hoa rơi, đom đóm nhấp nháy…): Sồi xanh, Thông tuyết, Anh đào có sẵn; Xương rồng nở hoa, Cam vàng, Trúc xanh, Phong đỏ, Liễu rủ, Tử đằng, Cây đom đóm, Cây pha lê, Bonsai cổ thụ **mở khoá theo số cây đã trồng** (3 → 70 cây)
- **Deep focus**: đang trồng cây mà mở YouTube, Facebook, TikTok, Steam… thì tai thỏ nhắc quay lại (mắt giận + khung đếm giờ đổi màu cam). **Chỉ nhắc, cây không chết**. Danh sách sửa được, thêm app / trang tuỳ ý. Thông báo trong lúc tập trung được **giữ lại**, trồng xong hiện một thông báo tổng
- **Thống kê Ngày / Tuần / Tháng / Năm** kèm **mảnh đất** vẽ lại tất cả cây bạn đã trồng (cây chết thành cây héo)

**Hạn chót** (tab mới, biểu tượng tím)
- Gõ tự nhiên: `Nộp báo cáo PRO 15/11 17h`, `Thi mai 9h30`, `Họp nhóm thứ 6 20h`. Không ghi giờ thì tính 23:59
- Đếm ngược đổi màu (tím → cam khi còn dưới 1 ngày → đỏ khi dưới 1 giờ). Nhắc **trước 1 ngày, trước 1 giờ và lúc đến hạn**, kể cả khi bật Không làm phiền. Tự xoá 2 ngày sau hạn

**Khác**
- **Tự cập nhật**: có bản mới thì tai thỏ báo, **bấm vào là tự tải và mở lại**. Menu khay có **Kiểm tra cập nhật**
- **Sóng nhạc chạy theo âm thanh thật** (to nhỏ theo bài hát)
- **Chế độ thuyết trình**: khi trình chiếu PowerPoint hoặc chia sẻ màn hình (Meet, Zoom, Teams) tai thỏ tự ẩn, thông báo giữ lại và báo tổng sau
- **Chấm Không làm phiền** nhỏ trên viên khi đang bật
- Biểu tượng thanh bên **có màu**: Nhạc hồng đỏ, Trồng cây xanh lá, Thông báo cam vàng, Copy xanh dương, Hạn chót tím

### Sửa trong v2.0.0
- **Copy ảnh không còn lưu 2 lần** (ảnh giống hệt chỉ được đưa lên đầu)
- **Lời bài hát tìm được nhiều bài hơn**: làm sạch tên bài YouTube (Official MV, Lyrics, ft…), thêm kho lời **NetEase**, và nếu chỉ có lời thường thì vẫn hiện (ghi "Lời không chạy theo nhạc")
- Thông báo Edge: ghi thêm chẩn đoán vào `startup.log` (quyền truy cập thông báo, thông báo nào bị bỏ qua vì sao) để tìm lỗi không nhận thông báo

### Sửa trong v1.9.1
- Sửa lỗi **tai thỏ tự ẩn khi mở Edge / tab khác** trên một số máy (thường là máy đặt thanh taskbar **tự ẩn**: cửa sổ phóng to khi đó phủ kín màn hình nên bị tưởng nhầm là đang xem phim toàn màn hình). Giờ chỉ ẩn khi thật sự toàn màn hình (F11, video, game)
- Menu khay thêm **Ẩn khi toàn màn hình**: tắt đi nếu muốn tai thỏ luôn hiện

### Sửa trong v1.9.0
- Đôi mắt quay về **mỗi 30 giây** cho đỡ rối mắt
- **Mây đổi bên**: đi sang trái rồi **ở lại ló đầu sau mép trái** (như bên phải), lần đi dạo sau mới đi ngược về bên phải
- Bỏ **mắt chóng mặt** khỏi các biểu cảm ngẫu nhiên (chỉ còn hiện khi bạn lắc chuột thật nhanh)

### Mới trong v1.8.9
- **Đôi mắt nhìn theo chuột** khi chuột nằm trong vùng khoảng **480 × 100px** quanh tai thỏ (cỡ thanh tab + thanh bookmark), sau 0,3 giây (lướt qua để đổi tab thì không hiện). Mắt **chớp tự nhiên** mỗi 2–6 giây (thỉnh thoảng chớp đôi) và đảo nhẹ; chuột rời vùng thì mắt nhìn theo thêm 1 giây rồi trả lại đồng hồ
- **Lắc chuột qua lại thật nhanh** → mắt chóng mặt; **để chuột đứng yên ~4 giây** → mắt lim dim; **chạm vào viên** → mắt híp vui ^ ^ trước khi bảng mở
- Viên chỉ mờ đi khi chuột **nằm ngay trên viên** (trước là khi còn cách 26px), nên mắt vẫn rõ khi chuột ở gần

### Sửa trong v1.8.8
- Đôi mắt **không còn lúc nào cũng buồn ngủ** vào ban đêm: giờ chỉ lim dim khi bạn **thật sự không làm gì** (3 phút không chạm phím / chuột); còn lại là **biểu cảm ngẫu nhiên**, không lặp lại cùng một kiểu hai lần liền (đang nghe nhạc thì thỉnh thoảng mới là mắt vui)
- Mắt xuất hiện **mỗi 20 giây** (trước là 30 giây)

### Mới trong v1.8.7
- **Đôi mắt trên tai thỏ**: cứ **30 giây** đồng hồ nhường chỗ cho một đôi mắt khoảng **5–6 giây** (viên nhô to hơn một chút cho dễ nhìn) rồi quay lại giờ. 11 biểu cảm: chớp mắt, liếc trái phải, vui, nháy mắt, ngạc nhiên, mắt trái tim, buồn ngủ, hờn dỗi, buồn, nghi ngờ, chóng mặt; chuyển động có nhịp lấy đà, nảy nhẹ và "thở" nên trông tự nhiên hơn
- **Mắt phản ứng theo tình huống**: có thông báo mới → mở to ngạc nhiên rồi thông báo mới trượt xuống; trồng xong cây → mắt trái tim, bỏ cuộc → mắt buồn; đang nghe nhạc → mắt vui nhún theo nhạc; sau 23 giờ hoặc máy để yên lâu → lim dim buồn ngủ; đưa chuột lại gần → mắt **nhìn theo con trỏ**
- Mây đi dạo và đôi mắt thay phiên nhau, không bao giờ cùng lúc

### Mới trong v1.8.6
- **Viên tai thỏ kiểu LED trắng**: số, sóng nhạc và viền cùng **một màu trắng cố định** (viền dịu hơn số một chút), không đổi theo bài hát nữa. Đang trồng cây: đếm ngược vẫn màu trắng, có **chiếc lá nhỏ** bên trái
- **Mây đi dạo**: cứ 10–15 giây Mây bước ra từ sau mép phải, đi dọc viên sang trái rồi quay về. Rê chuột lại gần là Mây thôi đi; đang trồng cây hoặc máy để yên thì Mây ngồi im. Tắt / bật ở menu khay → **Mây đi dạo**
- **Mây nằm ngửa khoe bụng** khi bạn đưa chuột lại gần Mây trong bảng, có trái tim nhỏ bay lên

### Sửa trong v1.8.5
- **Viên tai thỏ mới: đồng hồ LED** (số 7 vạch, vạch tắt hiện mờ phía sau) **luôn ở đúng một chỗ, không đổi độ dài**. Nghe nhạc thì số và viền sáng theo màu bài hát, kèm **sóng nhạc** bên phải; không nghe nhạc thì số trắng; đang trồng cây thì **đếm ngược màu xanh lá**. Bỏ ảnh bìa, tên bài và vạch tiến độ trên viên; **viền dày gấp đôi**
- **Ảnh bìa không còn bị lệch**: ảnh YouTube (ảnh ngang) giờ được cắt quanh phần giữa, cả trong bảng và trong tab Copy
- **Thông báo**: các nút lọc theo app xuống dòng khi nhiều, không còn bị khuất phía sau
- **Tự chạy khi bật máy chắc chắn hơn**: chờ Windows khởi động xong vài giây, thử lại nếu dịch vụ thông báo / nhạc chưa sẵn sàng, bật lại công tắc trong Task Manager → Startup apps nếu bị tắt. Ghi lại giờ khởi động và lỗi (nếu có) vào `%LOCALAPPDATA%\DynamicIslandNotifier\startup.log`

### Sửa trong v1.8.4
- Mây không còn đè lên thanh âm lượng: chuyển lên **góc trên bên trái**, ngay trên thanh icon, cạnh giờ

### Sửa trong v1.8.3
- **Cả bảng mang màu bài hát**: nền chuyển dần từ màu ảnh bìa sang gần đen (kiểu Apple Music); đổi bài là đổi màu. Không có nhạc thì nền đen
- **Trình phát kiểu Apple Music**: ảnh bìa to (96px) có bóng đổ, tên bài lớn, thanh tua sóng màu trắng, **nút Phát tròn trắng**
- **Tab Copy** gộp lại thành một tab, bên trong có công tắc **Văn bản | Hình ảnh** (kèm số lượng); mở tab là vào đúng mục vừa copy
- **Viên tai thỏ ngắn lại**: tên bài dài **chạy chữ ngang** trong khoảng ~120px thay vì kéo dài viên
- Thanh icon bên trái nằm trong một rãnh mờ, các thẻ và nút trong bảng trong suốt nhẹ để hợp với nền màu

### Sửa trong v1.8.2
- **Bố cục trở lại như v1.7**, giữ nguyên mọi tính năng: bảng rộng 620px, **chiều cao vừa với từng tab** (Nhạc, Trồng cây không còn khoảng trống), đổi tab thì bảng co giãn mượt
- **Thanh icon dọc bên trái** để chuyển tab (thay cho ‹ › và chấm trang); tab đang mở sáng theo màu bài hát. Vẫn vuốt ngang hai ngón để chuyển tab được
- Giờ · pin · tai nghe bên trái, **nút nhanh** bên phải trên cùng một dòng
- **Clipboard tách làm hai tab riêng**: **Văn bản** (ô tìm kiếm, mỗi mục 2 dòng xem trước) và **Hình ảnh** (ảnh to hơn, xếp hàng ngang với thanh cuộn kéo được, nút ‹ ›); mỗi tab có nút xoá riêng
- Tab Thông báo giữ nguyên

### Sửa trong v1.8.1
- **Bảng gọn lại đúng khung của v1.7** (rộng 620px) và **cao bằng nhau ở mọi tab**, không còn tab cao tab thấp. Chấm trang + tên tab chuyển lên giữa dòng trạng thái để bảng thấp hơn
- **Màu sáng hơn**: nền thẻ nội dung, các dòng thông báo / clipboard và chữ phụ đều sáng lên, dễ đọc hơn
- **Clipboard gọn hơn**: mỗi đoạn chữ một dòng (rê chuột để xem đầy đủ), hàng ảnh nhỏ lại, số chữ / ảnh hiện ngay trong ô tìm kiếm
- **Ô đếm giờ trồng cây**: **kéo đi đâu cũng được** (nhớ luôn vị trí), rê chuột vào thì **chỉ mờ đi** chứ không biến mất. Chuột phải: Về góc dưới bên phải · Ẩn đến khi trồng xong · Bỏ cuộc
- Chỉ còn **một Mây** trong bảng (ngồi ở góc dưới thẻ nội dung)

### Mới trong v1.8.0
- **Bố cục bảng mới**: bỏ thanh tab. Hai **nút ‹ ›** to ở hai bên mép để chuyển tab (hoặc **vuốt ngang hai ngón** trên touchpad), **chấm trang + tên tab** ở dưới. Giờ · pin · tai nghe nằm trên **dòng trạng thái**, các **nút nhanh** (micro, Không làm phiền, chụp màn hình, khoá máy) gọn ở góc phải dòng đó
- **Mây vẽ lại** bằng nét mềm, bo tròn cho hợp với giao diện. Lúc nghỉ, Mây **thò đầu ra sau mép phải tai thỏ**, đuôi buông xuống vẫy theo nhạc; trong bảng Mây ngồi ở góc dưới
- **Ô đếm giờ trồng cây nổi** ở góc màn hình: cây đang lớn, thời gian còn lại, số phút đã học. Rê chuột vào là **ô biến mất và bấm xuyên qua được**; **chuột phải** ngay chỗ đó để Bỏ cuộc, chuyển góc hoặc ẩn đến khi trồng xong
- **Thông báo trượt xuống chỉ hiện 2 giây** (rê chuột vào để giữ lại đọc)
- **Tab Thông báo đẹp hơn**: thẻ có viền màu theo logo app, giờ kiểu "5 phút trước", chia nhóm Hôm nay / Hôm qua, **chip lọc theo app**; rê chuột vào thẻ để xoá hoặc **tắt tiếng app**
- **Clipboard làm lại**: ô **tìm kiếm** (Enter để chép kết quả đầu tiên), mục mới nhất có nhãn **"Vừa copy"**, mỗi mục ghi **copy từ app nào**, bao lâu trước và bao nhiêu ký tự; mục đã ghim tách riêng lên trên. **Ảnh có khu riêng ở dưới** với **thanh cuộn ngang kéo được** và nút ‹ ›; rê chuột vào ảnh để xem to. Bấm để chép, bảng không tự đóng
- Bỏ tab **Việc cần làm** và các lời nhắc (sẽ thay bằng Lịch)

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
