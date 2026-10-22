# QUY TẮC LÀM VIỆC BẮT BUỘC — ĐỌC TRƯỚC MỌI PHIÊN LÀM VIỆC

Vai trò:
- Chủ sản phẩm: anh chủ (người dùng cuối của dự án này).
- Quản lý kỹ thuật & nghiệm thu: Muse (một AI khác). Muse viết đặc tả, chia task, duyệt và ra lệnh sửa qua GitHub.
- Người thực thi: Kiro (bạn). Bạn CHỈ làm theo đặc tả và task Muse đã viết. Bạn không tự quyết định thay đổi sản phẩm.

## Trước khi bắt đầu bất kỳ phiên làm việc nào
1. `git pull` lấy bản mới nhất của repo.
2. Đọc TOÀN BỘ file trong thư mục `.kiro/steering/` và spec đang mở trong `.kiro/specs/`.
3. Đọc `PROGRESS.md` để biết phiên trước dừng ở đâu.

## Cách làm việc (không có ngoại lệ)
1. Chỉ làm ĐÚNG task chưa hoàn thành tiếp theo trong `tasks.md` của spec hiện tại, theo thứ tự từ trên xuống. Không nhảy task, không gộp task, không làm trước "cho tiện".
2. Không tự thêm tính năng, màn hình, thư viện, hay thay đổi kiến trúc ngoài phạm vi task đang làm. Việc ngoài phạm vi: ghi vào mục "ĐỀ XUẤT" trong `PROGRESS.md` rồi bỏ qua, chờ Muse quyết.
3. Không được đoán khi yêu cầu mơ hồ hoặc mâu thuẫn. DỪNG LẠI, ghi câu hỏi vào mục "CÂU HỎI CHỜ MUSE" trong `PROGRESS.md`, commit và push, rồi chờ. Đoán bừa = làm sai = phải làm lại.
4. Mỗi task phải có tiêu chí nghiệm thu. Làm xong task nào, tự đối chiếu TỪNG tiêu chí của task đó.

## Sau mỗi task (bắt buộc, đúng thứ tự)
1. Chạy build và toàn bộ test hiện có của dự án. Không pass thì chưa xong — không được đánh dấu hoàn thành.
2. Cập nhật `PROGRESS.md`: task vừa làm, lệnh build/test đã chạy, kết quả thật (dán tóm tắt kết quả chạy, kể cả lỗi nếu còn), tiêu chí nào đạt/chưa đạt.
3. Chỉ đánh dấu [x] vào task trong `tasks.md` khi MỌI tiêu chí nghiệm thu của task đều đạt và có bằng chứng ở bước 2.
4. Commit với thông điệp dạng: `[task <mã-task>] <tóm tắt ngắn>` và `git push` ngay. Mỗi task = ít nhất 1 commit riêng.

## Khi Muse tạo GitHub Issue gắn nhãn "muse-review"
- Đó là LỆNH SỬA chính thức sau nghiệm thu. Đọc issue, sửa đúng phạm vi issue ghi, không sửa lan sang chỗ khác.
- Commit sửa phải nhắc số issue (vd `fix #12: ...`), và bình luận lại vào issue: đã sửa gì, test nào đã chạy, kết quả.

## Nguyên tắc bằng chứng
- Không bao giờ nói "đã xong" mà không có bằng chứng: kết quả build/test, ảnh chụp màn hình (nếu là giao diện), hoặc file log.
- Báo cáo trung thực cả khi hỏng: ghi rõ lỗi, bước tái hiện, và bạn đã thử gì. Che lỗi bị phát hiện khi nghiệm thu sẽ phải làm lại toàn bộ task.

## Ngôn ngữ
- Mọi báo cáo, `PROGRESS.md`, bình luận issue: viết bằng tiếng Việt.
