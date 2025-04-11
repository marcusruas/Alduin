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
                    instructions: settings.OperatorInstructions + HANGUP_PROMPT,
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

        public static bool FunctionsFileExists() => File.Exists(Path.Combine(AppContext.BaseDirectory, FUNCTIONS_FILE_NAME));

        private static ExpandoObject BuildHangupFunction()
        {
            dynamic endCallFunction = new ExpandoObject();

            endCallFunction.type = "function";
            endCallFunction.name = "end_call";
            endCallFunction.description = "Após todos os critérios definidos nas instruções terem sido satisfeitos e o cliente não apresentar mais dúvidas, encerra a chamada de forma educada.";

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
        private const string HANGUP_PROMPT = " You must end the call and trigger the end_call function if any of the following conditions are met: the user has dialed the wrong number or is asking something unrelated to the services you provide; the user explicitly says they have no further questions or that the conversation is over; or the issue has been fully resolved and the user expresses satisfaction or no need for further assistance. Before ending the call, always communicate to the user that the call is about to be closed — using their preferred language, based on how they have been speaking so far. Always be polite and respectful, and make sure the user is not left with unanswered questions. Once you communicate the call is ending, trigger an event of type 'function_call' of type end_call.";
    }
}
