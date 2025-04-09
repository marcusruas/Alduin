using Alduin.Core.Handlers.AlduinFunctions;
using Twilio.TwiML;
using Twilio.TwiML.Voice;

namespace Alduin
{
    public static class AlduinDependencyInjection
    {
        public static IServiceCollection AddAlduin(this IServiceCollection services, Action<AlduinSettings> alduinConfiguration, Action<IAlduinFunctionRegistry>? configureFunctions = null)
        {
            var settings = new AlduinSettings()
            {
                OpenAIApiKey = "",
                OperatorInstructions = ""
            };
            alduinConfiguration.Invoke(settings);
            services.AddSingleton(settings);

            if (settings.UseFunctions)
            {
                if (settings.UseFunctions && configureFunctions == null)
                    throw new ArgumentException("If you've set the AlduinSettings.UseFunctions to true, you must provide the functions");

                var registry = new AlduinFunctionRegistry();
                configureFunctions?.Invoke(registry);
                services.AddSingleton<IAlduinFunctionRegistry>(registry);
            }

            services.AddSingleton<ICustomerServiceHandler, CustomerServiceHandler>();

            return services;
        }

        public static IServiceCollection AddAlduin(this IServiceCollection services, IConfiguration configuration, Action<IAlduinFunctionRegistry>? configureFunctions = null)
        {
            var settings = configuration.GetSection("Alduin").Get<AlduinSettings>();

            if (settings == null)
                throw new ArgumentException("'Alduin' Section was not found in IConfiguration. Make sure you've added the configuration section properly in your appsettings.");

            services.AddSingleton(settings);

            if (settings.UseFunctions)
            {
                if (settings.UseFunctions && configureFunctions == null)
                    throw new ArgumentException("If you've set the AlduinSettings.UseFunctions to true, you must provide the functions");

                var registry = new AlduinFunctionRegistry();
                configureFunctions?.Invoke(registry);
                services.AddSingleton<IAlduinFunctionRegistry>(registry);
            }

            services.AddSingleton<ICustomerServiceHandler, CustomerServiceHandler>();

            return services;
        }

        public static WebApplication UseAlduin(this WebApplication app)
        {
            var settings = app.Services.GetService<AlduinSettings>();

            if (settings == null)
                throw new ArgumentException("AlduinSettings was not registered. Make sure to call services.AddAlduin(...) before using AddAlduin on the application.");

            app.MapPost(settings.IncomingCallsEndpointUrl, async (HttpContext context) =>
            {
                var form = await context.Request.ReadFormAsync();
                var callSid = form["CallSid"].ToString();

                var response = new VoiceResponse();
                var connect = new Connect();
                connect.Stream(url: $"wss://{context.Request.Host}{settings.WebSocketUrl}?CallSid={callSid}");

                response.Append(connect);

                return Results.Content(response.ToString(), "application/xml");
            });

            app.Map(settings.WebSocketUrl, async (HttpContext context) =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    return;
                }

                var handler = context.RequestServices.GetRequiredService<ICustomerServiceHandler>();
                await handler.HandleAsync(context);
            });

            return app;
        }
    }
}
