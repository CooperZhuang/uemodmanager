# Nhật ký thay đổi (Tiếng Việt)

Bản tiếng Việt của CHANGELOG.md: giữ nguyên tiêu đề, mỗi thay đổi một dòng. Phiên bản chưa dịch hiển thị bằng tiếng Anh.

## 2.0.0 (2026-11-11)
- Một ứng dụng cho nhiều game: Black Myth: Wukong và Phantom Blade Zero, cùng Stellar Blade: Blood Rain để xem thử giao diện trước khi game ra mắt
- Chọn game khi khởi động lần đầu hoặc bằng Đổi game trên thanh tiêu đề; Cài đặt có thể luôn hỏi chọn game khi khởi động, và màn hình chọn game có lời nhắn từ nhà phát triển
- Mỗi game có giao diện, hình ảnh và thẻ riêng; biểu tượng ứng dụng giờ là chồng mod, và Windows hiện biểu tượng mới ngay sau khi cập nhật
- Black Myth: Wukong: mod pak, UE4SS và C# Loader, cài C# Loader và UE4SS chỉ với một cú bấm, và sao lưu bản lưu
- Tùy chọn khởi động ở bên phải nút Khởi động game: các tham số game dùng khi khởi động, kèm tham số đề xuất của game, như -fileopenlog của Wukong cho mod pak
- Biểu tượng khay hệ thống cạnh đồng hồ, với menu để đổi hoặc khởi động game; Cài đặt có thể thu nhỏ và đóng ứng dụng xuống khay
- Chưa có game của bạn? Quét thư mục game trong màn hình chọn game để tạo báo cáo chia sẻ trên Discord, tên người dùng và các ID của bạn được ẩn
- Thẻ trong màn hình chọn game cho biết game nằm ở đâu trên máy và có bao nhiêu mod
- Lần khởi động đầu tiên, câu hỏi ngôn ngữ hiện trước màn hình chọn game, nơi cũng có ô chọn ngôn ngữ
- Màn hình xem trước khi cài cho biết mod được cài vào game nào
- UE4SS đặt vào bằng tay không còn bị báo có bản cập nhật khi nó đã là bản mới nhất
- Một tệp tải về cho mọi game, uemm_<version>.zip; các phiên bản cũ của ứng dụng vẫn tìm được bản cập nhật

## 1.7.2 (2026-11-10)
- Chi tiết mod có thể tắt việc kiểm tra cập nhật của mod đó để giữ phiên bản: ô sẽ hiện Not tracked, và việc kiểm tra bỏ qua mod đó
- Nút kiểm tra cập nhật dùng lại được sau 30 giây chờ; trước đây nó có thể bị xám mãi
- Nút dùng được ngay lần đầu mở trang Mod: chỉ khi bấm nút mới bắt đầu chờ, không phải lần kiểm tra khi khởi động
- Nút xoay màu xanh lá khi đang kiểm tra, ít nhất một giây, nên mỗi lần bấm đều có phản hồi

## 1.7.1 (2026-11-10)
- Khi cập nhật, tên bạn đặt cho mod bằng Sửa được giữ lại, giống như nhãn; tên do ứng dụng tạo từ tên tệp vẫn theo tệp tải về mới (cảm ơn qhq)

## 1.7.0 (2026-11-10)
- Kiểm tra cập nhật cho mod có trang mod: một yêu cầu đến API công khai của trang web mod hỏi phiên bản trên trang của tất cả các mod đó, mỗi lần khởi động và bằng nút mới ở tiêu đề danh sách mod, sau đó nút chờ 30 giây (cảm ơn msylgj)
- Hàng của các mod này và chi tiết mod hiện Update available, mở danh sách tệp của trang, hoặc Up to date; mục Mod hiện UPDATE khi có mod có phiên bản mới hơn
- Ghi chú kiểm tra cập nhật cạnh nút giải thích những gì được so sánh và khi nào việc kiểm tra không xác định được, như khi tác giả không cập nhật phiên bản trên trang
- Ứng dụng chỉ gửi số của mod và không tải gì về; Cài đặt ▸ Cập nhật mod tắt việc kiểm tra khi khởi động

## 1.6.4 (2026-11-10)
- Chương trình trùng tên với exe của game nhưng nằm ở thư mục khác, như chương trình chạy bằng quyền quản trị viên, không còn bị coi là game đang chạy (cảm ơn High-cla)
- Nhật ký ghi rõ tiến trình nào bị coi là game đang chạy, kèm đường dẫn của nó, hoặc lý do không đọc được đường dẫn

## 1.6.3 (2026-11-09)
- Tệp cấu hình mà mod script tạo trong ~mods, như PBZAutoCollect.txt của Auto Collect Loot, giờ thuộc về mod đó: được giữ khi cập nhật, được sao lưu khi gỡ cài đặt, và không còn hiện trong "Xem tệp" (cảm ơn CooperZhuang)
- Trình quản lý nhận các tệp này khi khởi động và khi game đóng
- Tệp cấu hình trong ~mods giờ cũng được cài cùng mod script khi tệp tải về có thêm một thư mục riêng ở trên cùng

## 1.6.2 (2026-11-08)
- Tệp tải về từ cùng trang mod với một mod đã cài nhưng có tên tệp khác (như mod đổi từ pak sang script) giờ sẽ hỏi: mặc định "Giữ cả hai", hoặc chọn "Cập nhật" (cảm ơn CooperZhuang)
- Tệp cấu hình mà mod script để trong ~mods giờ được cài cùng mod đó (cảm ơn CooperZhuang)
- Tệp tải về có phần dành cho nhiều trình nạp được cài đầy đủ theo hướng dẫn của tác giả; thiếu trình nạp giờ chỉ là cảnh báo, không chặn cài đặt (cảm ơn CooperZhuang)
- Không còn cảnh báo "Tệp bị bỏ qua" cho các tệp dữ liệu như MANIFEST.json nằm cạnh các thư mục game trong tệp tải về

## 1.6.1 (2026-11-07)
- Thêm nút Mở trang mod vào dòng và phần chi tiết của mỗi mod có tệp tải về giữ nguyên tên từ trang web mod; ứng dụng đọc mã số mod từ tên tệp mà không cần kết nối mạng
- Mod đã cài bằng phiên bản cũ cũng có liên kết này; mod khác hiện "Không có trang mod", còn mod trên thẻ không hiện gì
- Giờ đây ứng dụng cũng nhận diện được tên và phiên bản mod khi cùng một tệp được tải lần thứ hai, với tên tệp có thêm (1)
- Chưa thể tự kiểm tra bản cập nhật cho mod vì yêu cầu cấp quyền truy cập API của trang web mod vẫn đang chờ duyệt; hiện tại ứng dụng chỉ có thể mở trang mod để bạn tự kiểm tra

## 1.6.0 (2026-11-07)
- Có thể cài Mods Menu và PBZModLoader cùng lúc: Mods Menu 0.9.0 và PBZModLoader 2.1.0 trở lên chạy cùng nhau
- Cảnh báo nếu một trong hai đang dùng phiên bản cũ đã được xác định là gây xung đột; PBZModLoader không được dùng hid.dll để nạp vào game khi có Mods Menu
- Nút Xem tệp liệt kê các tệp trong thư mục mod không do trình quản lý cài
- Cửa sổ UE4SS chỉ hiện lưu ý chọn bản phát hành khi bạn tải thủ công

## 1.5.9 (2026-11-07)
- Lọc danh sách mod theo Tất cả, Đã bật hoặc Đã tắt; Đã bật sẽ ẩn các mod đã tắt
- Sắp xếp danh sách theo trạng thái, loại và tình trạng, không chỉ theo tên và ngày
- Gợi ý nhãn dưới ô Nhãn: gồm nhãn của bạn và các nhãn phổ biến, được lọc theo nội dung bạn nhập; nhấn Tab để tự động hoàn thành

## 1.5.8 (2026-11-07)
- Khi trùng tên tệp với một mod đã cài, cửa sổ cài đặt hỏi cập nhật mod đó hay giữ cả hai
- Giữ cả hai sẽ đặt mod mới vào thư mục riêng trong ~mods, kèm lưu ý có thể chỉ một kiểu được hiển thị
- Nút Ủng hộ phía trên nút Discord: hoàn toàn tùy ý, nút X gỡ nó vĩnh viễn (trang Giới thiệu vẫn giữ nút)

## 1.5.7 (2026-11-06)
- Nhãn cho mod: thêm khi cài hoặc bằng nút Thêm nhãn, rồi lọc danh sách chỉ với một cú nhấp
- Nút X trong ô tìm kiếm xóa nội dung, phím Esc cũng vậy
- Mod có tệp rời trong thư mục project của game được cài đủ, ví dụ ảnh của Quest Navigation
- Mod cho PBZModLoader cũng cài được từ tệp zip không có thư mục ProjectPBZ ở trên cùng
- Tệp .sig đi cùng tệp pak của nó, và tệp như license.json hoặc notice.ini không còn bị coi là tài liệu
- Cửa sổ cài đặt cảnh báo khi có tệp bị bỏ qua mà mod có thể cần
- Cửa sổ cài đặt cảnh báo mod chỉ có main.ts: PBZModLoader 2.0 không dùng được
- Thông báo rõ hơn khi tệp tải về không có tệp mod nào

## 1.5.6 (2026-11-05)
- PBZModLoader trên thẻ đề xuất: có sẵn bản sửa cho mod pak, không cần UE4SS (cảm ơn LKF)
- UE4SS C++ BYPASS vẫn ở trên thẻ như một bản tải thêm
- Nâng cấp từ PBZModLoader cài thủ công: tệp cũ chuyển vào bản sao lưu, mod và cấu hình được giữ
- Cài được mod cho PBZModLoader, và tệp zip có bản cho hai trình nạp chỉ cài một bản
- Mod script chờ đến khi trình nạp của nó được cài
- Mods Menu và PBZModLoader: mỗi lần chỉ một trong hai
- Mod chỉ dùng chung một tệp với mod khác không còn thay thế mod đó
- Script có tên như install.js hoặc notice.js không còn bị bỏ sót
- Không còn lưu ý trước khi cài mod loại "Tệp game" hoặc "Tệp cạnh exe"
- Lỗi khi cài luôn hiển thị trong tầm nhìn; ghi chú tệp dài sẽ tự xuống dòng

## 1.5.5 (2026-11-04)
- Mod script: nay cài cả các tệp trong Content/JavaScript, ví dụ Mods Menu
- Nút Làm mới ở trang Mod: kiểm tra lại thư mục mod mà không cần khởi động lại
- Mod loại "Tệp game" và "Tệp cạnh exe" có hàng màu xanh, và mod mới hiện một lưu ý ngắn trước khi cài
- Cửa sổ không còn nhỏ hơn kích thước lúc khởi động

## 1.5.4 (2026-11-04)
- Khi mod đề xuất có phiên bản mới, mục Mod ở thanh bên hiện chữ CẬP NHẬT màu xanh

## 1.5.3 (2026-11-04)
- Thẻ mod đề xuất riêng ở đầu trang Mod, kiểm tra cập nhật mỗi lần khởi động
- Cài bằng một cú nhấp, đồng thời cài hoặc cập nhật UE4SS khi mod cần
- Mod đầu tiên: UE4SS C++ BYPASS giúp các mod pak dạng script hoạt động trở lại sau đợt kiểm tra script mới của game (thử nghiệm)
- Ghi chú kỹ thuật trên thẻ: cách mod hoạt động và nơi có mã nguồn

## 1.5.2 (2026-11-03)
- Dữ liệu, bản cập nhật ứng dụng và tải UE4SS lấy từ Cloudflare trước, GitHub làm dự phòng
- Ô REMOTE ở thanh bên: cho biết dữ liệu lấy từ đâu, kèm giải thích ngắn
- Nút "Thử lại" trong cửa sổ đó khi lúc khởi động không lấy được dữ liệu
- Báo cáo sự cố ghi rõ dữ liệu và kiểm tra cập nhật lấy từ đâu
- Tiếng Trung phồn thể: nút "Open exe folder" nay ghi đúng thư mục (cảm ơn CooperZhuang)

## 1.5.1 (2026-11-02)
- Liên kết "Mở thư mục" trong chi tiết mod: mở từng thư mục của mod trong game

## 1.5.0 (2026-11-02)
- Tiếng Trung phồn thể (cảm ơn CooperZhuang)
- Câu chữ tiếng Trung giản thể tự nhiên hơn (cảm ơn CooperZhuang)
- Tiếng Trung phồn thể hiển thị bằng phông chữ Đài Loan
- Nhật ký UE4SS chia thành các thẻ: UE4SS, Mod và Không tên
- Các dòng UE4SS vô hại đã biết không còn tính là lỗi
- Giữ bản của tôi: tệp mod bị sửa bên ngoài trình quản lý không còn chặn mod
- Tiêu đề các trang cùng cỡ chữ; dòng người đóng góp dài được xuống dòng

## 1.4.1 (2026-11-01)
- Liên kết Changelog trong ô cập nhật: xem thay đổi của từng phiên bản
- Tệp bị chương trình khác giữ có thông báo rõ ràng thay vì lỗi (cảm ơn CooperZhuang)

## 1.4.0 (2026-11-01)
- Tệp gán phím và cấu hình trong thư mục riêng của game (cảm ơn CooperZhuang)
- ReShade, RenoDX và các mod loader khác đặt cạnh tệp exe của game
- Giữ lại và khôi phục tệp gốc của game; không ảnh hưởng đến bản lưu
- Cài kèm tệp cấu hình .txt cạnh pak ở bất kỳ thư mục nào
- Cửa sổ cài đặt hiển thị nơi lưu tệp và tên tệp bị bỏ qua

## 1.3.2 (2026-11-01)
- Đổi tên mod đã cài: ô "Sửa" màu xanh trong phần chi tiết mod
- Đã dịch gợi ý "Không bắt buộc"

## 1.3.1 (2026-10-31)
- Mod UE4SS có Scripts/config.json hỗ trợ trình sửa cấu hình

## 1.3.0 (2026-10-31)
- Tiếng Việt
- Cảnh báo bản UE4SS không chính thức
- Tên mod vẫn hiện khi cửa sổ hẹp
- Câu chữ tiếng Trung tự nhiên hơn

## 1.2.2 (2026-10-30)
- Cài nguyên thư mục UE4SS không chứa mã để hỗ trợ trang cấu hình mod

## 1.2.1 (2026-10-30)
- Mod UE4SS có thư viện Lua dùng chung được cài đầy đủ
- Nút xem cây thư mục cho từng mod
- "Xóa sạch" xóa nhật ký UE4SS
- Hiển thị ghi chú cập nhật có định dạng

## 1.2.0 (2026-10-30)
- Chi tiết mod liệt kê toàn bộ tệp đã cài
- Bộ mod: bật cả nhóm mod bằng một cú nhấp
- Chạy thử game không có mod
- Khôi phục bản lưu từ bản sao lưu
- Trình sửa cấu hình riêng có tô màu; nhận tệp .txt cạnh pak là tệp cấu hình
- Lỗi UE4SS: thông báo khi game đóng, đánh dấu đỏ trong danh sách, tìm kiếm trong nhật ký
- Tiếng Trung giản thể (cảm ơn Null993)
- Mở lại cửa sổ ở vị trí cũ; Ctrl+F và F5

## 1.1.1 (2026-10-29)
- Cài kèm tệp cấu hình cạnh pak (.json, .ini, v.v.)
- Tên mod tải về gọn hơn

## 1.1.0 (2026-10-29)
- Bản phát hành chính thức đầu tiên
- Cài UE4SS cho Phantom Blade Zero bằng một cú nhấp
- Giữ cấu hình đã chỉnh khi cập nhật UE4SS
- Nhận diện tệp exe có tên khác, như ở bản game từ cửa hàng khác

## 1.0.1 (2026-10-29)
- Hỗ trợ thư mục bản game chính thức (Steam)
- Sao lưu bản lưu
- "Tìm thư mục game" chỉ nhận thư mục của game

## 1.0.0 (2026-10-28)
- Bản thử nghiệm đầu tiên
