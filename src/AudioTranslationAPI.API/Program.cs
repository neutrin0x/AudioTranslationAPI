using AudioTranslationAPI.Application.Services;
using AudioTranslationAPI.Infrastructure.Services;
using AudioTranslationAPI.Infrastructure.ExternalServices;
using Hangfire;
using Serilog;
using System.Text.Json.Serialization;
using AudioTranslationAPI.Application.Configuration;
using AudioTranslationAPI.Infrastructure.Repositories;
using Hangfire.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// ===== CONFIGURACION LOG =====
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/audiotranslation-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// ===== CONFIGURACION SERVICIOS =====
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// SWAGGER DOC
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Audio Translation API",
        Version = "v1",
        Description = "API para traducción de audio en tiempo real entre idiomas"
    });

    // ACTIVAR UPLOAD EN SWAGGER
    c.OperationFilter<FileUploadOperationFilter>();
});

// ===== CONFIGURACION CORS =====
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFlutterApp", policy =>
    {
        policy
            .AllowAnyOrigin() // En producción, especificar dominios exactos
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// ===== CONFIGURACION HANGFIRE =====
var hangfireConnectionString = builder.Configuration.GetConnectionString("HangfireConnection")
    ?? "Data Source=audiotranslation.db"; // SQLite por defecto para desarrollo

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseInMemoryStorage());

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Environment.ProcessorCount * 2; // Ajustar según recursos
    options.Queues = new[] { "critical", "default", "low" };
});

// ===== CONFIGURACION CLIENTE HTTP =====
builder.Services.AddHttpClient();

// ===== CONFIGURACION PROCESAMIENTO DE AUDIO =====
builder.Services.Configure<AudioProcessingOptions>(
    builder.Configuration.GetSection("AudioProcessing"));

builder.Services.Configure<ExternalServicesOptions>(
    builder.Configuration.GetSection("ExternalServices"));

// ===== INYECCION DE DEPENDENCIAS =====

// Application 
builder.Services.AddScoped<IAudioTranslationService, AudioTranslationService>();

// Infrastructure - Audio 
builder.Services.AddScoped<IAudioValidationService, AudioValidationService>();
builder.Services.AddScoped<IAudioConverterService, AudioConverterService>();
builder.Services.AddScoped<IAudioProcessingService, FFmpegAudioProcessingService>();

// Infrastructure - AI/ML
builder.Services.AddScoped<ISpeechToTextService, GoogleSpeechToTextService>();
builder.Services.AddScoped<ITranslationService, MyMemoryTranslationService>();
builder.Services.AddScoped<ITextToSpeechService, GoogleTextToSpeechService>();

// Infrastructure - Storage
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.AddSingleton<IAudioRepository, InMemoryAudioRepository>(); // Temporal, luego Entity Framework

// Background Services
builder.Services.AddScoped<IAudioTranslationBackgroundService, AudioTranslationBackgroundService>();

// ===== LIMITES DE TAMAÑO DE REQUEST =====
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 50_000_000; // 50MB
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 50_000_000; // 50MB
});

var app = builder.Build();

// ===== MIDDLEWARE PIPELINE =====

if (app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                       Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
    });

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Audio Translation API V1");
        c.RoutePrefix = string.Empty;
    });
}

app.UseHttpsRedirection();
app.UseCors("AllowFlutterApp");

// HANGFIRE DASHBOARD
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = app.Environment.IsDevelopment()
        ? new[] { new AllowAllConnectionsFilter() }
        : new[] { new HangfireAuthorizationFilter() }
});

app.UseRouting();
app.UseAuthorization();

app.MapControllers();

// ===== STARTUP =====
try
{
    // Verificar que FFmpeg esté disponible

    using (var scope = app.Services.CreateScope())
    {
        var audioProcessor = scope.ServiceProvider.GetRequiredService<IAudioProcessingService>();
        await audioProcessor.ValidateFFmpegInstallationAsync();
    }

    Log.Information("Audio Translation API iniciada correctamente");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Error fatal al iniciar la aplicación");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// ===== HELPERS =====

public class AllowAllConnectionsFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}

public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        // Implementar autenticación real en producción
        return context.GetHttpContext().Request.IsHttps;
    }
}

public class FileUploadOperationFilter : Swashbuckle.AspNetCore.SwaggerGen.IOperationFilter
{
    public void Apply(Microsoft.OpenApi.Models.OpenApiOperation operation, Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
    {
        var hasFileParameter = context.MethodInfo.GetParameters()
            .Any(p => p.ParameterType == typeof(IFormFile) ||
                     p.ParameterType == typeof(IFormFile[]));

        if (hasFileParameter)
        {
            operation.RequestBody = new Microsoft.OpenApi.Models.OpenApiRequestBody
            {
                Content = new Dictionary<string, Microsoft.OpenApi.Models.OpenApiMediaType>
                {
                    ["multipart/form-data"] = new Microsoft.OpenApi.Models.OpenApiMediaType
                    {
                        Schema = new Microsoft.OpenApi.Models.OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, Microsoft.OpenApi.Models.OpenApiSchema>
                            {
                                ["audioFile"] = new Microsoft.OpenApi.Models.OpenApiSchema
                                {
                                    Type = "string",
                                    Format = "binary"
                                },
                                ["sourceLanguage"] = new Microsoft.OpenApi.Models.OpenApiSchema
                                {
                                    Type = "string",
                                    Default = new Microsoft.OpenApi.Any.OpenApiString("es")
                                },
                                ["targetLanguage"] = new Microsoft.OpenApi.Models.OpenApiSchema
                                {
                                    Type = "string",
                                    Default = new Microsoft.OpenApi.Any.OpenApiString("en")
                                }
                            }
                        }
                    }
                }
            };
        }
    }
}