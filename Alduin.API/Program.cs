using System.Text;
using Alduin.Core.Models.Configs;
using Alduin.Core.Services.OpenAI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();

var settings = new GeneralSettings();
builder.Configuration.GetSection("GeneralSettings").Bind(settings);
builder.Services.AddSingleton(settings);
builder.Services.AddMemoryCache();

builder.Services.AddScoped<IOpenAIService, OpenAIService>();

var app = builder.Build();

app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();