using System.Diagnostics;
using Alduin.Core.Models.Configs;
using Alduin.Core.Services.PhoneCalls;
using Microsoft.AspNetCore.Mvc;
using Twilio.TwiML;
using Twilio.TwiML.Voice;
namespace Alduin.API.Controllers
{
    [Route("api/phonecalls")]
    [ApiController]
    public class PhoneCallsController : Controller
    {

        public PhoneCallsController(IPhoneCallsService phoneCallService, GeneralSettings settings)
        {
            _phoneCallsService = phoneCallService;
            _settings = settings;
        }

        private IPhoneCallsService _phoneCallsService;
        private readonly GeneralSettings _settings;

        [HttpPost("incoming")]
        public IActionResult Incoming([FromForm] string CallSid)
        {
            _phoneCallsService.StartPhoneCall(CallSid);

            var response = new VoiceResponse();

            string speechUrl = Path.Combine($"{Request.Scheme}://{Request.Host}", _settings.AudiosFolder, _settings.GreetingsAudio).Replace("\\", "/");
            var greetingsAudio = new Uri(speechUrl);
            response.Play(greetingsAudio);

            var connect = new Connect();
            connect.Stream(url: $"wss://{Request.Host}/ws/customer-service");
            response.Append(connect);

            return Content(response.ToString(), "application/xml");
        }
    }
}
