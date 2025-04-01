using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Alduin.Core.Models.AI;
using Alduin.Core.Models.Configs;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;

namespace Alduin.Core.Services.OpenAI
{
    public class OpenAIService : IOpenAIService
    {
        public OpenAIService(GeneralSettings settings)
        {
            _settings = settings;
        }

        private readonly GeneralSettings _settings;

        public async Task<string> GenerateSpeech(string speech)
        {
            string staticFolder = Path.Combine("wwwroot", _settings.AudiosFolder);

            if (!Directory.Exists(staticFolder))
                Directory.CreateDirectory(staticFolder);

            string fileName = $"{Guid.NewGuid()}.mp3";
            string filePath = Path.Combine(staticFolder, fileName);

            var options = new SpeechGenerationOptions() { ResponseFormat = GeneratedSpeechFormat.Mp3 };
            var speechAudio = await new OpenAIClient(_settings.OpenAISettings.ApiKey)
                    .GetAudioClient(_settings.OpenAISettings.Models.SpeechGeneratorModel)
                    .GenerateSpeechAsync(speech, GeneratedSpeechVoice.Echo, options);

            await using var fileStream = new FileStream(filePath, FileMode.Create);
            await speechAudio.Value.ToStream().CopyToAsync(fileStream);

            return fileName;
        }

        public async Task<string> GenerateTranscriptFromRecording(Stream recording)
        {
            var audioTranscription = await new OpenAIClient(_settings.OpenAISettings.ApiKey)
                .GetAudioClient(_settings.OpenAISettings.Models.AudioTranscribeModel)
                .TranscribeAudioAsync(recording, "audio.wav");

            return audioTranscription.Value.Text;
        }

        public async Task<AIResponse> GenerateResponseFromCall(List<ChatMessage> chatHistory)
        {
            var chatResponse = await new OpenAIClient(_settings.OpenAISettings.ApiKey)
                .GetChatClient(_settings.OpenAISettings.Models.ChatModel)
                .CompleteChatAsync(chatHistory);

            string responseText = chatResponse.Value.Content.FirstOrDefault().Text;
            var responseAudio = await GenerateSpeech(responseText);

            return new AIResponse(responseAudio, responseText);
        }
    }
}
