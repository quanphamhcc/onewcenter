# TIÊU CHUẨN NGHIỆM THU CHUNG (Definition of Done) — Áp cho mọi task

Một task chỉ được coi là XONG khi đạt ĐỒNG THỜI tất cả mục sau:

1. **Đúng phạm vi**: làm đủ và chỉ làm những gì task/spec mô tả. Thừa tính năng cũng là lỗi.
2. **Build sạch**: dự án build/chạy được từ đầu theo đúng hướng dẫn trong BAO-CAO/README, không lỗi, không cảnh báo nghiêm trọng mới phát sinh.
3. **Test đạt**: toàn bộ test tự động hiện có đều pass; tính năng mới có test hoặc kịch bản kiểm tra đi kèm như spec yêu cầu.
4. **Đúng hành vi**: kết quả khớp từng tiêu chí nghiệm thu của task — gồm cả trường hợp biên và trường hợp lỗi (nhập sai, dữ liệu trống, mất kết nối...), không chỉ trường hợp thuận lợi.
5. **Giao diện khớp spec** (nếu task có UI): bố cục, chữ, trạng thái (rỗng / đang tải / lỗi / thành công) đúng mô tả; không tự ý đổi thiết kế đã chốt.
6. **Không nợ kỹ thuật lén**: không để lại code debug, dữ liệu giả, nút bấm chết, hàm rỗng giả vờ xong, hay TODO thay cho việc thật. Chỗ nào chưa làm được phải ghi rõ trong PROGRESS.md.
7. **Bằng chứng đã nộp**: PROGRESS.md cập nhật (đã chạy gì, kết quả), commit + push xong, ảnh chụp màn hình nếu task có giao diện.
8. **Không phá cái cũ**: các tính năng đã nghiệm thu ở vòng trước vẫn hoạt động (chạy lại test hồi quy nếu dự án có).

Muse sẽ nghiệm thu lại độc lập trên máy của Muse theo đúng các mục này. Không đạt một mục = task bị trả lại kèm issue "muse-review".
