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
                var desserializedFunctions = JsonSerializer.Deserialize<List<ExpandoObject>>(jsonFunctions, functionsSerializerOptions);

                if (desserializedFunctions == null)
                    throw new ArgumentException($"'{FUNCTIONS_FILE_NAME}' file is in the wrong format.");

                desserializedFunctions.Add(BuildHangupFunction());

                functions = desserializedFunctions;
            }

            var sessionUpdate = new SessionConfigsEvent(
                type: "session.update",
                session: new SessionContent(
                    tools: settings.UseFunctions ? functions : [BuildHangupFunction()],
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

        public static object BuildFunctionResponseEvent(string? callId, object response)
        {
            var serializerOptions = new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = false
            };

            return new
            {
                type = "conversation.item.create",
                item = new
                {
                    type = "function_call_output",
                    call_id = callId,
                    output = JsonSerializer.Serialize(response, serializerOptions)
                }
            };
        }
        
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
                        text = "The call has started. Greet the user politely using their language, based on the instructions provided."
                    }
                }
            }
        };

        public static bool FunctionsFileExists() => File.Exists(Path.Combine(AppContext.BaseDirectory, FUNCTIONS_FILE_NAME));

        private static ExpandoObject BuildHangupFunction()
        {
            dynamic endCallFunction = new ExpandoObject();

            endCallFunction.type = "function";
            endCallFunction.name = "end_call";
            endCallFunction.description = "Ends the call when the user's request has been resolved, the conversation has reached a natural conclusion, or the user has no further questions. Use this to politely close the session once all relevant topics have been addressed.";

            endCallFunction.parameters = new ExpandoObject();
            endCallFunction.parameters.type = "object";
            endCallFunction.parameters.properties = new ExpandoObject();
            endCallFunction.parameters.required = new List<string>();

            return endCallFunction;
        }

        private static bool TryReadFunctionsJson(out string? functions)
        {
            functions = null;

            if (!FunctionsFileExists())
                return false;

            string filePath = Path.Combine(AppContext.BaseDirectory, FUNCTIONS_FILE_NAME);

            functions = File.ReadAllText(filePath);
            return true;
        }

        private const string FUNCTIONS_FILE_NAME = "alduin.functions.json";
    }
}
