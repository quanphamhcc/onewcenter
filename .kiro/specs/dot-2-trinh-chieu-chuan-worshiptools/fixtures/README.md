# Fixture: Kinh Thánh mẫu định dạng Zefania XML

## Zefania XML là gì

Zefania XML là một định dạng XML mở để lưu trữ toàn văn Kinh Thánh, do dự án
**Zefania XML Bible Markup Language** (SourceForge: `zefania-sharp`) xây dựng —
đây là định dạng file mà tính năng "Import bản dịch từ XML" của OneWorshipCenter
(spec đợt 2, task T11) phải đọc được.

Cấu trúc chính của một file Zefania:

```
<XMLBIBLE biblename="..." version="..." type="x-bible" ...>
  <INFORMATION> ... metadata: title, creator, date, identifier, language ... </INFORMATION>
  <BIBLEBOOK bnumber="1" bname="Genesis" bsname="Gen">
    <CHAPTER cnumber="1">
      <VERS vnumber="1">...</VERS>
      ...
    </CHAPTER>
    ...
  </BIBLEBOOK>
  ...
</XMLBIBLE>
```

File được kiểm chứng theo schema `zef2005.xsd`. Tài liệu định dạng:
http://bgfdb.de/zefaniaxml/bml/

## File `kinh-thanh-mau-zefania.xml` trong thư mục này

Đây là **trích đoạn nguyên văn** (không sửa một ký tự nào trong phần nội dung)
từ một module Zefania thật:

- **Bản dịch:** New Vietnamese Bible (2002) — Kinh Thánh Bản Dịch Mới,
  module mã `VIETNVB`. Theo khối `INFORMATION` trong chính file gốc, văn bản
  do Vietnamese Bible Incorporated (VBI) cung cấp; fixture này chỉ dùng để
  kiểm thử bộ phân tích cú pháp (parser) trong task T11.
- **File gốc:** `SF_2009-03-11_VIE_VIETNVB_(NEW VIETNAMESE BIBLE (2002)).xml`
  (6.336.698 byte, 66 sách).
- **Nguồn lấy:** kho gương GitHub của chính dự án Zefania —
  `kohelet-net-admin/zefania-xml-bibles`, commit `7f2c4322d944932f08785931fc0463dcf2aa8d5b`,
  đường dẫn `Bibles/VIE/New Vietnamese Bible (2002)/`.
  Bản phân phối gốc là file nén trên SourceForge (xem mục dưới).
- **Phạm vi trích:** khối `INFORMATION` đầy đủ; Sáng Thế Ký
  (`BIBLEBOOK bnumber="1"`) chương 1–2; Thi Thiên (`bnumber="19"`)
  chương 23–24. Lưu ý: trong module này tên sách (`bname`/`bsname`) để bằng
  tiếng Anh, còn nội dung từng câu là tiếng Việt — giữ đúng như nguồn.

## Lấy file bản dịch đầy đủ ở đâu

- SourceForge (bản nén gốc của dự án Zefania):
  https://sourceforge.net/projects/zefania-sharp/files/Bibles/VIE/New%20Vietnamese%20Bible%20%282002%29/SF_2009-03-11_VIE_VIETNVB_%28NEW%20VIETNAMESE%20BIBLE%20%282002%29%29.zip/download
- Hoặc lấy thẳng file XML từ kho gương GitHub nêu trên (thư mục `Bibles/VIE/`
  còn có thêm module "Vietnamese Version" — bản 1934).
