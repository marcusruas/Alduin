using System.Net.Http.Headers;
using System.Text;
using Alduin.Core.Models.Configs;
using Alduin.Core.Services.OpenAI;
using Alduin.Core.Services.PhoneCalls;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();
builder.Services.AddMemoryCache(); 

var settings = new GeneralSettings();
builder.Configuration.GetSection("GeneralSettings").Bind(settings);
builder.Services.AddSingleton(settings);

builder.Services.AddHttpClient("Twillio", (serviceProvider, client) =>
{
    var token = settings.TwillioToken;
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
});

builder.Services.AddScoped<IOpenAIService, OpenAIService>();
builder.Services.AddScoped<IPhoneCallsService, PhoneCallsService>();

var app = builder.Build();

app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();