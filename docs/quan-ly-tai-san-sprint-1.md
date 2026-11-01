# Module quản lý tài sản — Sprint 1

## Kết quả rà soát

Ứng dụng dùng ASP.NET Core MVC .NET 8, NiceAdmin, session `TaiKhoanID`/`VaiTro`, Pomelo/MySQL. Entity tài sản là `Models/Tbltaisan.cs`, liên kết danh mục bằng `DMTS_ID` và các chứng từ bằng `TS_ID`. Module sử dụng `DoAn.Data.DataContext`; hai context cũ tiếp tục phục vụ đăng nhập/menu. Không thay kiến trúc đăng nhập, tên bảng/cột hoặc chạy migration trên CSDL hiện có.

Trước lần sửa này, Create và Edit dùng chung ViewModel cho phép đổi mã/trạng thái; Index thiếu lọc tình trạng, sắp xếp, chọn kích thước trang; chưa hiển thị tình trạng bảo hành. Những phần này đã được sửa.

## Quy tắc đã áp dụng

- Create chỉ nhận các trường hồ sơ. Server tạo ID/ngày tạo và luôn đặt `ChuaCapPhat`; các trường giả như `TrangThai`, `TS_TrangThai`, `TsTrangThai`, `TS_ID`, `TS_NgayTao` không được map vào entity.
- Edit lấy ID từ route; không bind ID/mã/trạng thái hiển thị, lấy lại các giá trị đó từ database. Chỉ map tên, danh mục, mô tả, ngày mua, nguyên giá, hạn bảo hành, tình trạng, ghi chú. Ngày tạo được giữ nguyên.
- Mã bắt buộc, trim, tối đa 30 ký tự, kiểm tra trùng trước lưu; giữ UNIQUE trong EF/database và xử lý lỗi 1062 nếu có request cạnh tranh. Tên bắt buộc, trim, tối đa 150 ký tự.
- Danh mục lấy từ database, phải tồn tại và đang hoạt động. Khi sửa tài sản thuộc danh mục cũ đã ngừng dùng, UI vẫn hiển thị danh mục cũ và yêu cầu chọn danh mục hoạt động trước khi lưu.
- Nguyên giá bắt buộc, decimal trong phạm vi `decimal(15,2)`, không âm, tối đa hai chữ số thập phân. Không lưu chuỗi định dạng tiền vào database.
- Ngày mua có thể bỏ trống; nếu nhập phải từ 01/01/1900 đến ngày hiện tại tại Việt Nam. Hạn bảo hành không trước ngày mua nếu biết cả hai; được nhập độc lập khi chưa biết ngày mua.
- Tình trạng chuẩn: `Tot`, `CanBaoTri`, `HuHong`, `XuongCap`. Dữ liệu cũ `Hong` được đọc/lọc như `HuHong`; khi lưu hồ sơ sẽ dùng mã chuẩn. Không chạy cập nhật hàng loạt dữ liệu cũ.
- Trạng thái chuẩn: `ChuaCapPhat`, `DaCapPhat`, `DangBaoTri`, `ChoThanhLy`, `DaThanhLy`. Trong Sprint 1 chỉ tạo trạng thái ban đầu, không cung cấp action chuyển trạng thái.
- Chỉ session Admin được vào toàn bộ module. Chưa đăng nhập được chuyển về đăng nhập; Nhân viên nhận HTTP 403 kể cả gõ URL hoặc gửi POST trực tiếp.
- Delete GET chỉ xác nhận; POST có antiforgery token. Chặn xóa nếu có chi tiết cấp phát/bảo trì/kiểm kê/thanh lý. Không thêm trường ngừng sử dụng vì chưa có nhu cầu tách trạng thái hồ sơ.
- Cấu hình EF dùng Restrict cho FK trỏ đến tài sản/danh mục. Đã kiểm tra đọc CSDL hiện có: cả 5 FK tương ứng đều là RESTRICT. Cấu hình EF không tự sửa constraint trong database.
- Kiểm tra tồn tại và xử lý lỗi concurrency khi Edit/Delete. Dùng trạng thái hiện có làm concurrency token, không thêm cột. Nếu workflow đổi trạng thái sau khi entity được tải trong request, Save bị từ chối với thông báo tải lại. Chưa áp dụng version token để phát hiện mọi thay đổi hồ sơ giữa hai lần mở form; hai người sửa cùng trường có thể ghi đè theo lần lưu cuối.

## Chức năng hoàn thành

- Danh sách, thêm, xem đủ thông tin, sửa và xóa có xác nhận.
- Kết hợp từ khóa mã/tên, danh mục, tình trạng và trạng thái. Tìm kiếm tuân theo collation MySQL hiện tại.
- Server-side pagination 10/20/50; hiển thị trang, khoảng kết quả và tổng số. Clamp trang ngoài phạm vi; kết quả rỗng hiển thị 0–0.
- Sắp xếp tăng/giảm theo mã, tên, nguyên giá, ngày mua, ngày tạo; thêm TS_ID để ổn định thứ tự khi giá trị bằng nhau.
- Chuyển trang giữ từ khóa/danh mục/tình trạng/trạng thái/sắp xếp/kích thước trang. Gửi form giữ trang hiện tại khi sắp xếp; số trang được điều chỉnh nếu kết quả ít đi. Đặt lại bỏ toàn bộ query và về trang 1.
- Count/Sum chạy trên truy vấn sau bộ lọc và trước Skip/Take, nên tổng nguyên giá bao phủ tất cả các trang phù hợp.
- NiceAdmin/Bootstrap: badge trạng thái có màu riêng, cảnh báo/thông báo và lỗi field; không dùng alert JavaScript.
- Bảo hành được tính khi hiển thị: chưa rõ, hết hạn, sắp hết trong 30 ngày (gồm ngày hết hạn), còn hạn. Không thêm cột dẫn xuất.

## Các file thay đổi trong lần sửa này

**Sửa:**

- `Areas/Admin/Controllers/TaiSanController.cs`
- `Areas/Admin/Models/TaiSanViewModels.cs`
- `Areas/Admin/Views/TaiSan/Index.cshtml`
- `Areas/Admin/Views/TaiSan/Form.cshtml` — chuyển thành partial chỉ chứa các trường hồ sơ được phép nhập.
- `Areas/Admin/Views/TaiSan/_ThongTin.cshtml`
- `Areas/Admin/Views/TaiSan/Delete.cshtml`
- `DoAn.csproj` — loại source/content kiểm thử khỏi ứng dụng Web.

**Thêm:**

- `Areas/Admin/Views/TaiSan/Create.cshtml`
- `Areas/Admin/Views/TaiSan/Edit.cshtml`
- `Services/Assets/AssetValues.cs` — constants tình trạng/trạng thái, nhãn/badge, định dạng tiền/bảo hành.
- `Services/Assets/AssetProfile.cs` — mapping hồ sơ theo danh sách trường cho phép.
- `Services/Assets/AssetQueries.cs` — truy vấn lọc/sắp xếp.
- `Services/Assets/AssetHistory.cs` — kiểm tra dữ liệu nghiệp vụ trước xóa.
- `Data/DataContext.AssetRules.cs` — partial cấu hình Restrict và concurrency, không sửa entity scaffold.
- `Tests/AssetModule.Checks/AssetModule.Checks.csproj`, `Program.cs`, `.gitignore`.
- Tài liệu này.

`Details.cshtml` tiếp tục dùng partial `_ThongTin`, nên nhận hiển thị tiền/bảo hành mới. Layout, session, CSS, các entity và bảng nghiệp vụ giữ cấu trúc hiện có. Những thay đổi từ công việc trước trong workspace không bị hoàn nguyên.

## Kiểm thử

Chạy từ thư mục gốc project:

```powershell
dotnet build --no-restore
dotnet run --project Tests/AssetModule.Checks --no-restore
dotnet run --project Tests/AssetModule.Checks --no-restore -- --mysql-integration --mysql-check
```

Lần đầu ở máy mới cần `dotnet restore Tests/AssetModule.Checks`. EF InMemory chỉ là dependency của project kiểm thử, không thêm provider vào ứng dụng Web.

- Mặc định: 51 ca kiểm tra độc lập dùng EF InMemory, có MVC model binding và kiểm tra SQL Pomelo sinh ra. InMemory không mô phỏng UNIQUE/FK/collation; những phần đó được xác nhận bằng chế độ MySQL riêng.
- `--mysql-integration`: đọc cấu hình kết nối từ `appsettings.json`, cần quyền tạo/xóa database kiểm thử. Tạo schema ngẫu nhiên `qltaisan_checks_<GUID>`, mỗi ca dùng transaction rollback, cuối cùng chỉ xóa schema do lần chạy đó tạo. Không ghi vào database đang cấu hình. Chạy được 56 ca, bao gồm UNIQUE, collation, FK, concurrency và HTTP qua Kestrel.
- HTTP test render thực Index/Create/Edit/Details/Delete, kiểm tra URL pagination/reset, POST Create/Edit giả mạo, CSRF, Admin/Nhân viên/khách. Danh tính session được giả lập **chỉ trong host kiểm thử**; không đổi login của ứng dụng thật.
- `--mysql-check`: chỉ đọc metadata các FK của database đang cấu hình, không sửa schema hay dữ liệu. Nếu kết nối không được, thông báo bỏ qua kiểm tra metadata; không coi đó là đã kiểm thử tích hợp thành công.

## Chuẩn bị cho Sprint sau

- TS_ID/FK hiện có và mã cố định giữ khả năng liên kết cấp phát, bàn giao, thu hồi, bảo trì, kiểm kê, thanh lý, báo cáo và QR.
- `AssetProfile.Update` là điểm tập trung để bổ sung giới hạn trường hoặc ghi audit khi có chứng từ tài chính.
- `AssetHistory.HasHistory` là điểm mở rộng điều kiện chặn xóa khi thêm bảng yêu cầu sửa chữa/timeline. Các bảng đó hiện chưa có, nên không tạo truy vấn hoặc dữ liệu giả cho chúng.
- Chưa triển khai workflow chuyển trạng thái, controller cấp phát/bảo trì/kiểm kê/thanh lý, timeline, QR, in tem, upload ảnh, Excel hoặc khấu hao. Đây là phạm vi các Sprint sau/phần mở rộng, không phải chức năng hồ sơ của Sprint 1.
