using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Models;
using DTT_Backend_API.DTOs;
using DTT_Backend_API.Helpers;
using System.Data;
using System.Data.Common;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/work-schedules")]
[Route("api/doctor-schedules")]
public class WorkSchedulesController : ControllerBase
{
    private readonly AppDbContext _context;

    public WorkSchedulesController(AppDbContext context)
    {
        _context = context;
    }



    // ── GET /api/work-schedules (Lấy danh sách Lịch làm & Lịch làm việc chi tiết) ────
    [HttpGet]
    public async Task<IActionResult> GetAllSchedules(
        [FromQuery] int? doctorId,
        [FromQuery] int? specialtyId,
        [FromQuery] string? workDate,
        [FromQuery] string? status,
        [FromQuery] string? search)
    {
        try
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();

            var sql = @"
                SELECT 
                    ds.schedule_id,
                    ds.doctor_id,
                    d.full_name AS doctor_name,
                    d.degree,
                    d.clinic_room,
                    s.specialty_id,
                    s.specialty_name,
                    ds.work_date,
                    ds.start_time,
                    ds.end_time,
                    ds.status,
                    ds.created_at,
                    ds.updated_at
                FROM doctor_schedules ds
                JOIN doctors d ON ds.doctor_id = d.doctor_id
                LEFT JOIN specialties s ON d.specialty_id = s.specialty_id
                WHERE 1=1";

            if (doctorId.HasValue && doctorId.Value > 0)
                sql += $" AND ds.doctor_id = {doctorId.Value}";

            if (specialtyId.HasValue && specialtyId.Value > 0)
                sql += $" AND d.specialty_id = {specialtyId.Value}";

            if (!string.IsNullOrEmpty(workDate))
            {
                if (DateTime.TryParse(workDate, out var dt))
                    sql += $" AND ds.work_date = '{dt:yyyy-MM-dd}'::date";
            }

            if (!string.IsNullOrEmpty(search))
                sql += " AND (LOWER(d.full_name) LIKE LOWER(@search) OR LOWER(s.specialty_name) LIKE LOWER(@search))";

            sql += " ORDER BY ds.schedule_id ASC";

            var scheduleList = new List<WorkScheduleResponseDto>();
            var scheduleIds = new List<int>();
            var rawStatusMap = new Dictionary<int, string>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                if (!string.IsNullOrEmpty(search))
                {
                    var pSearch = cmd.CreateParameter();
                    pSearch.ParameterName = "@search";
                    pSearch.Value = $"%{search.Trim()}%";
                    cmd.Parameters.Add(pSearch);
                }
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int schId = reader.GetInt32(0);
                    int docId = reader.GetInt32(1);
                    string docName = reader.IsDBNull(2) ? "Bác sĩ DTT" : reader.GetString(2);
                    string? specName = reader.IsDBNull(6) ? "Nội tổng quát" : reader.GetString(6);

                    DateTime wDate = reader.GetDateTime(7);
                    /* Old code comment:
                    TimeSpan startTimeSpan = reader.GetTimeSpan(8);
                    TimeSpan endTimeSpan = reader.GetTimeSpan(9);
                    */
                    TimeSpan startTimeSpan = GetTimeSpanValue(reader, 8);
                    TimeSpan endTimeSpan = GetTimeSpanValue(reader, 9);
                    string rawStatus = reader.IsDBNull(10) ? "Available" : reader.GetString(10);
                    DateTime? createdAt = reader.IsDBNull(11) ? null : reader.GetDateTime(11);
                    DateTime? updatedAt = reader.IsDBNull(12) ? null : reader.GetDateTime(12);

                    scheduleIds.Add(schId);
                    rawStatusMap[schId] = rawStatus;

                    scheduleList.Add(new WorkScheduleResponseDto
                    {
                        ScheduleId = schId,
                        DoctorId = docId,
                        DoctorName = docName,
                        Specialty = specName,
                        SpecialtyName = specName,
                        WorkDate = wDate.ToString("dd/MM/yyyy"),
                        StartTime = $"{startTimeSpan.Hours:D2}:{startTimeSpan.Minutes:D2}",
                        EndTime = $"{endTimeSpan.Hours:D2}:{endTimeSpan.Minutes:D2}",
                        Status = NormalizeScheduleStatus(rawStatus),
                        ScheduleCode = schId.ToString(),
                        IsAvailable = rawStatus != "Không hoạt động" && rawStatus != "Unavailable",
                        CreatedAt = createdAt,
                        UpdatedAt = updatedAt,
                        TimeSlots = new List<TimeSlotDto>()
                    });
                }
            }

            // Tải danh sách slots tương ứng từ doctor_schedule_slots
            if (scheduleIds.Count > 0)
            {
                var slotMap = new Dictionary<int, List<TimeSlotDto>>();
                var idsStr = string.Join(",", scheduleIds);
                var slotSql = $@"
                    SELECT 
                        slot_id,
                        schedule_id,
                        slot_order,
                        start_time,
                        end_time,
                        status,
                        created_at,
                        updated_at
                    FROM doctor_schedule_slots
                    WHERE schedule_id IN ({idsStr})
                    ORDER BY schedule_id ASC, slot_order ASC, start_time ASC";

                using (var slotCmd = conn.CreateCommand())
                {
                    slotCmd.CommandText = slotSql;
                    using var slotReader = await slotCmd.ExecuteReaderAsync();
                    while (await slotReader.ReadAsync())
                    {
                        int slotId = slotReader.GetInt32(0);
                        int schId = slotReader.GetInt32(1);
                        int slotOrder = slotReader.GetInt32(2);
                        /* Old code comment:
                        TimeSpan sTime = slotReader.GetTimeSpan(3);
                        TimeSpan eTime = slotReader.GetTimeSpan(4);
                        */
                        TimeSpan sTime = GetTimeSpanValue(slotReader, 3);
                        TimeSpan eTime = GetTimeSpanValue(slotReader, 4);
                        string rawSlotStatus = slotReader.IsDBNull(5) ? "Available" : slotReader.GetString(5);
                        DateTime? createdAt = slotReader.IsDBNull(6) ? null : slotReader.GetDateTime(6);
                        DateTime? updatedAt = slotReader.IsDBNull(7) ? null : slotReader.GetDateTime(7);

                        var slotDto = new TimeSlotDto
                        {
                            SlotId = slotId,
                            ScheduleId = schId,
                            ScheduleCode = schId.ToString(),
                            SlotCode = slotOrder.ToString(),
                            SlotOrder = slotOrder,
                            StartTime = $"{sTime.Hours:D2}:{sTime.Minutes:D2}",
                            EndTime = $"{eTime.Hours:D2}:{eTime.Minutes:D2}",
                            Status = NormalizeSlotStatus(rawSlotStatus),
                            CreatedAt = createdAt,
                            UpdatedAt = updatedAt
                        };

                            if (!slotMap.ContainsKey(schId)) slotMap[schId] = new List<TimeSlotDto>();
                            slotMap[schId].Add(slotDto);
                        }
                    }

                    // Gán slots vào danh sách ca làm việc siêu tốc và đồng bộ CSDL PostgreSQL
                    foreach (var sch in scheduleList)
                    {
                        var rawSlots = slotMap.TryGetValue(sch.ScheduleId, out var existing) ? existing : new List<TimeSlotDto>();

                        /* [OLD CODE COMMENTED OUT — tự động sinh thêm nấc 30 phút làm lệch số lượng khung giờ so với CSDL thật]
                        var slots = Generate30MinSlotsInMemory(sch.StartTime, sch.EndTime, sch.ScheduleId, rawSlots);
                        */

                        // Ưu tiên lấy đúng 100% danh sách khung giờ thật từ database:
                        var slots = (rawSlots != null && rawSlots.Count > 0)
                            ? rawSlots.OrderBy(s => s.SlotOrder).ThenBy(s => s.StartTime).ToList()
                            : Generate30MinSlotsInMemory(sch.StartTime, sch.EndTime, sch.ScheduleId, rawSlots);

                        sch.TimeSlots = slots;
                        string calculatedStatus = CalculateParentStatus(sch.Status, slots);
                        sch.Status = calculatedStatus;

                        // Đồng bộ trạng thái vào bảng doctor_schedules trong CSDL PostgreSQL
                        string rawDbStatus = rawStatusMap.TryGetValue(sch.ScheduleId, out var dbSt) ? dbSt : "";
                        string targetDenormalized = DenormalizeScheduleStatus(calculatedStatus);
                        if (rawDbStatus != targetDenormalized && rawDbStatus != "Unavailable" && rawDbStatus != "Off")
                        {
                            try
                            {
                                using var updateCmd = conn.CreateCommand();
                                updateCmd.CommandText = "UPDATE doctor_schedules SET status = @st, updated_at = NOW() WHERE schedule_id = @id";
                                var p1 = updateCmd.CreateParameter(); p1.ParameterName = "@st"; p1.Value = targetDenormalized; updateCmd.Parameters.Add(p1);
                                var p2 = updateCmd.CreateParameter(); p2.ParameterName = "@id"; p2.Value = sch.ScheduleId; updateCmd.Parameters.Add(p2);
                                await updateCmd.ExecuteNonQueryAsync();
                            }
                            catch (Exception dbEx)
                            {
                                Console.WriteLine($"[UpdateStatus Warning] Schedule {sch.ScheduleId}: {dbEx.Message}");
                            }
                        }
                    }
                }

                // Lọc theo status nếu có
                if (!string.IsNullOrEmpty(status) && status != "Tất cả")
                {
                    scheduleList = scheduleList.Where(s => s.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return Ok(scheduleList);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error in GetAllSchedules: " + ex.Message);
            return StatusCode(500, new { success = false, message = "Lỗi khi lấy danh sách lịch làm việc: " + ex.Message });
        }
    }

    // ── GET /api/work-schedules/{id} (Chi tiết 1 Lịch làm) ───────────────────
    [HttpGet("{id}")]
    public async Task<IActionResult> GetScheduleById(int id)
    {
        try
        {
            var listResult = await GetAllSchedules(null, null, null, null, null);
            if (listResult is OkObjectResult okObj && okObj.Value is List<WorkScheduleResponseDto> list)
            {
                var item = list.FirstOrDefault(s => s.ScheduleId == id);
                if (item != null) return Ok(item);
            }
            return NotFound(new { success = false, message = "Không tìm thấy lịch làm việc." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi: " + ex.Message });
        }
    }

    // ── POST /api/work-schedules (Tạo mới Lịch làm của bác sĩ) ───────────────
    // Chỉ nhân viên y tế (Web Admin) được tạo/sửa lịch làm việc của Bác sĩ — bệnh nhân
    // (role_id=3) tuyệt đối không được ghi đè lịch trực của người khác.
    [HttpPost]
    [StaffOnly]
    public async Task<IActionResult> CreateSchedule([FromBody] CreateWorkScheduleDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.DoctorId == dto.DoctorId);
            if (doctor == null)
            {
                return BadRequest(new { success = false, message = $"Không tìm thấy Bác sĩ với DoctorId = {dto.DoctorId}." });
            }

            if (!TryParseDate(dto.WorkDate, out DateTime parsedWorkDate))
            {
                return BadRequest(new { success = false, message = "Định dạng WorkDate không hợp lệ. Vui lòng sử dụng DD/MM/YYYY hoặc YYYY-MM-DD." });
            }

            TimeSpan startTime = ParseTime(dto.StartTime, new TimeSpan(8, 0, 0));
            TimeSpan endTime = ParseTime(dto.EndTime, new TimeSpan(17, 0, 0));

            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();

            int newScheduleId = 0;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO doctor_schedules (doctor_id, work_date, start_time, end_time, status, created_at, updated_at)
                    VALUES (@docId, @wDate, @sTime, @eTime, 'Available', NOW(), NOW())
                    RETURNING schedule_id;";

                var p1 = cmd.CreateParameter(); p1.ParameterName = "@docId"; p1.Value = dto.DoctorId; cmd.Parameters.Add(p1);
                var p2 = cmd.CreateParameter(); p2.ParameterName = "@wDate"; p2.Value = parsedWorkDate.Date; cmd.Parameters.Add(p2);
                var p3 = cmd.CreateParameter(); p3.ParameterName = "@sTime"; p3.Value = startTime; cmd.Parameters.Add(p3);
                var p4 = cmd.CreateParameter(); p4.ParameterName = "@eTime"; p4.Value = endTime; cmd.Parameters.Add(p4);

                var obj = await cmd.ExecuteScalarAsync();
                newScheduleId = Convert.ToInt32(obj);
            }

            // Tự động sinh ra các khung giờ 30 phút (slot 30 mins)
            int slotOrder = 1;
            TimeSpan currSlotStart = startTime;
            TimeSpan slotDuration = TimeSpan.FromMinutes(30);

            while (currSlotStart + slotDuration <= endTime)
            {
                TimeSpan currSlotEnd = currSlotStart + slotDuration;
                using (var slotCmd = conn.CreateCommand())
                {
                    slotCmd.CommandText = @"
                        INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status, created_at, updated_at)
                        VALUES (@schId, @sOrder, @sTime, @eTime, 'Available', NOW(), NOW());";

                    var sp1 = slotCmd.CreateParameter(); sp1.ParameterName = "@schId"; sp1.Value = newScheduleId; slotCmd.Parameters.Add(sp1);
                    var sp2 = slotCmd.CreateParameter(); sp2.ParameterName = "@sOrder"; sp2.Value = slotOrder; slotCmd.Parameters.Add(sp2);
                    var sp3 = slotCmd.CreateParameter(); sp3.ParameterName = "@sTime"; sp3.Value = currSlotStart; slotCmd.Parameters.Add(sp3);
                    var sp4 = slotCmd.CreateParameter(); sp4.ParameterName = "@eTime"; sp4.Value = currSlotEnd; slotCmd.Parameters.Add(sp4);

                    await slotCmd.ExecuteNonQueryAsync();
                }
                slotOrder++;
                currSlotStart = currSlotEnd;
            }

            var result = await GetScheduleById(newScheduleId);
            return Ok(new { success = true, message = "Tạo lịch làm việc cho Bác sĩ thành dụng.", data = (result as OkObjectResult)?.Value });
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error in CreateSchedule: " + ex.Message);
            return StatusCode(500, new { success = false, message = "Lỗi khi tạo lịch làm việc: " + ex.Message });
        }
    }

    // ── PUT /api/work-schedules/{id} (Cập nhật Lịch làm của bác sĩ) ───────────
    [HttpPut("{id}")]
    [StaffOnly]
    public async Task<IActionResult> UpdateSchedule(int id, [FromBody] UpdateWorkScheduleDto dto)
    {
        try
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();

            if (dto.DoctorId.HasValue && dto.DoctorId.Value > 0)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE doctor_schedules SET doctor_id = @docId, updated_at = NOW() WHERE schedule_id = @id";
                var p1 = cmd.CreateParameter(); p1.ParameterName = "@docId"; p1.Value = dto.DoctorId.Value; cmd.Parameters.Add(p1);
                var p2 = cmd.CreateParameter(); p2.ParameterName = "@id"; p2.Value = id; cmd.Parameters.Add(p2);
                await cmd.ExecuteNonQueryAsync();
            }

            if (!string.IsNullOrEmpty(dto.WorkDate) && TryParseDate(dto.WorkDate, out DateTime parsedWorkDate))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE doctor_schedules SET work_date = @wDate, updated_at = NOW() WHERE schedule_id = @id";
                var p1 = cmd.CreateParameter(); p1.ParameterName = "@wDate"; p1.Value = parsedWorkDate.Date; cmd.Parameters.Add(p1);
                var p2 = cmd.CreateParameter(); p2.ParameterName = "@id"; p2.Value = id; cmd.Parameters.Add(p2);
                await cmd.ExecuteNonQueryAsync();
            }

            if (!string.IsNullOrEmpty(dto.StartTime) && !string.IsNullOrEmpty(dto.EndTime))
            {
                TimeSpan sTime = ParseTime(dto.StartTime, new TimeSpan(8, 0, 0));
                TimeSpan eTime = ParseTime(dto.EndTime, new TimeSpan(17, 0, 0));

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE doctor_schedules SET start_time = @sTime, end_time = @eTime, updated_at = NOW() WHERE schedule_id = @id";
                var p1 = cmd.CreateParameter(); p1.ParameterName = "@sTime"; p1.Value = sTime; cmd.Parameters.Add(p1);
                var p2 = cmd.CreateParameter(); p2.ParameterName = "@eTime"; p2.Value = eTime; cmd.Parameters.Add(p2);
                var p3 = cmd.CreateParameter(); p3.ParameterName = "@id"; p3.Value = id; cmd.Parameters.Add(p3);
                await cmd.ExecuteNonQueryAsync();

                // Xóa các khung giờ cũ nằm ngoài phạm vi thời gian mới (CHỈ XÓA khung giờ Chưa đặt lịch và Đã đóng, KHÔNG XÓA Đã đặt lịch / Booked)
                using var delCmd = conn.CreateCommand();
                delCmd.CommandText = "DELETE FROM doctor_schedule_slots WHERE schedule_id = @id AND status != 'Booked' AND (start_time < @sTime OR end_time > @eTime)";
                var dp1 = delCmd.CreateParameter(); dp1.ParameterName = "@id"; dp1.Value = id; delCmd.Parameters.Add(dp1);
                var dp2 = delCmd.CreateParameter(); dp2.ParameterName = "@sTime"; dp2.Value = sTime; delCmd.Parameters.Add(dp2);
                var dp3 = delCmd.CreateParameter(); dp3.ParameterName = "@eTime"; dp3.Value = eTime; delCmd.Parameters.Add(dp3);
                await delCmd.ExecuteNonQueryAsync();
            }

            if (!string.IsNullOrEmpty(dto.Status))
            {
                string rawStatus = DenormalizeScheduleStatus(dto.Status);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE doctor_schedules SET status = @st, updated_at = NOW() WHERE schedule_id = @id";
                var p1 = cmd.CreateParameter(); p1.ParameterName = "@st"; p1.Value = rawStatus; cmd.Parameters.Add(p1);
                var p2 = cmd.CreateParameter(); p2.ParameterName = "@id"; p2.Value = id; cmd.Parameters.Add(p2);
                await cmd.ExecuteNonQueryAsync();
            }

            // Cập nhật hoặc Thêm mới danh sách khung giờ từ front-end
            if (dto.TimeSlots != null && dto.TimeSlots.Count > 0)
            {
                foreach (var slot in dto.TimeSlots)
                {
                    string rawSlotStatus = DenormalizeSlotStatus(slot.Status);
                    TimeSpan slotStart = ParseTime(slot.StartTime, new TimeSpan(8, 0, 0));
                    TimeSpan slotEnd = ParseTime(slot.EndTime, new TimeSpan(8, 30, 0));

                    try
                    {
                        if (slot.SlotId > 0)
                        {
                            using var slotCmd = conn.CreateCommand();
                            slotCmd.CommandText = "UPDATE doctor_schedule_slots SET start_time = @sTime, end_time = @eTime, status = @st, updated_at = NOW() WHERE slot_id = @slotId";
                            var sp1 = slotCmd.CreateParameter(); sp1.ParameterName = "@sTime"; sp1.Value = slotStart; slotCmd.Parameters.Add(sp1);
                            var sp2 = slotCmd.CreateParameter(); sp2.ParameterName = "@eTime"; sp2.Value = slotEnd; slotCmd.Parameters.Add(sp2);
                            var sp3 = slotCmd.CreateParameter(); sp3.ParameterName = "@st"; sp3.Value = rawSlotStatus; slotCmd.Parameters.Add(sp3);
                            var sp4 = slotCmd.CreateParameter(); sp4.ParameterName = "@slotId"; sp4.Value = slot.SlotId; slotCmd.Parameters.Add(sp4);
                            await slotCmd.ExecuteNonQueryAsync();
                        }
                        else
                        {
                            using var insCmd = conn.CreateCommand();
                            insCmd.CommandText = @"
                                INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status, created_at, updated_at) 
                                VALUES (@schId, @order, @sTime, @eTime, @st, NOW(), NOW())
                                ON CONFLICT (schedule_id, start_time) 
                                DO UPDATE SET end_time = EXCLUDED.end_time, status = EXCLUDED.status, updated_at = NOW();";
                            var ip1 = insCmd.CreateParameter(); ip1.ParameterName = "@schId"; ip1.Value = id; insCmd.Parameters.Add(ip1);
                            var ip2 = insCmd.CreateParameter(); ip2.ParameterName = "@order"; ip2.Value = slot.SlotOrder; insCmd.Parameters.Add(ip2);
                            var ip3 = insCmd.CreateParameter(); ip3.ParameterName = "@sTime"; ip3.Value = slotStart; insCmd.Parameters.Add(ip3);
                            var ip4 = insCmd.CreateParameter(); ip4.ParameterName = "@eTime"; ip4.Value = slotEnd; insCmd.Parameters.Add(ip4);
                            var ip5 = insCmd.CreateParameter(); ip5.ParameterName = "@st"; ip5.Value = rawSlotStatus; insCmd.Parameters.Add(ip5);
                            await insCmd.ExecuteNonQueryAsync();
                        }
                    }
                    catch (Exception slotEx)
                    {
                        Console.WriteLine($"[UpdateSlot Warning] Schedule {id}, Slot {slot.StartTime}: {slotEx.Message}");
                    }
                }
            }

            // Đồng bộ và bảo đảm đầy đủ các khung giờ 30 phút vật lý vào PostgreSQL DB khi cập nhật
            try
            {
                string sTimeStr = dto.StartTime ?? "08:00";
                string eTimeStr = dto.EndTime ?? "17:00";
                using (var getCmd = conn.CreateCommand())
                {
                    getCmd.CommandText = "SELECT start_time, end_time FROM doctor_schedules WHERE schedule_id = @id";
                    var gp = getCmd.CreateParameter(); gp.ParameterName = "@id"; gp.Value = id; getCmd.Parameters.Add(gp);
                    using var gr = await getCmd.ExecuteReaderAsync();
                    if (await gr.ReadAsync())
                    {
                        TimeSpan st = GetTimeSpanValue(gr, 0);
                        TimeSpan et = GetTimeSpanValue(gr, 1);
                        sTimeStr = $"{st.Hours:D2}:{st.Minutes:D2}";
                        eTimeStr = $"{et.Hours:D2}:{et.Minutes:D2}";
                    }
                }
                var currentRawSlots = new List<TimeSlotDto>();
                using (var slotCmd = conn.CreateCommand())
                {
                    slotCmd.CommandText = "SELECT slot_id, schedule_id, slot_order, start_time, end_time, status FROM doctor_schedule_slots WHERE schedule_id = @id ORDER BY start_time ASC";
                    var sp = slotCmd.CreateParameter(); sp.ParameterName = "@id"; sp.Value = id; slotCmd.Parameters.Add(sp);
                    using var sr = await slotCmd.ExecuteReaderAsync();
                    while (await sr.ReadAsync())
                    {
                        TimeSpan st = GetTimeSpanValue(sr, 3);
                        TimeSpan et = GetTimeSpanValue(sr, 4);
                        currentRawSlots.Add(new TimeSlotDto
                        {
                            SlotId = sr.GetInt32(0),
                            ScheduleId = sr.GetInt32(1),
                            SlotOrder = sr.GetInt32(2),
                            StartTime = $"{st.Hours:D2}:{st.Minutes:D2}",
                            EndTime = $"{et.Hours:D2}:{et.Minutes:D2}",
                            Status = NormalizeSlotStatus(sr.IsDBNull(5) ? "Available" : sr.GetString(5))
                        });
                    }
                }
                await SyncAndEnsure30MinSlotsAsync(conn, sTimeStr, eTimeStr, id, currentRawSlots);
            }
            catch (Exception syncEx)
            {
                Console.WriteLine($"[EnsureSlots Warning] Schedule {id}: {syncEx.Message}");
            }

            var result = await GetScheduleById(id);
            return Ok(new { success = true, message = "Cập nhật lịch làm việc thành công.", data = (result as OkObjectResult)?.Value });
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error in UpdateSchedule: " + ex.Message);
            return StatusCode(500, new { success = false, message = "Lỗi khi cập nhật lịch làm việc: " + ex.Message });
        }
    }

    // ── PUT /api/work-schedules/{id}/toggle-lock (Khóa / Mở khóa ca trực) ─────
    [HttpPut("{id}/toggle-lock")]
    [HttpPut("{id}/lock")]
    [StaffOnly]
    public async Task<IActionResult> ToggleLockSchedule(int id, [FromBody] ToggleLockScheduleDto? dto = null)
    {
        try
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();

            string currentStatus = "Available";
            using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.CommandText = "SELECT status FROM doctor_schedules WHERE schedule_id = @id";
                var p = checkCmd.CreateParameter(); p.ParameterName = "@id"; p.Value = id; checkCmd.Parameters.Add(p);
                var obj = await checkCmd.ExecuteScalarAsync();
                if (obj != null && obj != DBNull.Value) currentStatus = Convert.ToString(obj) ?? "Available";
            }

            bool isCurrentlyLocked = currentStatus == "Off" || currentStatus == "Unavailable" || currentStatus == "Không hoạt động";
            bool lockAction = !isCurrentlyLocked;

            if (dto?.IsLocked.HasValue == true)
            {
                lockAction = dto.IsLocked.Value;
            }
            else if (!string.IsNullOrEmpty(dto?.Status))
            {
                lockAction = (dto.Status == "Không hoạt động" || dto.Status == "Off");
            }

            if (lockAction)
            {
                // Khi KHÓA: Đổi ca trực sang "Unavailable", cập nhật các slot "Available" -> "Closed". Giữ nguyên các slot "Booked"!
                using var cmd1 = conn.CreateCommand();
                cmd1.CommandText = "UPDATE doctor_schedules SET status = 'Unavailable', updated_at = NOW() WHERE schedule_id = @id";
                var p1 = cmd1.CreateParameter(); p1.ParameterName = "@id"; p1.Value = id; cmd1.Parameters.Add(p1);
                await cmd1.ExecuteNonQueryAsync();

                using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = "UPDATE doctor_schedule_slots SET status = 'Closed', updated_at = NOW() WHERE schedule_id = @id AND status != 'Booked'";
                var p2 = cmd2.CreateParameter(); p2.ParameterName = "@id"; p2.Value = id; cmd2.Parameters.Add(p2);
                await cmd2.ExecuteNonQueryAsync();
            }
            else
            {
                // Khi MỞ KHÓA: Đổi slot "Closed" quay lại "Available"
                using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = "UPDATE doctor_schedule_slots SET status = 'Available', updated_at = NOW() WHERE schedule_id = @id AND status = 'Closed'";
                var p2 = cmd2.CreateParameter(); p2.ParameterName = "@id"; p2.Value = id; cmd2.Parameters.Add(p2);
                await cmd2.ExecuteNonQueryAsync();

                using var cmd1 = conn.CreateCommand();
                cmd1.CommandText = "UPDATE doctor_schedules SET status = 'Available', updated_at = NOW() WHERE schedule_id = @id";
                var p1 = cmd1.CreateParameter(); p1.ParameterName = "@id"; p1.Value = id; cmd1.Parameters.Add(p1);
                await cmd1.ExecuteNonQueryAsync();
            }

            var result = await GetScheduleById(id);
            return Ok(new { success = true, message = lockAction ? "Đã khóa lịch làm việc thành công." : "Đã mở khóa lịch làm việc thành công.", data = (result as OkObjectResult)?.Value });
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error in ToggleLockSchedule: " + ex.Message);
            return StatusCode(500, new { success = false, message = "Lỗi khi đổi trạng thái khóa: " + ex.Message });
        }
    }

    // ── PUT /api/work-schedules/{id}/cancel (Hủy ca trực — CHỈ HỦY, KHÔNG XÓA ROW) ──────
    [HttpPut("{id}/cancel")]
    [HttpDelete("{id}")]
    [StaffOnly]
    public async Task<IActionResult> CancelSchedule(int id)
    {
        try
        {
            // CHỈ HỦY BẰNG CÁCH CHUYỂN TRẠNG THÁI SANG KHÔNG HOẠT ĐỘNG, KHÔNG XÓA CSDL
            return await ToggleLockSchedule(id, new ToggleLockScheduleDto { IsLocked = true, Status = "Không hoạt động" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi khi hủy lịch làm việc: " + ex.Message });
        }
    }

    // ── Helper Methods ────────────────────────────────────────────────────────
    private static string NormalizeScheduleStatus(string rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus)) return "Trống lịch";
        var s = rawStatus.Trim();
        if (s == "Available") return "Trống lịch";
        if (s == "Partially Booked") return "Còn lịch để đặt";
        if (s == "Fully Booked") return "Đã hết lịch để đặt";
        if (s == "Unavailable" || s == "Off" || s == "Không hoạt động") return "Không hoạt động";
        return s;
    }

    private static string DenormalizeScheduleStatus(string statusVi)
    {
        if (string.IsNullOrWhiteSpace(statusVi)) return "Available";
        var s = statusVi.Trim();
        if (s == "Trống lịch") return "Available";
        if (s == "Còn lịch để đặt") return "Partially Booked";
        if (s == "Đã hết lịch để đặt") return "Fully Booked";
        if (s == "Không hoạt động") return "Unavailable";
        return s;
    }

    private static string NormalizeSlotStatus(string rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus)) return "Chưa đặt lịch";
        var s = rawStatus.Trim();
        if (s == "Available") return "Chưa đặt lịch";
        if (s == "Booked") return "Đã đặt lịch";
        if (s == "Closed" || s == "Off") return "Đã đóng";
        return s;
    }

    private static string DenormalizeSlotStatus(string statusVi)
    {
        if (string.IsNullOrWhiteSpace(statusVi)) return "Available";
        var s = statusVi.Trim();
        if (s == "Chưa đặt lịch") return "Available";
        if (s == "Đã đặt lịch") return "Booked";
        if (s == "Đã đóng") return "Closed";
        return s;
    }

    private static async Task<List<TimeSlotDto>> SyncAndEnsure30MinSlotsAsync(DbConnection conn, string startTimeStr, string endTimeStr, int scheduleId, List<TimeSlotDto> rawSlots)
    {
        var result = new List<TimeSlotDto>();
        TimeSpan start = ParseTime(startTimeStr, new TimeSpan(8, 0, 0));
        TimeSpan end = ParseTime(endTimeStr, new TimeSpan(17, 0, 0));
        TimeSpan step = TimeSpan.FromMinutes(30);

        int order = 1;
        for (TimeSpan curr = start; curr + step <= end; curr += step)
        {
            TimeSpan next = curr + step;
            string sStart = $"{curr.Hours:D2}:{curr.Minutes:D2}";
            string sEnd = $"{next.Hours:D2}:{next.Minutes:D2}";

            var matched = rawSlots?.FirstOrDefault(s => {
                TimeSpan st = ParseTime(s.StartTime, TimeSpan.Zero);
                TimeSpan et = ParseTime(s.EndTime, TimeSpan.Zero);
                return st <= curr && et >= next;
            });

            if (matched != null)
            {
                result.Add(new TimeSlotDto
                {
                    SlotId = matched.SlotId,
                    ScheduleId = scheduleId,
                    ScheduleCode = scheduleId.ToString(),
                    SlotCode = order.ToString(),
                    SlotOrder = order,
                    StartTime = sStart,
                    EndTime = sEnd,
                    Status = matched.Status,
                    CreatedAt = matched.CreatedAt,
                    UpdatedAt = matched.UpdatedAt
                });
            }
            else
            {
                int newSlotId = 0;
                try
                {
                    using var insCmd = conn.CreateCommand();
                    insCmd.CommandText = @"
                        INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status, created_at, updated_at)
                        VALUES (@schId, (SELECT COALESCE(MAX(slot_order), 0) + 1 FROM doctor_schedule_slots WHERE schedule_id = @schId), @sTime, @eTime, 'Available', NOW(), NOW())
                        ON CONFLICT (schedule_id, start_time) 
                        DO UPDATE SET end_time = EXCLUDED.end_time, updated_at = NOW()
                        RETURNING slot_id;";
                    var p1 = insCmd.CreateParameter(); p1.ParameterName = "@schId"; p1.Value = scheduleId; insCmd.Parameters.Add(p1);
                    var p3 = insCmd.CreateParameter(); p3.ParameterName = "@sTime"; p3.Value = curr; insCmd.Parameters.Add(p3);
                    var p4 = insCmd.CreateParameter(); p4.ParameterName = "@eTime"; p4.Value = next; insCmd.Parameters.Add(p4);

                    var objId = await insCmd.ExecuteScalarAsync();
                    if (objId != null && objId != DBNull.Value) newSlotId = Convert.ToInt32(objId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[InsertMissingSlot Warning] Schedule {scheduleId}, Slot {sStart}: {ex.Message}");
                }

                result.Add(new TimeSlotDto
                {
                    SlotId = newSlotId,
                    ScheduleId = scheduleId,
                    ScheduleCode = scheduleId.ToString(),
                    SlotCode = order.ToString(),
                    SlotOrder = order,
                    StartTime = sStart,
                    EndTime = sEnd,
                    Status = "Chưa đặt lịch"
                });
            }
            order++;
        }

        try
        {
            using var reorderCmd = conn.CreateCommand();
            reorderCmd.CommandText = @"
                UPDATE doctor_schedule_slots SET slot_order = 10000 + slot_id WHERE schedule_id = @schId;
                WITH reordered AS (
                    SELECT slot_id, ROW_NUMBER() OVER (PARTITION BY schedule_id ORDER BY start_time ASC) AS new_order
                    FROM doctor_schedule_slots
                    WHERE schedule_id = @schId
                )
                UPDATE doctor_schedule_slots s
                SET slot_order = r.new_order
                FROM reordered r
                WHERE s.slot_id = r.slot_id;";
            var p1 = reorderCmd.CreateParameter(); p1.ParameterName = "@schId"; p1.Value = scheduleId; reorderCmd.Parameters.Add(p1);
            await reorderCmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ReorderSlots Warning] Schedule {scheduleId}: {ex.Message}");
        }

        return result.Count > 0 ? result : (rawSlots ?? new List<TimeSlotDto>());
    }

    private static List<TimeSlotDto> Generate30MinSlotsInMemory(string startTimeStr, string endTimeStr, int scheduleId, List<TimeSlotDto> rawSlots)
    {
        var result = new List<TimeSlotDto>();
        if (!TimeSpan.TryParse(startTimeStr, out var startTs) || !TimeSpan.TryParse(endTimeStr, out var endTs))
        {
            return rawSlots != null ? rawSlots.OrderBy(s => s.SlotOrder).ThenBy(s => s.StartTime).ToList() : new List<TimeSlotDto>();
        }

        var existingMap = new Dictionary<string, TimeSlotDto>();
        if (rawSlots != null)
        {
            foreach (var slot in rawSlots)
            {
                if (!string.IsNullOrEmpty(slot.StartTime))
                {
                    string key = slot.StartTime.Length >= 5 ? slot.StartTime.Substring(0, 5) : slot.StartTime;
                    existingMap[key] = slot;
                }
            }
        }

        int order = 1;
        var curr = startTs;
        while (curr < endTs)
        {
            var next = curr.Add(TimeSpan.FromMinutes(30));
            if (next > endTs) break;

            string timeKey = $"{curr.Hours:D2}:{curr.Minutes:D2}";
            string nextKey = $"{next.Hours:D2}:{next.Minutes:D2}";

            if (existingMap.TryGetValue(timeKey, out var existingSlot))
            {
                existingSlot.SlotOrder = order;
                existingSlot.SlotCode = order.ToString();
                result.Add(existingSlot);
            }
            else
            {
                result.Add(new TimeSlotDto
                {
                    SlotId = 0,
                    ScheduleId = scheduleId,
                    ScheduleCode = scheduleId.ToString(),
                    SlotCode = order.ToString(),
                    SlotOrder = order,
                    StartTime = timeKey,
                    EndTime = nextKey,
                    Status = "Chưa đặt lịch"
                });
            }
            order++;
            curr = next;
        }

        return result;
    }

    private static string CalculateParentStatus(string currentStatus, List<TimeSlotDto> slots)
    {
        if (currentStatus == "Không hoạt động" || currentStatus == "Unavailable") return "Không hoạt động";
        if (slots == null || slots.Count == 0) return "Trống lịch";

        bool hasBooked = slots.Any(s => s.Status == "Đã đặt lịch" || s.Status == "Booked");
        bool hasAvailable = slots.Any(s => s.Status == "Chưa đặt lịch" || s.Status == "Available");

        if (hasBooked && hasAvailable) return "Còn lịch để đặt";
        if (hasBooked && !hasAvailable) return "Đã hết lịch để đặt";
        if (!hasBooked && hasAvailable) return "Trống lịch";
        if (slots.All(s => s.Status == "Đã đóng" || s.Status == "Closed")) return "Không hoạt động";

        return currentStatus;
    }

    private static bool TryParseDate(string dateStr, out DateTime dt)
    {
        dt = DateTime.MinValue;
        if (string.IsNullOrWhiteSpace(dateStr)) return false;
        dateStr = dateStr.Trim();

        string[] formats = { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "yyyy/MM/dd" };
        if (DateTime.TryParseExact(dateStr, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
        {
            return true;
        }
        return DateTime.TryParse(dateStr, out dt);
    }

    private static TimeSpan ParseTime(string timeStr, TimeSpan fallback)
    {
        if (string.IsNullOrWhiteSpace(timeStr)) return fallback;
        if (TimeSpan.TryParse(timeStr.Trim(), out var ts)) return ts;
        return fallback;
    }

    private static TimeSpan GetTimeSpanValue(System.Data.Common.DbDataReader reader, int colIndex)
    {
        if (reader.IsDBNull(colIndex)) return TimeSpan.Zero;
        var val = reader.GetValue(colIndex);
        if (val is TimeSpan ts) return ts;
        if (val is DateTime dt) return dt.TimeOfDay;
        if (TimeSpan.TryParse(val?.ToString(), out var parsed)) return parsed;
        return TimeSpan.Zero;
    }
}
