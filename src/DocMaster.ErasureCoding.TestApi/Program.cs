using Microsoft.OpenApi.Models;
using DocMaster.ErasureCoding;
using DocMaster.ErasureCoding.TestApi.Services;
using DocMaster.ErasureCoding.TestApi.Models;
using System.Reflection;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger configuration
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DocMaster Erasure Coding Test API",
        Version = "v1",
        Description = "Test API for ISA-L Erasure Coding Library - Manual validation of encode/decode operations for DocMaster object storage",
        Contact = new OpenApiContact
        {
            Name = "DocMaster Team"
        }
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

// Configuration binding
builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<ErasureCodingOptions>(
    builder.Configuration.GetSection(ErasureCodingOptions.SectionName));

// Register EC library
builder.Services.AddSingleton<IErasureCoder>(sp =>
{
    var options = sp.GetRequiredService<IOptions<ErasureCodingOptions>>().Value;
    return new IsaLErasureCoder(options.DataShards, options.ParityShards);
});

// Register storage service
builder.Services.AddSingleton<IStorageService, LocalStorageService>();

var app = builder.Build();

// Validate ISA-L on startup
ValidateIsaL(app.Services);

// Enable Swagger UI (at root for convenience)
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "DocMaster EC Test API v1");
    options.RoutePrefix = string.Empty;  // Swagger at http://localhost:5000/
    options.DocumentTitle = "DocMaster EC Test API";
});

// Health endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.MapControllers();

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
Console.WriteLine("DocMaster Erasure Coding Test API starting...");
Console.WriteLine($"Swagger UI available at: http://localhost:{port}");

app.Run();

void ValidateIsaL(IServiceProvider services)
{
    try
    {
        var coder = services.GetRequiredService<IErasureCoder>();
        var testData = new byte[100];
        var testShards = coder.Encode(testData);

        Console.WriteLine("✓ ISA-L library loaded successfully");
        Console.WriteLine($"  Configuration: RS({coder.DataShards},{coder.ParityShards})");
        Console.WriteLine($"  Test encode: {testData.Length} bytes → {testShards.Length} shards");
        Console.WriteLine($"  Library version: {IsaLErasureCoder.LibraryVersion}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ ISA-L library failed to load: {ex.Message}");
        Console.WriteLine("  Please install ISA-L (see README.md for instructions)");
        Environment.Exit(1);
    }
}
