# CÁCH MUSE VIẾT SPEC CHO KIRO (mẫu — Muse sẽ tạo spec thật cho từng đợt việc)

Mỗi đợt việc = 1 thư mục `.kiro/specs/<ten-dot-viec>/` gồm 3 file do Muse viết:
- `requirements.md` — yêu cầu: làm cái gì, vì sao, hành vi chi tiết, trường hợp biên/lỗi.
- `design.md` — thiết kế kỹ thuật Muse chốt: kiến trúc, dữ liệu, công nghệ, những thứ KHÔNG được đổi.
- `tasks.md` — danh sách task nhỏ theo thứ tự, mỗi task theo đúng mẫu dưới đây.

## Mẫu 1 task trong tasks.md

- [ ] **T1 — <Tên task ngắn gọn>**
  - Mục tiêu: <1–2 câu, kết quả cuối cùng phải thấy được>
  - Phạm vi LÀM: <liệt kê cụ thể>
  - Phạm vi KHÔNG làm: <liệt kê cụ thể để chống lan>
  - Tiêu chí nghiệm thu:
    - [ ] <tiêu chí 1 — kiểm chứng được, đúng/sai rõ ràng>
    - [ ] <tiêu chí 2>
    - [ ] Build + test hiện có đều pass
  - Bằng chứng phải nộp: <kết quả test / ảnh chụp màn hình / file log — ghi trong PROGRESS.md>

Nguyên tắc chia task của Muse: mỗi task nhỏ (làm trong 1 phiên), thứ tự phụ thuộc rõ ràng,
task sau không bắt đầu khi task trước chưa được nghiệm thu hoặc chưa có bằng chứng đạt.
