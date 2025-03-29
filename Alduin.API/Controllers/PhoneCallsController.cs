using Alduin.Core.Models.Configs;
using Microsoft.AspNetCore.Mvc;
using Twilio.TwiML;
using Twilio.TwiML.Voice;

namespace Alduin.API.Controllers
{
    [Route("api/phonecalls")]
    [ApiController]
    public class PhoneCallsController : Controller
    {
        private readonly GeneralSettings _settings;

        public PhoneCallsController(GeneralSettings settings)
        {
            _settings = settings;
        }

        public const string CURRENT_NGROK_URL = "https://604a-2804-14d-4c84-11a7-e4e2-5314-17cf-f3c3.ngrok-free.app/api/phonecalls/transcript";

        [HttpPost("incoming")]
        public IActionResult Incoming()
        {
            Console.WriteLine("Received phonecall");

            var response = new VoiceResponse();
            response.Say(_settings.OpenAISettings.Prompts.GreetingsPrompt, voice: "alice", language: "pt-BR");

            response.Record(
                timeout: 2,
                action: new Uri(CURRENT_NGROK_URL),
                maxLength: 30
            );

            return Content(response.ToString(), "application/xml");
        }

        [HttpPost("transcript")]
        public async Task<IActionResult> Transcript()
        {
            var form = await Request.ReadFormAsync();
            var transcription = form["TranscriptionText"];
            var from = form["From"];
            var callSid = form["CallSid"];

            Console.WriteLine($"Nova transcrição recebida:");
            Console.WriteLine($"De: {from}");
            Console.WriteLine($"CallSid: {callSid}");
            Console.WriteLine($"Texto transcrito: {transcription}");

            var resposta = $"Você disse: {transcription}";
            var twiml = new VoiceResponse();
            twiml.Say(resposta, voice: "alice", language: "pt-BR");

            return Content(twiml.ToString(), "application/xml");
        }
    }
}
