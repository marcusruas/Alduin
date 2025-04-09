using System.Dynamic;
using System.Text.Json;
using System.Text;
using Alduin.Core.Helpers;
using System.Reflection;
using System.Net.Http.Json;

namespace Alduin.Models
{
    internal static class OpenAIEventsBuilder
    {
        public static string BuildSessionUpdateEvent(AlduinSettings settings)
        {
            dynamic? functions = null;

            if (settings.UseFunctions)
            {
                if (!TryReadFunctionsJson(out var jsonFunctions))
                    throw new ArgumentException("If you want to use functions, you will need to setup the 'alduin.functions.json' file.");
                
                var functionsSerializerOptions = new JsonSerializerOptions
                {
                    Converters = { new JsonConverterExpandoObject() },
                    PropertyNameCaseInsensitive = true
                };
                functions = JsonSerializer.Deserialize<ExpandoObject[]>(jsonFunctions, functionsSerializerOptions) ?? throw new ArgumentException("'alduin.functions.json' file is in the wrong format.");
            }

            var sessionUpdate = new SessionConfigsEvent(
                type: "session.update",
                session: new SessionContent(
                    tools: settings.UseFunctions ? functions : [],
                    voice: settings.AIVoice,
                    instructions: settings.OperatorInstructions,
                    turn_detection: new TurnDetection(type: "server_vad"),
                    input_audio_format: "g711_ulaw",
                    output_audio_format: "g711_ulaw",
                    modalities: ["text", "audio"],
                    temperature: 0.8
                )
            );

            var resultSerializerOptions = new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = false
            };

            return JsonSerializer.Serialize(sessionUpdate, resultSerializerOptions);
        }

        public static object BuildDeltaEvent(string streamId, string? delta) => new
        {
            @event = "media",
            streamSid = streamId,
            media = new
            {
                payload = delta
            }
        };

        public static object BuildFunctionResponseEvent(string? callId, object response) => new
        {
            type = "conversation.item.create",
            item = new
            {
                type = "function_call_output",
                call_id = callId,
                output = JsonSerializer.Serialize(response)
            }
        };

        public static object StartConversationEvent = new
        {
            type = "conversation.item.create",
            item = new
            {
                type = "message",
                role = "user",
                content = new[]
                {
                    new
                    {
                        type = "input_text",
                        text = "A chamada começou. Cumprimente o usuário com base nas instruções fornecidas."
                    }
                }
            }
        };

        private static bool TryReadFunctionsJson(out string? functions)
        {
            functions = null;
            string fileName = "alduin.functions.json";
            string filePath = Path.Combine(AppContext.BaseDirectory, fileName);

            if (!File.Exists(filePath))
                return false;

            functions = File.ReadAllText(filePath);
            return true;
        }
    }
}
