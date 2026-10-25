# Autostart dengan RAM dan CPU seminimal mungkin

## 1. Menyalakan autostart
Pilih salah satu:
- **Di aplikasi:** buka Settings NOVA → centang *Start NOVA when I sign in to Windows* → Save.
- **Lewat installer:** centang tugas *Start NOVA when I sign in to Windows* saat instalasi.

Keduanya menulis satu nilai di `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (tanpa hak admin) dengan argumen `--startup`.
Cek di **Task Manager → Startup apps** (kolom *Startup impact*), atau **Settings Windows → Apps → Startup**. Untuk mematikan: hapus centang di Settings NOVA, atau matikan di daftar Startup Windows.

**Kenapa `--startup`?** Saat login, NOVA bisa jalan sebelum Explorer siap. Taskbar baru bisa disembunyikan dan ruang layar baru bisa dicadangkan setelah Explorer ada. Dengan `--startup`, NOVA menunggu taskbar muncul (maksimal 30 detik) plus 2 detik, lalu baru membuat jendelanya. Selama menunggu ia hampir tidak memakai apa-apa dan tidak berebut CPU/disk dengan proses boot lain.

## 2. Build yang ringan
```powershell
.\build\publish.ps1            # framework-dependent: butuh .NET 8 Desktop Runtime, folder kecil
```
Sudah diatur di proyek: ReadyToRun (start lebih cepat, lebih sedikit JIT), GC workstation non-concurrent (tanpa thread GC tambahan), tanpa file debug, tanpa bahasa satelit.
Self-contained (`-SelfContained`) hanya menambah ukuran folder, bukan RAM.

## 3. Pengaturan paling hemat
| Pengaturan | Saran | Efek |
|---|---|---|
| Eco mode | **Nyala** (default) | Prioritas rendah, mode efisiensi Windows (EcoQoS), memori yang tidak terpakai dikembalikan tiap 45 detik saat Island tertutup |
| Show system status | Matikan jika tak perlu | Menghentikan pembacaan Wi-Fi/Bluetooth/baterai tiap 5 detik (termasuk `netsh` tiap 60 detik) |
| Show running apps in dock | Matikan jika tak perlu | Menghentikan pengecekan jendela terbuka tiap 2 detik |
| Weather, Lyrics, Clipboard history | Biarkan mati | Tidak ada jaringan, tidak ada pemantau clipboard |
| Live activities | Nyala atau mati | Hanya callback event, hampir tanpa biaya |
| Reduced motion | Nyala jika mau paling hemat | Tanpa animasi equalizer dan visualizer |

Saat Island tertutup, yang masih berjalan: jam (1x per menit), penjaga posisi di atas top bar (tiap 3 detik), pembersih memori (tiap 45 detik) dan, jika dinyalakan, status top bar (5 detik) serta daftar aplikasi dock (2 detik). Visualizer, progres lagu, timer halaman, dan polling status hanya hidup saat Island terbuka di halaman yang membutuhkannya.

## 4. Mengukur sendiri
```powershell
Get-Process NOVA | Select-Object Name, @{n='WorkingSetMB';e={[int]($_.WorkingSet64/1MB)}}, @{n='PrivateMB';e={[int]($_.PrivateMemorySize64/1MB)}}, CPU
```
Atau Task Manager → Details → kolom *Memory (private working set)* dan *CPU*. Biarkan 2 menit setelah login supaya pembersih memori sempat jalan. Angka ini belum saya ukur (aplikasinya belum pernah dijalankan); ukur di PC Anda, dan kirim hasilnya jika terasa berat.

## 5. Jika ada masalah saat boot
- Taskbar Windows masih terlihat: tunggu beberapa detik. Jika terus terjadi, jalankan ulang NOVA dari tray.
- Jalankan `NOVA.exe --safe-mode` untuk hanya jam dan dock, tanpa mengubah apa pun di Windows.
- Log ada di `%LocalAppData%\NOVA\logs`.
