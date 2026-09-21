using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;

namespace DTT_Backend_API.Services
{
    /// <summary>
    /// Tự đóng các lịch hẹn của NGÀY TRƯỚC mà bệnh nhân không đi hết quy trình tới bước khám của Bác sĩ.
    ///
    /// Vấn đề: mọi hàng chờ (Lễ tân/Điều dưỡng/Bác sĩ) chỉ lấy lịch hẹn của hôm nay nên các ca cũ tự biến mất khỏi màn hình,
    /// nhưng trong DB chúng vẫn nằm ở trạng thái "đang chờ" mãi mãi → thống kê đếm sai, App Mobile hiện lịch cũ như lịch sắp tới.
    ///
    /// Chỉ đóng các trạng thái CHƯA có bác sĩ tham gia: 1 Scheduled, 2 Waiting, 7 CheckedIn, 8 WaitingForDoctor → 6 NoShow.
    /// KHÔNG đụng 3 InProgress / 9 AwaitingTestResults (bác sĩ đã mở hồ sơ, có thể đã có bệnh án/chỉ định CLS) và
    /// 10 PendingDispensing / 11 PendingPayment (liên quan tiền và thuốc) — các ca này cần người xử lý.
    /// Không gửi thông báo cho bệnh nhân (tránh bắn hàng loạt thông báo "bỏ khám" cho lịch cũ).
    /// </summary>
    public class StaleAppointmentSweeper : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
        private const int NoShowStatusId = 6;
        private static readonly int[] SweepableStatusIds = { 1, 2, 7, 8 };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<StaleAppointmentSweeper> _logger;

        public StaleAppointmentSweeper(IServiceScopeFactory scopeFactory, ILogger<StaleAppointmentSweeper> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Đợi app khởi động xong (seed trạng thái ở Program.cs chạy trước) rồi mới quét lần đầu.
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    // Lỗi thoáng qua (mất kết nối DB...) không được làm sập app — thử lại ở chu kỳ sau.
                    _logger.LogWarning(ex, "[StaleAppointmentSweeper] Lỗi khi quét lịch hẹn cũ");
                }

                try { await Task.Delay(Interval, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }

        private async Task SweepOnceAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // "Hôm nay" theo giờ VN (UTC+7), giống AppointmentsController. Lịch chưa có appointment_date (dữ liệu rất cũ) bị bỏ qua.
            var todayVn = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
            var now = DateTime.UtcNow;

            int affected = await db.Appointments
                .Where(a => a.AppointmentDate != null
                            && a.AppointmentDate < todayVn
                            && SweepableStatusIds.Contains(a.StatusId))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.StatusId, NoShowStatusId)
                    .SetProperty(a => a.UpdatedAt, now), ct);

            if (affected > 0)
                _logger.LogInformation("[StaleAppointmentSweeper] Đã chuyển {Count} lịch hẹn của các ngày trước sang NoShow.", affected);
        }
    }
}
