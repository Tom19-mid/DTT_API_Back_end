using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Services;

// Kết quả 1 lượt gọi Gemini — Success=false khi lỗi mạng/timeout/quota/parse JSON thất bại;
// ChatController dùng Success để quyết định lưu Reply mặc định (graceful degradation, xem
// Tai Lieu/ai_chatbot_roadmap.md mục 5.6) thay vì để lỗi văng thẳng ra Mobile App.
public record GeminiChatResult(bool Success, string Reply, string? SuggestedSpecialtyName, bool ShouldEscalate, bool ShouldClose = false);

public class GeminiService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    public GeminiService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    private const string DefaultFallbackReply =
        "Xin lỗi, hệ thống trợ lý AI hiện đang bận. Bạn có muốn chuyển sang tư vấn trực tiếp với Nhân viên Lễ tân không?";

    // history phải đã sắp theo created_at tăng dần và đã bao gồm tin nhắn mới nhất của bệnh nhân.
    // Chỉ lấy tin nhắn Patient/AI (Staff không tham gia vào ngữ cảnh gửi cho Gemini).
    public async Task<GeminiChatResult> GetReplyAsync(IEnumerable<ChatMessage> history, IReadOnlyList<string> specialtyNames, CancellationToken ct = default)
    {
        var historyList = history.ToList();

        // Retry 1 lần khi gặp lỗi mạng có tính chất tạm thời (socket bị reset/aborted giữa chừng —
        // quan sát thực tế cho thấy Gemini API thỉnh thoảng rớt kết nối rồi lần gọi tiếp theo thành công
        // ngay). KHÔNG retry khi lỗi rõ ràng là "cấu hình sai" (thiếu key) — retry vô ích, chỉ tổ chờ lâu.
        var attempt1 = await TryCallOnceAsync(historyList, specialtyNames, ct);
        if (attempt1 != null) return attempt1;

        await Task.Delay(400, ct);
        var attempt2 = await TryCallOnceAsync(historyList, specialtyNames, ct);
        return attempt2 ?? new GeminiChatResult(false, DefaultFallbackReply, null, true);
    }

    // Trả về null khi lỗi (để GetReplyAsync quyết định có retry hay không), khác với việc trả thẳng
    // GeminiChatResult(Success=false,...) — tránh lẫn giữa "đã thử và fallback" với "chưa thử được".
    private async Task<GeminiChatResult?> TryCallOnceAsync(IReadOnlyList<ChatMessage> history, IReadOnlyList<string> specialtyNames, CancellationToken ct)
    {
        var apiKey = _config["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return new GeminiChatResult(false, DefaultFallbackReply, null, true); // sai cấu hình — không retry

        var model = _config["Gemini:Model"] ?? "gemini-3.6-flash";
        var timeoutSeconds = _config.GetValue<int?>("Gemini:TimeoutSeconds") ?? 12;

        var contents = history
            .Where(m => m.SenderType == "Patient" || m.SenderType == "AI")
            .Select(m => new
            {
                role = m.SenderType == "Patient" ? "user" : "model",
                parts = new[] { new { text = m.Content } }
            })
            .ToArray();

        if (contents.Length == 0)
            return new GeminiChatResult(false, DefaultFallbackReply, null, true);

        var requestBody = new
        {
            systemInstruction = new { parts = new[] { new { text = BuildSystemInstruction(specialtyNames) } } },
            contents,
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        reply = new { type = "STRING" },
                        suggested_specialty_name = new { type = "STRING", nullable = true },
                        should_escalate = new { type = "BOOLEAN" },
                        should_close = new { type = "BOOLEAN" }
                    },
                    required = new[] { "reply", "should_escalate", "should_close" }
                }
            }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _http.PostAsync(url, content, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(cts.Token);
                Console.WriteLine($"[GeminiService] HTTP {(int)response.StatusCode} từ Gemini API: {errBody}");
                return null; // có thể là lỗi tạm thời (503/429/socket phía server Google) — để GetReplyAsync retry
            }

            var responseJson = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(responseJson);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            if (string.IsNullOrWhiteSpace(text))
            {
                Console.WriteLine($"[GeminiService] Response không có 'text': {responseJson}");
                return null;
            }

            var parsed = JsonSerializer.Deserialize<GeminiStructuredReply>(text);
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.Reply))
            {
                Console.WriteLine($"[GeminiService] Không parse được structured reply: {text}");
                return null;
            }

            return new GeminiChatResult(true, parsed.Reply, parsed.SuggestedSpecialtyName, parsed.ShouldEscalate, parsed.ShouldClose);
        }
        catch (Exception ex)
        {
            // Bắt mọi lỗi (timeout, mất mạng, quota hết, JSON không hợp lệ) — không để lỗi của Gemini
            // làm hỏng trải nghiệm bệnh nhân (graceful degradation, xử lý ở tầng GetReplyAsync qua retry
            // + fallback cuối cùng). Vẫn log ra console để debug được nguyên nhân thật.
            Console.WriteLine($"[GeminiService] Exception: {ex}");
            return null;
        }
    }

    private static string BuildSystemInstruction(IReadOnlyList<string> specialtyNames)
    {
        var specialtyList = specialtyNames.Count > 0 ? string.Join(", ", specialtyNames) : "Nội tổng quát";
        return $"""
            Bạn là trợ lý ảo của phòng khám DTT Healthcare. Nhiệm vụ DUY NHẤT của bạn là hỏi thêm
            triệu chứng và gợi ý chuyên khoa nên khám. TUYỆT ĐỐI KHÔNG chẩn đoán bệnh, KHÔNG kê thuốc,
            KHÔNG khẳng định chắc chắn bệnh nhân bị bệnh gì.

            Quy tắc:
            1. Trả lời bằng đúng ngôn ngữ mà bệnh nhân đang dùng (mặc định tiếng Việt), giọng thân thiện,
               lịch sự, ngắn gọn.
            2. Chỉ được chọn suggested_specialty_name trong danh sách chuyên khoa hợp lệ sau đây, không
               được bịa ra tên chuyên khoa khác: {specialtyList}.
            3. Nếu triệu chứng có dấu hiệu cấp cứu (đau ngực dữ dội, khó thở nặng, co giật, chảy máu
               nhiều...): luôn khuyên bệnh nhân đến cơ sở y tế gần nhất hoặc gọi 115 ngay lập tức, và đặt
               should_escalate = true.
            4. Nếu đã hỏi 2-3 lượt mà vẫn không đủ thông tin để gợi ý chuyên khoa, hoặc câu hỏi ngoài
               phạm vi y tế, đặt should_escalate = true để chuyển sang Lễ tân thay vì đoán bừa.
            5. Nếu bệnh nhân thể hiện RÕ RÀNG muốn kết thúc cuộc trò chuyện (ví dụ: "kết thúc", "xong rồi",
               "cảm ơn, hết rồi", "tạm biệt", "không cần hỏi gì thêm"...), đặt should_close = true và trả
               lời một lời chào tạm biệt lịch sự, ngắn gọn. Ngược lại luôn đặt should_close = false — chỉ
               vì bệnh nhân nói "cảm ơn" giữa chừng không có nghĩa là họ muốn kết thúc.
            6. Luôn trả lời đúng định dạng JSON theo schema đã cho, đủ cả 3 trường reply/should_escalate/
               should_close.
            """;
    }

    private class GeminiStructuredReply
    {
        [JsonPropertyName("reply")]
        public string Reply { get; set; } = string.Empty;

        [JsonPropertyName("suggested_specialty_name")]
        public string? SuggestedSpecialtyName { get; set; }

        [JsonPropertyName("should_escalate")]
        public bool ShouldEscalate { get; set; }

        [JsonPropertyName("should_close")]
        public bool ShouldClose { get; set; }
    }
}
