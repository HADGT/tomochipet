using GenerativeAI;
using Google;
using Google.Apis.Sheets.v4.Data;
using Microsoft.AspNet.SignalR.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using qlthucung.Models;
using qlthucung.Models.Chat;
using System;
using System.Collections.Generic;
using System.Composition;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using static qlthucung.Models.Chatbot;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace qlthucung.Services.chat
{
    public class ChatService : IChatService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly string _baseApiUrl;
        private readonly string _apiKey;
        private readonly GenerativeModel _model;

        // NEW: TF-IDF indexer
        private readonly TfidfIndexer _indexer;

        // Optional embedding provider (resolved via IServiceProvider)
        private readonly IEmbeddingService _embeddingService;

        public string SanPhamApiUrl { get; set; }
        public string DichVuApiUrl { get; set; }

        // Basic Vietnamese stopwords (extend as needed)
        private static readonly HashSet<string> VietnameseStopwords = new(StringComparer.OrdinalIgnoreCase)
        {
            "và","của","là","có","cho","với","để","trong","một","những","đã","rất","như","khi","tôi","bạn","cần","các","đó","này","ở","ra","về","vẫn"
        };

        public ChatService(AppDbContext context, IConfiguration configuration, TfidfIndexer indexer, IEmbeddingService embeddingService)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
            _embeddingService = embeddingService;
            _apiKey = _configuration["ChatAI:ApiKey"]
                ?? throw new Exception("Missing Gemini API key");
            _baseApiUrl = _configuration["App:BaseApiUrl"] ?? "http://localhost:5172";

            // Ensure URLs are initialized
            SanPhamApiUrl = _baseApiUrl + "/SanPham";
            DichVuApiUrl = _baseApiUrl + "/DichVu";
        }
        private List<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            // Normalize and lowercase
            text = text.Normalize(System.Text.NormalizationForm.FormKD).ToLowerInvariant();

            // Replace punctuation (keep letters, numbers and spaces). This works for Vietnamese characters.
            text = Regex.Replace(text, @"[^\p{L}\p{Nd}\s]+", " ");

            // Split into tokens
            var tokens = text
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 1) // remove single-char tokens (adjust if needed)
                .Where(t => !VietnameseStopwords.Contains(t))
                .ToList();

            return tokens;
        }

        // Build bag-of-words vector preserving term frequency (no Distinct).
        private Dictionary<string, double> GetVector(string text)
        {
            var tokens = Tokenize(text);

            var vector = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var token in tokens)
            {
                if (!vector.ContainsKey(token))
                    vector[token] = 1;
                else
                    vector[token] += 1;
            }

            return vector;
        }

        // Small helper: extract short phrases (2-grams) to improve keyword coverage
        private IEnumerable<string> ExtractNgrams(IEnumerable<string> tokens, int n = 2)
        {
            var list = tokens.ToList();
            for (int i = 0; i + n <= list.Count; i++)
            {
                yield return string.Join(' ', list.Skip(i).Take(n));
            }
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

        private double Normalize(double score) => Math.Min(score / 5.0, 1.0);

        public async Task TrainContentIndexAsync()
        {
            await _indexer.BuildIndexAsync();
            if (_indexer.IsReady)
                await _indexer.SaveAsync();
            else
                throw new Exception("Indexer build failed or no documents to index.");
        }

        public async Task<List<Product>> GetProducts(HybridQuery queryVector, string petType)
        {
            // If index ready, use TF-IDF retrieval
            if (_indexer.IsReady)
            {
                var hits = _indexer.Query(queryVector.RawText ?? "", 10); // get top 10 documents
                var productIds = hits.Where(h => h.docId.StartsWith("P_")).Select(h => int.Parse(h.docId.Substring(2))).ToList();
                if (productIds.Count == 0)
                    return await GetProductsFallback(queryVector, petType);

                var products = await _context.SanPhams.Where(sp => productIds.Contains(sp.Masp)).ToListAsync();
                var categoryMap = await _context.DanhMucs.ToDictionaryAsync(dm => dm.IdDanhmuc, dm => dm.Tendanhmuc);

                var dto = products.Select(sp => new Product
                {
                    Id = sp.Masp,
                    Name = sp.Tensp,
                    Price = sp.Giakhuyenmai ?? 0,
                    ProductUrl = SanPhamApiUrl + "/Details/" + sp.Masp,
                    Type = (categoryMap.ContainsKey(sp.IdDanhmuc ?? 0) ? categoryMap[sp.IdDanhmuc ?? 0] : "") + " " + (sp.Mota ?? "")
                }).ToList();

                var results = new List<(Product product, double score)>();
                foreach (var p in dto)
                {
                    double keywordScore = CalculateKeywordScore(queryVector.Keywords, p);
                    var productVector = GetVector(p.Name + p.Type);
                    double vectorScore = CosineSimilarity(queryVector.Vector, productVector);

                    // If embeddings available, you can compute semantic score separately and combine here.
                    double finalScore = 0.4 * keywordScore + 0.6 * vectorScore;
                    results.Add((p, finalScore));
                }

                return results.OrderByDescending(x => x.score).Take(5).Select(x => x.product).ToList();
            }

            return await GetProductsFallback(queryVector, petType);
        }
        public async Task<List<Product>> GetProductsFallback(HybridQuery queryVector, string petType)
        {
            var parent = await _context.DanhMucs.FirstOrDefaultAsync(dm => dm.Tendanhmuc.Trim().ToLower() == petType.Trim().ToLower());
            if (parent == null) return new List<Product>();

            List<int> allCategoryNames = await _context.DanhMucs
                .Where(dm => (dm.ParentID != null && dm.ParentID.Trim().ToLower() == parent.Tendanhmuc.Trim().ToLower())
                            || dm.IdDanhmuc == parent.IdDanhmuc)
                .Select(dm => dm.IdDanhmuc).ToListAsync();

            var products = await _context.SanPhams.Where(sp => allCategoryNames.Contains((int)sp.IdDanhmuc)).ToListAsync();
            var categoryMap = await _context.DanhMucs.ToDictionaryAsync(dm => dm.IdDanhmuc, dm => dm.Tendanhmuc);

            var dto = products.Select(sp => new Product
            {
                Id = sp.Masp,
                Name = sp.Tensp,
                Price = sp.Giakhuyenmai ?? 0,
                ProductUrl = SanPhamApiUrl + "/Details/" + sp.Masp,
                Type = (categoryMap.ContainsKey(sp.IdDanhmuc ?? 0) ? categoryMap[sp.IdDanhmuc ?? 0] : "") + " " + (sp.Mota ?? "")
            }).ToList();

            var results = new List<(Product product, double score)>();
            foreach (var p in dto)
            {
                double keywordScore = CalculateKeywordScore(queryVector.Keywords, p);
                var productVector = GetVector(p.Name + p.Type);
                double vectorScore = CosineSimilarity(queryVector.Vector, productVector);
                double finalScore = 0.4 * keywordScore + 0.6 * vectorScore;
                results.Add((p, finalScore));
            }

            return results.OrderByDescending(x => x.score).Take(5).Select(x => x.product).ToList();
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
            if (_indexer.IsReady)
            {
                var hits = _indexer.Query(queryVector.RawText ?? "", 10);
                var productIds = hits.Where(h => h.docId.StartsWith("P_")).Select(h => int.Parse(h.docId.Substring(2))).ToList();
                var serviceIds = hits.Where(h => h.docId.StartsWith("S_")).Select(h => int.Parse(h.docId.Substring(2))).ToList();

                var sanPhams = await _context.SanPhams.Where(sp => productIds.Contains(sp.Masp)).ToListAsync();
                var dichVus = await _context.SPDichVu.Where(dv => serviceIds.Contains(dv.DichVuID)).ToListAsync();
                var categoryMap = await _context.DanhMucs.ToDictionaryAsync(dm => dm.IdDanhmuc, dm => dm.Tendanhmuc);

                var results = new List<(Product product, double score)>();

                foreach (var sp in sanPhams)
                {
                    var product = new Product
                    {
                        Id = sp.Masp,
                        Name = sp.Tensp,
                        Price = sp.Giakhuyenmai ?? 0,
                        ProductUrl = SanPhamApiUrl + "/Details/" + sp.Masp,
                        Type = (categoryMap.ContainsKey(sp.IdDanhmuc ?? 0) ? categoryMap[sp.IdDanhmuc ?? 0] : "") + " " + (sp.Mota ?? "")
                    };

                    int keywordScore = CalculateKeywordScore(queryVector.Keywords, product);
                    var vectorStr = GetVector(sp.Tensp + " " + sp.Mota);
                    double vectorScore = CosineSimilarity(queryVector.Vector, vectorStr);
                    double finalScore = 0.4 * keywordScore + 0.6 * vectorScore;
                    results.Add((product, finalScore));
                }

                foreach (var dv in dichVus)
                {
                    var service = new Product
                    {
                        Id = dv.DichVuID,
                        Name = dv.TenDichVu,
                        Price = dv.Gia ?? 0,
                        ProductUrl = DichVuApiUrl + "/Datlich",
                        Type = dv.MoTa + " thú cưng"
                    };

                    int keywordScore = CalculateKeywordScore(queryVector.Keywords, service);
                    var vectorStr = GetVector(dv.TenDichVu);
                    double vectorScore = CosineSimilarity(queryVector.Vector, vectorStr);
                    double finalScore = 0.4 * keywordScore + 0.6 * vectorScore;
                    results.Add((service, finalScore));
                }

                return results.OrderByDescending(x => x.score).Take(5).Select(x => x.product).ToList();
            }

            return await GetProductsForHealthFallback(queryVector);
        }

        private async Task<List<Product>> GetProductsForHealthFallback(HybridQuery queryVector)
        {
            const string EXCLUDED_CATEGORY_NAME = "Thú cưng";
            List<int> excludedCategoryIds = new List<int>();
            var parent = await _context.DanhMucs
                .FirstOrDefaultAsync(dm => dm.Tendanhmuc.Trim().ToLower() != EXCLUDED_CATEGORY_NAME.ToLower());

            if (parent != null)
            {
                excludedCategoryIds = await _context.DanhMucs
                    .Where(dm => (dm.ParentID != null && dm.ParentID.Trim().ToLower() == parent.Tendanhmuc.Trim().ToLower())
                             || dm.IdDanhmuc == parent.IdDanhmuc)
                    .Select(dm => dm.IdDanhmuc)
                    .ToListAsync();
            }

            var sanPhams = await _context.SanPhams
                .Where(sp => !excludedCategoryIds.Contains(sp.IdDanhmuc ?? 0))
                .ToListAsync();

            var dichVus = await _context.SPDichVu.ToListAsync();
            var results = new List<(Product product, double score)>();

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
                        parts = new[] { new { text = prompt } }
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

            // If API returned non-success, return null to trigger fallback message
            if (!response.IsSuccessStatusCode)
            {
                // Optionally you can log `result` somewhere to inspect the failure
                return null;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(result);
            var root = doc.RootElement;

            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.GetArrayLength() == 0)
            {
                // no candidates returned
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

            // Attempt to extract JSON block even if the model included extra text
            int start = reply.IndexOf('{');
            int end = reply.LastIndexOf('}');
            if (start != -1 && end != -1 && end > start)
            {
                reply = reply.Substring(start, end - start + 1);
            }

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
                // parsing failed - you can log `reply` and `ex.Message` for debugging
                return null;
            }
        }

        public async Task<string> AskAsync(string query)
        {
            try
            {
                if (query != null && !string.IsNullOrWhiteSpace(query))
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

                return "Vui lòng nhập câu hỏi.";
            }
            catch (Exception ex)
            {
                return $"Lỗi AI: {ex.Message}";
            }
        }

        public async Task<HybridQuery> EncodeHybridQueryAsync(string rawText, string petType, IEnumerable<string> aiKeywords = null)
        {
            rawText ??= "";

            // 1. Build base token list from raw text
            var baseTokens = Tokenize(rawText);

            // 2. Start keywords from AI suggestions if provided (prefer AI)
            List<string> keywords;
            if (aiKeywords != null && aiKeywords.Any())
            {
                keywords = aiKeywords
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim().ToLowerInvariant())
                    .Distinct()
                    .ToList();
            }
            else
            {
                // combine tokens + 2-grams to create richer keyword set
                var ngrams = ExtractNgrams(baseTokens, 2);
                keywords = baseTokens.Concat(ngrams)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .Distinct()
                    .ToList();
            }

            // 3. Boost with petType tokens (helps bias retrieval towards pet category)
            if (!string.IsNullOrWhiteSpace(petType))
            {
                var ptTokens = petType
                    .ToLowerInvariant()
                    .Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim());
                foreach (var t in ptTokens)
                {
                    if (!keywords.Contains(t))
                        keywords.Add(t);
                }
            }

            // 4. Build lexical retrieval vector from keywords (preserve TF by joining keywords)
            var retrievalText = string.Join(" ", keywords);
            var lexicalVector = GetVector(retrievalText);

            // 5. Optionally compute dense embedding if embedding provider present
            float[] embedding = null;
            if (_embeddingService != null)
            {
                try
                {
                    // prefer using rawText for embedding (semantic) but fall back to retrievalText
                    var embedSource = string.IsNullOrWhiteSpace(rawText) ? retrievalText : rawText;
                    embedding = await _embeddingService.GetEmbeddingAsync(embedSource);
                }
                catch
                {
                    // embedding failure is non-fatal; continue with lexical only
                    embedding = null;
                }
            }

            return new HybridQuery
            {
                RawText = rawText,
                Keywords = keywords,
                Vector = lexicalVector,
                Embedding = embedding // note: HybridQuery must include Embedding property (float[]). Update model accordingly.
            };
        }

        string BuildIntentPrompt(string query, string pettype)
        {
            return $@"
                You are a Pet Care AI assistant.
                You MUST respond in Vietnamese.

                Return EXACTLY one JSON object and NOTHING ELSE (no markdown, no commentary, no code fences).

                JSON SCHEMA (use these exact property names & casing):
                {{
                  ""Intent"": ""product | service | health | other"",
                  ""SearchTerms"": [],
                  ""HealthAnalysis"": null | {{
                      ""Symptoms"": [],
                      ""Measures"": [],
                      ""NeededProducts"": [],
                      ""NeededServices"": []
                  }}
                }}

                RULES:
                - If Intent != ""health"" then HealthAnalysis MUST be null.
                - SearchTerms must be an array of short keyword strings (no sentences).
                - Do NOT include additional fields.
                - Use plain JSON only. Do NOT add explanation or text outside the JSON object.

                INPUT:
                - PetType: {pettype}
                - UserQuery: {query}

                EXAMPLES (showing exact JSON objects only):

                Product example:
                {{
                  ""Intent"": ""product"",
                  ""SearchTerms"": [""chó phốc"", ""sản phẩm""],
                  ""HealthAnalysis"": null
                }}

                Health example:
                {{
                  ""Intent"": ""health"",
                  ""SearchTerms"": [""sốt"", ""nôn""],
                  ""HealthAnalysis"": {{
                      ""Symptoms"": [""sốt"", ""nôn""],
                      ""Measures"": [""cho uống nước"", ""theo dõi nhiệt độ""],
                      ""NeededProducts"": [""thuốc hạ sốt""],
                      ""NeededServices"": [""khám thú y""]
                  }}
                }}

                Now generate the single JSON object that matches the schema above for the provided input. Output only the JSON object."
             ;
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
            var products = new List<Product>();
            if (analysisJson.Intent == "product")
            {
                var hybridQuery = await EncodeHybridQueryAsync(query.Text ?? "", query.PetType, analysisJson.SearchTerms ?? new List<string>());
                products = await GetProducts(hybridQuery, query.PetType);
            }
            else if (analysisJson.Intent == "service")
            {
                var hybridQuery = await EncodeHybridQueryAsync(query.Text ?? "", query.PetType, analysisJson.SearchTerms ?? new List<string>());
                products = await GetProductsForHealth(hybridQuery);
            }

            /* =========================
             * 3. TRẢ DATA CHO AI TƯ VẤN
             * ========================= */
            var productJson = System.Text.Json.JsonSerializer.Serialize(products) ?? null;
            var finalPrompt = BuildFinalAdvicePrompt(query.Text, query.Category + "-" + query.PetType, analysisJson.Intent, analysisJson.HealthAnalysis, productJson);
            return await AskAsync(finalPrompt);
        }
    }
}