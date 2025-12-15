using GenerativeAI;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using QuanlykhoAPI.Models;
using QuanlykhoAPI.Services;
using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Tesseract;
namespace Quanlykhohang.Services
{
    public class AIService
    {
        private readonly GenerativeModel _model;
        private readonly AppDbContext _context;
        private readonly List<ChatHistory> _conversationHistory;
        private readonly IWebHostEnvironment _environment; // Add this field

        public AIService(
            IOptions<GeminiOptions> options,
            AppDbContext context,
            IWebHostEnvironment environment // Add this parameter
        )
        {
            var opt = options.Value ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(opt.ApiKey))
                throw new InvalidOperationException("Gemini:ApiKey chưa được cấu hình.");

            _model = new GenerativeModel(
                opt.ApiKey,
                string.IsNullOrWhiteSpace(opt.Model) ? "gemini-2.0-flash" : opt.Model
            );

            _context = context;
            _conversationHistory = new List<ChatHistory>();
            _environment = environment; // Assign the injected environment
        }
        // ================== QUY TRÌNH CHÍNH CẢI TIẾN ==================
        public async Task<string> ProcessImageAndCreateOrder(byte[] imageData, string fileName, string userId = "guest")
        {
            try
            {
                Console.WriteLine($"🖼️ Bắt đầu xử lý ảnh: {fileName} ({imageData.Length} bytes)");

                // 1. OCR - Trích xuất văn bản từ ảnh
                string extractedText = await ExtractTextFromImage(imageData);
                if (string.IsNullOrWhiteSpace(extractedText))
                    return "❌ Không thể đọc được văn bản từ ảnh. Vui lòng thử với ảnh rõ nét hơn.";

                Console.WriteLine($"📝 Văn bản OCR gốc: {extractedText}");

                // 2. GỬI TEXT LÊN AI ĐỂ CHUẨN HÓA VÀ TRÍCH XUẤT
                var aiExtractedItems = await ExtractProductsWithAI(extractedText);
                Console.WriteLine($"🤖 AI đã trích xuất {aiExtractedItems.Count} sản phẩm");

                List<ExtractedItem> finalExtractedItems;

                // 3. Kết hợp kết quả AI và regex truyền thống để tăng độ chính xác
                if (aiExtractedItems.Any())
                {
                    // Ưu tiên sử dụng kết quả từ AI
                    finalExtractedItems = aiExtractedItems;
                    Console.WriteLine("✅ Sử dụng kết quả trích xuất từ AI");
                }
                else
                {
                    // Fallback: sử dụng phương pháp regex truyền thống
                    finalExtractedItems = ExtractProductsFromText(extractedText);
                    Console.WriteLine($"🔍 Sử dụng phương pháp regex, trích xuất {finalExtractedItems.Count} mục");
                }

                if (!finalExtractedItems.Any())
                    return $"🔍 **KHÔNG TÌM THẤY THÔNG TIN SẢN PHẨM**\n\nVăn bản đọc được từ ảnh:\n`{extractedText}`\n\nKhông tìm thấy thông tin sản phẩm nào trong ảnh.";

                // 4. So khớp với database
                var matched = await MatchExtractedItemsToProducts(finalExtractedItems);
                Console.WriteLine($"📦 Tìm thấy {matched.Count} sản phẩm khớp trong database");

                // 5. Tạo response với định dạng HTML table
                var response = FormatProductResponse(matched, extractedText);

                // 6. Phân tích AI nếu có sản phẩm
                if (matched.Any())
                {
                    try
                    {
                        var aiAnalysis = await AnalyzeProductsWithAI(matched, extractedText);
                        response += $@"
<div id='aiAnalysisSection' style='margin-top:15px; background:#f0f8ff; border:1px solid #b3d9ff; padding:12px; border-radius:8px;'>
    <h4 style='color:#0066cc; text-align:center; margin-bottom:10px;'>🤖 PHÂN TÍCH THÔNG MINH</h4>
    <div style='white-space:pre-wrap; line-height:1.5;'>{aiAnalysis}</div>
</div>";
                    }
                    catch (Exception aiEx)
                    {
                        Console.WriteLine($"⚠️ Lỗi phân tích AI: {aiEx.Message}");
                        // Tiếp tục mà không có phân tích AI
                    }
                }

                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi xử lý ảnh: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return $"❌ Lỗi khi xử lý ảnh: {ex.Message}";
            }
        }

        // ================== TRÍCH XUẤT SẢN PHẨM BẰNG AI ==================
        public async Task<List<ExtractedItem>> ExtractProductsWithAI(string ocrText)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ocrText))
                    return new List<ExtractedItem>();

                Console.WriteLine("🤖 Bắt đầu trích xuất sản phẩm bằng AI...");

                var prompt = $@"
HÃY PHÂN TÍCH VĂN BẢN OCR TỪ HÓA ĐƠN NHẬP HÀNG VÀ TRÍCH XUẤT DANH SÁCH SẢN PHẨM:

VĂN BẢN OCR GỐC:
'{ocrText}'

YÊU CẦU THỰC HIỆN:
1. SỬA LỖI OCR: Sửa các lỗi chính tả, bổ sung từ bị thiếu, chuẩn hóa tên sản phẩm
2. CHUẨN HÓA DỮ LIỆU: Viết hoa chữ cái đầu, sửa lỗi thường gặp (Talanh → Tủ lạnh, Tvi → Tivi, SamSung → Samsung)
3. TRÍCH XUẤT THÔNG TIN: Mã sản phẩm, Tên sản phẩm, Số lượng, Giá nhập
4. ĐỊNH DẠNG JSON: Trả về duy nhất JSON array

ĐỊNH DẠNG JSON MẪU:
[
    {{
        ""Code"": ""SP001"",
        ""Name"": ""iPhone 13 Pro Max"",
        ""Quantity"": 10,
        ""Price"": 25000000
    }}
]

QUY TẮC XỬ LÝ:
- Code: Chuẩn hóa thành SP + số (P001 → SP001, SP 001 → SP001)
- Name: Viết hoa chữ cái đầu, sửa lỗi OCR phổ biến
- Quantity: Chỉ lấy số, bỏ ký tự đặc biệt
- Price: Chuyển đổi thành số (1.000.000 → 1000000)

CHỈ TRẢ VỀ JSON ARRAY, KHÔNG THÊM BẤT KỲ TEXT NÀO KHÁC:";

                var response = await _model.GenerateContentAsync(prompt);

                if (string.IsNullOrWhiteSpace(response?.Text))
                {
                    Console.WriteLine("❌ AI không trả về kết quả");
                    return new List<ExtractedItem>();
                }

                Console.WriteLine($"📄 Phản hồi từ AI: {response.Text}");

                // Xử lý và parse JSON response từ AI
                var extractedItems = ParseAIResponse(response.Text);
                Console.WriteLine($"✅ Đã parse được {extractedItems.Count} sản phẩm từ AI");

                return extractedItems;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi trích xuất AI: {ex.Message}");
                return new List<ExtractedItem>();
            }
        }

        // ================== PARSE PHẢN HỒI TỪ AI ==================
        private List<ExtractedItem> ParseAIResponse(string aiResponse)
        {
            var results = new List<ExtractedItem>();

            try
            {
                // Làm sạch response - loại bỏ markdown code blocks và text thừa
                var cleanResponse = aiResponse
                    .Replace("```json", "")
                    .Replace("```", "")
                    .Replace("JSON", "")
                    .Trim();

                // Tìm JSON array trong response
                var jsonMatch = Regex.Match(cleanResponse, @"\[.*\]", RegexOptions.Singleline);
                if (!jsonMatch.Success)
                {
                    Console.WriteLine("❌ Không tìm thấy JSON array trong response AI");
                    return ExtractFromAIResponseText(aiResponse);
                }

                cleanResponse = jsonMatch.Value;

                // Thử parse JSON
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                };

                var extractedItems = JsonSerializer.Deserialize<List<ExtractedItem>>(cleanResponse, options);
                if (extractedItems != null)
                {
                    // Chuẩn hóa dữ liệu sau khi parse
                    foreach (var item in extractedItems)
                    {
                        item.Code = NormalizeCode(item.Code);
                        item.Name = NormalizeProductName(item.Name);

                        // Đảm bảo số lượng và giá hợp lệ
                        if (item.Quantity <= 0) item.Quantity = 1;
                        if (item.Price < 0) item.Price = 0;

                        if (!string.IsNullOrWhiteSpace(item.Name))
                        {
                            results.Add(item);
                        }
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                Console.WriteLine($"❌ Lỗi parse JSON từ AI: {jsonEx.Message}");
                Console.WriteLine($"Response: {aiResponse}");

                // Fallback: thử trích xuất bằng regex từ response AI
                results = ExtractFromAIResponseText(aiResponse);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi xử lý response AI: {ex.Message}");
            }

            return results;
        }
        // ================== CẢI TIẾN CHUẨN HÓA TÊN SẢN PHẨM ==================
        private string NormalizeProductName(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName))
                return productName;

            // Sửa lỗi OCR phổ biến
            var normalized = productName
                .Replace("Tũ Lạnh", "Tủ Lạnh", StringComparison.OrdinalIgnoreCase)
                .Replace("Talanh", "Tủ Lạnh", StringComparison.OrdinalIgnoreCase)
                .Replace("Tvi", "Tivi", StringComparison.OrdinalIgnoreCase)
                .Replace("SamSung", "Samsung", StringComparison.OrdinalIgnoreCase)
                .Replace("Iphone", "iPhone", StringComparison.OrdinalIgnoreCase)
                .Replace("Macbook", "MacBook", StringComparison.OrdinalIgnoreCase)
                .Replace("SsP", "SP", StringComparison.OrdinalIgnoreCase)
                .Replace("sP", "SP", StringComparison.OrdinalIgnoreCase);

            // Viết hoa chữ cái đầu mỗi từ
            normalized = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(normalized.ToLower());

            return normalized.Trim();
        }

        // ================== FALLBACK: TRÍCH XUẤT TỪ TEXT RESPONSE AI ==================
        private List<ExtractedItem> ExtractFromAIResponseText(string aiResponse)
        {
            var results = new List<ExtractedItem>();

            try
            {
                // Pattern để tìm các sản phẩm trong response AI
                var patterns = new[]
                {
            // Pattern cho JSON-like format
            @"\{\s*""Code""\s*:\s*""([^""]*)""\s*,\s*""Name""\s*:\s*""([^""]*)""\s*,\s*""Quantity""\s*:\s*(\d+)\s*,\s*""Price""\s*:\s*(\d+)\s*\}",
            @"\{\s*""Name""\s*:\s*""([^""]*)""\s*,\s*""Quantity""\s*:\s*(\d+)\s*,\s*""Price""\s*:\s*(\d+)\s*\}"
        };

                foreach (var pattern in patterns)
                {
                    var matches = Regex.Matches(aiResponse, pattern, RegexOptions.IgnoreCase);
                    foreach (Match match in matches)
                    {
                        try
                        {
                            var item = new ExtractedItem();

                            if (match.Groups.Count >= 5) // Có đủ 4 groups (bao gồm cả Code)
                            {
                                item.Code = NormalizeCode(match.Groups[1].Value);
                                item.Name = match.Groups[2].Value.Trim();
                                int.TryParse(match.Groups[3].Value, out int qty);
                                item.Quantity = qty;
                                decimal.TryParse(match.Groups[4].Value, out decimal price);
                                item.Price = price;
                            }
                            else if (match.Groups.Count >= 4) // Chỉ có Name, Quantity, Price
                            {
                                item.Name = match.Groups[1].Value.Trim();
                                int.TryParse(match.Groups[2].Value, out int qty);
                                item.Quantity = qty;
                                decimal.TryParse(match.Groups[3].Value, out decimal price);
                                item.Price = price;
                            }

                            if (!string.IsNullOrWhiteSpace(item.Name))
                            {
                                results.Add(item);
                            }
                        }
                        catch (Exception itemEx)
                        {
                            Console.WriteLine($"⚠️ Lỗi extract từ text: {itemEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi fallback extraction: {ex.Message}");
            }

            return results;
        }

        // ================== OCR (giữ nguyên nhưng thêm kiểm tra) ==================
        // ================== OCR IMPROVED (OpenCV + Tesseract) ==================
        public async Task<string> ExtractTextFromImage(byte[] imageData)
        {
            Mat? mat = null;
            Mat? processed = null;
            string tempImagePath = string.Empty;

            try
            {
                // 1. Validate input
                if (imageData == null || imageData.Length == 0)
                {
                    Console.WriteLine("❌ Dữ liệu ảnh trống");
                    return string.Empty;
                }

                // 2. Decode image với multiple attempts
                mat = await DecodeImageWithFallback(imageData);
                if (mat == null || mat.Empty())
                {
                    Console.WriteLine("❌ Không thể decode ảnh");
                    return string.Empty;
                }

                // 3. Tiền xử lý ảnh thông minh
                processed = PreprocessImageForOCR(mat);

                // 4. OCR với multiple configurations
                string resultText = await PerformOCR(processed);

                return resultText?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi OCR: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                // Cleanup resources
                mat?.Dispose();
                processed?.Dispose();

                if (!string.IsNullOrEmpty(tempImagePath) && File.Exists(tempImagePath))
                {
                    try { File.Delete(tempImagePath); } catch { }
                }
            }
        }

        private async Task<Mat?> DecodeImageWithFallback(byte[] imageData)
        {
            // Thử decode với OpenCV
            using var ms = new MemoryStream(imageData);
            var mat = Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);

            if (!mat.Empty()) return mat;

            // Fallback: thử với System.Drawing nếu OpenCV fail
            try
            {
                using var originalImage = System.Drawing.Image.FromStream(ms);
                using var bitmap = new System.Drawing.Bitmap(originalImage);

                mat = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC3);
                bitmap.ToMat(mat);

                return mat;
            }
            catch
            {
                return null;
            }
        }

        private Mat PreprocessImageForOCR(Mat input)
        {
            Mat result = new Mat();

            // Chuyển sang grayscale
            if (input.Channels() == 3)
                Cv2.CvtColor(input, result, ColorConversionCodes.BGR2GRAY);
            else
                input.CopyTo(result);

            // Adaptive preprocessing based on image characteristics
            double meanBrightness = Cv2.Mean(result)[0];

            if (meanBrightness < 50) // Ảnh tối
            {
                // Tăng brightness và contrast
                Cv2.ConvertScaleAbs(result, result, alpha: 1.5, beta: 50);
            }
            else if (meanBrightness > 200) // Ảnh sáng quá
            {
                // Giảm brightness
                Cv2.ConvertScaleAbs(result, result, alpha: 0.7, beta: 0);
            }

            // Loại bỏ noise với multiple strategies
            Mat denoised = new Mat();
            Cv2.MedianBlur(result, denoised, 3);

            // Adaptive threshold với parameters dynamic
            int blockSize = (int)(Math.Min(input.Width, input.Height) * 0.05) | 1; // Lẻ và >= 3
            blockSize = Math.Max(3, Math.Min(blockSize, 35));

            Mat binary = new Mat();
            Cv2.AdaptiveThreshold(denoised, binary, 255,
                AdaptiveThresholdTypes.GaussianC,
                ThresholdTypes.Binary,
                blockSize, 8);

            // Morphology operations để cải thiện text quality
            Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(1, 1));
            Cv2.MorphologyEx(binary, binary, MorphTypes.Close, kernel);
            Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernel);

            // Sharpening filter để làm rõ chữ
            Mat sharpened = ApplySharpeningFilter(binary);

            denoised.Dispose();
            binary.Dispose();
            kernel.Dispose();

            return sharpened;
        }
        // ================== SHARPEN IMAGE ==================
        private Mat ApplySharpeningFilter(Mat input)
        {
            Mat sharpened = new Mat();

            // Sharpening kernel 3x3
            float[,] kernelData = new float[,]
            {
        { -1, -1, -1 },
        { -1,  9, -1 },
        { -1, -1, -1 }
            };

            using var kernel = Mat.FromArray(kernelData); // tạo kernel từ mảng 2 chiều
            Cv2.Filter2D(input, sharpened, input.Depth(), kernel);

            return sharpened;
        }
        private async Task<string> PerformOCR(Mat processedImage)
        {
            string tempImagePath = string.Empty;

            try
            {
                // Tạo thư mục tạm
                var tempPath = Path.GetTempPath();
                tempImagePath = Path.Combine(tempPath, $"ocr_{Guid.NewGuid()}.png");

                // Lưu ảnh đã xử lý
                Cv2.ImWrite(tempImagePath, processedImage);

                // Đảm bảo tessdata tồn tại
                var tessDataPath = GetTessDataPath();
                if (!Directory.Exists(tessDataPath))
                {
                    Console.WriteLine($"⚠️ Tessdata directory không tồn tại: {tessDataPath}");
                    return string.Empty;
                }

                string resultText = string.Empty;

                // Thử multiple OCR configurations
                var configurations = new[]
                {
            new { Mode = Tesseract.EngineMode.LstmOnly, PageSegMode = Tesseract.PageSegMode.Auto },
            new { Mode = Tesseract.EngineMode.TesseractOnly, PageSegMode = Tesseract.PageSegMode.SingleBlock },
            new { Mode = Tesseract.EngineMode.Default, PageSegMode = Tesseract.PageSegMode.Auto }
        };

                foreach (var config in configurations)
                {
                    try
                    {
                        using var engine = new Tesseract.TesseractEngine(tessDataPath, "eng+vie", config.Mode);
                        engine.SetVariable("tessedit_pageseg_mode", (int)config.PageSegMode);

                        // Cấu hình để cải thiện accuracy
                        engine.SetVariable("user_defined_dpi", 300);
                        engine.SetVariable("preserve_interword_spaces", "1");

                        using var img = Tesseract.Pix.LoadFromFile(tempImagePath);
                        using var page = engine.Process(img);

                        resultText = page.GetText();

                        // Nếu kết quả có ý nghĩa, dừng lại
                        if (!string.IsNullOrWhiteSpace(resultText) && resultText.Length > 2)
                            break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ OCR config thất bại: {config.Mode}, {ex.Message}");
                        // Continue với config tiếp theo
                    }
                }

                return resultText ?? string.Empty;
            }
            finally
            {
                // Cleanup
                if (!string.IsNullOrEmpty(tempImagePath) && File.Exists(tempImagePath))
                {
                    try { File.Delete(tempImagePath); } catch { }
                }
            }
        }

        private string GetTessDataPath()
        {
            // Ưu tiên tìm tessdata trong multiple locations
            var possiblePaths = new[]
            {
        Path.Combine(_environment.WebRootPath ?? "", "tessdata"),
        Path.Combine(AppContext.BaseDirectory, "tessdata"),
        Path.Combine(Path.GetTempPath(), "tessdata"),
        @"C:\Program Files\Tesseract-OCR\tessdata" // Windows default
    };

            foreach (var path in possiblePaths)
            {
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "eng.traineddata")))
                    return path;
            }

            // Fallback
            return Path.Combine(_environment.WebRootPath ?? Path.GetTempPath(), "tessdata");
        }
        // ================== TRÍCH XUẤT HÀNG (code, name, qty, price) ==================
        public List<ExtractedItem> ExtractProductsFromText(string text)
        {
            var results = new List<ExtractedItem>();
            if (string.IsNullOrWhiteSpace(text)) return results;

            // chuẩn hóa vài ký tự do OCR lỗi
            text = Regex.Replace(text, @"[•·\t\r]+", " ");
            text = Regex.Replace(text, @"\s{2,}", " ");
            text = text.Replace("Talanh", "Tủ lạnh", StringComparison.OrdinalIgnoreCase)
                       .Replace("talanh", "tủ lạnh", StringComparison.OrdinalIgnoreCase)
                       .Replace("Tvi", "Tivi")
                       .Replace("SamSung", "Samsung");

            // tách dòng
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim())
                            .Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

            // pattern ưu tiên: "1 SP01 Iphone 11 100 10000000"
            var patternFull = new Regex(@"^\s*\d+\s+(SP[_\-\s]?\d+|P\d+)\s+(.+?)\s+(\d{1,6})\s+(\d{4,})\s*$",
                                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            foreach (var line in lines)
            {
                var m = patternFull.Match(line);
                if (m.Success)
                {
                    var rawCode = Regex.Replace(m.Groups[1].Value, @"[^A-Za-z0-9]", "");
                    var code = NormalizeCode(rawCode);
                    var name = m.Groups[2].Value.Trim();
                    int qty = int.TryParse(m.Groups[3].Value.Trim(), out var qv) ? qv : 0;
                    decimal price = decimal.TryParse(m.Groups[4].Value.Trim(), out var pv) ? pv : 0;

                    results.Add(new ExtractedItem { Code = code, Name = name, Quantity = qty, Price = price });
                    continue;
                }

                // fallback: dòng chứa mã và số lượng & giá nhưng tách không chuẩn
                // phân tích token: tìm token có dạng SPxxx hoặc Pxx
                var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string foundCode = null;
                int codeIndex = -1;
                for (int i = 0; i < tokens.Length; i++)
                {
                    if (Regex.IsMatch(tokens[i], @"^(SP[_\-\s]?\d+|P\d+)$", RegexOptions.IgnoreCase))
                    {
                        foundCode = NormalizeCode(tokens[i]);
                        codeIndex = i;
                        break;
                    }
                }
                if (foundCode != null)
                {
                    // tìm số lượng (first numeric token after name)
                    int qty = 0; decimal price = 0;
                    // name will be tokens between codeIndex+1 and the token before first numeric that looks like qty
                    int qtyIndex = -1;
                    for (int j = codeIndex + 1; j < tokens.Length; j++)
                    {
                        if (Regex.IsMatch(tokens[j], @"^\d+$"))
                        {
                            qtyIndex = j;
                            break;
                        }
                    }
                    string name = "";
                    if (qtyIndex > codeIndex)
                    {
                        name = string.Join(' ', tokens.Skip(codeIndex + 1).Take(qtyIndex - (codeIndex + 1)));
                        int.TryParse(tokens[qtyIndex], out qty);
                        // price maybe next token(s)
                        if (qtyIndex + 1 < tokens.Length)
                            decimal.TryParse(Regex.Replace(tokens[qtyIndex + 1], @"\D", ""), out price);
                    }
                    else
                    {
                        // không tìm qty -> lấy phần còn lại làm name
                        name = string.Join(' ', tokens.Skip(codeIndex + 1));
                    }
                    results.Add(new ExtractedItem { Code = foundCode, Name = name, Quantity = qty, Price = price });
                    continue;
                }

                // fallback 2: dòng có cấu trúc "SP01 Iphone 11 12 1000000" (không có index)
                var patternNoIndex = new Regex(@"^(SP[_\-\s]?\d+|P\d+)\s+(.+?)\s+(\d{1,6})\s+(\d{4,})$", RegexOptions.IgnoreCase);
                var m2 = patternNoIndex.Match(line);
                if (m2.Success)
                {
                    var code = NormalizeCode(m2.Groups[1].Value);
                    var name = m2.Groups[2].Value.Trim();
                    int qty = int.TryParse(m2.Groups[3].Value.Trim(), out var qv2) ? qv2 : 0;
                    decimal price = decimal.TryParse(m2.Groups[4].Value.Trim(), out var pv2) ? pv2 : 0;
                    results.Add(new ExtractedItem { Code = code, Name = name, Quantity = qty, Price = price });
                    continue;
                }

                // fallback 3: tên + qty + price (no code) e.g. "Iphone 11 12 1000000"
                var patternNameQtyPrice = new Regex(@"^([A-Za-z\p{L}\s]+?)\s+(\d{1,6})\s+(\d{4,})$", RegexOptions.IgnoreCase);
                var m3 = patternNameQtyPrice.Match(line);
                if (m3.Success)
                {
                    var name = m3.Groups[1].Value.Trim();
                    int qty = int.TryParse(m3.Groups[2].Value.Trim(), out var qv3) ? qv3 : 0;
                    decimal price = decimal.TryParse(m3.Groups[3].Value.Trim(), out var pv3) ? pv3 : 0;
                    results.Add(new ExtractedItem { Code = null, Name = name, Quantity = qty, Price = price });
                    continue;
                }
            }

            // loại bỏ trùng lặp (ưu tiên theo Code nếu có)
            var dedup = new List<ExtractedItem>();
            foreach (var it in results)
            {
                if (!string.IsNullOrEmpty(it.Code))
                {
                    if (!dedup.Any(d => !string.IsNullOrEmpty(d.Code) && d.Code.Equals(it.Code, StringComparison.OrdinalIgnoreCase)))
                        dedup.Add(it);
                }
                else
                {
                    // nếu đã có name tương tự thì bỏ
                    if (!dedup.Any(d => !string.IsNullOrEmpty(d.Name) && NormalizeString(d.Name).Equals(NormalizeString(it.Name))))
                        dedup.Add(it);
                }
            }

            return dedup;
        }

        // ================== CẢI TIẾN PHƯƠNG THỨC CHUẨN HÓA MÃ ==================
        private string NormalizeCode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.ToLower() == "null")
                return null;

            // Làm sạch mã
            var cleaned = Regex.Replace(raw, @"[^A-Za-z0-9]", "");

            if (string.IsNullOrWhiteSpace(cleaned))
                return null;

            cleaned = cleaned.ToUpperInvariant();

            // Chuẩn hóa định dạng mã
            if (cleaned.StartsWith("P") && !cleaned.StartsWith("SP"))
                cleaned = "SP" + cleaned.Substring(1);

            // Đảm bảo mã có định dạng SP + số
            if (!cleaned.StartsWith("SP") && Regex.IsMatch(cleaned, @"^\d+$"))
                cleaned = "SP" + cleaned;

            return cleaned;
        }

        private string NormalizeString(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim().ToLowerInvariant();

        // ================== SO KHỚP VỚI DATABASE (ĐÃ SỬA) ==================
        public async Task<List<MatchedProduct>> MatchExtractedItemsToProducts(List<ExtractedItem> items)
        {
            var matched = new List<MatchedProduct>();
            if (!items.Any()) return matched;

            // Lấy tất cả sản phẩm từ database - KHÔNG lọc theo TrangThai
            var allProducts = await _context.SanPhams.ToListAsync();

            Console.WriteLine($"🔍 So khớp {items.Count} sản phẩm với {allProducts.Count} sản phẩm trong database");

            foreach (var item in items)
            {
                ModelSanPham foundProduct = null;
                var matchScore = 0.0;

                // Tìm kiếm theo thứ tự ưu tiên
                if (!string.IsNullOrEmpty(item.Code))
                {
                    // Ưu tiên 1: Tìm theo mã sản phẩm (chính xác)
                    foundProduct = allProducts.FirstOrDefault(p =>
                        (!string.IsNullOrEmpty(p.MaSanPham) &&
                         p.MaSanPham.Equals(item.Code, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(p.MaSKU) &&
                         p.MaSKU.Equals(item.Code, StringComparison.OrdinalIgnoreCase)));

                    if (foundProduct != null)
                    {
                        matchScore = 1.0; // Khớp chính xác mã
                        Console.WriteLine($"✅ Khớp theo mã: {item.Code} -> {foundProduct.TenSanPham}");
                    }
                }

                // Ưu tiên 2: Tìm theo tên sản phẩm (contains)
                if (foundProduct == null && !string.IsNullOrWhiteSpace(item.Name))
                {
                    var normalizedItemName = NormalizeString(item.Name);

                    // Tìm sản phẩm có tên chứa tên item (và ngược lại)
                    var potentialMatches = allProducts
                        .Where(p => !string.IsNullOrWhiteSpace(p.TenSanPham))
                        .Select(p => new
                        {
                            Product = p,
                            NormalizedName = NormalizeString(p.TenSanPham),
                            Score = CalculateNameSimilarity(normalizedItemName, NormalizeString(p.TenSanPham))
                        })
                        .Where(x => x.Score > 0.6) // Ngưỡng similarity
                        .OrderByDescending(x => x.Score)
                        .ToList();

                    if (potentialMatches.Any())
                    {
                        foundProduct = potentialMatches.First().Product;
                        matchScore = potentialMatches.First().Score;
                        Console.WriteLine($"🔍 Khớp theo tên: {item.Name} -> {foundProduct.TenSanPham} (Score: {matchScore:F2})");
                    }
                }

                if (foundProduct != null)
                {
                    // Kiểm tra trùng lặp - sử dụng MaSanPham thay vì Id
                    if (!matched.Any(m => m.Product.MaSanPham == foundProduct.MaSanPham))
                    {
                        matched.Add(new MatchedProduct
                        {
                            Product = foundProduct,
                            Item = item,
                            MatchScore = matchScore
                        });
                    }
                }
                else
                {
                    Console.WriteLine($"❌ Không tìm thấy sản phẩm phù hợp: {item.Name} ({item.Code})");
                }
            }

            return matched;
        }

        // ================== TÍNH ĐỘ TƯƠNG ĐỒNG TÊN ==================
        private double CalculateNameSimilarity(string name1, string name2)
        {
            if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
                return 0.0;

            // Chuẩn hóa tên
            name1 = NormalizeString(name1);
            name2 = NormalizeString(name2);

            // Kiểm tra contains
            if (name1.Contains(name2) || name2.Contains(name1))
                return 0.9;

            // Tính Levenshtein distance
            var maxLength = Math.Max(name1.Length, name2.Length);
            if (maxLength == 0) return 1.0;

            var distance = LevenshteinDistance(name1, name2);
            var similarity = 1.0 - (double)distance / maxLength;

            return similarity;
        }

        // hàm Levenshtein (đơn giản)
        private int LevenshteinDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
            if (string.IsNullOrEmpty(b)) return a.Length;

            var n = a.Length;
            var m = b.Length;
            var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;
            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = new[] {
                d[i - 1, j] + 1,
                d[i, j - 1] + 1,
                d[i - 1, j - 1] + cost
            }.Min();
                }
            }
            return d[n, m];
        }

        // ================== RESPONSE FORMATTING (CÓ ĐỘ KHỚP %) ==================
        private string FormatProductResponse(List<MatchedProduct> matched, string extractedText)
        {
            if (matched == null || !matched.Any())
            {
                return $@"❌ **KHÔNG TÌM THẤY SẢN PHẨM PHÙ HỢP**

📝 **Văn bản đọc được từ ảnh:**
`{extractedText}`

💡 **Gợi ý:**
- Kiểm tra chất lượng ảnh
- Đảm bảo ảnh chứa thông tin sản phẩm rõ ràng
- Thử với ảnh khác nếu cần";
            }

            var sb = new StringBuilder();

            // Header
            sb.AppendLine($"🎯 **ĐÃ TÌM THẤY {matched.Count} SẢN PHẨM**");
            sb.AppendLine($"📝 **Văn bản đọc được:** `{extractedText}`");

            // Add All Products Button
            sb.AppendLine(@"
<div style='text-align:center; margin:15px 0;'>
    <button class='btn-transfer-all' onclick='transferAllProducts()' 
            style='background:linear-gradient(45deg, #28a745, #20c997); color:white; border:none; padding:10px 20px; border-radius:8px; font-size:14px; cursor:pointer; font-weight:bold; box-shadow:0 2px 5px rgba(0,0,0,0.2);'>
        📤 THÊM TẤT CẢ SẢN PHẨM
    </button>
</div>");

            sb.AppendLine(@"<div style='border: 1px solid #ddd; border-radius: 10px; padding: 15px; background: #f8f9fa; margin-top:10px;'>");
            sb.AppendLine(@"<h3 style='text-align:center; color:#007bff; font-weight:bold; margin-bottom:15px;'>📦 DANH SÁCH SẢN PHẨM</h3>");

            foreach (var match in matched)
            {
                var product = match.Product;
                var item = match.Item;

                int inventory = (product.SoLuongNhap - product.SoLuongXuat);
                decimal salePrice = product.GiaBan;
                decimal purchasePrice = product.GiaNhap;
                int quantity = item.Quantity > 0 ? item.Quantity : 1;

                // ✅ Trạng thái tồn kho với màu sắc
                string inventoryStatusHtml = inventory > 50
                    ? "<span style='color:green; font-weight:bold;'>✅ Tồn kho tốt</span>"
                    : inventory > 0
                        ? "<span style='color:orange; font-weight:bold;'>⚠️ Sắp hết hàng</span>"
                        : "<span style='color:red; font-weight:bold;'>❌ Hết hàng</span>";

                // ✅ Thêm Độ Khớp %
                string matchScoreHtml = $@"<span style='color:#007bff; font-weight:bold;'>{(match.MatchScore * 100):F0}%</span>";

                // ✅ Hiển thị từng sản phẩm dạng thẻ dọc
                sb.AppendLine($@"
<div class='product-item' style='margin: 12px 0; padding: 14px; border-left: 4px solid #007bff; background: #fff; border-radius: 8px; 
                                 box-shadow: 0 1px 3px rgba(0,0,0,0.1); transition: transform 0.2s;'>

    <div style='display:flex; align-items:center; justify-content:space-between;'>
        <b style='color:#007bff; font-size:16px;'>🔹 {product.TenSanPham}</b>
        <span style='font-size:13px;'>{inventoryStatusHtml}</span>
    </div>

    <div style='display:flex; flex-direction:column; gap:6px; margin-top:8px; font-size:13px; line-height:1.6;'>
        <div><b>📋 Mã:</b> {product.MaSanPham}</div>
        <div><b>🏷️ SKU:</b> {product.MaSKU ?? "N/A"}</div>
        <div><b>📁 Loại:</b> {product.LoaiSanPham ?? "N/A"}</div>
        <div><b>📦 Số lượng (ảnh):</b> {quantity}</div>
        <div><b>📊 Tồn kho:</b> {inventory}</div>
        <div><b>💰 Giá nhập (ảnh):</b> {item.Price:N0}đ</div>
        <div><b>💰 Giá nhập (DB):</b> {purchasePrice:N0}đ</div>
        <div><b>🏷️ Giá bán:</b> {salePrice:N0}đ</div>
        <div><b>🎯 Độ khớp:</b> {matchScoreHtml}</div>
    </div>

    <button class='btn-transfer-single' 
            onclick='transferSingleProduct(""{product.MaSanPham}"", ""{product.TenSanPham.Replace("\"", "\\\"")}"", ""{product.MaSKU ?? "N/A"}"", {item.Price}, {quantity})' 
            style='background: #007bff; color: white; border: none; padding: 8px 16px; border-radius: 5px; 
                   font-size: 13px; cursor: pointer; margin-top: 10px; transition: background 0.3s, transform 0.2s;'>
        Thêm vào bảng
    </button>
</div>");
            }

            sb.AppendLine("</div>");

            // Hidden Data for JavaScript
            sb.AppendLine(@"<div id='hiddenProductsData' style='display:none;'>");
            sb.AppendLine("<!-- PRODUCTS_DATA_START -->");
            sb.AppendLine("[");

            for (int i = 0; i < matched.Count; i++)
            {
                var match = matched[i];
                var product = match.Product;
                var item = match.Item;

                sb.AppendLine("  {");
                sb.AppendLine($"    \"maSanPham\": \"{product.MaSanPham}\",");
                sb.AppendLine($"    \"tenSanPham\": \"{product.TenSanPham.Replace("\"", "\\\"")}\",");
                sb.AppendLine($"    \"maSKU\": \"{product.MaSKU ?? "N/A"}\",");
                sb.AppendLine($"    \"giaNhap\": {item.Price},");
                sb.AppendLine($"    \"soLuong\": {(item.Quantity > 0 ? item.Quantity : 1)},");
                sb.AppendLine($"    \"loaiSanPham\": \"{product.LoaiSanPham ?? "N/A"}\",");
                sb.AppendLine($"    \"donViTinh\": \"{product.DonViTinh ?? "Chiếc"}\",");
                sb.AppendLine($"    \"doKhớp\": {(match.MatchScore * 100):F0}");
                sb.AppendLine("  }" + (i < matched.Count - 1 ? "," : ""));
            }

            sb.AppendLine("]");
            sb.AppendLine("<!-- PRODUCTS_DATA_END -->");
            sb.AppendLine("</div>");

            return sb.ToString();
        }

        // ================== PHÂN TÍCH AI ==================
        public async Task<string> AnalyzeProductsWithAI(List<MatchedProduct> matched, string extractedText)
        {
            try
            {
                var productList = string.Join("\n", matched.Select(m =>
                    $"- {m.Product.TenSanPham} (Mã: {m.Product.MaSanPham}): Tồn kho {(m.Product.SoLuongNhap - m.Product.SoLuongXuat)}, SL ảnh: {m.Item.Quantity}, Giá nhập: {m.Item.Price:N0}đ"));

                var prompt = $@"Từ ảnh hóa đơn/nhập hàng, hệ thống đã trích xuất được thông tin sau:

VĂN BẢN GỐC TỪ ẢNH:
'{extractedText}'

DANH SÁCH SẢN PHẨM ĐÃ NHẬN DIỆN:
{productList}

Hãy phân tích ngắn gọn (không quá 250 từ) về:
1. Tình hình tồn kho hiện tại so với số lượng trong ảnh
2. Đề xuất nhập thêm hàng hoặc điều chỉnh tồn kho nếu cần
3. Nhận xét về chênh lệch giá (nếu có)
4. Gợi ý quản lý hiệu quả
5. CẢNH BÁO: Nếu có sản phẩm bất thường (số lượng/quá cao/thấp)
Phân tích bằng tiếng Việt, tập trung vào insights thực tế và đề xuất hành động cụ thể.";

                var response = await _model.GenerateContentAsync(prompt);
                return response?.Text ?? "🤖 Hiện không thể phân tích chi tiết. Vui lòng kiểm tra kết nối AI.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Lỗi phân tích AI: {ex.Message}");
                return "🤖 Tính năng phân tích AI tạm thời gián đoạn. Các thông tin sản phẩm vẫn hiển thị đầy đủ.";
            }
        }
        //==================================ChatBox====================================
        public async Task<string> AskAsync(string question, string userId = "default")
        {
            if (string.IsNullOrWhiteSpace(question))
                return "Bạn vui lòng nhập câu hỏi.";

            try
            {
                Console.WriteLine($"Bắt đầu xử lý câu hỏi: {question}");

                // Kiểm tra kết nối database trước
                var dbConnected = await CheckDatabaseConnectionAsync();
                if (!dbConnected)
                {
                    return "Xin lỗi, hiện không thể kết nối đến cơ sở dữ liệu. Vui lòng thử lại sau.";
                }

                string contextData = await GetContextualInformation(question, userId);
                Console.WriteLine($"Context data length: {contextData?.Length ?? 0}");

                string enhancedPrompt = BuildEnhancedPrompt(question, contextData, userId);

                var res = await _model.GenerateContentAsync(enhancedPrompt);
                string response = !string.IsNullOrWhiteSpace(res.Text)
                    ? res.Text
                    : "Xin lỗi, tôi chưa thể trả lời câu hỏi này ngay lúc này.";

                SaveConversation(userId, question, response, contextData);
                await UpdateLearningPatterns(question, response);

                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong AskAsync: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return $"Xin lỗi, có lỗi xảy ra khi xử lý: {ex.Message}";
            }
        }

        public async Task<bool> CheckDatabaseConnectionAsync()
        {
            try
            {
                return await _context.Database.CanConnectAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi kết nối database: {ex.Message}");
                return false;
            }
        }

        public async Task<string> TestConnectionAsync()
        {
            try
            {
                var dbConnected = await CheckDatabaseConnectionAsync();
                var productCount = await _context.SanPhams.CountAsync();

                return $"Database connected: {dbConnected}, Số sản phẩm: {productCount}";
            }
            catch (Exception ex)
            {
                return $"Lỗi test connection: {ex.Message}";
            }
        }

        private async Task<string> GetContextualInformation(string question, string userId)
        {
            try
            {
                var contextBuilder = new StringBuilder();
                string q = question.ToLowerInvariant();

                // A. Ý định cụ thể
                contextBuilder.AppendLine(await ProcessSpecificIntent(q));

                // B. Lịch sử hội thoại gần đây
                contextBuilder.AppendLine(GetRecentConversationHistory(userId));

                // C. Thông tin đã học
                contextBuilder.AppendLine(await GetLearnedPatterns(q));

                return contextBuilder.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetContextualInformation: {ex.Message}");
                return $"Lỗi khi lấy thông tin context: {ex.Message}";
            }
        }

        // ================== Ý ĐỊNH ==================
        private async Task<string> ProcessSpecificIntent(string question)
        {
            try
            {
                string q = question.ToLowerInvariant();
                var info = new List<string>();
                // ========== KIỂM TRA DỮ LIỆU ==========
                if (q.Contains("kiểm tra dữ liệu") || q.Contains("test data") || q.Contains("data check"))
                {
                    Console.WriteLine("🎯 Gọi kiểm tra dữ liệu hệ thống");
                    return await CheckDataAvailability();
                }

                // ========== Ý ĐỊNH PHÂN TÍCH AI CHUYÊN SÂU ==========
                if (q.Contains("dư thừa nhiều nhất") || q.Contains("sản phẩm dư thừa nhất"))
                {
                    Console.WriteLine("🎯 Gọi phân tích sản phẩm dư thừa nhiều nhất");
                    var analysis = await AnalyzeInventoryAndSales();
                    if (analysis != null && analysis.Any())
                    {
                        var mostOverstock = analysis
                            .Where(a => a.TyLeDuThua > 0)
                            .OrderByDescending(a => a.TyLeDuThua)
                            .FirstOrDefault();

                        if (mostOverstock != null)
                        {
                            return $"🏆 **SẢN PHẨM DƯ THỪA NHIẾT NHẤT**\n\n" +
                                   $"🔸 **{mostOverstock.TenSanPham}** (Mã: {mostOverstock.MaSanPham})\n" +
                                   $"   - Tồn kho: {mostOverstock.TonKhoHienTai}\n" +
                                   $"   - Tỷ lệ dư thừa: {mostOverstock.TyLeDuThua:P0}\n" +
                                   $"   - Trạng thái: {mostOverstock.TrangThai}\n" +
                                   $"   - Khuyến nghị: {mostOverstock.KhuyenNghi}";
                        }
                    }
                    return "❌ Không tìm thấy sản phẩm dư thừa nào.";
                }

                // ========== Ý ĐỊNH PHÂN TÍCH AI TỔNG QUÁT ==========
                if (q.Contains("dư thừa") || q.Contains("du thua") || q.Contains("thừa") || q.Contains("ứ đọng") ||
                    q.Contains("thống kê dư thừa") || q.Contains("danh sách dư thừa") ||
                    q.Contains("sản phẩm dư thừa"))
                {
                    Console.WriteLine("🎯 Gọi phân tích AI cho sản phẩm dư thừa");
                    return await GetAIAnalysisAndRecommendations(question);
                }
                // Thêm dòng này tạm thời để debug
                if (q.Contains("debug hết hạn"))
                {
                    return await DebugExpiryData();
                }

                if (q.Contains("thiếu hàng") || q.Contains("thieu hang") || q.Contains("sắp hết") ||
                    q.Contains("cần đặt") || q.Contains("hết hàng"))
                {
                    Console.WriteLine("🎯 Gọi phân tích AI cho sản phẩm thiếu hàng");
                    return await GetAIAnalysisAndRecommendations(question);
                }

                if (q.Contains("gợi ý đặt hàng") || q.Contains("khuyến nghị đặt hàng") ||
                    q.Contains("đặt hàng tối ưu") || q.Contains("sản phẩm ưu tiên"))
                {
                    Console.WriteLine("🎯 Gọi phân tích AI cho gợi ý đặt hàng");
                    return await GetAIAnalysisAndRecommendations(question);
                }

                if (q.Contains("phân tích tồn kho") || q.Contains("inventory analysis") ||
                    q.Contains("báo cáo tồn kho") || q.Contains("stock analysis"))
                {
                    Console.WriteLine("🎯 Gọi phân tích AI tổng quan");
                    return await GetAIAnalysisAndRecommendations(question);
                }
                // ========== Ý ĐỊNH PHÂN TÍCH AI & GỢI Ý ĐẶT HÀNG ==========
                if (q.Contains("dư thừa") || q.Contains("du thua") || q.Contains("thừa") || q.Contains("ứ đọng") ||
                    q.Contains("thống kê dư thừa") || q.Contains("danh sách dư thừa") ||
                    q.Contains("sản phẩm dư thừa") || q.Contains("dư thừa nhiều nhất"))
                {
                    Console.WriteLine("🎯 Gọi phân tích AI cho sản phẩm dư thừa");
                    return await GetAIAnalysisAndRecommendations(question);
                }

                if (q.Contains("thiếu hàng") || q.Contains("thieu hang") || q.Contains("sắp hết") ||
                    q.Contains("cần đặt") || q.Contains("hết hàng"))
                {
                    Console.WriteLine("🎯 Gọi phân tích AI cho sản phẩm thiếu hàng");
                    return await GetAIAnalysisAndRecommendations(question);
                }

                if (q.Contains("gợi ý đặt hàng") || q.Contains("khuyến nghị đặt hàng") ||
                    q.Contains("đặt hàng tối ưu") || q.Contains("sản phẩm ưu tiên"))
                {
                    Console.WriteLine("🎯 Gọi phân tích AI cho gợi ý đặt hàng");
                    return await GetAIAnalysisAndRecommendations(question);
                }

                if (q.Contains("phân tích tồn kho") || q.Contains("inventory analysis") ||
                    q.Contains("báo cáo tồn kho") || q.Contains("stock analysis"))
                {
                    Console.WriteLine("🎯 Gọi phân tích AI tổng quan");
                    return await GetAIAnalysisAndRecommendations(question);
                }
                // ========== Ý ĐỊNH PHÂN TÍCH AI & GỢI Ý ĐẶT HÀNG ==========
                if (q.Contains("gợi ý đặt hàng") || q.Contains("khuyến nghị đặt hàng") ||
                    q.Contains("đặt hàng tối ưu") || q.Contains("sản phẩm ưu tiên") ||
                    q.Contains("cần đặt hàng") || q.Contains("phân tích tồn kho") ||
                    q.Contains("dự báo nhu cầu") || q.Contains("tối ưu hóa kho"))
                {
                    return await GetAIAnalysisAndRecommendations(question);
                }
                // ========== Ý ĐỊNH THỐNG KÊ HẾT HẠN ==========
                if (q.Contains("thống kê") && (q.Contains("hết hạn") || q.Contains("het han")))
                {
                    return await GetExpiryStatistics();
                }

                // ========== Ý ĐỊNH VỀ KHO HÀNG CHI TIẾT ==========
                if (q.Contains("mã kho") || q.Contains("ma kho") || q.Contains("kho số") ||
                    q.Contains("thông tin kho") || q.Contains("thong tin kho"))
                {
                    var maKho = ExtractWarehouseCode(question);
                    return await GetWarehouseDetails(maKho);
                }

                if ((q.Contains("sản phẩm trong kho") || q.Contains("san pham trong kho") ||
                     q.Contains("hàng trong kho") || q.Contains("hang trong kho")) &&
                    !string.IsNullOrEmpty(ExtractWarehouseCode(question)))
                {
                    var maKho = ExtractWarehouseCode(question);
                    return await GetProductsInWarehouse(maKho);
                }

                if (q.Contains("thống kê kho") || q.Contains("thong ke kho") ||
                    q.Contains("báo cáo kho") || q.Contains("bao cao kho"))
                {
                    return await GetWarehouseStatistics();
                }

                if (q.Contains("sắp hết hàng") || q.Contains("sap het hang") || q.Contains("hết hàng") ||
                    q.Contains("het hang") || q.Contains("low stock") || q.Contains("sắp hết"))
                {
                    return await GetLowStockProducts();
                }

                // ========== Ý ĐỊNH VỀ HẾT HẠN SẢN PHẨM ==========
                if ((q.Contains("hết hạn") || q.Contains("het han") || q.Contains("sắp hết hạn") ||
                     q.Contains("sap het han") || q.Contains("expir") || q.Contains("hạn sử dụng")))
                {
                    // Nếu có mã sản phẩm cụ thể
                    if (q.Contains("sp") || Regex.IsMatch(q, @"sp\d+", RegexOptions.IgnoreCase))
                    {
                        var maSP = ExtractProductCode(question);
                        if (!string.IsNullOrEmpty(maSP))
                        {
                            return await GetProductExpiryInfo(maSP);
                        }
                    }

                    // Nếu không có mã sản phẩm cụ thể, trả về danh sách
                    return await GetLowStockProducts();
                }
                // ========== Ý ĐỊNH VỀ ĐƠN HÀNG ==========
                if ((q.Contains("chi tiết") && q.Contains("đơn hàng") && q.Contains("khách hàng")) ||
                    (q.Contains("liệt kê") && q.Contains("từng sản phẩm") && q.Contains("từng đơn hàng")))
                {
                    return await GetCustomerOrderDetails(question);
                }

                if ((q.Contains("chi tiết đơn nhập") || q.Contains("chi tiet don nhap") ||
                     q.Contains("xem đơn nhập") || q.Contains("xem don nhap")) &&
                    (q.Contains("dnh") || Regex.IsMatch(q, @"dnh[_\-]?\d+", RegexOptions.IgnoreCase)))
                {
                    var maDon = ExtractImportOrderCode(question);
                    if (!string.IsNullOrEmpty(maDon))
                    {
                        return await GetSpecificImportOrderDetails(maDon);
                    }
                }

                if ((q.Contains("từng sản phẩm") || q.Contains("tung san pham") ||
                     q.Contains("chi tiết sản phẩm") || q.Contains("chi tiet san pham") ||
                     q.Contains("sản phẩm trong đơn") || q.Contains("san pham trong don")) &&
                    (q.Contains("đơn nhập") || q.Contains("don nhap") || q.Contains("nhập hàng")))
                {
                    return await GetImportOrderProductDetails();
                }

                if (q.Contains("đơn hàng") || q.Contains("don hang") || q.Contains("đơn nhập") || q.Contains("đơn xuất"))
                {
                    return await GetOrderInformation(question);
                }

                // ========== Ý ĐỊNH VỀ SẢN PHẨM CHI TIẾT ==========
                if (q.Contains("số lượng từng sản phẩm") || q.Contains("số lượng nhập xuất") ||
                    q.Contains("liệt kê số lượng") || (q.Contains("số lượng") && q.Contains("từng")))
                {
                    return await GetAllProductsInventoryDetails();
                }

                if ((q.Contains("số lượng nhập") || q.Contains("số lượng xuất")) &&
                    (q.Contains("của") || q.Contains("các")))
                {
                    return await GetSpecificProductsInventory(question);
                }

                if (q.Contains("thông tin sản phẩm") || q.Contains("chi tiết sản phẩm") || q.Contains("product info"))
                {
                    var productName = ExtractProductName(question);
                    return await GetProductBasicInfo(productName);
                }

                if (q.Contains("nhà cung cấp của sản phẩm") || q.Contains("ncc của") || q.Contains("supplier"))
                {
                    var productName = ExtractProductName(question);
                    return await GetProductSupplierInfo(productName);
                }

                if (q.Contains("kho của sản phẩm") || q.Contains("sản phẩm lưu ở kho nào") || q.Contains("warehouse"))
                {
                    var productName = ExtractProductName(question);
                    return await GetProductWarehouseInfo(productName);
                }

                if (q.Contains("lịch sử nhập") || q.Contains("nhập hàng của sản phẩm") || q.Contains("import history"))
                {
                    var productName = ExtractProductName(question);
                    return await GetProductImportHistory(productName);
                }

                if (q.Contains("lịch sử xuất") || q.Contains("xuất hàng của sản phẩm") || q.Contains("export history"))
                {
                    var productName = ExtractProductName(question);
                    return await GetProductExportHistory(productName);
                }

                if (q.Contains("thống kê sản phẩm") || q.Contains("thống kê tồn kho") || q.Contains("product statistics"))
                {
                    var productName = ExtractProductName(question);
                    return await GetProductStatistics(productName);
                }

                // ========== Ý ĐỊNH VỀ KHÁCH HÀNG ==========
                if ((q.Contains("danh sách khách hàng") || q.Contains("liệt kê khách hàng") ||
                     q.Contains("khách hàng") && (q.Contains("tất cả") || q.Contains("toàn bộ"))) &&
                    !q.Contains("mua") && !q.Contains("đơn"))
                {
                    return await GetCustomerTableInfo();
                }

                if (q.Contains("khách hàng") || q.Contains("khach hang") || q.Contains("danh sách khách"))
                {
                    return await GetCustomerInformation(q);
                }

                // ========== Ý ĐỊNH VỀ NHÀ CUNG CẤP ==========
                if ((q.Contains("danh sách nhà cung cấp") || q.Contains("liệt kê nhà cung cấp") ||
                     q.Contains("nhà cung cấp") && (q.Contains("tất cả") || q.Contains("toàn bộ"))))
                {
                    return await GetSupplierTableInfo();
                }

                if (q.Contains("nhà cung cấp") || q.Contains("nha cung cap") || q.Contains("ncc"))
                {
                    return await GetSupplierInformation(q);
                }

                // ========== Ý ĐỊNH THỐNG KÊ TỔNG QUAN ==========
                if (q.Contains("thống kê") || q.Contains("thong ke") || q.Contains("báo cáo"))
                {
                    return await GetStatisticalInformation(q);
                }

                // ========== Ý ĐỊNH TỔNG QUÁT (để cuối cùng) ==========
                if (q.Contains("tồn kho") || q.Contains("ton kho") || q.Contains("kho hàng") ||
                    q.Contains("kho") || q.Contains("warehouse") || q.Contains("inventory"))
                {
                    return await GetWarehouseInformation(q);
                }

                if (q.Contains("sản phẩm") || q.Contains("san pham"))
                {
                    return await GetProductInformation(q);
                }

                if (q.Contains("tất cả") || q.Contains("toàn bộ") || q.Contains("danh sách"))
                {
                    return await GetAllInformation(q);
                }

                // ========== Ý ĐỊNH PHÂN TÍCH AI TỔNG QUÁT (để cuối) ==========
                if (q.Contains("phân tích") || q.Contains("gợi ý") || q.Contains("khuyến nghị") ||
                    q.Contains("tối ưu") || q.Contains("dự báo") || q.Contains("ưu tiên") ||
                    q.Contains("thiếu hàng") || q.Contains("dư thừa"))
                {
                    return await GetAIAnalysisAndRecommendations(question);
                }
                if (q.Contains("dư thừa") || q.Contains("du thua") || q.Contains("thừa hàng") || q.Contains("ứ đọng"))
                {
                    return await GetAIAnalysisAndRecommendations(question);
                }
                if (q.Contains("phân tích") || q.Contains("gợi ý") || q.Contains("khuyến nghị") ||
                    q.Contains("tối ưu") || q.Contains("dự báo") || q.Contains("ưu tiên"))
                {
                    Console.WriteLine("🎯 Gọi phân tích AI tổng quát");
                    return await GetAIAnalysisAndRecommendations(question);
                }
                // Sản phẩm thiếu hàng cụ thể  
                if (q.Contains("thiếu hàng") || q.Contains("thieu hang") || q.Contains("sắp hết") || q.Contains("cần đặt"))
                {
                    return await GetAIAnalysisAndRecommendations(question);
                }
                // ========== Ý ĐỊNH PHÂN TÍCH AI TỔNG QUÁT ==========
                if (q.Contains("phân tích") || q.Contains("gợi ý") || q.Contains("khuyến nghị") ||
                    q.Contains("tối ưu") || q.Contains("dự báo") || q.Contains("ưu tiên"))
                {
                    Console.WriteLine("🎯 Gọi phân tích AI tổng quát");
                    return await GetAIAnalysisAndRecommendations(question);
                }
                // ========== MẶC ĐỊNH: TỔNG QUAN HỆ THỐNG ==========
                return await GetGeneralOverview();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong ProcessSpecificIntent: {ex.Message}");
                return $"❌ Lỗi khi xử lý ý định: {ex.Message}";
            }
        }
        private async Task<string> GetExpiryStatistics()
        {
            try
            {
                Console.WriteLine("🔍 Đang thống kê sản phẩm sắp hết hạn...");

                var allWarehouseProducts = await _context.KhoHangs
                    .Where(k => k.SoLuongSapHetHan > 0)
                    .Join(_context.SanPhams,
                        k => k.MaSanPham,
                        p => p.MaSanPham,
                        (k, p) => new { Kho = k, SanPham = p })
                    .ToListAsync();

                Console.WriteLine($"✅ Thống kê: {allWarehouseProducts.Count} bản ghi sắp hết hạn");

                if (!allWarehouseProducts.Any())
                    return "📊 **THỐNG KÊ SẢN PHẨM SẮP HẾT HẠN**: Hiện không có sản phẩm nào sắp hết hạn.";

                var result = new StringBuilder();
                result.AppendLine("📊 **THỐNG KÊ SẢN PHẨM SẮP HẾT HẠN**");
                result.AppendLine();

                // Tính số loại sản phẩm duy nhất
                var distinctProducts = allWarehouseProducts
                    .Select(x => x.SanPham.MaSanPham)
                    .Distinct()
                    .Count();

                // Thống kê theo kho
                var byWarehouse = allWarehouseProducts
                    .GroupBy(x => x.Kho.MaKho)
                    .Select(g => new
                    {
                        MaKho = g.Key,
                        SoSanPham = g.Select(x => x.SanPham.MaSanPham).Distinct().Count(),
                        TongSapHetHan = g.Sum(x => x.Kho.SoLuongSapHetHan)
                    })
                    .OrderByDescending(x => x.TongSapHetHan)
                    .ToList();

                result.AppendLine("🏭 **PHÂN BỐ THEO KHO:**");
                foreach (var wh in byWarehouse)
                {
                    result.AppendLine($"- Kho {wh.MaKho}: {wh.SoSanPham} loại, {wh.TongSapHetHan} sản phẩm sắp hết hạn");
                }

                // Top sản phẩm có nhiều sắp hết hạn nhất
                var topProducts = allWarehouseProducts
                    .GroupBy(x => new { x.SanPham.MaSanPham, x.SanPham.TenSanPham })
                    .Select(g => new
                    {
                        TenSanPham = g.Key.TenSanPham,
                        MaSanPham = g.Key.MaSanPham,
                        TongSapHetHan = g.Sum(x => x.Kho.SoLuongSapHetHan),
                        SoKho = g.Select(x => x.Kho.MaKho).Distinct().Count()
                    })
                    .OrderByDescending(x => x.TongSapHetHan)
                    .Take(5)
                    .ToList();

                if (topProducts.Any())
                {
                    result.AppendLine($"\n🔥 **TOP {topProducts.Count} SẢN PHẨM SẮP HẾT HẠN NHIỀU NHẤT:**");
                    foreach (var product in topProducts)
                    {
                        result.AppendLine($"- {product.TenSanPham} (Mã: {product.MaSanPham}): {product.TongSapHetHan} sản phẩm ở {product.SoKho} kho");
                    }
                }

                // Tổng kết
                result.AppendLine($"\n📈 **TỔNG KẾT:**");
                result.AppendLine($"- Tổng loại sản phẩm: {distinctProducts} loại");
                result.AppendLine($"- Tổng số lượng: {allWarehouseProducts.Sum(x => x.Kho.SoLuongSapHetHan)} sản phẩm sắp hết hạn");
                result.AppendLine($"- Số kho có sản phẩm sắp hết hạn: {byWarehouse.Count}");

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi trong GetExpiryStatistics: {ex.Message}");
                return $"❌ Lỗi khi thống kê sản phẩm hết hạn: {ex.Message}";
            }
        }
        private async Task<string> DebugExpiryData()
        {
            try
            {
                var allKhoHang = await _context.KhoHangs.ToListAsync();

                var result = new StringBuilder();
                result.AppendLine("🔍 **DEBUG DỮ LIỆU KHO HÀNG:**");
                result.AppendLine($"Tổng số bản ghi trong KhoHang: {allKhoHang.Count}");

                var withExpiry = allKhoHang.Where(k => k.SoLuongSapHetHan > 0).ToList();
                result.AppendLine($"Số bản ghi có SoLuongSapHetHan > 0: {withExpiry.Count}");

                foreach (var item in withExpiry)
                {
                    result.AppendLine($"- Kho: {item.MaKho}, SP: {item.MaSanPham}, Tồn: {item.SoLuongTon}, Sắp hết hạn: {item.SoLuongSapHetHan}");
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                return $"Lỗi debug: {ex.Message}";
            }
        }
        // ================== chi tiết sản phẩm ==================
        private async Task<string> GetImportOrderProductDetails()
        {
            try
            {
                var importOrders = await _context.DonNhapHangs
                    .OrderByDescending(d => d.NgayDatHang)
                    .ToListAsync();

                if (!importOrders.Any())
                    return "Hiện không có đơn nhập hàng nào trong hệ thống.";

                var result = new StringBuilder();
                result.AppendLine("📦 CHI TIẾT SẢN PHẨM TRONG CÁC ĐƠN NHẬP HÀNG:");

                foreach (var order in importOrders)
                {
                    result.AppendLine($"\n🏷️ **ĐƠN NHẬP: {order.MaDonNhap}**");
                    result.AppendLine($"📅 Ngày đặt: {order.NgayDatHang:dd/MM/yyyy}");
                    result.AppendLine($"🏢 Nhà cung cấp: {order.MaNCC}");
                    result.AppendLine($"💰 Tổng tiền: {order.TongTien.ToString("N0")}đ");
                    result.AppendLine($"📊 Trạng thái: {order.TrangThai}");
                    result.AppendLine("──────────────────────────────────");

                    // Lấy chi tiết sản phẩm trong đơn hàng
                    var orderDetails = await _context.ChiTietDonNhaps
                        .Where(ct => ct.MaDonNhap == order.MaDonNhap)
                        .Join(_context.SanPhams,
                            ct => ct.MaSanPham,
                            sp => sp.MaSanPham,
                            (ct, sp) => new { ChiTiet = ct, SanPham = sp })
                        .ToListAsync();

                    if (orderDetails.Any())
                    {
                        result.AppendLine("📋 **DANH SÁCH SẢN PHẨM:**");
                        foreach (var detail in orderDetails)
                        {
                            result.AppendLine($"┌─ 📦 {detail.SanPham.TenSanPham}");
                            result.AppendLine($"│  ├ Mã SP: {detail.SanPham.MaSanPham}");
                            result.AppendLine($"│  ├ Số lượng: {detail.ChiTiet.SoLuong}");
                            result.AppendLine($"│  ├ Giá nhập: {detail.ChiTiet.GiaNhap.ToString("N0")}đ");
                            result.AppendLine($"│  └ Thành tiền: {detail.ChiTiet.ThanhTien.ToString("N0")}đ");
                            result.AppendLine($"└────────────────────────");
                        }
                    }
                    else
                    {
                        result.AppendLine("❌ Không có sản phẩm trong đơn hàng này");
                    }

                    result.AppendLine("==================================");
                }

                // Thống kê tổng quan
                var totalProducts = await _context.ChiTietDonNhaps
                    .Where(ct => importOrders.Select(o => o.MaDonNhap).Contains(ct.MaDonNhap))
                    .CountAsync();

                var totalQuantity = await _context.ChiTietDonNhaps
                    .Where(ct => importOrders.Select(o => o.MaDonNhap).Contains(ct.MaDonNhap))
                    .SumAsync(ct => ct.SoLuong);

                result.AppendLine($"\n📊 **TỔNG KẾT:**");
                result.AppendLine($"- Tổng số đơn nhập: {importOrders.Count}");
                result.AppendLine($"- Tổng số sản phẩm nhập: {totalProducts} loại");
                result.AppendLine($"- Tổng số lượng nhập: {totalQuantity} cái");

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetImportOrderProductDetails: {ex.Message}");
                return $"Lỗi khi lấy chi tiết sản phẩm trong đơn nhập: {ex.Message}";
            }
        }
        private async Task<string> GetSpecificImportOrderDetails(string maDon)
        {
            try
            {
                var order = await _context.DonNhapHangs
                    .FirstOrDefaultAsync(d => d.MaDonNhap == maDon);

                if (order == null)
                    return $"❌ Không tìm thấy đơn nhập hàng với mã {maDon}";

                var result = new StringBuilder();
                result.AppendLine($"🏷️ **CHI TIẾT ĐƠN NHẬP: {order.MaDonNhap}**");
                result.AppendLine($"📅 Ngày đặt: {order.NgayDatHang:dd/MM/yyyy}");
                result.AppendLine($"🏢 Nhà cung cấp: {order.MaNCC}");
                result.AppendLine($"💰 Tổng tiền: {order.TongTien.ToString("N0")}đ");
                result.AppendLine($"📊 Trạng thái: {order.TrangThai}");
                result.AppendLine("──────────────────────────────────");

                // Lấy thông tin nhà cung cấp nếu có
                var supplier = await _context.NhaCungCaps
                    .FirstOrDefaultAsync(ncc => ncc.MaNCC == order.MaNCC);

                if (supplier != null)
                {
                    result.AppendLine($"🏭 **Thông tin NCC:**");
                    result.AppendLine($"- Tên: {supplier.TenNCC}");
                    result.AppendLine($"- Người liên hệ: {supplier.NguoiLienHe}");
                    result.AppendLine($"- SĐT: {supplier.SoDienThoai}");
                }

                result.AppendLine("\n📋 **DANH SÁCH SẢN PHẨM:**");

                // Lấy chi tiết sản phẩm
                var orderDetails = await _context.ChiTietDonNhaps
                    .Where(ct => ct.MaDonNhap == maDon)
                    .Join(_context.SanPhams,
                        ct => ct.MaSanPham,
                        sp => sp.MaSanPham,
                        (ct, sp) => new { ChiTiet = ct, SanPham = sp })
                    .ToListAsync();

                if (orderDetails.Any())
                {
                    int stt = 1;
                    foreach (var detail in orderDetails)
                    {
                        result.AppendLine($"\n#{stt++} 📦 **{detail.SanPham.TenSanPham}**");
                        result.AppendLine($"   ├ Mã SP: {detail.SanPham.MaSanPham}");
                        result.AppendLine($"   ├ Loại: {detail.SanPham.LoaiSanPham}");
                        result.AppendLine($"   ├ Số lượng: {detail.ChiTiet.SoLuong}");
                        result.AppendLine($"   ├ Giá nhập: {detail.ChiTiet.GiaNhap.ToString("N0")}đ/SP");
                        result.AppendLine($"   └ Thành tiền: {detail.ChiTiet.ThanhTien.ToString("N0")}đ");
                    }

                    // Tổng kết đơn hàng
                    result.AppendLine($"\n💵 **TỔNG KẾT ĐƠN HÀNG:**");
                    result.AppendLine($"- Tổng số sản phẩm: {orderDetails.Count} loại");
                    result.AppendLine($"- Tổng số lượng: {orderDetails.Sum(d => d.ChiTiet.SoLuong)} cái");
                    result.AppendLine($"- Tổng tiền hàng: {order.TongTien.ToString("N0")}đ");
                }
                else
                {
                    result.AppendLine("❌ Không có sản phẩm trong đơn hàng này");
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetSpecificImportOrderDetails: {ex.Message}");
                return $"Lỗi khi lấy chi tiết đơn nhập: {ex.Message}";
            }
        }
        private string ExtractImportOrderCode(string input)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(input))
                    return null;

                var normalized = RemoveDiacritics(input).ToUpperInvariant();

                // Pattern cho mã đơn nhập: DNHxxx, DNH-xxx, DNH_xxx
                var patterns = new[]
                {
            @"\bDNH[_\-]?\d+\b",
            @"\bDONNHAP[_\-]?\d+\b",
            @"\bIMPORT[_\-]?\d+\b",
            @"\bDNH\s*(\d+)\b"
        };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(normalized, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        if (match.Groups.Count > 1 && !string.IsNullOrEmpty(match.Groups[1].Value))
                        {
                            return "DNH_" + match.Groups[1].Value.PadLeft(6, '0');
                        }
                        return match.Value.ToUpperInvariant();
                    }
                }

                // Fallback: tìm các token có dạng DNH_ + số
                var fallback = new Regex(@"\bDNH_?\d{1,6}\b", RegexOptions.IgnoreCase);
                var fallbackMatch = fallback.Match(normalized);
                if (fallbackMatch.Success)
                    return fallbackMatch.Value.ToUpperInvariant();

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong ExtractImportOrderCode: {ex.Message}");
                return null;
            }
        }
        // ================== KHO HÀNG ==================
        private async Task<string> GetWarehouseInformation(string question)
        {
            try
            {
                string q = question.ToLowerInvariant();

                // Trích xuất mã kho từ câu hỏi
                var maKho = ExtractWarehouseCode(question);

                if (!string.IsNullOrEmpty(maKho))
                {
                    return await GetWarehouseDetails(maKho);
                }

                // Nếu không có mã kho cụ thể, trả về tổng quan kho hàng
                return await GetWarehouseOverview();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetWarehouseInformation: {ex.Message}");
                return $"Lỗi khi lấy thông tin kho hàng: {ex.Message}";
            }
        }
        private string ExtractProductCode(string input)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(input))
                    return null;

                var normalized = RemoveDiacritics(input).ToUpperInvariant();

                // Pattern cho mã sản phẩm: SPxxx, SP-xxx, SP_xxx
                var patterns = new[]
                {
            @"\bSP[_\-]?\d+\b",
            @"\bSANPHAM[_\-]?\d+\b",
            @"\bPRODUCT[_\-]?\d+\b",
            @"\bSP\s*(\d+)\b"
        };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(normalized, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        if (match.Groups.Count > 1 && !string.IsNullOrEmpty(match.Groups[1].Value))
                        {
                            return "SP" + match.Groups[1].Value.PadLeft(3, '0');
                        }
                        return match.Value.ToUpperInvariant();
                    }
                }

                // Fallback: tìm các token có dạng SP + số
                var fallback = new Regex(@"\bSP\d{1,6}\b", RegexOptions.IgnoreCase);
                var fallbackMatch = fallback.Match(normalized);
                if (fallbackMatch.Success)
                    return fallbackMatch.Value.ToUpperInvariant();

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong ExtractProductCode: {ex.Message}");
                return null;
            }
        }
        private async Task<string> GetProductExpiryInfo(string maSP)
        {
            try
            {
                Console.WriteLine($"🔍 Kiểm tra hết hạn cho sản phẩm: {maSP}");

                // Tìm sản phẩm trong kho
                var productInWarehouse = await _context.KhoHangs
                    .FirstOrDefaultAsync(k => k.MaSanPham == maSP);

                if (productInWarehouse == null)
                    return $"❌ Không tìm thấy thông tin kho hàng cho sản phẩm {maSP}.";

                // Tìm thông tin chi tiết sản phẩm
                var product = await _context.SanPhams
                    .FirstOrDefaultAsync(p => p.MaSanPham == maSP);

                var result = new StringBuilder();

                if (product != null)
                {
                    result.AppendLine($"📦 **THÔNG TIN HẾT HẠN - {product.TenSanPham}**");
                    result.AppendLine($"🔸 Mã SP: {maSP}");
                }
                else
                {
                    result.AppendLine($"📦 **THÔNG TIN HẾT HẠN - {maSP}**");
                }

                result.AppendLine($"🔸 Kho: {productInWarehouse.MaKho}");
                result.AppendLine($"🔸 Số lượng tồn kho: {productInWarehouse.SoLuongTon}");
                result.AppendLine($"🔸 Số lượng sắp hết hạn: {productInWarehouse.SoLuongSapHetHan}");

                // Phân loại tình trạng
                if (productInWarehouse.SoLuongSapHetHan > 0)
                {
                    double tyLe = productInWarehouse.SoLuongTon > 0 ?
                        (double)productInWarehouse.SoLuongSapHetHan / productInWarehouse.SoLuongTon * 100 : 100;

                    result.AppendLine($"🔸 Tỷ lệ sắp hết hạn: {tyLe:F1}%");

                    if (tyLe >= 50)
                    {
                        result.AppendLine("🚨 **CẢNH BÁO**: Sản phẩm có hơn 50% sắp hết hạn!");
                    }
                    else if (tyLe >= 30)
                    {
                        result.AppendLine("⚠️ **LƯU Ý**: Sản phẩm có tỷ lệ hết hạn đáng kể");
                    }
                    else
                    {
                        result.AppendLine("🔶 **THẬN TRỌNG**: Có sản phẩm sắp hết hạn");
                    }
                }
                else
                {
                    result.AppendLine("✅ Sản phẩm không có lô nào sắp hết hạn");
                }

                // Thêm thông tin ngày tháng
                result.AppendLine($"🔸 Ngày nhập gần nhất: {productInWarehouse.NgayNhapGanNhat:dd/MM/yyyy}");
                if (productInWarehouse.NgayBanGanNhat.HasValue)
                {
                    result.AppendLine($"🔸 Ngày bán gần nhất: {productInWarehouse.NgayBanGanNhat.Value:dd/MM/yyyy}");
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi trong GetProductExpiryInfo: {ex.Message}");
                return $"❌ Lỗi khi lấy thông tin hết hạn sản phẩm: {ex.Message}";
            }
        }

        private async Task<string> GetProductExpiryDetails(string maSP)
        {
            try
            {
                // Giả sử chúng ta có bảng truy xuất nguồn gốc hoặc lịch sử nhập
                // Nếu không có, có thể sử dụng dữ liệu từ KhoHang
                var warehouseInfo = await _context.KhoHangs
                    .Where(k => k.MaSanPham == maSP)
                    .ToListAsync();

                var result = new StringBuilder();
                result.AppendLine("\n📊 CHI TIẾT TỒN KHO:");

                foreach (var wh in warehouseInfo)
                {
                    var status = wh.SoLuongSapHetHan > 0 ? "⚠️ SẮP HẾT HẠN" : "✅ BÌNH THƯỜNG";
                    result.AppendLine($"- Kho {wh.MaKho}: Tồn {wh.SoLuongTon}, Sắp hết {wh.SoLuongSapHetHan} - {status}");
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetProductExpiryDetails: {ex.Message}");
                return "";
            }
        }
        private async Task<string> GetWarehouseOverview()
        {
            try
            {
                var allWarehouses = await _context.KhoHangs.ToListAsync();

                if (!allWarehouses.Any())
                    return "Hiện chưa có dữ liệu kho hàng.";

                var totalWarehouses = allWarehouses.Select(k => k.MaKho).Distinct().Count();
                var totalProducts = allWarehouses.Count;
                var totalInventory = allWarehouses.Sum(k => k.SoLuongTon);
                var totalLowStock = allWarehouses.Sum(k => k.SoLuongSapHetHan);

                var warehouses = allWarehouses
                    .GroupBy(k => k.MaKho)
                    .Select(g => new
                    {
                        MaKho = g.Key,
                        SoSanPham = g.Count(),
                        TongTon = g.Sum(x => x.SoLuongTon),
                        TongSapHetHan = g.Sum(x => x.SoLuongSapHetHan)
                    })
                    .ToList();

                var result = new StringBuilder();
                result.AppendLine("🏭 TỔNG QUAN KHO HÀNG:");
                result.AppendLine($"- Tổng số kho: {totalWarehouses}");
                result.AppendLine($"- Tổng số sản phẩm trong kho: {totalProducts}");
                result.AppendLine($"- Tổng số lượng tồn kho: {totalInventory}");
                result.AppendLine($"- Tổng sản phẩm sắp hết hạn: {totalLowStock}");

                result.AppendLine("\n📦 CHI TIẾT TỪNG KHO:");
                foreach (var wh in warehouses)
                {
                    result.AppendLine($"- Kho {wh.MaKho}: {wh.SoSanPham} sản phẩm, Tồn: {wh.TongTon}, Sắp hết: {wh.TongSapHetHan}");
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetWarehouseOverview: {ex.Message}");
                return $"Lỗi khi lấy tổng quan kho hàng: {ex.Message}";
            }
        }

        private async Task<string> GetWarehouseDetails(string maKho)
        {
            try
            {
                var warehouseProducts = await _context.KhoHangs
                    .Where(k => k.MaKho == maKho)
                    .ToListAsync();

                if (!warehouseProducts.Any())
                    return $"Không tìm thấy thông tin cho kho {maKho}.";

                var result = new StringBuilder();
                result.AppendLine($"🏭 CHI TIẾT KHO {maKho}:");

                foreach (var product in warehouseProducts)
                {
                    result.AppendLine($"\n📦 {product.TenSanPham} (Mã: {product.MaSanPham})");
                    result.AppendLine($"  - Số lượng tồn: {product.SoLuongTon}");
                    result.AppendLine($"  - Số lượng sắp hết hạn: {product.SoLuongSapHetHan}");
                    result.AppendLine($"  - Ngày nhập gần nhất: {product.NgayNhapGanNhat:dd/MM/yyyy}");

                    if (product.NgayBanGanNhat.HasValue && product.NgayBanGanNhat.Value > DateTime.MinValue)
                    {
                        result.AppendLine($"  - Ngày bán gần nhất: {product.NgayBanGanNhat.Value:dd/MM/yyyy}");
                    }
                }

                var totalProducts = warehouseProducts.Count;
                var totalInventory = warehouseProducts.Sum(k => k.SoLuongTon);
                var totalLowStock = warehouseProducts.Sum(k => k.SoLuongSapHetHan);

                result.AppendLine($"\n📊 TỔNG KẾT KHO {maKho}:");
                result.AppendLine($"- Tổng sản phẩm: {totalProducts}");
                result.AppendLine($"- Tổng tồn kho: {totalInventory}");
                result.AppendLine($"- Tổng sắp hết hạn: {totalLowStock}");

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetWarehouseDetails: {ex.Message}");
                return $"Lỗi khi lấy chi tiết kho hàng: {ex.Message}";
            }
        }

        private async Task<string> GetLowStockProducts()
        {
            try
            {
                Console.WriteLine("🔍 Đang tìm sản phẩm sắp hết hạn...");

                // Sử dụng cùng query với GetExpiryStatistics để đảm bảo nhất quán
                var lowStockProducts = await _context.KhoHangs
                    .Where(k => k.SoLuongSapHetHan > 0)
                    .Join(_context.SanPhams,
                        k => k.MaSanPham,
                        p => p.MaSanPham,
                        (k, p) => new { Kho = k, SanPham = p })
                    .OrderByDescending(x => x.Kho.SoLuongSapHetHan)
                    .ToListAsync();

                Console.WriteLine($"✅ Tìm thấy {lowStockProducts.Count} sản phẩm sắp hết hạn");

                if (!lowStockProducts.Any())
                    return "✅ **DANH SÁCH SẢN PHẨM SẮP HẾT HẠN**: Hiện không có sản phẩm nào sắp hết hạn trong kho.";

                var result = new StringBuilder();
                result.AppendLine("⚠️ **DANH SÁCH SẢN PHẨM SẮP HẾT HẠN**");
                result.AppendLine();

                foreach (var item in lowStockProducts)
                {
                    var tyLe = item.Kho.SoLuongTon > 0 ?
                        (double)item.Kho.SoLuongSapHetHan / item.Kho.SoLuongTon * 100 : 0;

                    result.AppendLine($"📦 **{item.SanPham.TenSanPham}**");
                    result.AppendLine($"   - Mã SP: {item.SanPham.MaSanPham}");
                    result.AppendLine($"   - Kho: {item.Kho.MaKho}");
                    result.AppendLine($"   - Tồn kho: {item.Kho.SoLuongTon}");
                    result.AppendLine($"   - Sắp hết hạn: {item.Kho.SoLuongSapHetHan}");
                    result.AppendLine($"   - Tỷ lệ: {tyLe:F1}%");
                    result.AppendLine($"   - Ngày nhập: {item.Kho.NgayNhapGanNhat:dd/MM/yyyy}");
                    result.AppendLine();
                }

                // Thêm thống kê tổng để nhất quán
                var totalProducts = lowStockProducts.Select(x => x.SanPham.MaSanPham).Distinct().Count();
                var totalQuantity = lowStockProducts.Sum(x => x.Kho.SoLuongSapHetHan);

                result.AppendLine($"📊 **TỔNG KẾT:**");
                result.AppendLine($"- Số loại sản phẩm: {totalProducts}");
                result.AppendLine($"- Tổng số lượng: {totalQuantity} sản phẩm sắp hết hạn");

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi trong GetLowStockProducts: {ex.Message}");
                return $"❌ Lỗi khi lấy sản phẩm sắp hết hạn: {ex.Message}";
            }
        }
        private async Task<string> GetProductsInWarehouse(string maKho)
        {
            try
            {
                var products = await _context.KhoHangs
                    .Where(k => k.MaKho == maKho)
                    .OrderByDescending(k => k.SoLuongTon)
                    .ToListAsync();

                if (!products.Any())
                    return $"Kho {maKho} hiện không có sản phẩm nào.";

                var result = new StringBuilder();
                result.AppendLine($"📦 DANH SÁCH SẢN PHẨM TRONG KHO {maKho}:");

                foreach (var product in products)
                {
                    var status = product.SoLuongSapHetHan > 0 ? " ⚠️ SẮP HẾT" : " ✅ ĐỦ HÀNG";
                    result.AppendLine($"- {product.TenSanPham} (Mã: {product.MaSanPham})");
                    result.AppendLine($"  Tồn: {product.SoLuongTon} {status}");
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetProductsInWarehouse: {ex.Message}");
                return $"Lỗi khi lấy sản phẩm trong kho: {ex.Message}";
            }
        }

        private async Task<string> GetWarehouseStatistics()
        {
            try
            {
                var allWarehouses = await _context.KhoHangs.ToListAsync();

                if (!allWarehouses.Any())
                    return "Chưa có dữ liệu thống kê kho hàng.";

                var stats = allWarehouses
                    .GroupBy(k => k.MaKho)
                    .Select(g => new
                    {
                        MaKho = g.Key,
                        TongSanPham = g.Count(),
                        TongTonKho = g.Sum(x => x.SoLuongTon),
                        TongSapHetHan = g.Sum(x => x.SoLuongSapHetHan),
                        TyLeSapHetHan = g.Sum(x => x.SoLuongSapHetHan) * 100.0 / g.Sum(x => x.SoLuongTon)
                    })
                    .OrderByDescending(x => x.TongTonKho)
                    .ToList();

                var result = new StringBuilder();
                result.AppendLine("📊 THỐNG KÊ KHO HÀNG:");

                foreach (var stat in stats)
                {
                    result.AppendLine($"\n🏭 KHO {stat.MaKho}:");
                    result.AppendLine($"- Số sản phẩm: {stat.TongSanPham}");
                    result.AppendLine($"- Tổng tồn kho: {stat.TongTonKho}");
                    result.AppendLine($"- Sắp hết hạn: {stat.TongSapHetHan}");
                    result.AppendLine($"- Tỷ lệ sắp hết: {stat.TyLeSapHetHan:F1}%");
                }

                // Top 5 sản phẩm tồn nhiều nhất
                var topProducts = allWarehouses
                    .OrderByDescending(k => k.SoLuongTon)
                    .Take(5)
                    .ToList();

                if (topProducts.Any())
                {
                    result.AppendLine("\n🔥 TOP 5 SẢN PHẨM TỒN NHIỀU NHẤT:");
                    foreach (var product in topProducts)
                    {
                        result.AppendLine($"- {product.TenSanPham}: {product.SoLuongTon} (Kho: {product.MaKho})");
                    }
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetWarehouseStatistics: {ex.Message}");
                return $"Lỗi khi lấy thống kê kho hàng: {ex.Message}";
            }
        }
        private string ExtractWarehouseCode(string input)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(input))
                    return null;

                var normalized = RemoveDiacritics(input).ToUpperInvariant();

                // Pattern cho mã kho: KHOxxx, WHxxx, kho-xxx, v.v.
                var patterns = new[]
                {
            @"\bKHO[_\-]?\d+\b",
            @"\bWH[_\-]?\d+\b",
            @"\bKHO\s*(\d+)\b",
            @"\bWAREHOUSE\s*(\d+)\b"
        };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(normalized, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        if (match.Groups.Count > 1 && !string.IsNullOrEmpty(match.Groups[1].Value))
                        {
                            return "KHO" + match.Groups[1].Value.PadLeft(3, '0');
                        }
                        return match.Value.ToUpperInvariant();
                    }
                }

                // Fallback: tìm các token có dạng chữ + số
                var fallback = new Regex(@"\b[A-Z]{2,4}[_\-]?\d{1,6}\b", RegexOptions.IgnoreCase);
                var fallbackMatch = fallback.Match(normalized);
                if (fallbackMatch.Success)
                    return fallbackMatch.Value.ToUpperInvariant();

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong ExtractWarehouseCode: {ex.Message}");
                return null;
            }
        }


        // ================== XỬ LÝ ĐƠN HÀNG CHI TIẾT ==================

        private async Task<string> GetCustomerOrderDetails(string question)
        {
            try
            {
                string q = question.ToLowerInvariant();

                // Trích xuất mã khách hàng từ câu hỏi (nếu có)
                var maKH = ExtractCustomerCode(question);

                // Nếu không có mã cụ thể, lấy tất cả đơn hàng
                var allOrders = await _context.DonXuatHangs
                    .Where(d => string.IsNullOrEmpty(maKH) || d.MaKhachHang == maKH)
                    .ToListAsync();

                if (!allOrders.Any())
                    return "Không tìm thấy đơn hàng nào.";

                var result = new StringBuilder();
                result.AppendLine("CHI TIẾT ĐƠN HÀNG THEO KHÁCH HÀNG:");

                foreach (var order in allOrders)
                {
                    result.AppendLine($"\nĐƠN HÀNG: {order.MaDonXuat}");
                    result.AppendLine($"Khách hàng: {order.TenKhachHang} (Mã: {order.MaKhachHang})");
                    result.AppendLine($"Ngày đặt: {order.NgayXuatHang:dd/MM/yyyy}");
                    result.AppendLine($"Trạng thái: {order.TrangThai}");
                    result.AppendLine($"Tổng tiền: {order.TongTien.ToString("N0")}đ");

                    // Lấy chi tiết sản phẩm trong đơn hàng - SỬA QUERY
                    var orderDetails = await _context.ChiTietDonXuats
                        .Where(ct => ct.MaDonXuat == order.MaDonXuat)
                        .Join(_context.SanPhams,
                            ct => ct.MaSanPham,
                            sp => sp.MaSanPham,
                            (ct, sp) => new { ChiTiet = ct, SanPham = sp })
                        .ToListAsync();

                    if (orderDetails.Any())
                    {
                        result.AppendLine("Chi tiết sản phẩm:");
                        foreach (var detail in orderDetails)
                        {
                            result.AppendLine($"- {detail.SanPham.TenSanPham} (Mã: {detail.SanPham.MaSanPham}): " +
                                            $"{detail.ChiTiet.SoLuong} x {detail.ChiTiet.GiaBan.ToString("N0")}đ = " +
                                            $"{detail.ChiTiet.ThanhTien.ToString("N0")}đ");
                        }
                    }
                    else
                    {
                        result.AppendLine("Không có sản phẩm trong đơn hàng này.");
                    }
                    result.AppendLine("────────────────────────");
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetCustomerOrderDetails: {ex.Message}");
                return $"Lỗi khi lấy chi tiết đơn hàng: {ex.Message}";
            }
        }
        // ================== SẢN PHẨM ==================
        private async Task<string> GetProductInformation(string question)
        {
            try
            {
                string q = question.ToLowerInvariant();
                var productName = ExtractProductName(question);

                // Kiểm tra nếu là mã sản phẩm (SP + số)
                if (Regex.IsMatch(productName, @"^SP\d+$", RegexOptions.IgnoreCase))
                {
                    var product = await _context.SanPhams
                        .FirstOrDefaultAsync(p => p.MaSanPham == productName);

                    if (product != null)
                    {
                        int tonKho = product.SoLuongNhap - product.SoLuongXuat;
                        return $"Sản phẩm {product.TenSanPham} (Mã: {product.MaSanPham}) có tồn kho: {tonKho} cái";
                    }
                    else
                    {
                        return $"Không tìm thấy sản phẩm với mã {productName}";
                    }
                }

                // Trường hợp hỏi số lượng
                if (q.Contains("số lượng") || q.Contains("sl") || q.Contains("tồn kho"))
                {
                    if (!string.IsNullOrEmpty(productName))
                    {
                        return await GetDetailedProductInfo(productName);
                    }
                    else if (q.Contains("tất cả") || q.Contains("các"))
                    {
                        return await GetAllProductsInventoryDetails();
                    }
                }

                if (!string.IsNullOrEmpty(productName))
                {
                    return await GetDetailedProductInfo(productName);
                }

                if (q.Contains("tồn kho"))
                {
                    var types = await GetTotalProductTypesAsync();
                    var total = await GetTotalOnHandAsync();
                    return $"TỒN KHO: {types} loại sản phẩm, {total} tổng số lượng";
                }

                return await GetAllProductsInfo();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetProductInformation: {ex.Message}");
                return $"Lỗi khi lấy thông tin sản phẩm: {ex.Message}";
            }
        }

        private List<string> ExtractAllProductNames(string input)
        {
            try
            {
                var productNames = new List<string>();
                var productKeywords = new[]
                {
                    "iphone11", "iphone 11", "tủ lạnh", "tivi", "laptop",
                    "iphone 13", "iphone13", "iphone 12", "iphone12",
                    "iphone 16", "iphone16", "iphone 15", "iphone15"
                };

                foreach (var keyword in productKeywords)
                {
                    if (input.ToLower().Contains(keyword))
                    {
                        productNames.Add(keyword);
                    }
                }

                return productNames.Distinct().ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong ExtractAllProductNames: {ex.Message}");
                return new List<string>();
            }
        }

        private async Task<string> GetSpecificProductsInventory(string question)
        {
            try
            {
                var productNames = ExtractAllProductNames(question);

                if (!productNames.Any())
                    return await GetAllProductsInventoryDetails();

                var results = new List<string>();
                foreach (var productName in productNames)
                {
                    var products = await _context.SanPhams
                        .Where(p => p.TenSanPham.Contains(productName))
                        .ToListAsync();

                    if (products.Any())
                    {
                        var details = products.Select(p =>
                            $"{p.TenSanPham} (Mã: {p.MaSanPham}): Nhập = {p.SoLuongNhap}, Xuất = {p.SoLuongXuat}, Tồn = {p.SoLuongNhap - p.SoLuongXuat} cái");
                        results.AddRange(details);
                    }
                    else
                    {
                        results.Add($"Không tìm thấy sản phẩm '{productName}'");
                    }
                }

                return $"THÔNG TIN SỐ LƯỢNG: {string.Join("; ", results)}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetSpecificProductsInventory: {ex.Message}");
                return $"Lỗi khi lấy thông tin tồn kho: {ex.Message}";
            }
        }

        private async Task<string> GetAllProductsInventoryDetails()
        {
            try
            {
                var allProducts = await GetAllSanPhamAsync();
                if (!allProducts.Any())
                    return "📦 Không có sản phẩm nào trong kho.";

                var result = new StringBuilder();
                result.AppendLine("📊 **CHI TIẾT SỐ LƯỢNG XUẤT NHẬP TẤT CẢ SẢN PHẨM**");
                result.AppendLine();

                foreach (var product in allProducts.OrderBy(p => p.MaSanPham))
                {
                    int tonKho = product.SoLuongNhap - product.SoLuongXuat;
                    result.AppendLine($"🔹 **{product.TenSanPham}** (Mã: {product.MaSanPham})");
                    result.AppendLine($"   - Số lượng nhập: {product.SoLuongNhap}");
                    result.AppendLine($"   - Số lượng xuất: {product.SoLuongXuat}");
                    result.AppendLine($"   - Tồn kho: {tonKho}");
                    result.AppendLine($"   - Giá bán: {product.GiaBan:N0}đ");
                    result.AppendLine();
                }

                // Thống kê tổng
                int tongNhap = allProducts.Sum(p => p.SoLuongNhap);
                int tongXuat = allProducts.Sum(p => p.SoLuongXuat);
                int tongTon = tongNhap - tongXuat;

                result.AppendLine($"📈 **TỔNG KẾT:**");
                result.AppendLine($"- Tổng nhập: **{tongNhap}** sản phẩm");
                result.AppendLine($"- Tổng xuất: **{tongXuat}** sản phẩm");
                result.AppendLine($"- Tổng tồn kho: **{tongTon}** sản phẩm");
                result.AppendLine($"- Số loại sản phẩm: **{allProducts.Count}**");

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetAllProductsInventoryDetails: {ex.Message}");
                return $"❌ Lỗi khi lấy thông tin tồn kho: {ex.Message}";
            }
        }
        private async Task<string> GetDetailedProductInfo(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName))
                return "❌ Tên sản phẩm không hợp lệ.";

            var product = await _context.SanPhams
                .Where(p => p.TenSanPham.Contains(productName))
                .Join(_context.KhoHangs,
                      sp => sp.MaSanPham,
                      kh => kh.MaSanPham,
                      (sp, kh) => new
                      {
                          sp.TenSanPham,
                          sp.MaSanPham,
                          sp.SoLuongNhap,
                          sp.SoLuongXuat,
                          sp.GiaBan,
                          sp.GiaNhap,
                          kh.SoLuongTon,
                          kh.NgayNhapGanNhat,
                          kh.NgayBanGanNhat
                      })
                .FirstOrDefaultAsync();

            if (product == null)
                return $"⚠️ Không tìm thấy thông tin tồn kho cho sản phẩm '{productName}'.";

            return
                $"📦 **{product.TenSanPham}** (Mã: {product.MaSanPham})\n" +
                $"- Số lượng nhập: **{product.SoLuongNhap}**\n" +
                $"- Số lượng đã xuất: **{product.SoLuongXuat}**\n" +
                $"- Số lượng tồn hiện tại: **{product.SoLuongTon}**\n" +
                $"- Giá nhập: {product.GiaNhap:N0} đ | Giá bán: {product.GiaBan:N0} đ\n" +
                $"- Ngày nhập gần nhất: {product.NgayNhapGanNhat:dd/MM/yyyy}\n" +
                $"{(product.NgayBanGanNhat.HasValue ? $"- Ngày bán gần nhất: {product.NgayBanGanNhat:dd/MM/yyyy}\n" : "")}";
        }

        private async Task<string> GetProductBasicInfo(string productName)
        {
            try
            {
                var product = await _context.SanPhams
                    .FirstOrDefaultAsync(p => p.TenSanPham.Contains(productName));

                if (product == null)
                    return $"Không tìm thấy sản phẩm '{productName}'";

                return $"SẢN PHẨM: {product.TenSanPham} (Mã: {product.MaSanPham})\n" +
                       $"Loại: {product.LoaiSanPham}\n" +
                       $"Giá nhập: {product.GiaNhap:N0} VND\n" +
                       $"Giá bán: {product.GiaBan:N0} VND\n" +
                       $"Tồn kho: {product.SoLuongNhap - product.SoLuongXuat} cái";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetProductBasicInfo: {ex.Message}");
                return $"Lỗi khi lấy thông tin cơ bản sản phẩm: {ex.Message}";
            }
        }

        private async Task<string> GetProductSupplierInfo(string productName)
        {
            try
            {
                var query = from sp in _context.SanPhams
                            join ncc in _context.NhaCungCaps
                                on sp.MaSanPham equals ncc.MaNCC
                            where sp.TenSanPham.Contains(productName)
                            select new { sp.TenSanPham, ncc.TenNCC, ncc.SoDienThoai, ncc.Email };

                var result = await query.FirstOrDefaultAsync();
                if (result == null)
                    return $"Không tìm thấy nhà cung cấp cho sản phẩm '{productName}'";

                return $"SẢN PHẨM '{result.TenSanPham}' được cung cấp bởi {result.TenNCC} " +
                       $"(SĐT: {result.SoDienThoai}, Email: {result.Email})";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetProductSupplierInfo: {ex.Message}");
                return $"Lỗi khi lấy thông tin nhà cung cấp: {ex.Message}";
            }
        }

        private async Task<string> GetProductWarehouseInfo(string productName)
        {
            try
            {
                var query = from sp in _context.SanPhams
                            join kh in _context.KhoHangs
                                on sp.MaSanPham equals kh.MaKho
                            where sp.TenSanPham.Contains(productName)
                            select new { sp.TenSanPham, kh.SoLuongTon, kh.SoLuongSapHetHan };

                var result = await query.FirstOrDefaultAsync();
                if (result == null)
                    return $"Không tìm thấy thông tin kho của sản phẩm '{productName}'";

                return $"SẢN PHẨM '{result.TenSanPham}' hiện đang lưu trữ tại kho {result.SoLuongTon}, địa chỉ {result.SoLuongSapHetHan}.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetProductWarehouseInfo: {ex.Message}");
                return $"Lỗi khi lấy thông tin kho: {ex.Message}";
            }
        }
        private async Task<string> GetProductImportHistory(string productName)
        {
            try
            {
                // Tìm mã sản phẩm
                var product = await _context.SanPhams
                    .FirstOrDefaultAsync(p => p.TenSanPham.Contains(productName));

                if (product == null)
                    return $"Không tìm thấy sản phẩm '{productName}'";

                // Lấy lịch sử nhập hàng
                var importDetails = await _context.ChiTietDonNhaps
                    .Where(ct => ct.MaSanPham == product.MaSanPham)
                    .Join(_context.DonNhapHangs,
                        ct => ct.MaDonNhap,
                        dn => dn.MaDonNhap,
                        (ct, dn) => new { ChiTiet = ct, DonNhap = dn })
                    .OrderByDescending(x => x.DonNhap.NgayDatHang)
                    .Take(5)
                    .ToListAsync();

                if (!importDetails.Any())
                    return $"Không có lịch sử nhập hàng cho sản phẩm '{productName}'";

                var result = new StringBuilder();
                result.AppendLine($"📦 **LỊCH SỬ NHẬP HÀNG - {product.TenSanPham}**\n");

                foreach (var detail in importDetails)
                {
                    result.AppendLine($"🏷️ Đơn: {detail.DonNhap.MaDonNhap}");
                    result.AppendLine($"   - Ngày: {detail.DonNhap.NgayDatHang:dd/MM/yyyy}");
                    result.AppendLine($"   - Số lượng: +{detail.ChiTiet.SoLuong}");
                    result.AppendLine($"   - Giá nhập: {detail.ChiTiet.GiaNhap:N0}đ");
                    result.AppendLine();
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetProductImportHistory: {ex.Message}");
                return $"Lỗi khi lấy lịch sử nhập hàng: {ex.Message}";
            }
        }

        private async Task<string> GetProductExportHistory(string productName)
        {
            try
            {
                // Tìm mã sản phẩm
                var product = await _context.SanPhams
                    .FirstOrDefaultAsync(p => p.TenSanPham.Contains(productName));

                if (product == null)
                    return $"Không tìm thấy sản phẩm '{productName}'";

                // Lấy lịch sử xuất hàng
                var exportDetails = await _context.ChiTietDonXuats
                    .Where(ct => ct.MaSanPham == product.MaSanPham)
                    .Join(_context.DonXuatHangs,
                        ct => ct.MaDonXuat,
                        dx => dx.MaDonXuat,
                        (ct, dx) => new { ChiTiet = ct, DonXuat = dx })
                    .OrderByDescending(x => x.DonXuat.NgayXuatHang)
                    .Take(5)
                    .ToListAsync();

                if (!exportDetails.Any())
                    return $"Không có lịch sử xuất hàng cho sản phẩm '{productName}'";

                var result = new StringBuilder();
                result.AppendLine($"🚚 **LỊCH SỬ XUẤT HÀNG - {product.TenSanPham}**\n");

                foreach (var detail in exportDetails)
                {
                    result.AppendLine($"🏷️ Đơn: {detail.DonXuat.MaDonXuat}");
                    result.AppendLine($"   - Ngày: {detail.DonXuat.NgayXuatHang:dd/MM/yyyy}");
                    result.AppendLine($"   - Số lượng: -{detail.ChiTiet.SoLuong}");
                    result.AppendLine($"   - Giá bán: {detail.ChiTiet.GiaBan:N0}đ");
                    result.AppendLine($"   - Khách: {detail.DonXuat.TenKhachHang}");
                    result.AppendLine();
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetProductExportHistory: {ex.Message}");
                return $"Lỗi khi lấy lịch sử xuất hàng: {ex.Message}";
            }
        }

        private async Task<string> GetProductStatistics(string productName)
        {
            try
            {
                var product = await _context.SanPhams
                    .FirstOrDefaultAsync(p => p.TenSanPham.Contains(productName));

                if (product == null)
                    return $"Không tìm thấy sản phẩm '{productName}'";

                int ton = product.SoLuongNhap - product.SoLuongXuat;

                return $"THỐNG KÊ '{product.TenSanPham}':\n" +
                       $"- Tổng nhập: {product.SoLuongNhap}\n" +
                       $"- Tổng xuất: {product.SoLuongXuat}\n" +
                       $"- Hiện tồn: {ton}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetProductStatistics: {ex.Message}");
                return $"Lỗi khi lấy thống kê sản phẩm: {ex.Message}";
            }
        }

        // ================== KHÁCH HÀNG ==================
        private async Task<string> GetCustomerInformation(string question)
        {
            try
            {
                string q = question.ToLowerInvariant();
                var allCustomers = await GetAllKhachHangAsync();

                if (allCustomers == null || !allCustomers.Any())
                    return "👥 Hiện chưa có dữ liệu khách hàng.";

                // Trích xuất mã khách hàng từ câu hỏi
                var maKH = ExtractCustomerCode(question);

                // ========== HIỂN THỊ DANH SÁCH KHÁCH HÀNG ==========
                if (q.Contains("danh sách") || q.Contains("tất cả") || q.Contains("liệt kê") ||
                    (q.Contains("khách hàng") && !q.Contains("mua") && !q.Contains("đơn") && !q.Contains("nhiều nhất")))
                {
                    return await GetCustomerTableInfo();
                }

                // ========== XỬ LÝ CÂU HỎI VỀ KHÁCH HÀNG CỤ THỂ ==========
                if (!string.IsNullOrEmpty(maKH))
                {
                    var customer = await _context.KhachHangs
                        .FirstOrDefaultAsync(kh => kh.MaKhachHang == maKH);

                    if (customer == null)
                        return $"❌ Không tìm thấy khách hàng với mã {maKH}";

                    var result = new StringBuilder();
                    result.AppendLine($"👤 **THÔNG TIN CHI TIẾT KHÁCH HÀNG**");
                    result.AppendLine($"┌────────────────────────────────────────┐");
                    result.AppendLine($"│ 🔸 Mã KH: {customer.MaKhachHang}");
                    result.AppendLine($"│ 🔸 Tên: {customer.TenKhachHang}");
                    result.AppendLine($"│ 🔸 SĐT: {customer.SoDienThoai ?? "N/A"}");
                    result.AppendLine($"│ 🔸 Địa chỉ: {customer.DiaChi ?? "N/A"}");
                    result.AppendLine($"└────────────────────────────────────────┘");

                    // Lấy thông tin đơn hàng của khách hàng
                    var purchaseDetails = await GetCustomerPurchaseDetailsAsync(maKH);

                    if (purchaseDetails != null && purchaseDetails.Any())
                    {
                        result.AppendLine($"\n🛒 **LỊCH SỬ MUA HÀNG:**");

                        // Hiển thị dạng danh sách thay vì bảng
                        var recentOrders = purchaseDetails
                            .OrderByDescending(p => p.NgayMua)
                            .Take(5)
                            .GroupBy(p => p.MaDonHang)
                            .ToList();

                        foreach (var orderGroup in recentOrders)
                        {
                            var firstItem = orderGroup.First();
                            result.AppendLine($"\n📦 **Đơn {orderGroup.Key}** (Ngày: {firstItem.NgayMua:dd/MM/yyyy})");
                            foreach (var item in orderGroup)
                            {
                                result.AppendLine($"   - {item.TenSanPham} x{item.SoLuong} = {item.ThanhTien:N0}đ");
                            }
                        }

                        // Thống kê tổng
                        var totalSpent = purchaseDetails.Sum(p => p.ThanhTien);
                        var totalOrders = purchaseDetails.Select(p => p.MaDonHang).Distinct().Count();
                        var totalProducts = purchaseDetails.Sum(p => p.SoLuong);

                        result.AppendLine($"\n📊 **TỔNG KẾT:**");
                        result.AppendLine($"- Tổng số đơn: {totalOrders}");
                        result.AppendLine($"- Tổng sản phẩm đã mua: {totalProducts}");
                        result.AppendLine($"- Tổng chi tiêu: {totalSpent:N0}đ");
                    }
                    else
                    {
                        result.AppendLine($"\n📭 Khách hàng chưa có đơn hàng nào.");
                    }

                    return result.ToString();
                }

                // ========== KHÁCH HÀNG MUA NHIỀU NHẤT ==========
                if (q.Contains("nhiều nhất") || q.Contains("mua nhiều") || q.Contains("top khách"))
                {
                    var topCustomers = await _context.DonXuatHangs
                        .Where(dx => dx.MaKhachHang != null)
                        .GroupBy(dx => new { dx.MaKhachHang, dx.TenKhachHang })
                        .Select(g => new
                        {
                            MaKH = g.Key.MaKhachHang,
                            TenKH = g.Key.TenKhachHang,
                            TongTien = g.Sum(dx => dx.TongTien),
                            SoDon = g.Count()
                        })
                        .OrderByDescending(x => x.TongTien)
                        .Take(5)
                        .ToListAsync();

                    if (!topCustomers.Any())
                        return "📊 Chưa có dữ liệu đơn hàng để thống kê khách hàng.";

                    var result = new StringBuilder();
                    result.AppendLine("🏆 **TOP KHÁCH HÀNG MUA NHIỀU NHẤT**\n");

                    foreach (var customer in topCustomers)
                    {
                        result.AppendLine($"🔹 **{customer.TenKH}**");
                        result.AppendLine($"   - Mã KH: {customer.MaKH}");
                        result.AppendLine($"   - Số đơn: {customer.SoDon}");
                        result.AppendLine($"   - Tổng tiền: {customer.TongTien:N0}đ");
                        result.AppendLine();
                    }

                    return result.ToString();
                }

                // ========== MẶC ĐỊNH: HIỂN THỊ DANH SÁCH KHÁCH HÀNG ==========
                return await GetCustomerTableInfo();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetCustomerInformation: {ex.Message}");
                return $"❌ Lỗi khi lấy thông tin khách hàng: {ex.Message}";
            }
        }
        // ================== ĐƠN HÀNG ==================
        private async Task<string> GetOrderInformation(string question)
        {
            try
            {
                string q = question.ToLowerInvariant();

                if (q.Contains("đơn nhập") || q.Contains("don nhap") || q.Contains("nhập hàng"))
                {
                    // Nếu có yêu cầu chi tiết sản phẩm
                    if (q.Contains("chi tiết") || q.Contains("chi tiet") ||
                        q.Contains("từng sản phẩm") || q.Contains("tung san pham"))
                    {
                        return await GetImportOrderProductDetails();
                    }

                    var orders = await GetAllDonNhapAsync();
                    if (!orders.Any()) return "Chưa có đơn nhập hàng nào.";

                    var orderInfo = orders.Select(o =>
                        $"Đơn {o.MaDonNhap} - {o.TrangThai} - {o.TongTien.ToString("N0")}đ - Ngày: {o.NgayDatHang:dd/MM/yyyy}");

                    return $"ĐƠN NHẬP HÀNG: {string.Join("; ", orderInfo)}\n\n" +
                           "💡 Gõ 'chi tiết đơn nhập hàng' để xem thông tin từng sản phẩm";
                }
                if (q.Contains("đơn nhập") || q.Contains("don nhap") || q.Contains("nhập hàng"))
                {
                    var orders = await GetAllDonNhapAsync();
                    if (!orders.Any()) return "Chưa có đơn nhập hàng nào.";

                    var orderInfo = orders.Select(o => $"Đơn {o.MaDonNhap} - {o.TrangThai} - {o.TongTien.ToString("N0")}đ - Ngày: {o.NgayDatHang:dd/MM/yyyy}");
                    return $"ĐƠN NHẬP HÀNG: {string.Join("; ", orderInfo)}";
                }

                if (q.Contains("đơn xuất") || q.Contains("don xuat") || q.Contains("xuất hàng"))
                {
                    var orders = await GetAllDonXuatAsync();
                    if (!orders.Any()) return "Chưa có đơn xuất hàng nào.";

                    var orderInfo = orders.Select(o => $"Đơn {o.MaDonXuat} - {o.TenKhachHang} - {o.TongTien.ToString("N0")}đ - Ngày: {o.NgayXuatHang:dd/MM/yyyy}");
                    return $"ĐƠN XUẤT HÀNG: {string.Join("; ", orderInfo)}";
                }

                // Xử lý câu hỏi chi tiết đơn hàng cụ thể
                if (q.Contains("chi tiết") || q.Contains("xem đơn") || q.Contains("đơn") && (q.Contains("dxh") || q.Contains("dnh")))
                {
                    // Trích xuất mã đơn hàng từ câu hỏi
                    var maDonPattern = @"(DXH_|DNH_)[A-Z0-9_]+|\b([A-Z0-9_]{8,})\b";
                    var match = Regex.Match(question, maDonPattern, RegexOptions.IgnoreCase);
                    string maDon = match.Success ? match.Value : "";

                    if (string.IsNullOrEmpty(maDon))
                    {
                        return "Vui lòng cung cấp mã đơn hàng để xem chi tiết. Ví dụ: 'chi tiết đơn DXH_000001'";
                    }

                    // Kiểm tra xem là đơn nhập hay đơn xuất
                    if (maDon.StartsWith("DNH_", StringComparison.OrdinalIgnoreCase))
                    {
                        var donNhap = await _context.DonNhapHangs.FirstOrDefaultAsync(d => d.MaDonNhap == maDon);
                        if (donNhap == null)
                            return $"Không tìm thấy đơn nhập hàng với mã {maDon}";

                        var chiTiet = await _context.ChiTietDonNhaps
                            .Where(ct => ct.MaDonNhap == maDon)
                            .Join(_context.SanPhams,
                                ct => ct.MaSanPham,
                                sp => sp.MaSanPham,
                                (ct, sp) => new { ChiTiet = ct, SanPham = sp })
                            .ToListAsync();

                        var result = new StringBuilder();
                        result.AppendLine($"CHI TIẾT ĐƠN NHẬP: {maDon}");
                        result.AppendLine($"Nhà cung cấp: {donNhap.MaNCC}");
                        result.AppendLine($"Ngày nhập: {donNhap.NgayDatHang:dd/MM/yyyy}");
                        result.AppendLine($"Trạng thái: {donNhap.TrangThai}");
                        result.AppendLine($"Tổng tiền: {donNhap.TongTien.ToString("N0")}đ");
                        result.AppendLine("────────────────────────");

                        if (chiTiet.Any())
                        {
                            result.AppendLine("DANH SÁCH SẢN PHẨM:");
                            foreach (var item in chiTiet)
                            {
                                result.AppendLine($"- {item.SanPham.TenSanPham} (Mã: {item.SanPham.MaSanPham})");
                                result.AppendLine($"  Số lượng: {item.ChiTiet.SoLuong}");
                                result.AppendLine($"  Giá nhập: {item.ChiTiet.GiaNhap.ToString("N0")}đ");
                                result.AppendLine($"  Thành tiền: {item.ChiTiet.ThanhTien.ToString("N0")}đ");
                                result.AppendLine("  ──────────────────────");
                            }
                        }
                        else
                        {
                            result.AppendLine("Không có sản phẩm trong đơn hàng này.");
                        }

                        return result.ToString();
                    }
                    else if (maDon.StartsWith("DXH_", StringComparison.OrdinalIgnoreCase))
                    {
                        var donXuat = await _context.DonXuatHangs.FirstOrDefaultAsync(d => d.MaDonXuat == maDon);
                        if (donXuat == null)
                            return $"Không tìm thấy đơn xuất hàng với mã {maDon}";

                        var chiTiet = await _context.ChiTietDonXuats
                            .Where(ct => ct.MaDonXuat == maDon)
                            .Join(_context.SanPhams,
                                ct => ct.MaSanPham,
                                sp => sp.MaSanPham,
                                (ct, sp) => new { ChiTiet = ct, SanPham = sp })
                            .ToListAsync();

                        var result = new StringBuilder();
                        result.AppendLine($"CHI TIẾT ĐƠN XUẤT: {maDon}");
                        result.AppendLine($"Khách hàng: {donXuat.TenKhachHang} (Mã: {donXuat.MaKhachHang})");
                        result.AppendLine($"Ngày xuất: {donXuat.NgayXuatHang:dd/MM/yyyy}");
                        result.AppendLine($"Trạng thái: {donXuat.TrangThai}");
                        result.AppendLine($"Tổng tiền: {donXuat.TongTien.ToString("N0")}đ");
                        result.AppendLine("────────────────────────");

                        if (chiTiet.Any())
                        {
                            result.AppendLine("DANH SÁCH SẢN PHẨM:");
                            foreach (var item in chiTiet)
                            {
                                result.AppendLine($"- {item.SanPham.TenSanPham} (Mã: {item.SanPham.MaSanPham})");
                                result.AppendLine($"  Số lượng: {item.ChiTiet.SoLuong}");
                                result.AppendLine($"  Giá bán: {item.ChiTiet.GiaBan.ToString("N0")}đ");
                                result.AppendLine($"  Thành tiền: {item.ChiTiet.ThanhTien.ToString("N0")}đ");
                                result.AppendLine("  ──────────────────────");
                            }
                        }
                        else
                        {
                            result.AppendLine("Không có sản phẩm trong đơn hàng này.");
                        }

                        return result.ToString();
                    }
                    else
                    {
                        return $"Mã đơn hàng {maDon} không đúng định dạng. Vui lòng kiểm tra lại (DXH_... cho đơn xuất, DNH_... cho đơn nhập).";
                    }
                }

                return "Có dữ liệu về đơn hàng nhập và xuất. Bạn muốn xem chi tiết loại nào?";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetOrderInformation: {ex.Message}");
                return $"Lỗi khi lấy thông tin đơn hàng: {ex.Message}";
            }
        }

        // ================== NHÀ CUNG CẤP ==================
        private async Task<string> GetSupplierInformation(string question)
        {
            try
            {
                string q = question.ToLowerInvariant();

                // Nếu người dùng có nhắc đến mã NCC
                var maNCC = ExtractSupplierCode(question);

                var suppliers = await GetAllNCCAsync();
                if (!suppliers.Any()) return "🏭 Chưa có dữ liệu nhà cung cấp.";

                // ========== HIỂN THỊ DANH SÁCH NHÀ CUNG CẤP ==========
                if ((q.Contains("danh sách") || q.Contains("tất cả") || q.Contains("liệt kê")) &&
                    string.IsNullOrEmpty(maNCC))
                {
                    return await GetSupplierTableInfo();
                }

                var result = new StringBuilder();

                if (!string.IsNullOrEmpty(maNCC))
                {
                    // Lấy thông tin chi tiết NCC cụ thể
                    var supplier = suppliers.FirstOrDefault(s => s.MaNCC == maNCC);
                    if (supplier == null) return $"❌ Không tìm thấy nhà cung cấp với mã {maNCC}.";

                    result.AppendLine($"🏭 **THÔNG TIN CHI TIẾT NHÀ CUNG CẤP**");
                    result.AppendLine($"┌────────────────────────────────────────┐");
                    result.AppendLine($"│ 🔸 Mã NCC: {supplier.MaNCC}");
                    result.AppendLine($"│ 🔸 Tên: {supplier.TenNCC}");
                    result.AppendLine($"│ 🔸 Người liên hệ: {supplier.NguoiLienHe ?? "N/A"}");
                    result.AppendLine($"│ 🔸 SĐT: {supplier.SoDienThoai ?? "N/A"}");
                    result.AppendLine($"│ 🔸 Email: {supplier.Email ?? "N/A"}");
                    result.AppendLine($"│ 🔸 Địa chỉ: {supplier.DiaChi ?? "N/A"}");
                    result.AppendLine($"└────────────────────────────────────────┘");

                    // Lấy thống kê đơn nhập liên quan đến NCC
                    var orders = await _context.DonNhapHangs
                        .Where(d => d.MaNCC == maNCC)
                        .ToListAsync();

                    if (orders.Any())
                    {
                        decimal tongGiaTri = orders.Sum(o => o.TongTien);
                        int tongDon = orders.Count;

                        result.AppendLine($"\n📊 **THỐNG KÊ ĐƠN NHẬP:**");
                        result.AppendLine($"- Tổng số đơn nhập: {tongDon}");
                        result.AppendLine($"- Tổng giá trị nhập: {tongGiaTri:N0}đ");

                        // Liệt kê 3 đơn nhập gần nhất
                        var latestOrders = orders.OrderByDescending(o => o.NgayDatHang).Take(3).ToList();
                        result.AppendLine($"\n🕒 **3 ĐƠN NHẬP GẦN NHẤT:**");
                        foreach (var o in latestOrders)
                        {
                            result.AppendLine($"- Đơn {o.MaDonNhap}: Ngày {o.NgayDatHang:dd/MM/yyyy}, Trị giá {o.TongTien:N0}đ");
                        }
                    }
                    else
                    {
                        result.AppendLine("\n❌ Nhà cung cấp này chưa có đơn nhập nào.");
                    }
                }
                else
                {
                    // Nếu không chỉ định mã → hiển thị danh sách NCC
                    return await GetSupplierTableInfo();
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetSupplierInformation: {ex.Message}");
                return $"❌ Lỗi khi lấy thông tin nhà cung cấp: {ex.Message}";
            }
        }
        // ================== THỐNG KÊ ==================
        private async Task<string> GetStatisticalInformation(string question)
        {
            try
            {
                string q = question.ToLowerInvariant();
                var stats = new List<string>();

                // Thống kê tồn kho
                var totalProducts = await GetTotalProductTypesAsync();
                var totalInventory = await GetTotalOnHandAsync();
                stats.Add($"Tổng số loại sản phẩm: {totalProducts}");
                stats.Add($"Tổng số lượng tồn kho: {totalInventory}");

                // Sản phẩm tồn nhiều nhất
                var topStock = await _context.SanPhams
                    .OrderByDescending(p => p.SoLuongNhap - p.SoLuongXuat)
                    .FirstOrDefaultAsync();
                if (topStock != null)
                    stats.Add($"Sản phẩm tồn nhiều nhất: {topStock.TenSanPham} ({topStock.SoLuongNhap - topStock.SoLuongXuat} cái)");

                // Thống kê đơn hàng
                var importOrders = await _context.DonNhapHangs.CountAsync();
                var exportOrders = await _context.DonXuatHangs.CountAsync();
                stats.Add($"Tổng đơn nhập: {importOrders}");
                stats.Add($"Tổng đơn xuất: {exportOrders}");

                // Doanh thu & lợi nhuận
                if (q.Contains("doanh thu") || q.Contains("lợi nhuận") || q.Contains("thu nhập") || q.Contains("doanh số"))
                {
                    var totalRevenue = await _context.DonXuatHangs.SumAsync(d => d.TongTien);
                    var totalCost = await _context.DonNhapHangs.SumAsync(d => d.TongTien);
                    var profit = totalRevenue - totalCost;

                    stats.Add($"Tổng doanh thu: {totalRevenue.ToString("N0")}đ");
                    stats.Add($"Tổng chi phí: {totalCost.ToString("N0")}đ");
                    stats.Add($"Lợi nhuận: {profit.ToString("N0")}đ");

                    // Doanh thu tháng hiện tại
                    var now = DateTime.Now;
                    var monthlyRevenue = await _context.DonXuatHangs
                        .Where(d => d.NgayXuatHang.Month == now.Month && d.NgayXuatHang.Year == now.Year)
                        .SumAsync(d => d.TongTien);
                    stats.Add($"Doanh thu tháng {now.Month}/{now.Year}: {monthlyRevenue.ToString("N0")}đ");
                }

                // Thống kê khách hàng
                var totalCustomers = await _context.KhachHangs.CountAsync();
                stats.Add($"Tổng số khách hàng: {totalCustomers}");

                // Khách hàng mua nhiều nhất
                var topCustomer = await _context.DonXuatHangs
                    .GroupBy(d => d.MaKhachHang)
                    .Select(g => new { MaKH = g.Key, TongMua = g.Sum(x => x.TongTien) })
                    .OrderByDescending(g => g.TongMua)
                    .FirstOrDefaultAsync();
                if (topCustomer != null)
                {
                    var kh = await _context.KhachHangs.FindAsync(topCustomer.MaKH);
                    if (kh != null)
                        stats.Add($"Khách hàng mua nhiều nhất: {kh.TenKhachHang} ({topCustomer.TongMua.ToString("N0")}đ)");
                }

                // Thống kê nhà cung cấp
                var totalSuppliers = await _context.NhaCungCaps.CountAsync();
                stats.Add($"Tổng số nhà cung cấp: {totalSuppliers}");

                var topSupplier = await _context.DonNhapHangs
                    .GroupBy(d => d.MaNCC)
                    .Select(g => new { MaNCC = g.Key, TongNhap = g.Sum(x => x.TongTien) })
                    .OrderByDescending(g => g.TongNhap)
                    .FirstOrDefaultAsync();
                if (topSupplier != null)
                {
                    var ncc = await _context.NhaCungCaps.FindAsync(topSupplier.MaNCC);
                    if (ncc != null)
                        stats.Add($"Nhà cung cấp lớn nhất: {ncc.TenNCC} ({topSupplier.TongNhap.ToString("N0")}đ)");
                }

                return $"THỐNG KÊ HỆ THỐNG: {string.Join("; ", stats)}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetStatisticalInformation: {ex.Message}");
                return $"Lỗi khi lấy thông tin thống kê: {ex.Message}";
            }
        }

        // ================== TRÍCH XUẤT MÃ KHÁCH HÀNG NÂNG CAO ==================
        private string? ExtractCustomerCode(string input)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(input))
                    return null;

                var normalized = RemoveDiacritics(input).ToUpperInvariant();

                // ================== 1) Mẫu KHxxx ==================
                // Ví dụ: KH001, KH-001, KH_002
                var p1 = new Regex(@"\bKH[_\-]?\d{1,6}\b", RegexOptions.IgnoreCase);
                var m = p1.Match(normalized);
                if (m.Success)
                    return m.Value.ToUpperInvariant();

                // ================== 2) Mẫu "Mã khách" ==================
                // Ví dụ: "Mã khách: KH001", "MA KH 002"
                var p2 = new Regex(@"\b(?:MA|MÃ)\s*(?:KHACH|KH)?\s*[:\-]?\s*([A-Z]{2,4}[_\-]?\d{1,6})\b", RegexOptions.IgnoreCase);
                m = p2.Match(normalized);
                if (m.Success && !string.IsNullOrEmpty(m.Groups[1].Value))
                    return m.Groups[1].Value.ToUpperInvariant();

                // ================== 3) Mẫu chỉ số (nếu người dùng gõ "khách 001") ==================
                var p3 = new Regex(@"\bKHACH\s*(\d{1,6})\b", RegexOptions.IgnoreCase);
                m = p3.Match(normalized);
                if (m.Success)
                    return "KH" + m.Groups[1].Value.PadLeft(3, '0'); // Chuẩn hóa thành KHxxx

                // ================== 4) Fallback ==================
                // Bắt bất kỳ token nào giống KH001, NCC01, SP01
                var fallback = new Regex(@"\b[A-Z]{2,4}[_\-]?\d{1,6}\b", RegexOptions.IgnoreCase);
                m = fallback.Match(normalized);
                if (m.Success)
                    return m.Value.ToUpperInvariant();

                // ================== 5) Trường hợp người dùng chỉ gõ số ==================
                // Ví dụ: "Khách hàng 15" → "KH015"
                var p5 = new Regex(@"\b(?:KHACH|KH)\s*(\d{1,6})\b", RegexOptions.IgnoreCase);
                m = p5.Match(normalized);
                if (m.Success)
                    return "KH" + m.Groups[1].Value.PadLeft(3, '0');

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong ExtractCustomerCode: {ex.Message}");
                return null;
            }
        }

        private string? ExtractSupplierCode(string input)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(input))
                    return null;

                // Chuẩn hóa: xóa dấu, chuyển về chữ thường để dễ so khớp
                var normalized = RemoveDiacritics(input).ToUpperInvariant();

                // 1) Tìm các pattern rõ ràng như NCC123, NCC-123, NCC_123
                var patternCodeLike = new Regex(@"\bNCC[_\-]?\d+\b", RegexOptions.IgnoreCase);
                var m = patternCodeLike.Match(normalized);
                if (m.Success) return m.Value.ToUpperInvariant();

                // 2) Nếu có cụm "MÃ NHA CUNG CAP", "MÃ NCC", "MA NCC"
                var patternAfterKeyword = new Regex(@"\b(?:MA|MÃ)\s*(?:NHÀ\s*CUNG\s*CẤP|NHA\s*CUNG\s*CAP|NCC)?\s*[:\-]?\s*([A-Z0-9_\-]+)\b",
                                                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                m = patternAfterKeyword.Match(normalized);
                if (m.Success && !string.IsNullOrEmpty(m.Groups[1].Value))
                    return m.Groups[1].Value.ToUpperInvariant();

                // 3) Fallback: tìm token có dạng 2-4 chữ cái + số (ví dụ: KH12, NCC01, DN0001, DXH0001)
                var fallback = new Regex(@"\b[A-Z]{2,4}[_\-]?\d{1,6}\b", RegexOptions.IgnoreCase);
                m = fallback.Match(normalized);
                if (m.Success) return m.Value.ToUpperInvariant();

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong ExtractSupplierCode: {ex.Message}");
                return null;
            }
        }

        // Helper: remove dấu tiếng Việt để regex dễ match với "ma" / "mã"
        private string RemoveDiacritics(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;
                var normalized = text.Normalize(NormalizationForm.FormD);
                var sb = new StringBuilder();
                foreach (var ch in normalized)
                {
                    var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
                    if (uc != UnicodeCategory.NonSpacingMark)
                        sb.Append(ch);
                }
                return sb.ToString().Normalize(NormalizationForm.FormC);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong RemoveDiacritics: {ex.Message}");
                return text;
            }
        }

        // ================== XỬ LÝ THÔNG TIN TỔNG QUAN ==================
        private async Task<string> GetAllInformation(string question)
        {
            try
            {
                string q = question.ToLowerInvariant();

                // ========== XỬ LÝ THEO ĐỘ ƯU TIÊN ==========

                // 1. Kiểm tra dữ liệu hệ thống
                if (q.Contains("kiểm tra") || q.Contains("test") || q.Contains("data check"))
                    return await CheckDataAvailability();

                // 2. Phân tích AI & Thông minh
                if (q.Contains("phân tích") || q.Contains("gợi ý") || q.Contains("khuyến nghị") ||
                    q.Contains("tối ưu") || q.Contains("dự báo") || q.Contains("ưu tiên") ||
                    q.Contains("dư thừa") || q.Contains("thiếu hàng") || q.Contains("inventory analysis"))
                    return await GetAIAnalysisAndRecommendations(question);

                // 3. Phân tích tồn kho cơ bản
                if (q.Contains("tồn kho") || q.Contains("kho hàng") || q.Contains("inventory") ||
                    q.Contains("stock") || q.Contains("tồn") || q.Contains("kho"))
                    return await GetBasicInventoryAnalysis();

                // 4. Thông tin sản phẩm
                if (q.Contains("sản phẩm") || q.Contains("san pham") || q.Contains("hàng") || q.Contains("hang"))
                    return await GetAllProductsInfo();

                // 5. Thông tin khách hàng
                if (q.Contains("khách hàng") || q.Contains("khach hang") || q.Contains("customer"))
                    return await GetCustomerTableInfo();

                // 6. Thông tin đơn hàng
                if (q.Contains("đơn hàng") || q.Contains("don hang") || q.Contains("order") ||
                    q.Contains("đơn nhập") || q.Contains("đơn xuất"))
                    return await GetOrderInformation(question);

                // 7. Thông tin nhà cung cấp
                if (q.Contains("nhà cung cấp") || q.Contains("nha cung cap") || q.Contains("ncc") ||
                    q.Contains("supplier") || q.Contains("vendor"))
                    return await GetSupplierTableInfo();

                // 8. Tổng quan hệ thống
                if (q.Contains("tổng quan") || q.Contains("overview") || q.Contains("thống kê") ||
                    q.Contains("statistics") || q.Contains("báo cáo"))
                    return await GetGeneralOverview();

                // ========== MẶC ĐỊNH ==========
                // Nếu không khớp với bất kỳ điều kiện nào, trả về tổng quan hệ thống
                return await GetGeneralOverview();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi trong GetAllInformation: {ex.Message}");
                return $"❌ Lỗi khi lấy thông tin tổng quan: {ex.Message}";
            }
        }
        private async Task<string> GetAllProductsInfo()
        {
            try
            {
                var allProducts = await GetAllSanPhamAsync();
                if (!allProducts.Any()) return "Không có sản phẩm nào trong kho.";

                var productList = allProducts.Select(p =>
                    $"{p.TenSanPham} (Mã: {p.MaSanPham}, Tồn: {p.SoLuongNhap - p.SoLuongXuat} cái, Giá: {p.GiaBan.ToString("N0")}đ)");

                return $"📦 DANH SÁCH SẢN PHẨM: {string.Join("; ", productList)}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetAllProductsInfo: {ex.Message}");
                return $"Lỗi khi lấy danh sách sản phẩm: {ex.Message}";
            }
        }

        private async Task<string> GetGeneralOverview()
        {
            try
            {
                var overview = new List<string>();

                // Thống kê cơ bản
                var totalProducts = await GetTotalProductTypesAsync();
                var totalInventory = await GetTotalOnHandAsync();
                var totalCustomers = await _context.KhachHangs.CountAsync();
                var totalSuppliers = await _context.NhaCungCaps.CountAsync();
                var totalImportOrders = await _context.DonNhapHangs.CountAsync();
                var totalExportOrders = await _context.DonXuatHangs.CountAsync();

                overview.Add($"📊 HỆ THỐNG HIỆN CÓ:");
                overview.Add($"- {totalProducts} loại sản phẩm");
                overview.Add($"- {totalInventory} sản phẩm tồn kho");
                overview.Add($"- {totalCustomers} khách hàng");
                overview.Add($"- {totalSuppliers} nhà cung cấp");
                overview.Add($"- {totalImportOrders} đơn nhập hàng");
                overview.Add($"- {totalExportOrders} đơn xuất hàng");

                // Doanh thu & chi phí
                var totalRevenue = await _context.DonXuatHangs.SumAsync(d => d.TongTien);
                var totalCost = await _context.DonNhapHangs.SumAsync(d => d.TongTien);
                var profit = totalRevenue - totalCost;
                overview.Add($"💰 Doanh thu: {totalRevenue.ToString("N0")}đ");
                overview.Add($"💸 Chi phí: {totalCost.ToString("N0")}đ");
                overview.Add($"📈 Lợi nhuận: {profit.ToString("N0")}đ");

                // Top 5 sản phẩm bán chạy
                var topSelling = await _context.ChiTietDonXuats
                    .GroupBy(c => c.MaSanPham)
                    .Select(g => new
                    {
                        MaSP = g.Key,
                        SoLuongBan = g.Sum(x => x.SoLuong)
                    })
                    .OrderByDescending(x => x.SoLuongBan)
                    .Take(5)
                    .Join(_context.SanPhams,
                          g => g.MaSP,
                          sp => sp.MaSanPham,
                          (g, sp) => new { sp.TenSanPham, g.SoLuongBan })
                    .ToListAsync();

                if (topSelling.Any())
                {
                    overview.Add("🔥 Top 5 sản phẩm bán chạy:");
                    foreach (var item in topSelling)
                    {
                        overview.Add($"   - {item.TenSanPham}: {item.SoLuongBan} cái");
                    }
                }

                // Sản phẩm sắp hết hàng (tồn < 5)
                var lowStock = await _context.SanPhams
                    .Where(sp => (sp.SoLuongNhap - sp.SoLuongXuat) < 5)
                    .ToListAsync();

                if (lowStock.Any())
                {
                    overview.Add("⚠️ Sản phẩm sắp hết hàng:");
                    foreach (var sp in lowStock)
                    {
                        int tonKho = sp.SoLuongNhap - sp.SoLuongXuat;
                        overview.Add($"   - {sp.TenSanPham} (Còn {tonKho} cái)");
                    }
                }

                // 5 đơn hàng gần nhất
                var recentOrders = await _context.DonXuatHangs
                    .OrderByDescending(d => d.NgayXuatHang)
                    .Take(5)
                    .ToListAsync();

                if (recentOrders.Any())
                {
                    overview.Add("🕒 5 đơn hàng gần nhất:");
                    foreach (var o in recentOrders)
                    {
                        overview.Add($"   - {o.MaDonXuat} | {o.TenKhachHang} | {o.NgayXuatHang:dd/MM/yyyy} | {o.TongTien.ToString("N0")}đ");
                    }
                }
                // ========== THÊM THỐNG KÊ KHO HÀNG ==========
                var warehouseStats = await _context.KhoHangs
                    .GroupBy(k => k.MaKho)
                    .Select(g => new { MaKho = g.Key, Count = g.Count() })
                    .ToListAsync();

                if (warehouseStats.Any())
                {
                    overview.Add($"🏭 Số lượng kho: {warehouseStats.Count}");
                    foreach (var wh in warehouseStats.Take(3)) // Hiển thị 3 kho đầu
                    {
                        overview.Add($"   - Kho {wh.MaKho}: {wh.Count} sản phẩm");
                    }
                }

                // Sản phẩm sắp hết hàng trong kho
                var lowStockInWarehouse = await _context.KhoHangs
                    .Where(k => k.SoLuongSapHetHan > 0)
                    .CountAsync();

                if (lowStockInWarehouse > 0)
                {
                    overview.Add($"⚠️ Cảnh báo: {lowStockInWarehouse} sản phẩm sắp hết hạn trong kho");
                }
                return string.Join("\n", overview);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetGeneralOverview: {ex.Message}");
                return $"Lỗi khi lấy tổng quan hệ thống: {ex.Message}";
            }
        }

        // ================== CÁC PHƯƠNG THỨC HỖ TRỢ KHÁC ==================
        private string GetRecentConversationHistory(string userId)
        {
            try
            {
                var recentHistory = _conversationHistory
                    .Where(h => h.MaNguoiDung == userId)
                    .OrderByDescending(h => h.Timestamp)
                    .Take(5) // lấy 5 câu gần nhất thay vì 3
                    .Select(h => $"[{h.Timestamp:dd/MM HH:mm}] Hỏi: {h.Question} | Đáp: {h.Response}")
                    .ToList();

                return recentHistory.Any()
                    ? $"📜 Lịch sử gần đây:\n{string.Join("\n", recentHistory)}"
                    : "❌ Không có lịch sử trò chuyện.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetRecentConversationHistory: {ex.Message}");
                return "Lỗi khi lấy lịch sử hội thoại";
            }
        }

        private async Task<string> GetLearnedPatterns(string currentQuestion)
        {
            try
            {
                if (_context.Set<LearnedPattern>() == null) return "";

                var keywords = ExtractKeywords(currentQuestion);

                var patterns = await _context.Set<LearnedPattern>()
                    .Where(lp => keywords.Any(k => lp.Keyword.Contains(k))) // fuzzy
                    .OrderByDescending(lp => lp.LearnCount)
                    .Take(3)
                    .Select(lp => $"{lp.Keyword} → {lp.Pattern} (dùng {lp.LearnCount} lần)")
                    .ToListAsync();

                return patterns.Any()
                    ? $"🧠 Đã học:\n{string.Join("\n", patterns)}"
                    : "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetLearnedPatterns: {ex.Message}");
                return "";
            }
        }

        private string BuildEnhancedPrompt(string question, string contextData, string userId)
        {
            try
            {
                // Giới hạn độ dài context data
                if (contextData.Length > 2000)
                {
                    contextData = contextData.Substring(0, 2000) + "... [đã cắt bớt]";
                }

                var prompt = new StringBuilder();
                prompt.AppendLine("Bạn là trợ lý AI cho hệ thống quản lý kho thông minh.");
                prompt.AppendLine("Trả lời ngắn gọn, rõ ràng, chính xác, thân thiện bằng tiếng Việt.");
                prompt.AppendLine("Luôn dựa vào dữ liệu context, không được bịa.");

                if (!string.IsNullOrEmpty(contextData))
                {
                    prompt.AppendLine("\n=== DỮ LIỆU CONTEXT ===");
                    prompt.AppendLine(contextData);
                    prompt.AppendLine("======================");
                }

                var history = GetRecentConversationHistory(userId);
                if (!string.IsNullOrEmpty(history))
                {
                    prompt.AppendLine("\n=== LỊCH SỬ HỘI THOẠI ===");
                    prompt.AppendLine(history);
                    prompt.AppendLine("=========================");
                }

                prompt.AppendLine($"\n👤 User: {userId}");
                prompt.AppendLine($"❓ Câu hỏi: {question}");
                prompt.AppendLine("\n💡 Trả lời:");

                return prompt.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong BuildEnhancedPrompt: {ex.Message}");
                return $"Bạn là trợ lý AI. Hãy trả lời câu hỏi: {question}";
            }
        }

        private void SaveConversation(string userId, string question, string response, string contextUsed)
        {
            try
            {
                _conversationHistory.Add(new ChatHistory
                {
                    MaNguoiDung = userId,
                    Question = question,
                    Response = response,
                    ContextUsed = contextUsed,
                    Timestamp = DateTime.Now
                });

                if (_conversationHistory.Count > 100)
                    _conversationHistory.RemoveAt(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong SaveConversation: {ex.Message}");
            }
        }

        private async Task UpdateLearningPatterns(string question, string response)
        {
            try
            {
                if (_context.Set<LearnedPattern>() == null) return;

                var keywords = ExtractKeywords(question);
                foreach (var keyword in keywords)
                {
                    var existing = await _context.Set<LearnedPattern>()
                        .FirstOrDefaultAsync(lp => lp.Keyword == keyword);

                    if (existing == null)
                    {
                        _context.Set<LearnedPattern>().Add(new LearnedPattern
                        {
                            Keyword = keyword,
                            Pattern = response,
                            LearnCount = 1,
                            LastUsed = DateTime.Now
                        });
                    }
                    else
                    {
                        existing.LearnCount++;
                        existing.LastUsed = DateTime.Now;
                        if (!existing.Pattern.Contains(response))
                        {
                            existing.Pattern = response; // cập nhật mẫu mới
                        }
                    }
                }
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong UpdateLearningPatterns: {ex.Message}");
            }
        }

        private List<string> ExtractKeywords(string text)
        {
            try
            {
                var stopWords = new HashSet<string> {
                    "cái", "gì", "nào", "bao", "nhiêu", "của", "cho", "trong", "là", "có",
                    "ở", "tại", "về", "và", "hoặc", "các", "những", "có", "để", "được",
                    "vi", "và", "các", "có", "được", "là", "của", "trong", "để", "với"
                };

                return text.Split(' ', ',', '.', '?', '!', ';', ':')
                    .Select(word => word.ToLower().Trim())
                    .Where(word => word.Length > 2 && !stopWords.Contains(word))
                    .Distinct()
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong ExtractKeywords: {ex.Message}");
                return new List<string>();
            }
        }

        private string ExtractProductName(string input)
        {
            try
            {
                // Danh sách từ khóa sản phẩm thông dụng
                var productKeywords = new[]
                {
                    "iphone", "tủ lạnh", "tivi", "laptop", "máy tính", "điện thoại",
                    "ipad", "macbook", "samsung", "màn hình", "tablet", "máy tính bảng",
                    "loa", "tai nghe", "chuột", "bàn phím", "máy in", "máy scan"
                };

                // Kiểm tra từ khóa sản phẩm trước
                foreach (var keyword in productKeywords)
                {
                    if (input.ToLower().Contains(keyword))
                    {
                        // Tìm cụm từ chứa từ khóa
                        var match = Regex.Match(input, $@"\b({keyword}[\s\w]*)\b", RegexOptions.IgnoreCase);
                        if (match.Success)
                            return match.Groups[1].Value.Trim();
                    }
                }

                // Các pattern regex để trích xuất
                var patterns = new[]
                {
                    @"(sản phẩm|mặt hàng|hàng|tồn kho của|tồn kho|số lượng)\s+([^\d\s][^\.\?\!]{3,})",
                    @"(iphone\s?\d+[\s\w]*|ipad\s?\d+[\s\w]*|macbook[\s\w]*|samsung[\s\w]*)",
                    @"(màn hình|laptop|máy tính|điện thoại|tablet|tủ lạnh|tivi|máy in)[\s\w]*",
                    @"\b([A-Za-z0-9]+[\s\-]*[A-Za-z0-9]+)\b"
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
                    if (match.Success && match.Groups.Count >= 2)
                    {
                        for (int i = 1; i < match.Groups.Count; i++)
                        {
                            var productName = match.Groups[i].Value.Trim();
                            if (!string.IsNullOrEmpty(productName) && productName.Length > 2)
                                return productName;
                        }
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong ExtractProductName: {ex.Message}");
                return string.Empty;
            }
        }

        // ================== khách hàng==================
        private async Task<string> GetCustomerTableInfo()
        {
            try
            {
                var customers = await _context.KhachHangs.Take(10).ToListAsync(); // Giới hạn 10 khách
                if (!customers.Any()) return "👥 Không có dữ liệu khách hàng.";

                var result = new StringBuilder();
                result.AppendLine("👥 **DANH SÁCH KHÁCH HÀNG**");
                result.AppendLine();

                foreach (var kh in customers)
                {
                    result.AppendLine($"🔹 **{kh.TenKhachHang}**");
                    result.AppendLine($"   - Mã KH: {kh.MaKhachHang}");
                    result.AppendLine($"   - SĐT: {kh.SoDienThoai ?? "N/A"}");
                    if (!string.IsNullOrEmpty(kh.DiaChi))
                        result.AppendLine($"   - Địa chỉ: {kh.DiaChi}");
                    result.AppendLine();
                }

                result.AppendLine($"📊 **Tổng số: {customers.Count} khách hàng**");

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetCustomerTableInfo: {ex.Message}");
                return "❌ Lỗi khi lấy danh sách khách hàng";
            }
        }

        private async Task<string> GetSupplierTableInfo()
        {
            try
            {
                var suppliers = await _context.NhaCungCaps.Take(10).ToListAsync();
                if (!suppliers.Any()) return "🏭 Không có dữ liệu nhà cung cấp.";

                var result = new StringBuilder();
                result.AppendLine("🏭 **DANH SÁCH NHÀ CUNG CẤP**");
                result.AppendLine();

                foreach (var ncc in suppliers)
                {
                    result.AppendLine($"🔹 **{ncc.TenNCC}**");
                    result.AppendLine($"   - Mã NCC: {ncc.MaNCC}");
                    result.AppendLine($"   - Người liên hệ: {ncc.NguoiLienHe ?? "N/A"}");
                    result.AppendLine($"   - SĐT: {ncc.SoDienThoai ?? "N/A"}");
                    if (!string.IsNullOrEmpty(ncc.Email))
                        result.AppendLine($"   - Email: {ncc.Email}");
                    if (!string.IsNullOrEmpty(ncc.DiaChi))
                        result.AppendLine($"   - Địa chỉ: {ncc.DiaChi}");
                    result.AppendLine();
                }

                result.AppendLine($"📊 **Tổng số: {suppliers.Count} nhà cung cấp**");

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetSupplierTableInfo: {ex.Message}");
                return "❌ Lỗi khi lấy danh sách nhà cung cấp";
            }
        }
        // ================== CÁC PHƯƠNG THỨC TRUY VẤN DỮ LIỆU ==================
        private async Task<List<ModelSanPham>> GetAllSanPhamAsync()
        {
            try
            {
                return await _context.SanPhams.ToListAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetAllSanPhamAsync: {ex.Message}");
                return new List<ModelSanPham>();
            }
        }

        private async Task<int> GetTotalProductTypesAsync()
        {
            try
            {
                return await _context.SanPhams.CountAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetTotalProductTypesAsync: {ex.Message}");
                return 0;
            }
        }

        private async Task<int> GetTotalOnHandAsync()
        {
            try
            {
                return await _context.SanPhams.SumAsync(p => p.SoLuongNhap - p.SoLuongXuat);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetTotalOnHandAsync: {ex.Message}");
                return 0;
            }
        }

        private async Task<List<ModelKhachHang>> GetAllKhachHangAsync()
        {
            try
            {
                return await _context.KhachHangs.ToListAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetAllKhachHangAsync: {ex.Message}");
                return new List<ModelKhachHang>();
            }
        }

        private async Task<List<CustomerPurchaseDetail>> GetCustomerPurchaseDetailsAsync(string maKH)
        {
            try
            {
                // Thử phương thức chính với JOIN
                var purchases = await (from dx in _context.DonXuatHangs
                                       where dx.MaKhachHang == maKH && dx.MaDonXuat != null
                                       join ctdx in _context.ChiTietDonXuats on dx.MaDonXuat equals ctdx.MaDonXuat
                                       where ctdx.MaSanPham != null
                                       join sp in _context.SanPhams on ctdx.MaSanPham equals sp.MaSanPham
                                       select new CustomerPurchaseDetail
                                       {
                                           MaDonHang = dx.MaDonXuat,
                                           TenSanPham = sp.TenSanPham,
                                           SoLuong = ctdx.SoLuong,
                                           DonGia = ctdx.GiaBan,
                                           ThanhTien = ctdx.ThanhTien,
                                           NgayMua = dx.NgayXuatHang
                                       }).ToListAsync();

                return purchases;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetCustomerPurchaseDetailsAsync (phương thức chính): {ex.Message}");
                Console.WriteLine("Chuyển sang sử dụng phương thức fallback...");

                // Nếu có lỗi, sử dụng phương thức fallback
                return await GetCustomerPurchaseDetailsFallback(maKH);
            }
        }
        private async Task<List<CustomerPurchaseDetail>> GetCustomerPurchaseData(string maKH)
        {
            try
            {
                // Thử phương thức chính trước
                return await GetCustomerPurchaseDetailsAsync(maKH);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Phương thức chính thất bại, sử dụng fallback: {ex.Message}");
                return await GetCustomerPurchaseDetailsFallback(maKH);
            }
        }
        private async Task<List<ModelDonNhapHang>> GetAllDonNhapAsync()
        {
            try
            {
                return await _context.DonNhapHangs.ToListAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetAllDonNhapAsync: {ex.Message}");
                return new List<ModelDonNhapHang>();
            }
        }

        private async Task<List<ModelDonXuatHang>> GetAllDonXuatAsync()
        {
            try
            {
                return await _context.DonXuatHangs.ToListAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetAllDonXuatAsync: {ex.Message}");
                return new List<ModelDonXuatHang>();
            }
        }

        private async Task<List<ModelNhaCungCap>> GetAllNCCAsync()
        {
            try
            {
                return await _context.NhaCungCaps.ToListAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetAllNCCAsync: {ex.Message}");
                return new List<ModelNhaCungCap>();
            }
        }
        ///////////////////////////////////////////////////////////////////////////
        // ================== PHÂN TÍCH AI & GỢI Ý ĐẶT HÀNG ==================
        private async Task<List<CustomerPurchaseDetail>> GetCustomerPurchaseDetailsFallback(string maKH)
        {
            try
            {
                // Phương thức dự phòng nếu có lỗi relationship
                var purchases = new List<CustomerPurchaseDetail>();

                // Lấy đơn hàng của khách
                var orders = await _context.DonXuatHangs
                    .Where(d => d.MaKhachHang == maKH)
                    .ToListAsync();

                foreach (var order in orders)
                {
                    // Lấy chi tiết đơn hàng
                    var orderDetails = await _context.ChiTietDonXuats
                        .Where(ct => ct.MaDonXuat == order.MaDonXuat)
                        .ToListAsync();

                    foreach (var detail in orderDetails)
                    {
                        var product = await _context.SanPhams
                            .FirstOrDefaultAsync(p => p.MaSanPham == detail.MaSanPham);

                        if (product != null)
                        {
                            purchases.Add(new CustomerPurchaseDetail
                            {
                                MaDonHang = order.MaDonXuat,
                                TenSanPham = product.TenSanPham,
                                SoLuong = detail.SoLuong,
                                DonGia = detail.GiaBan,
                                ThanhTien = detail.ThanhTien,
                                NgayMua = order.NgayXuatHang
                            });
                        }
                    }
                }

                return purchases;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetCustomerPurchaseDetailsFallback: {ex.Message}");
                return new List<CustomerPurchaseDetail>();
            }
        }

        private async Task<string> GetAIAnalysisAndRecommendations(string question)
        {
            try
            {
                Console.WriteLine($"🔍 Bắt đầu phân tích AI cho câu hỏi: {question}");

                string q = question.ToLowerInvariant();

                // Phân tích dữ liệu tồn kho
                var analysis = await AnalyzeInventoryAndSales();

                if (analysis == null || !analysis.Any())
                {
                    Console.WriteLine("❌ Không có dữ liệu phân tích");
                    return "❌ Hiện không có dữ liệu tồn kho để phân tích. Vui lòng thử lại sau.";
                }

                Console.WriteLine($"✅ Đã phân tích {analysis.Count} sản phẩm");

                var recommendations = await GenerateReorderRecommendations(analysis);

                // ========== GỌI CÁC PHƯƠNG THỨC PHÂN TÍCH CHUYÊN SÂU ==========

                // Xử lý sản phẩm dư thừa
                if (q.Contains("dư thừa") || q.Contains("du thua") || q.Contains("thừa") || q.Contains("ứ đọng") ||
                    q.Contains("thống kê dư thừa") || q.Contains("danh sách dư thừa"))
                {
                    return await GetOverstockProductsAnalysis(analysis);
                }

                // Xử lý sản phẩm thiếu hàng
                if (q.Contains("thiếu hàng") || q.Contains("thieu hang") || q.Contains("sắp hết") ||
                    q.Contains("cần đặt") || q.Contains("hết hàng"))
                {
                    return await GetLowStockProductsAnalysis(analysis);
                }

                // Xử lý gợi ý đặt hàng
                if (q.Contains("gợi ý đặt hàng") || q.Contains("khuyến nghị đặt hàng") ||
                    q.Contains("đặt hàng tối ưu") || q.Contains("sản phẩm ưu tiên"))
                {
                    return FormatReorderRecommendations(recommendations);
                }

                // Xử lý phân tích cơ bản
                if (q.Contains("phân tích cơ bản") || q.Contains("basic analysis") ||
                    q.Contains("tổng quan tồn kho") || q.Contains("quick analysis"))
                {
                    return await GetBasicInventoryAnalysis();
                }

                // Mặc định: sử dụng FormatAnalysisReport hiện có
                return FormatAnalysisReport(analysis, recommendations, question);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi trong GetAIAnalysisAndRecommendations: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                // Fallback: sử dụng phân tích cơ bản khi có lỗi
                return await GetBasicInventoryAnalysis();
            }
        }

        private async Task<string> CheckDataAvailability()
        {
            try
            {
                var productCount = await _context.SanPhams.CountAsync();
                var warehouseCount = await _context.KhoHangs.CountAsync();
                var customerCount = await _context.KhachHangs.CountAsync();
                var supplierCount = await _context.NhaCungCaps.CountAsync();

                var result = new StringBuilder();
                result.AppendLine("📊 **KIỂM TRA DỮ LIỆU HỆ THỐNG**");
                result.AppendLine();
                result.AppendLine($"- Số sản phẩm: {productCount}");
                result.AppendLine($"- Số kho hàng: {warehouseCount}");
                result.AppendLine($"- Số khách hàng: {customerCount}");
                result.AppendLine($"- Số nhà cung cấp: {supplierCount}");

                if (productCount == 0)
                {
                    result.AppendLine("\n⚠️ **CẢNH BÁO**: Không có sản phẩm nào trong hệ thống!");
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                return $"❌ Lỗi khi kiểm tra dữ liệu: {ex.Message}";
            }
        }
        private async Task<string> GetLowStockProductsAnalysis(List<ProductAnalysis> analysis)
        {
            try
            {
                var lowStockProducts = analysis
                    .Where(a => a.TyLeThieuHang > 0.3 || a.TrangThai.Contains("THIẾU HÀNG") || a.TrangThai.Contains("SẮP HẾT"))
                    .OrderByDescending(a => a.TyLeThieuHang)
                    .ToList();

                if (!lowStockProducts.Any())
                {
                    return "✅ **KHÔNG CÓ SẢN PHẨM THIẾU HÀNG**\n\nTất cả sản phẩm đều có đủ hàng trong kho.";
                }

                var result = new StringBuilder();
                result.AppendLine("⚠️ **DANH SÁCH SẢN PHẨM THIẾU HÀNG**");
                result.AppendLine();

                foreach (var product in lowStockProducts.Take(10))
                {
                    result.AppendLine($"🔴 **{product.TenSanPham}** (Mã: {product.MaSanPham})");
                    result.AppendLine($"   - Tồn kho hiện tại: {product.TonKhoHienTai}");
                    result.AppendLine($"   - Trạng thái: {product.TrangThai}");
                    result.AppendLine($"   - Tỷ lệ thiếu hàng: {product.TyLeThieuHang:P0}");
                    result.AppendLine($"   - Khuyến nghị đặt hàng: {product.LuongDatHangKhuyenNghi} sản phẩm");
                    result.AppendLine();
                }

                // Thống kê
                result.AppendLine($"📊 **THỐNG KÊ THIẾU HÀNG:**");
                result.AppendLine($"- Tổng số sản phẩm thiếu hàng: {lowStockProducts.Count}");
                result.AppendLine($"- Sản phẩm cần ưu tiên nhất: {lowStockProducts.First().TenSanPham}");

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetLowStockProductsAnalysis: {ex.Message}");
                return "❌ Lỗi khi phân tích sản phẩm thiếu hàng";
            }
        }
        private async Task<string> GetOverstockProductsAnalysis(List<ProductAnalysis> analysis)
        {
            try
            {
                var overstockProducts = analysis
                    .Where(a => a.TyLeDuThua > 0.3 || a.TrangThai.Contains("Ứ ĐỌNG") || a.TrangThai.Contains("DƯ THỪA"))
                    .OrderByDescending(a => a.TyLeDuThua)
                    .ToList();

                if (!overstockProducts.Any())
                {
                    return "✅ **KHÔNG CÓ SẢN PHẨM DƯ THỪA**\n\nHiện tại tất cả sản phẩm đều có mức tồn kho hợp lý.";
                }

                var result = new StringBuilder();
                result.AppendLine("💎 **DANH SÁCH SẢN PHẨM DƯ THỪA**");
                result.AppendLine();

                foreach (var product in overstockProducts.Take(10)) // Giới hạn 10 sản phẩm
                {
                    result.AppendLine($"🔸 **{product.TenSanPham}** (Mã: {product.MaSanPham})");
                    result.AppendLine($"   - Tồn kho hiện tại: {product.TonKhoHienTai}");
                    result.AppendLine($"   - Trạng thái: {product.TrangThai}");
                    result.AppendLine($"   - Tỷ lệ dư thừa: {product.TyLeDuThua:P0}");
                    result.AppendLine($"   - Khuyến nghị: {product.KhuyenNghi}");
                    result.AppendLine();
                }

                // Thống kê
                result.AppendLine($"📊 **THỐNG KÊ DƯ THỪA:**");
                result.AppendLine($"- Tổng số sản phẩm dư thừa: {overstockProducts.Count}");
                result.AppendLine($"- Sản phẩm dư thừa nhiều nhất: {overstockProducts.First().TenSanPham}");
                result.AppendLine($"- Tỷ lệ dư thừa cao nhất: {overstockProducts.First().TyLeDuThua:P0}");

                result.AppendLine($"\n💡 **GIẢI PHÁP:**");
                result.AppendLine("- Triển khai chương trình khuyến mãi");
                result.AppendLine("- Giảm nhập hàng hoặc tạm ngừng đặt hàng");
                result.AppendLine("- Tìm kiếm kênh phân phối mới");

                return result.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetOverstockProductsAnalysis: {ex.Message}");
                return "❌ Lỗi khi phân tích sản phẩm dư thừa";
            }
        }
        private string FormatReorderRecommendations(List<ReorderRecommendation> recommendations)
        {
            var result = new StringBuilder();
            result.AppendLine("🎯 **GỢI Ý ĐẶT HÀNG TỐI ƯU**");
            result.AppendLine();

            var highPriority = recommendations.Where(r => r.MucUuTien == "CAO").ToList();
            var mediumPriority = recommendations.Where(r => r.MucUuTien == "TRUNG_BINH").ToList();

            if (highPriority.Any())
            {
                result.AppendLine("🚨 **ƯU TIÊN CAO - CẦN ĐẶT HÀNG NGAY**");
                foreach (var rec in highPriority.Take(5))
                {
                    result.AppendLine($"🔴 **{rec.TenSanPham}**");
                    result.AppendLine($"   - Số lượng: {rec.LuongDatHangToiUu} sản phẩm");
                    result.AppendLine($"   - Chi phí dự kiến: {rec.TongChiPhi:N0}đ");
                    result.AppendLine($"   - Lý do: {rec.LyDo}");
                    result.AppendLine();
                }
            }

            if (mediumPriority.Any())
            {
                result.AppendLine("🟡 **ƯU TIÊN TRUNG BÌNH - LÊN KẾ HOẠCH**");
                foreach (var rec in mediumPriority.Take(3))
                {
                    result.AppendLine($"🟡 **{rec.TenSanPham}**");
                    result.AppendLine($"   - Số lượng: {rec.LuongDatHangToiUu} sản phẩm");
                    result.AppendLine($"   - Chi phí: {rec.TongChiPhi:N0}đ");
                    result.AppendLine();
                }
            }

            if (!highPriority.Any() && !mediumPriority.Any())
            {
                result.AppendLine("✅ **TỒN KHO ĐANG ỔN ĐỊNH**");
                result.AppendLine("Không có sản phẩm nào cần đặt hàng ngay lúc này.");
            }

            return result.ToString();
        }
        private async Task<List<ProductAnalysis>> AnalyzeInventoryAndSales()
        {
            try
            {
                Console.WriteLine("🔍 Bắt đầu phân tích tồn kho...");

                var products = await _context.SanPhams.ToListAsync();
                Console.WriteLine($"✅ Đã lấy {products.Count} sản phẩm từ database");

                if (!products.Any())
                {
                    Console.WriteLine("❌ Không có sản phẩm nào trong database");
                    return new List<ProductAnalysis>();
                }

                var analysisResults = new List<ProductAnalysis>();

                // Lấy dữ liệu bán hàng 30 ngày gần nhất
                var thirtyDaysAgo = DateTime.Now.AddDays(-30);

                // Lấy tất cả đơn xuất trong 30 ngày
                var recentExportOrders = await _context.DonXuatHangs
                    .Where(d => d.NgayXuatHang >= thirtyDaysAgo)
                    .Select(d => d.MaDonXuat)
                    .ToListAsync();

                Console.WriteLine($"✅ Đã lấy {recentExportOrders.Count} đơn xuất gần đây");

                // Lấy chi tiết đơn xuất
                var salesData = await _context.ChiTietDonXuats
                    .Where(x => recentExportOrders.Contains(x.MaDonXuat))
                    .GroupBy(x => x.MaSanPham)
                    .Select(g => new
                    {
                        MaSanPham = g.Key,
                        SoLuongBan = g.Sum(x => x.SoLuong)
                    })
                    .ToListAsync();

                Console.WriteLine($"✅ Đã lấy dữ liệu bán hàng cho {salesData.Count} sản phẩm");

                foreach (var product in products)
                {
                    var productSales = salesData.FirstOrDefault(s => s.MaSanPham == product.MaSanPham);
                    int soLuongBan30Ngay = productSales?.SoLuongBan ?? 0;
                    int soLuongBanTrungBinh = soLuongBan30Ngay > 0 ? soLuongBan30Ngay / 30 : 0;
                    int tonKhoHienTai = product.SoLuongNhap - product.SoLuongXuat;

                    Console.WriteLine($"📊 {product.TenSanPham}: Tồn {tonKhoHienTai}, Bán TB {soLuongBanTrungBinh}/ngày");

                    // Tính toán số lượng tối ưu
                    var optimalQuantity = CalculateEOQ(soLuongBanTrungBinh, product.GiaNhap);
                    var recommendedOrder = CalculateRecommendedOrder(tonKhoHienTai, soLuongBanTrungBinh, optimalQuantity);

                    var analysis = new ProductAnalysis
                    {
                        MaSanPham = product.MaSanPham,
                        TenSanPham = product.TenSanPham,
                        TonKhoHienTai = tonKhoHienTai,
                        SoLuongToiUu = optimalQuantity,
                        LuongDatHangKhuyenNghi = recommendedOrder,
                        TyLeThieuHang = CalculateStockoutRisk(tonKhoHienTai, soLuongBanTrungBinh),
                        TyLeDuThua = CalculateOverstockRisk(tonKhoHienTai, soLuongBanTrungBinh),
                        TrangThai = GetInventoryStatus(tonKhoHienTai, soLuongBanTrungBinh),
                        KhuyenNghi = GetRecommendation(tonKhoHienTai, soLuongBanTrungBinh, optimalQuantity),
                        ChiPhiDuKien = recommendedOrder * product.GiaNhap
                    };

                    analysisResults.Add(analysis);
                }

                Console.WriteLine($"✅ Đã phân tích xong {analysisResults.Count} sản phẩm");
                return analysisResults;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi trong AnalyzeInventoryAndSales: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<ProductAnalysis>();
            }
        }
        private async Task<List<ReorderRecommendation>> GenerateReorderRecommendations(List<ProductAnalysis> analysis)
        {
            var recommendations = new List<ReorderRecommendation>();

            foreach (var product in analysis)
            {
                if (product.LuongDatHangKhuyenNghi > 0)
                {
                    var productInfo = await _context.SanPhams
                        .FirstOrDefaultAsync(p => p.MaSanPham == product.MaSanPham);

                    var recommendation = new ReorderRecommendation
                    {
                        MaSanPham = product.MaSanPham,
                        TenSanPham = product.TenSanPham,
                        LuongDatHangToiUu = product.LuongDatHangKhuyenNghi,
                        MucUuTien = GetPriorityLevel(product.TyLeThieuHang, product.TonKhoHienTai),
                        LyDo = product.KhuyenNghi,
                        TongChiPhi = product.ChiPhiDuKien
                    };

                    recommendations.Add(recommendation);
                }
            }

            return recommendations.OrderByDescending(r => r.MucUuTien).ToList();
        }


        // ================== CÔNG THỨC TÍNH TOÁN AI ==================
        private int CalculateEOQ(int demand, decimal holdingCost)
        {
            // Công thức EOQ căn bản: √(2DS/H)
            // D: Nhu cầu hàng năm, S: Chi phí đặt hàng, H: Chi phí lưu kho
            double setupCost = 500000; // Chi phí đặt hàng cố định
            double annualDemand = demand * 365;
            double holdingCostPerUnit = (double)holdingCost * 0.2; // Giả định chi phí lưu kho = 20% giá trị

            if (holdingCostPerUnit == 0) return 0;

            int eoq = (int)Math.Sqrt((2 * annualDemand * setupCost) / holdingCostPerUnit);
            return Math.Max(eoq, 0);
        }

        private int CalculateRecommendedOrder(int currentStock, int dailyDemand, int optimalQuantity)
        {
            int safetyStock = dailyDemand * 7; // Tồn kho an toàn = 7 ngày
            int reorderPoint = dailyDemand * 14; // Điểm đặt hàng = 14 ngày

            if (currentStock <= reorderPoint)
            {
                return Math.Max(optimalQuantity, safetyStock - currentStock);
            }

            return 0;
        }

        private double CalculateStockoutRisk(int currentStock, int dailyDemand)
        {
            if (dailyDemand == 0) return 0;

            int daysOfSupply = currentStock / Math.Max(dailyDemand, 1);

            if (daysOfSupply <= 3) return 0.8; // Rất cao
            if (daysOfSupply <= 7) return 0.5; // Cao
            if (daysOfSupply <= 14) return 0.2; // Trung bình
            return 0.05; // Thấp
        }

        private double CalculateOverstockRisk(int currentStock, int dailyDemand)
        {
            if (dailyDemand == 0) return currentStock > 0 ? 0.9 : 0;

            int daysOfSupply = currentStock / Math.Max(dailyDemand, 1);

            if (daysOfSupply > 90) return 0.8; // Rất cao
            if (daysOfSupply > 60) return 0.5; // Cao
            if (daysOfSupply > 30) return 0.2; // Trung bình
            return 0.05; // Thấp
        }

        private string GetInventoryStatus(int currentStock, int dailyDemand)
        {
            if (dailyDemand == 0) return currentStock > 0 ? "Ứ ĐỌNG" : "KHÔNG BÁN";

            int daysOfSupply = currentStock / Math.Max(dailyDemand, 1);

            return daysOfSupply switch
            {
                <= 3 => "⚠️ THIẾU HÀNG",
                <= 7 => "🔸 SẮP HẾT",
                <= 14 => "✅ TỐI ƯU",
                <= 30 => "🔹 ĐỦ DÙNG",
                <= 60 => "💎 DƯ THỪA",
                _ => "🚨 Ứ ĐỌNG"
            };
        }

        private string GetRecommendation(int currentStock, int dailyDemand, int optimalQuantity)
        {
            if (dailyDemand == 0)
            {
                return currentStock > 0 ? "NGỪNG NHẬP - XẢ HÀNG" : "THEO DÕI";
            }

            int daysOfSupply = currentStock / Math.Max(dailyDemand, 1);

            return daysOfSupply switch
            {
                <= 3 => $"ĐẶT GẤP {optimalQuantity} SP",
                <= 7 => $"ĐẶT NGAY {optimalQuantity} SP",
                <= 14 => "THEO DÕI ĐỊNH KỲ",
                <= 30 => "ĐỦ DÙNG - KHÔNG ĐẶT",
                _ => "GIẢM NHẬP - KHUYẾN MÃI"
            };
        }

        private string GetPriorityLevel(double stockoutRisk, int currentStock)
        {
            if (stockoutRisk >= 0.7 || currentStock <= 0) return "CAO";
            if (stockoutRisk >= 0.4) return "TRUNG_BINH";
            return "THAP";
        }

        // ================== FORMAT BÁO CÁO ==================
        private string FormatAnalysisReport(List<ProductAnalysis> analysis, List<ReorderRecommendation> recommendations, string question)
        {
            var result = new StringBuilder();
            string q = question.ToLowerInvariant();
            // ========== GỌI CÁC PHƯƠNG THỨC CHUYÊN SÂU ==========
            if (q.Contains("dư thừa") || q.Contains("du thua"))
            {
                // Sử dụng phương thức chuyên sâu cho dư thừa
                var overstockResult = GetOverstockProductsAnalysis(analysis).Result;
                return overstockResult;
            }

            if (q.Contains("thiếu hàng") || q.Contains("thieu hang"))
            {
                // Sử dụng phương thức chuyên sâu cho thiếu hàng
                var lowStockResult = GetLowStockProductsAnalysis(analysis).Result;
                return lowStockResult;
            }

            if (q.Contains("gợi ý") || q.Contains("đặt hàng"))
            {
                // Sử dụng phương thức chuyên sâu cho gợi ý đặt hàng
                return FormatReorderRecommendations(recommendations);
            }

            // ========== XỬ LÝ SẢN PHẨM DƯ THỪA ==========
            if (q.Contains("dư thừa") || q.Contains("du thua") || q.Contains("thừa") || q.Contains("ứ đọng"))
            {
                var overstockProductsList = analysis
                    .Where(a => a.TyLeDuThua > 0.3 || a.TrangThai.Contains("Ứ ĐỌNG") || a.TrangThai.Contains("DƯ THỪA"))
                    .OrderByDescending(a => a.TyLeDuThua)
                    .ToList();

                if (!overstockProductsList.Any())
                {
                    return "✅ **KHÔNG CÓ SẢN PHẨM DƯ THỪA**\n\nHiện tại tất cả sản phẩm đều có mức tồn kho hợp lý.";
                }

                result.AppendLine("💎 **DANH SÁCH SẢN PHẨM DƯ THỪA**");
                result.AppendLine();

                foreach (var product in overstockProductsList.Take(10))
                {
                    result.AppendLine($"🔸 **{product.TenSanPham}** (Mã: {product.MaSanPham})");
                    result.AppendLine($"   - Tồn kho hiện tại: {product.TonKhoHienTai}");
                    result.AppendLine($"   - Trạng thái: {product.TrangThai}");
                    result.AppendLine($"   - Tỷ lệ dư thừa: {product.TyLeDuThua:P0}");
                    result.AppendLine($"   - Khuyến nghị: {product.KhuyenNghi}");
                    result.AppendLine();
                }

                // Thống kê
                result.AppendLine($"📊 **THỐNG KÊ DƯ THỪA:**");
                result.AppendLine($"- Tổng số sản phẩm dư thừa: {overstockProductsList.Count}");
                result.AppendLine($"- Sản phẩm dư thừa nhiều nhất: {overstockProductsList.First().TenSanPham}");
                result.AppendLine($"- Tỷ lệ dư thừa cao nhất: {overstockProductsList.First().TyLeDuThua:P0}");

                return result.ToString();
            }

            // ========== XỬ LÝ SẢN PHẨM THIẾU HÀNG ==========
            if (q.Contains("thiếu hàng") || q.Contains("thieu hang") || q.Contains("sắp hết") || q.Contains("cần đặt"))
            {
                var lowStockProductsList = analysis
                    .Where(a => a.TyLeThieuHang > 0.3 || a.TrangThai.Contains("THIẾU HÀNG") || a.TrangThai.Contains("SẮP HẾT"))
                    .OrderByDescending(a => a.TyLeThieuHang)
                    .ToList();

                if (!lowStockProductsList.Any())
                {
                    return "✅ **KHÔNG CÓ SẢN PHẨM THIẾU HÀNG**\n\nTất cả sản phẩm đều có đủ hàng trong kho.";
                }

                result.AppendLine("⚠️ **DANH SÁCH SẢN PHẨM THIẾU HÀNG**");
                result.AppendLine();

                foreach (var product in lowStockProductsList.Take(10))
                {
                    result.AppendLine($"🔴 **{product.TenSanPham}** (Mã: {product.MaSanPham})");
                    result.AppendLine($"   - Tồn kho hiện tại: {product.TonKhoHienTai}");
                    result.AppendLine($"   - Trạng thái: {product.TrangThai}");
                    result.AppendLine($"   - Tỷ lệ thiếu hàng: {product.TyLeThieuHang:P0}");
                    result.AppendLine($"   - Khuyến nghị đặt hàng: {product.LuongDatHangKhuyenNghi} sản phẩm");
                    result.AppendLine();
                }

                return result.ToString();
            }

            // ========== GỢI Ý ĐẶT HÀNG ==========
            if (q.Contains("ưu tiên") || q.Contains("gợi ý") || q.Contains("khuyến nghị") || q.Contains("đặt hàng"))
            {
                result.AppendLine("🎯 **GỢI Ý ĐẶT HÀNG TỐI ƯU**");
                result.AppendLine();

                var highPriority = recommendations.Where(r => r.MucUuTien == "CAO").ToList();
                var mediumPriority = recommendations.Where(r => r.MucUuTien == "TRUNG_BINH").ToList();

                if (highPriority.Any())
                {
                    result.AppendLine("🚨 **ƯU TIÊN CAO - CẦN ĐẶT HÀNG NGAY**");
                    foreach (var rec in highPriority.Take(5))
                    {
                        result.AppendLine($"🔴 **{rec.TenSanPham}**");
                        result.AppendLine($"   - Số lượng: {rec.LuongDatHangToiUu} sản phẩm");
                        result.AppendLine($"   - Chi phí dự kiến: {rec.TongChiPhi:N0}đ");
                        result.AppendLine($"   - Lý do: {rec.LyDo}");
                        result.AppendLine();
                    }
                }

                if (mediumPriority.Any())
                {
                    result.AppendLine("🟡 **ƯU TIÊN TRUNG BÌNH - LÊN KẾ HOẠCH**");
                    foreach (var rec in mediumPriority.Take(3))
                    {
                        result.AppendLine($"🟡 **{rec.TenSanPham}**");
                        result.AppendLine($"   - Số lượng: {rec.LuongDatHangToiUu} sản phẩm");
                        result.AppendLine($"   - Chi phí: {rec.TongChiPhi:N0}đ");
                        result.AppendLine();
                    }
                }

                if (!highPriority.Any() && !mediumPriority.Any())
                {
                    result.AppendLine("✅ **TỒN KHO ĐANG ỔN ĐỊNH**");
                    result.AppendLine("Không có sản phẩm nào cần đặt hàng ngay lúc này.");
                }

                // Thống kê tổng
                decimal totalCost = recommendations.Sum(r => r.TongChiPhi);
                result.AppendLine($"\n💰 **TỔNG CHI PHÍ DỰ KIẾN: {totalCost:N0}đ**");

                return result.ToString();
            }

            // ========== PHÂN TÍCH TỔNG QUAN (MẶC ĐỊNH) ==========
            result.AppendLine("📊 **PHÂN TÍCH TỒN KHO CHI TIẾT**");
            result.AppendLine();

            // Sử dụng tên biến khác để tránh trùng lặp
            var criticalItems = analysis.Where(a => a.TrangThai.Contains("THIẾU HÀNG") || a.TrangThai.Contains("SẮP HẾT")).ToList();
            var excessItems = analysis.Where(a => a.TrangThai.Contains("Ứ ĐỌNG") || a.TrangThai.Contains("DƯ THỪA")).ToList();

            if (criticalItems.Any())
            {
                result.AppendLine("⚠️ **SẢN PHẨM CẦN CHÚ Ý**");
                foreach (var product in criticalItems.Take(5))
                {
                    result.AppendLine($"🔸 **{product.TenSanPham}**");
                    result.AppendLine($"   - Tồn kho: {product.TonKhoHienTai}");
                    result.AppendLine($"   - Trạng thái: {product.TrangThai}");
                    result.AppendLine($"   - Khuyến nghị: {product.KhuyenNghi}");
                    result.AppendLine();
                }
            }

            if (excessItems.Any())
            {
                result.AppendLine("💎 **SẢN PHẨM DƯ THỪA**");
                foreach (var product in excessItems.Take(3))
                {
                    result.AppendLine($"🔹 **{product.TenSanPham}**");
                    result.AppendLine($"   - Tồn kho: {product.TonKhoHienTai}");
                    result.AppendLine($"   - Tỷ lệ dư thừa: {product.TyLeDuThua:P0}");
                    result.AppendLine($"   - Khuyến nghị: {product.KhuyenNghi}");
                    result.AppendLine();
                }
            }

            // Thống kê tổng quan
            var totalProducts = analysis.Count;
            var optimalProducts = analysis.Count(a => a.TrangThai.Contains("TỐI ƯU"));
            var criticalCount = criticalItems.Count;
            var excessCount = excessItems.Count;

            result.AppendLine("📈 **THỐNG KÊ TỔNG QUAN**");
            result.AppendLine($"- Tổng sản phẩm: {totalProducts}");
            result.AppendLine($"- Sản phẩm tối ưu: {optimalProducts} ({optimalProducts * 100.0 / totalProducts:F1}%)");
            result.AppendLine($"- Sản phẩm cần chú Ý: {criticalCount}");
            result.AppendLine($"- Sản phẩm dư thừa: {excessCount}");

            // Gợi ý tra cứu
            result.AppendLine($"\n💡 **GỢI Ý TRA CỨU:**");
            result.AppendLine("- Gõ 'sản phẩm dư thừa' để xem chi tiết");
            result.AppendLine("- Gõ 'sản phẩm thiếu hàng' để xem khẩn cấp");
            result.AppendLine("- Gõ 'gợi ý đặt hàng' để xem khuyến nghị");

            return result.ToString();
        }

        private async Task<string> GetBasicInventoryAnalysis()
        {
            try
            {
                var analysis = await AnalyzeInventoryAndSales();

                var result = new StringBuilder();
                result.AppendLine("🤖 **PHÂN TÍCH TỒN KHO THÔNG MINH**");
                result.AppendLine();

                // Top 5 sản phẩm cần đặt hàng
                var needReorder = analysis
                    .Where(a => a.LuongDatHangKhuyenNghi > 0)
                    .OrderByDescending(a => a.TyLeThieuHang)
                    .Take(5)
                    .ToList();

                if (needReorder.Any())
                {
                    result.AppendLine("🔄 **SẢN PHẨM CẦN ĐẶT HÀNG**");
                    foreach (var product in needReorder)
                    {
                        result.AppendLine($"📦 **{product.TenSanPham}**");
                        result.AppendLine($"   - Tồn hiện tại: {product.TonKhoHienTai}");
                        result.AppendLine($"   - Khuyến nghị: {product.LuongDatHangKhuyenNghi} sản phẩm");
                        result.AppendLine($"   - Trạng thái: {product.TrangThai}");
                        result.AppendLine();
                    }
                }

                // Sản phẩm dư thừa
                var overstock = analysis
                    .Where(a => a.TyLeDuThua > 0.5)
                    .OrderByDescending(a => a.TyLeDuThua)
                    .Take(3)
                    .ToList();

                if (overstock.Any())
                {
                    result.AppendLine("💎 **SẢN PHẨM DƯ THỪA**");
                    foreach (var product in overstock)
                    {
                        result.AppendLine($"🔸 **{product.TenSanPham}**");
                        result.AppendLine($"   - Tồn kho: {product.TonKhoHienTai}");
                        result.AppendLine($"   - Tỷ lệ dư thừa: {product.TyLeDuThua:P0}");
                        result.AppendLine($"   - Khuyến nghị: {product.KhuyenNghi}");
                        result.AppendLine();
                    }
                }

                result.AppendLine("💡 **Gợi ý:**");
                result.AppendLine("- Gõ 'gợi ý đặt hàng' để xem khuyến nghị chi tiết");
                result.AppendLine("- Gõ 'phân tích tồn kho' để xem báo cáo đầy đủ");
                result.AppendLine("- Gõ 'sản phẩm ưu tiên' để xem sản phẩm cần đặt gấp");

                return result.ToString();
            }
            catch (Exception ex)
            {
                return $"❌ Lỗi phân tích: {ex.Message}";
            }
        }
        // thêm lớp helper để chứa mục trích xuất
        public class ExtractedItem
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public int Quantity { get; set; }
            public decimal Price { get; set; }
        }

        public class MatchedProduct
        {
            public ModelSanPham Product { get; set; }
            public ExtractedItem Item { get; set; }
            public double MatchScore { get; internal set; }
        }
    }

    // ================== LỚP HỖ TRỢ CHO CHI TIẾT MUA HÀNG CỦA KHÁCH ==================
    public class CustomerPurchaseDetail
    {
        public string MaDonHang { get; set; }
        public string TenSanPham { get; set; }
        public int SoLuong { get; set; }
        public decimal DonGia { get; set; }
        public decimal ThanhTien { get; set; }
        public DateTime NgayMua { get; set; }
    }

    // ================== LỚP HỖ TRỢ CHO CHI TIẾT ĐƠN HÀNG ==================
    public class CustomerOrderDetail
    {
        public string MaDonHang { get; set; }
        public string TenKhachHang { get; set; }
        public DateTime NgayDat { get; set; }
        public string TrangThai { get; set; }
        public decimal TongTien { get; set; }
        public List<OrderProductDetail> SanPham { get; set; }
    }

    // ================== LỚP HỖ TRỢ CHO CHI TIẾT SẢN PHẨM TRONG ĐƠN HÀNG ==================
    public class OrderProductDetail
    {
        public string MaSanPham { get; set; }
        public string TenSanPham { get; set; }
        public int SoLuong { get; set; }
        public decimal DonGia { get; set; }
        public decimal ThanhTien { get; set; }
    }
    // ================== MODELS PHÂN TÍCH AI ==================
    public class ProductAnalysis
    {
        public string MaSanPham { get; set; }
        public string TenSanPham { get; set; }
        public int TonKhoHienTai { get; set; }
        public int SoLuongToiUu { get; set; }
        public int LuongDatHangKhuyenNghi { get; set; }
        public double TyLeThieuHang { get; set; }
        public double TyLeDuThua { get; set; }
        public string TrangThai { get; set; }
        public string KhuyenNghi { get; set; }
        public decimal ChiPhiDuKien { get; set; }
    }

    public class SalesForecast
    {
        public string MaSanPham { get; set; }
        public string TenSanPham { get; set; }
        public int DuBaoBan { get; set; }
        public double DoTinCay { get; set; }
        public int SoNgayConLai { get; set; }
    }

    public class ReorderRecommendation
    {
        public string MaSanPham { get; set; }
        public string TenSanPham { get; set; }
        public int LuongDatHangToiUu { get; set; }
        public string MucUuTien { get; set; } // CAO, TRUNG_BINH, THAP
        public string LyDo { get; set; }
        public decimal TongChiPhi { get; set; }
    }
}

