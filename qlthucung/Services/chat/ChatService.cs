using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using qlthucung.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Composition;
using Microsoft.Extensions.Options;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Text;
using Microsoft.AspNet.SignalR.Json;
using Newtonsoft.Json;
using GenerativeAI.Types;
using GenerativeAI;
using qlthucung.Models.Chat;
using Google.Apis.Sheets.v4.Data;
using Microsoft.VisualBasic;
using static System.Net.Mime.MediaTypeNames;
using System.Security.Policy;
using Microsoft.Identity.Client;
using Google;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using static qlthucung.Models.Chatbot;

namespace qlthucung.Services.chat
{
    public class ChatService : IChatService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly string _baseApiUrl;
        private readonly string _apiKey;
        private readonly GenerativeModel _model;

        public string SanPhamApiUrl { get; set; }
        public string DichVuApiUrl { get; set; }

        public ChatService(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _apiKey = configuration["ChatAI:ApiKey"]
            ?? throw new Exception("Missing Gemini API key");
            _baseApiUrl = configuration["App:BaseApiUrl"] ?? "http://localhost:5172";
        }

        public void API_link_web(ChatService service)
        {
            string base_api_url = service._baseApiUrl;
            SanPhamApiUrl = base_api_url + "/SanPham";
            DichVuApiUrl = base_api_url + "/DichVu";
        }

        private List<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            return text
                .ToLower()
                .Replace(",", " ")
                .Replace(".", " ")
                .Replace("-", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Distinct()
                .ToList();
        }


        private Dictionary<string, double> GetVector(string text)
        {
            var tokens = Tokenize(text);

            var vector = new Dictionary<string, double>();

            foreach (var token in tokens)
            {
                if (!vector.ContainsKey(token))
                    vector[token] = 1;
                else
                    vector[token]++;
            }

            return vector;
        }

        private double CosineSimilarity(
            Dictionary<string, double> v1,
            Dictionary<string, double> v2)
        {
            double dot = 0;
            double mag1 = 0;
            double mag2 = 0;

            foreach (var kv in v1)
            {
                if (v2.ContainsKey(kv.Key))
                    dot += kv.Value * v2[kv.Key];

                mag1 += kv.Value * kv.Value;
            }

            foreach (var kv in v2)
                mag2 += kv.Value * kv.Value;

            if (mag1 == 0 || mag2 == 0)
                return 0;

            return dot / (Math.Sqrt(mag1) * Math.Sqrt(mag2));
        }


        private int CalculateKeywordScore(List<string> keywords, Product p)
        {
            var text = $"{p.Name} {p.Type}".ToLower();
            int score = 0;

            foreach (var kw in keywords)
            {
                if (text.Contains(kw))
                    score++;
            }

            return score;
        }

        private double Normalize(double score)
        {
            return Math.Min(score / 5.0, 1.0);
        }

        public async Task<List<Product>> GetProducts(HybridQuery queryVector, string petType)
        {
            // Tìm kiếm Danh mục 
            var parent = await _context.DanhMucs
                .FirstOrDefaultAsync(dm => dm.Tendanhmuc.Trim().ToLower() == petType.Trim().ToLower());

            if (parent == null)
                return new List<Product>();

            List<int> allCategoryNames = await _context.DanhMucs
                            .Where(dm => (dm.ParentID != null && dm.ParentID.Trim().ToLower() == parent.Tendanhmuc.Trim().ToLower())
                                     || dm.IdDanhmuc == parent.IdDanhmuc)
                            .Select(dm => dm.IdDanhmuc) // Lấy tên danh mục để lọc metadata trong Vector DB
                            .ToListAsync();

            // Lấy Dữ liệu chi tiết từ DB (SQL)
            var products = await _context.SanPhams
                .Where(sp => allCategoryNames.Contains((int)sp.IdDanhmuc))
                .ToListAsync();

            var categoryMap = await _context.DanhMucs
                .ToDictionaryAsync(dm => dm.IdDanhmuc, dm => dm.Tendanhmuc);

            var dto = products.Select(sp => new Product
            {
                Id = sp.Masp,
                Name = sp.Tensp,
                Price = sp.Giakhuyenmai ?? 0,
                ProductUrl = SanPhamApiUrl + "/Details/" + sp.Masp,
                Type = (categoryMap.ContainsKey(sp.IdDanhmuc ?? 0)
                ? categoryMap[sp.IdDanhmuc ?? 0]
                : "") + " " + (sp.Mota ?? "")
            }).ToList();

            var results = new List<(Product product, double score)>();

            foreach (var p in dto)
            {
                double keywordScore = CalculateKeywordScore(queryVector.Keywords, p);


                Dictionary<string, double> productVector = GetVector(p.Name + p.Type);

                double vectorScore =
                    CosineSimilarity(queryVector.Vector, productVector);

                double finalScore =
                    0.4 * keywordScore +
                    0.6 * vectorScore;
                results.Add((p, finalScore));
            }

            return results
                .OrderByDescending(x => x.score)
                .Take(3)
                .Select(x => x.product)
                .ToList();
        }

        public async Task<List<Message>> GetMessagesAsync(string userId)
        {
            return await _context.Messages
                .Where(m => m.SenderId == userId || m.ReceiverId == userId)
                .OrderBy(m => m.SentAt)
                .Select(m => new Message
                {
                    ReceiverId = m.ReceiverId,
                    SenderId = m.SenderId,
                    Content = m.Content,
                    SentAt = m.SentAt,
                    IsRead = m.IsRead
                })
                .ToListAsync();
        }

        public async Task<List<Product>> GetProductsForHealth(HybridQuery queryVector)
        {
            // CÁC BƯỚC LỌC TRƯỚC (Giữ nguyên logic loại trừ danh mục "Thú cưng")

            // 1. Xác định tên danh mục cần loại trừ
            const string EXCLUDED_CATEGORY_NAME = "Thú cưng";

            // KHAI BÁO BIẾN Ở PHẠM VI TOÀN BỘ PHƯƠNG THỨC
            List<int> excludedCategoryIds = new List<int>();

            // Tìm kiếm Danh mục (Giữ nguyên logic giới hạn danh mục)
            var parent = await _context.DanhMucs
                .FirstOrDefaultAsync(dm => dm.Tendanhmuc.Trim().ToLower() == EXCLUDED_CATEGORY_NAME.ToLower());

            if (parent != null) // Đã tìm thấy danh mục cha (parent)
            {
                // 1. Lấy tất cả IDs của danh mục "Thú cưng" (parent) và các danh mục con của nó (IDs CẦN LOẠI TRỪ)
                excludedCategoryIds = await _context.DanhMucs
                    .Where(dm => (dm.ParentID != null && dm.ParentID.Trim().ToLower() == parent.Tendanhmuc.Trim().ToLower())
                             || dm.IdDanhmuc == parent.IdDanhmuc)
                    .Select(dm => dm.IdDanhmuc) // CHỈ LẤY ID
                    .ToListAsync();
            }
            /* =======================
             * 2. Lấy toàn bộ SẢN PHẨM hợp lệ
             * ======================= */
            var sanPhams = await _context.SanPhams
                .Where(sp => !excludedCategoryIds.Contains(sp.IdDanhmuc ?? 0))
                .ToListAsync();

            /* =======================
             * 3. Lấy toàn bộ DỊCH VỤ
             * ======================= */
            var dichVus = await _context.SPDichVu.ToListAsync();

            /* =======================
             * 4. Hybrid Scoring
             * ======================= */
            var results = new List<(Product product, double score)>();

            // ---- SẢN PHẨM ----
            foreach (var sp in sanPhams)
            {
                var categoryMap = await _context.DanhMucs
                .ToDictionaryAsync(dm => dm.IdDanhmuc, dm => dm.Tendanhmuc);

                var product = new Product
                {
                    Id = sp.Masp,
                    Name = sp.Tensp,
                    Price = sp.Giakhuyenmai ?? 0,
                    ProductUrl = SanPhamApiUrl + "/Details/" + sp.Masp,
                    Type = (categoryMap.ContainsKey(sp.IdDanhmuc ?? 0)
                ? categoryMap[sp.IdDanhmuc ?? 0]
                : "") + " " + (sp.Mota ?? "")
                };

                int keywordScore =
                    CalculateKeywordScore(queryVector.Keywords, product);

                var vectorStr =
                    GetVector(sp.Tensp + " " + sp.Mota);

                double vectorScore =
                    CosineSimilarity(queryVector.Vector, vectorStr);

                double finalScore =
                    0.4 * keywordScore + 0.6 * vectorScore;

                results.Add((product, finalScore));
            }

            // ---- DỊCH VỤ ----
            foreach (var dv in dichVus)
            {
                var service = new Product
                {
                    Id = dv.DichVuID,
                    Name = dv.TenDichVu,
                    Price = dv.Gia ?? 0,
                    ProductUrl = DichVuApiUrl + "/Datlich",
                    Type = dv.MoTa + "thú cưng"
                };

                int keywordScore =
                    CalculateKeywordScore(queryVector.Keywords, service);

                var vectorStr =
                    GetVector(dv.TenDichVu);

                double vectorScore =
                    CosineSimilarity(queryVector.Vector, vectorStr);

                double finalScore =
                    0.4 * keywordScore + 0.6 * vectorScore;

                results.Add((service, finalScore));
            }

            /* =======================
             * 5. Sort + Take
             * ======================= */
            return results
                .OrderByDescending(x => x.score)
                .Take(5)
                .Select(x => x.product)
                .ToList();
        }

        private string CleanJsonFromAI(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            text = text.Trim();

            // Remove ```json or ```
            if (text.StartsWith("```"))
            {
                int firstNewLine = text.IndexOf('\n');
                int lastFence = text.LastIndexOf("```");

                if (firstNewLine != -1 && lastFence != -1)
                {
                    text = text.Substring(firstNewLine + 1, lastFence - firstNewLine - 1);
                }
            }

            return text.Trim();
        }


        public async Task<IntentResult> CallAIAsync(string prompt)
        {
            string apiKey = _apiKey;
            string url =
                "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key="
                + apiKey;

            using var http = new HttpClient();

            var requestBody = new
            {
                contents = new[]
                {
            new
            {
                parts = new[]
                {
                    new { text = prompt }
                }
            }
        },
                generationConfig = new
                {
                    temperature = 0.0,
                    topK = 1,
                    topP = 1.0,
                    maxOutputTokens = 4096
                }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await http.PostAsync(url, content);
            var result = await response.Content.ReadAsStringAsync();

            using var doc = System.Text.Json.JsonDocument.Parse(result);
            var root = doc.RootElement;

            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.GetArrayLength() == 0)
            {
                return null;
            }

            var reply = candidates[0]
    .GetProperty("content")
    .GetProperty("parts")[0]
    .GetProperty("text")
    .GetString();

            if (string.IsNullOrWhiteSpace(reply))
                return null;

            // 🔥 CLEAN JSON
            reply = CleanJsonFromAI(reply);

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<IntentResult>(
                    reply,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public async Task<string> AskAsync(string query)
        {
            try
            {
                string apiKey = _apiKey;
                string url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key=" + apiKey;
                using (var http = new HttpClient())
                {
                    var requestBody = new
                    {
                        contents = new[]
                        {
                            new {
                                parts = new[]
                                {
                                    new { text = query }
                                }
                            }
                        },
                        generationConfig = new
                        {
                            temperature = 0.0,
                            topK = 1,
                            topP = 1.0,
                            maxOutputTokens = 4096
                        }
                    };

                    var json = System.Text.Json.JsonSerializer.Serialize(requestBody);

                    var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                    var response = await http.PostAsync(url, content);
                    var result = await response.Content.ReadAsStringAsync();

                    using var doc = System.Text.Json.JsonDocument.Parse(result);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("candidates", out var candidates) ||
                        candidates.GetArrayLength() == 0)
                    {
                        return "AI hiện không thể trả lời. Vui lòng thử lại sau.";
                    }

                    var reply = candidates[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString();

                    return reply ?? "AI không trả lời.";
                }
            }
            catch (Exception ex)
            {
                return $"Lỗi AI: {ex.Message}";
            }
        }

        public HybridQuery EncodeHybridQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new HybridQuery();

            var keywords = query
                .ToLower()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Distinct()
                .ToList();

            Dictionary<string, double> vectorStr = GetVector(query);

            return new HybridQuery
            {
                RawText = query,
                Keywords = keywords,
                Vector = vectorStr
            };
        }

        string BuildIntentPrompt(string query, string pettype)
        {
            return $@"
                You are a Pet Care AI assistant.
                You MUST respond in Vietnamese.

                Your FIRST TASK is to analyze the user's query and OUTPUT a JSON object with the following structure ONLY:

                {{
                  ""intent"": ""product | service | health | other"",
                  ""search_terms"": [],
                  ""health_analysis"": {{
                      ""symptoms"": [],
                      ""measures"": [],
                      ""needed_products"": [],
                      ""needed_services"": []
                  }}
                }}

                RULES:
                - intent MUST be one of the four values.
                - If intent != health → health_analysis MUST be null.
                - search_terms must be meaningful keywords extracted from the query.
                - DO NOT explain anything.
                - DO NOT add text outside JSON.

                INPUT:
                - PetType: {pettype}
                - UserQuery: {query}
                ";
        }


        private string BuildFinalAdvicePrompt(
            string query,
            string petType,
            string intent,
            HealthAnalysis? healthAnalysis,
            string productJson
        )
                {
                    return $@"
                        You are a **Pet Care AI Advisor**.
                        You MUST respond in **Vietnamese**.
                        You are NOT allowed to invent products or services.

                        ----------------------------------
                        USER QUERY:
                        {query}

                        PET TYPE:
                        {petType}

                        INTENT:
                        {intent}

                        ----------------------------------
                        AVAILABLE DATA (JSON):
                        {productJson}
                        ----------------------------------

                        INSTRUCTIONS:

                        1. ONLY use the products/services from AVAILABLE DATA.
                        2. DO NOT mention any product or service not in the JSON.
                        3. DO NOT explain system logic or analysis steps.

                        INTENT HANDLING RULES:

                        - If intent = ""product"":
                          * Recommend suitable products.
                          * Explain briefly why each product is appropriate.

                        - If intent = ""service"":
                          * Suggest suitable services from the data.
                          * Explain briefly.

                        - If intent = ""health"":
                          * Base your advice on:
                              - Symptoms: {System.Text.Json.JsonSerializer.Serialize(healthAnalysis?.Symptoms)}
                              - Measures: {System.Text.Json.JsonSerializer.Serialize(healthAnalysis?.Measures)}
                          * Provide:
                              - Basic health guidance.
                              - When to use the suggested products/services.
                              - If symptoms are serious → advise visiting a veterinarian.
                          * DO NOT give medical dosages.

                        - If intent = ""other"":
                          * Provide general pet care advice.
                          * Use product data ONLY if relevant.

                        OUTPUT RULES:
                        - Be clear, concise, friendly.
                        - No markdown JSON blocks.
                        - No emojis.
                        ";
        }


        public async Task<string> GetPetAdviceAsync(ChatQuery query)
        {
            if (string.IsNullOrWhiteSpace(query.Text))
                return "Vui lòng nhập câu hỏi.";

            /* =========================
             * 1. GỌI AI PHÂN TÍCH INTENT
             * ========================= */
            string analysisPrompt = BuildIntentPrompt(query.Text, query.Category + "-" + query.PetType);

            IntentResult analysisJson = await CallAIAsync(analysisPrompt);

            if (analysisJson == null)
                return "Em chưa hiểu rõ yêu cầu của bạn, bạn mô tả chi tiết hơn giúp em nhé.";

            /* =========================
             * 2. XÂY DỰNG HYBRID QUERY
             * ========================= */
            List<Product> products = new();

            if (analysisJson.Intent == "product")
            {
                var hybridQuery =
                    EncodeHybridQuery(
                        string.Join(analysisPrompt, " ", analysisJson.SearchTerms));

                products = await GetProducts(hybridQuery, query.PetType);
            }
            else if (analysisJson.Intent == "service")
            {
                var hybridQuery =
                    EncodeHybridQuery(
                        string.Join(analysisPrompt, " ", analysisJson.SearchTerms));

                products = await GetProductsForHealth(hybridQuery);
            }

            /* =========================
             * 3. TRẢ DATA CHO AI TƯ VẤN
             * ========================= */
            var productJson = System.Text.Json.JsonSerializer.Serialize(products) ?? null;

            var finalPrompt = BuildFinalAdvicePrompt(
                query.Text,
                query.Category + "-" + query.PetType,
                analysisJson.Intent,
                analysisJson.HealthAnalysis,
                productJson
            );

            return await AskAsync(finalPrompt);
        }

        public Task<string> AskAsync(ChatQuery query)
        {
            throw new NotImplementedException();
        }
    }
}
