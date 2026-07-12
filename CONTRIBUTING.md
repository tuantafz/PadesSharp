# Contributing to PadesSharp

## Quy tắc chung

- Không copy code từ iText 5/7 hoặc bất kỳ nguồn AGPL/commercial nào
- Mỗi file mới phải có header: `// Original implementation based on public standards, no code copied from iText 5/7.`
- Phải có unit test cho mọi public API
- Chạy `dotnet test` trước khi commit
- Không commit secret/key

## Quy trình

1. Fork repository
2. Tạo nhánh: `feature/ten-tinh-nang` hoặc `fix/ten-loi`
3. Commit + push
4. Mở Pull Request về `main`
5. Đợi CI build + review

## Coding conventions

- Interface trước, implementation sau
- Mọi public API phải có XML doc comment
- Không dùng static mutable state
- Stream phải được dispose đúng
