using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Alduin.Core.Models.Configs;
using Alduin.Core.Models.Configs.OpenAI;
using Alduin.Core.Services.CustomerService;
using Alduin.Core.Services.PhoneCalls;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualBasic;
using NAudio.Wave;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();
builder.Services.AddMemoryCache(); 

var settings = new GeneralSettings();
builder.Configuration.GetSection("GeneralSettings").Bind(settings);
settings.OpenAISettings.Events = OpenAIEventsFactory.CreateEvents();

builder.Services.AddSingleton(settings);

builder.Services.AddHttpClient("Twillio", (serviceProvider, client) =>
{
    var token = settings.TwillioToken;
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
});

builder.Services.AddScoped<IPhoneCallsService, PhoneCallsService>();
builder.Services.AddSingleton<ICustomerServiceHandler, CustomerServiceHandler>();

var app = builder.Build();

app.UseStaticFiles();

app.UseWebSockets();

app.Map("/ws/customer-service", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
        context.Response.StatusCode = 400;

    using var socket = await context.WebSockets.AcceptWebSocketAsync();

    var handler = context.RequestServices.GetRequiredService<ICustomerServiceHandler>();
    await handler.HandleWebSocket(socket);
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();