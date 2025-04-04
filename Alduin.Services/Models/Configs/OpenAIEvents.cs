using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alduin.Core.Models.Configs
{
    public static class OpenAIEvents
    {
        public static object SessionUpdate(string operatorPrompt) => new
        {
            type = "session.update",
            session = new
            {
                tools = new[]
                    {
                        new
                        {
                            type = "function",
                            name = "consulta_via_cep",
                            description = "Consulta informações de endereço usando o ViaCEP com base no CEP fornecido.",
                            parameters = new
                            {
                                type = "object",
                                properties = new
                                {
                                    cep = new
                                    {
                                        type = "string",
                                        description = "O CEP a ser consultado, no formato '12345678'."
                                    }
                                },
                                required = new[] { "cep" }
                            }
                        },
                    },
                turn_detection = new { type = "server_vad" },
                input_audio_format = "g711_ulaw",
                output_audio_format = "g711_ulaw",
                voice = "echo",
                instructions = operatorPrompt,
                modalities = new[] { "text", "audio" },
                temperature = 0.8
            }
        };


        public static object StartConversation(string operatorPrompt) => new
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
                        text = operatorPrompt
                    }
                }
            }
        };
    }
}
