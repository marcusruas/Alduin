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
        /// Realtime-specific settings for OpenAI and Twilio integration
        /// </summary>
        public AlduinRealtimeSettings Realtime { get; set; } = new();

        internal void EnsureIsValid()
        {
            if (string.IsNullOrWhiteSpace(OpenAIApiKey))
                throw new ArgumentException("OpenAIApiKey is required and cannot be empty.");

            if (string.IsNullOrWhiteSpace(OperatorInstructions))
                throw new ArgumentException("OperatorInstructions is required and cannot be empty.");

            Realtime.EnsureIsValid();
        }
    }
}
