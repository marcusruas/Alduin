using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Alduin.Core.Helpers;
using Alduin.Core.Models.AI;

namespace Alduin.Core.Models.Configs.OpenAI
{
    public static class OpenAIEventsFactory
    {
        public static OpenAIEvents CreateEvents()
        {
            var result = new OpenAIEvents();

            var json = File.ReadAllText("OpenAIEvents.json");

            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Não foi possível carregar os eventos da Open AI.");

            var options = new JsonSerializerOptions
            {
                Converters = { new JsonConverterExpandoObject() },
                PropertyNameCaseInsensitive = true
            };

            dynamic? events = JsonSerializer.Deserialize<ExpandoObject>(json, options);

            if (events == null)
                throw new Exception("Não foi possível converter os eventos.");

            events.SessionUpdate.session.instructions = LoadPrompt("OperatorPrompt");

            

            result.SessionUpdate = JsonSerializer.Serialize(events.SessionUpdate, SerializerOptions);
            result.StartConversation = JsonSerializer.Serialize(events.StartConversation, SerializerOptions);

            return result;
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

        private static JsonSerializerOptions SerializerOptions => new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };

        private static string LoadPrompt(string promptName)
        {
            var promptNameExt = promptName.EndsWith(".txt") ? promptName : $"{promptName}.txt";

            var promptsFolder = Path.Combine(AppContext.BaseDirectory, "Models", "Configs", "Prompts");
            string promptPath = Path.Combine(promptsFolder, promptNameExt);

            if (!File.Exists(promptPath))
                throw new Exception("Prompt not found");

            var operatorPrompt = File.ReadAllLines(promptPath, Encoding.UTF8);
            return string.Join(' ', operatorPrompt.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
    }
}
