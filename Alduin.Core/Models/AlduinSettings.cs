using Alduin.Models;

namespace Alduin
{
    public class AlduinSettings
    {
        /// <summary>
        /// Your Open AI API Key, that will be used for the customer service assistant.
        /// </summary>
        public required string OpenAIApiKey { get; set; }
        /// <summary>
        /// This is the instructions prompt the assistant will have to conduct the conversation with the customer.
        /// </summary>
        public required string OperatorInstructions { get; set; }
        /// <summary>
        /// Flag to check if the AI will use any functions. If you set it to true, you will need to setup the 'alduin.functions.json' file, as per the documentation
        /// </summary>
        public bool UseFunctions { get; set; } = false;
        /// <summary>
        /// The model that open AI will use for its web socket. the default is gpt-4o-realtime-preview-2024-10-01.
        /// </summary>
        public string RealtimeModel { get; set; } = "gpt-4o-realtime-preview-2024-10-01";
        /// <summary>
        /// The type of voice the AI will have. The supported voices are located in the OpenAI Documentation <see cref="https://platform.openai.com/docs/api-reference/audio/createSpeech"/>
        /// </summary>
        public string AIVoice { get; set; } = "echo";
        /// <summary>
        /// The POST Endpoint URL that will be used for receiving any incoming calls from Twillio.
        /// </summary>
        public string IncomingCallsEndpointUrl { get; set; } = "/api/phonecalls/incoming";
        /// <summary>
        /// The Web Socket endpoint URL that will be used for redirecting any incoming calls from Twillio.
        /// </summary>
        public string WebSocketUrl { get; set; } = "/ws/customer-service";

        internal const string OPEN_AI_WEBSOCKET_URL = "wss://api.openai.com/v1/realtime?model={0}";
        internal void EnsureIsValid()
        {
            if (string.IsNullOrWhiteSpace(OpenAIApiKey))
                throw new ArgumentException("OpenAIApiKey is required and cannot be empty.");

            if (string.IsNullOrWhiteSpace(OperatorInstructions))
                throw new ArgumentException("OperatorInstructions is required and cannot be empty.");

            if (string.IsNullOrWhiteSpace(RealtimeModel))
                throw new ArgumentException("RealtimeModel cannot be null or empty.");

            if (string.IsNullOrWhiteSpace(AIVoice))
                throw new ArgumentException("AIVoice cannot be null or empty.");

            if (!IncomingCallsEndpointUrl.StartsWith('/'))
                throw new ArgumentException("IncomingCallsEndpointUrl must start with '/'.");

            if (!WebSocketUrl.StartsWith('/'))
                throw new ArgumentException("WebSocketUrl must start with '/'.");
        }

    }
}
