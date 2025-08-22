using Alduin.Models;

namespace Alduin
{
    public class AlduinRealtimeSettings
    {
        /// <summary>
        /// The model that open AI will use for its web socket. the default is gpt-4o-realtime-preview-2024-12-17.
        /// </summary>
        public string RealtimeModel { get; set; } = "gpt-4o-realtime-preview-2025-06-03";
        
        /// <summary>
        /// The type of voice the AI will have. The supported voices are located in the OpenAI Documentation <see cref="https://platform.openai.com/docs/api-reference/audio/createSpeech"/>
        /// </summary>
        public string AIVoice { get; set; } = "echo";
        
        /// <summary>
        /// The POST Endpoint URL that will be used for receiving any incoming calls from Twilio.
        /// </summary>
        public string IncomingCallsEndpointUrl { get; set; } = "/api/phonecalls/incoming";
        
        /// <summary>
        /// The Web Socket endpoint URL that will be used for redirecting any incoming calls from Twilio.
        /// </summary>
        public string WebSocketUrl { get; set; } = "/ws/customer-service";
        
        /// <summary>
        /// Sets the time in seconds for the AI to finish the call in case the user is inactive or havent spoken in a while
        /// </summary>
        public int ClientInactivityTimeout { get; set; } = 600;

        internal const string OPEN_AI_WEBSOCKET_URL = "wss://api.openai.com/v1/realtime?model={0}";

        internal void EnsureIsValid()
        {
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