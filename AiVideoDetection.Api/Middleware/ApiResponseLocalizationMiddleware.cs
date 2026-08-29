using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AiVideoDetection.Api.Middleware;

public sealed partial class ApiResponseLocalizationMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> LocalizablePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "message",
        "errors",
        "errorMessage",
        "userMessage",
        "currentStep"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var originalBody = context.Response.Body;
        await using var bufferedBody = new MemoryStream();
        context.Response.Body = bufferedBody;

        try
        {
            await next(context);

            bufferedBody.Position = 0;
            if (!ShouldLocalize(context))
            {
                await bufferedBody.CopyToAsync(originalBody);
                return;
            }

            var language = ResolveLanguage(context.Request);
            if (language == "en")
            {
                await bufferedBody.CopyToAsync(originalBody);
                return;
            }

            using var reader = new StreamReader(bufferedBody, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body))
            {
                return;
            }

            JsonNode? json;
            try
            {
                json = JsonNode.Parse(body);
            }
            catch
            {
                await WriteOriginalAsync(originalBody, body);
                return;
            }

            if (json is null)
            {
                await WriteOriginalAsync(originalBody, body);
                return;
            }

            LocalizeNode(json, language);
            var localized = json.ToJsonString();
            context.Response.ContentLength = Encoding.UTF8.GetByteCount(localized);
            await originalBody.WriteAsync(Encoding.UTF8.GetBytes(localized));
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static bool ShouldLocalize(HttpContext context)
    {
        var contentType = context.Response.ContentType;
        return !string.IsNullOrWhiteSpace(contentType)
            && contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveLanguage(HttpRequest request)
    {
        var explicitLanguage = request.Headers["X-Language"].FirstOrDefault();
        var acceptedLanguage = request.Headers.AcceptLanguage.FirstOrDefault();
        var language = explicitLanguage ?? acceptedLanguage ?? "en";

        return language.StartsWith("ur", StringComparison.OrdinalIgnoreCase) ? "ur"
            : language.StartsWith("ps", StringComparison.OrdinalIgnoreCase) ? "ps"
            : "en";
    }

    private static void LocalizeNode(JsonNode node, string language)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                if (property.Value is null)
                {
                    continue;
                }

                if (LocalizablePropertyNames.Contains(property.Key))
                {
                    jsonObject[property.Key] = LocalizeValue(property.Value, language);
                    continue;
                }

                LocalizeNode(property.Value, language);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null)
                {
                    LocalizeNode(item, language);
                }
            }
        }
    }

    private static JsonNode? LocalizeValue(JsonNode node, string language)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return LocalizeText(text, language);
        }

        if (node is JsonArray array)
        {
            var localizedArray = new JsonArray();
            foreach (var item in array)
            {
                if (item is JsonValue itemValue && itemValue.TryGetValue<string>(out var itemText))
                {
                    localizedArray.Add(LocalizeText(itemText, language));
                }
                else
                {
                    localizedArray.Add(item?.DeepClone());
                }
            }

            return localizedArray;
        }

        return node.DeepClone();
    }

    private static string LocalizeText(string text, string language)
    {
        var trimmed = text.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return text;
        }

        var sizeMatch = MaxSizeRegex().Match(trimmed);
        if (sizeMatch.Success)
        {
            var scan = LocalizeScanMode(sizeMatch.Groups["scan"].Value, language);
            var size = sizeMatch.Groups["size"].Value;
            return language switch
            {
                "ur" => $"{scan} کے لیے زیادہ سے زیادہ ویڈیو سائز {size} MB ہے۔",
                "ps" => $"د {scan} لپاره د ویډیو اعظمي اندازه {size} MB ده.",
                _ => text
            };
        }

        return language switch
        {
            "ur" => Urdu.TryGetValue(trimmed, out var translated) ? translated : text,
            "ps" => Pashto.TryGetValue(trimmed, out var translated) ? translated : text,
            _ => text
        };
    }

    private static string LocalizeScanMode(string scanMode, string language)
    {
        var normalized = scanMode.Trim();
        if (normalized.Equals("Detailed Scan", StringComparison.OrdinalIgnoreCase))
        {
            return language switch
            {
                "ur" => "تفصیلی اسکین",
                "ps" => "تفصیلي سکین",
                _ => normalized
            };
        }

        return language switch
        {
            "ur" => "سمارٹ اسکین",
            "ps" => "سمارټ سکین",
            _ => normalized
        };
    }

    private static Task WriteOriginalAsync(Stream originalBody, string body)
    {
        return originalBody.WriteAsync(Encoding.UTF8.GetBytes(body)).AsTask();
    }

    [GeneratedRegex(@"^The maximum allowed video size for (?<scan>.+?) is (?<size>\d+)\s+MB\.$", RegexOptions.IgnoreCase)]
    private static partial Regex MaxSizeRegex();

    private static readonly IReadOnlyDictionary<string, string> Urdu = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Request completed successfully."] = "درخواست کامیابی سے مکمل ہو گئی۔",
        ["An unexpected error occurred."] = "ایک غیر متوقع خرابی پیش آئی۔",
        ["Please try again later."] = "براہ کرم کچھ دیر بعد دوبارہ کوشش کریں۔",
        ["Unauthorized."] = "آپ کو اس عمل کی اجازت نہیں ہے۔",
        ["A video file is required."] = "ویڈیو فائل لازمی ہے۔",
        ["The uploaded file is empty."] = "اپ لوڈ کی گئی فائل خالی ہے۔",
        ["The uploaded file extension is not supported."] = "اپ لوڈ کی گئی فائل کی ایکسٹینشن سپورٹ نہیں ہے۔",
        ["The uploaded filename is invalid."] = "اپ لوڈ کی گئی فائل کا نام درست نہیں ہے۔",
        ["The uploaded file content type is not supported."] = "اپ لوڈ کی گئی فائل کی قسم سپورٹ نہیں ہے۔",
        ["You must confirm that you have the right to upload this video for analysis."] = "آپ کو تصدیق کرنی ہوگی کہ آپ کو یہ ویڈیو تجزیے کے لیے اپ لوڈ کرنے کا حق حاصل ہے۔",
        ["Video was not found."] = "ویڈیو نہیں ملی۔",
        ["Analysis job was not found."] = "تجزیے کا جاب نہیں ملا۔",
        ["Job status was not found."] = "جاب اسٹیٹس نہیں ملا۔",
        ["Analysis result is not available yet."] = "تجزیے کا نتیجہ ابھی دستیاب نہیں ہے۔",
        ["Analysis report is not available yet."] = "رپورٹ ابھی دستیاب نہیں ہے۔",
        ["Video metadata was not found."] = "ویڈیو میٹا ڈیٹا نہیں ملا۔",
        ["Video frames were not found."] = "ویڈیو فریمز نہیں ملے۔",
        ["Origin matches were not found."] = "اورجن میچز نہیں ملے۔",
        ["No failed analysis job was found for this video."] = "اس ویڈیو کے لیے کوئی ناکام تجزیاتی جاب نہیں ملا۔",
        ["Only failed analyses can be retried from this endpoint."] = "اس endpoint سے صرف ناکام تجزیے دوبارہ چلائے جا سکتے ہیں۔",
        ["Only failed jobs can be retried."] = "صرف ناکام جابز دوبارہ چلائے جا سکتے ہیں۔",
        ["Maximum retry count was reached."] = "دوبارہ کوشش کی حد مکمل ہو چکی ہے۔",
        ["Pause requested."] = "تجزیہ روکنے کی درخواست بھیج دی گئی۔",
        ["Pause is already requested."] = "تجزیہ روکنے کی درخواست پہلے ہی بھیجی جا چکی ہے۔",
        ["The analysis is already paused."] = "تجزیہ پہلے ہی رکا ہوا ہے۔",
        ["The analysis has already completed."] = "تجزیہ پہلے ہی مکمل ہو چکا ہے۔",
        ["This analysis cannot be paused in its current state."] = "موجودہ حالت میں یہ تجزیہ روکا نہیں جا سکتا۔",
        ["The analysis was cancelled before it could be resumed."] = "دوبارہ شروع ہونے سے پہلے تجزیہ منسوخ ہو گیا تھا۔",
        ["This analysis cannot be resumed in its current state."] = "موجودہ حالت میں یہ تجزیہ دوبارہ شروع نہیں کیا جا سکتا۔",
        ["The analysis is already queued or processing."] = "تجزیہ پہلے ہی قطار میں ہے یا چل رہا ہے۔",
        ["Analysis resume queued."] = "تجزیہ دوبارہ شروع کرنے کی درخواست قطار میں شامل ہو گئی۔",
        ["Analysis already completed."] = "تجزیہ پہلے ہی مکمل ہو چکا ہے۔",
        ["Analysis is not running."] = "تجزیہ اس وقت نہیں چل رہا۔",
        ["Cancellation requested."] = "منسوخی کی درخواست بھیج دی گئی۔",
        ["Job retry queued."] = "جاب دوبارہ چلانے کی درخواست قطار میں شامل ہو گئی۔",
        ["Video deleted successfully."] = "ویڈیو کامیابی سے حذف ہو گئی۔",
        ["Preparing video"] = "ویڈیو تیار ہو رہی ہے",
        ["Analysis completed"] = "تجزیہ مکمل",
        ["Waiting for processing worker"] = "پروسیسنگ ورکر کا انتظار ہے"
    };

    private static readonly IReadOnlyDictionary<string, string> Pashto = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Request completed successfully."] = "غوښتنه په بریالیتوب بشپړه شوه.",
        ["An unexpected error occurred."] = "یوه ناڅاپي تېروتنه رامنځته شوه.",
        ["Please try again later."] = "مهرباني وکړئ وروسته بیا هڅه وکړئ.",
        ["Unauthorized."] = "تاسو د دې عمل اجازه نه لرئ.",
        ["A video file is required."] = "د ویډیو فایل اړین دی.",
        ["The uploaded file is empty."] = "اپلوډ شوی فایل تش دی.",
        ["The uploaded file extension is not supported."] = "د اپلوډ شوي فایل توسیع ملاتړ نه کېږي.",
        ["The uploaded filename is invalid."] = "د اپلوډ شوي فایل نوم سم نه دی.",
        ["The uploaded file content type is not supported."] = "د اپلوډ شوي فایل ډول ملاتړ نه کېږي.",
        ["You must confirm that you have the right to upload this video for analysis."] = "تاسو باید تایید کړئ چې د دې ویډیو د تحلیل لپاره د اپلوډ حق لرئ.",
        ["Video was not found."] = "ویډیو ونه موندل شوه.",
        ["Analysis job was not found."] = "د تحلیل دنده ونه موندل شوه.",
        ["Job status was not found."] = "د دندې حالت ونه موندل شو.",
        ["Analysis result is not available yet."] = "د تحلیل پایله لا تر اوسه شتون نه لري.",
        ["Analysis report is not available yet."] = "راپور لا تر اوسه شتون نه لري.",
        ["Video metadata was not found."] = "د ویډیو میټاډیټا ونه موندل شوه.",
        ["Video frames were not found."] = "د ویډیو فریمونه ونه موندل شول.",
        ["Origin matches were not found."] = "د سرچینې میچونه ونه موندل شول.",
        ["No failed analysis job was found for this video."] = "د دې ویډیو لپاره ناکامه تحلیل دنده ونه موندل شوه.",
        ["Only failed analyses can be retried from this endpoint."] = "له دې endpoint څخه یوازې ناکام تحلیلونه بیا چلول کېدای شي.",
        ["Only failed jobs can be retried."] = "یوازې ناکامې دندې بیا چلول کېدای شي.",
        ["Maximum retry count was reached."] = "د بیا هڅې اعظمي حد پوره شوی.",
        ["Pause requested."] = "د تحلیل ځنډولو غوښتنه واستول شوه.",
        ["Pause is already requested."] = "د تحلیل ځنډولو غوښتنه مخکې شوې ده.",
        ["The analysis is already paused."] = "تحلیل مخکې ځنډول شوی دی.",
        ["The analysis has already completed."] = "تحلیل مخکې بشپړ شوی دی.",
        ["This analysis cannot be paused in its current state."] = "په اوسني حالت کې دا تحلیل نه شي ځنډېدای.",
        ["The analysis was cancelled before it could be resumed."] = "تحلیل د بیا پیل مخکې لغوه شوی و.",
        ["This analysis cannot be resumed in its current state."] = "په اوسني حالت کې دا تحلیل بیا نه شي پیل کېدای.",
        ["The analysis is already queued or processing."] = "تحلیل مخکې په قطار کې دی یا روان دی.",
        ["Analysis resume queued."] = "د تحلیل بیا پیل غوښتنه قطار ته اضافه شوه.",
        ["Analysis already completed."] = "تحلیل مخکې بشپړ شوی دی.",
        ["Analysis is not running."] = "تحلیل اوس نه روان دی.",
        ["Cancellation requested."] = "د لغوه کولو غوښتنه واستول شوه.",
        ["Job retry queued."] = "د دندې بیا هڅه قطار ته اضافه شوه.",
        ["Video deleted successfully."] = "ویډیو په بریالیتوب حذف شوه.",
        ["Preparing video"] = "ویډیو چمتو کېږي",
        ["Analysis completed"] = "تحلیل بشپړ شو",
        ["Waiting for processing worker"] = "د پروسس کارکوونکي ته انتظار دی"
    };

    }
