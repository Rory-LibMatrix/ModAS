using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.OpenApi.Models;
using ModAS.Server;
using System.Diagnostics;
using System.Text.Json;
using Elastic.Apm;
using Elastic.Apm.Api;
using Elastic.Apm.NetCoreAll;
using LibMatrix;
using LibMatrix.Services;
using ModAS.Server.Authentication;
using ModAS.Server.Services;
using MxApiExtensions.Services;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options => { options.JsonSerializerOptions.WriteIndented = true; });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => {
    c.SwaggerDoc("v1", new OpenApiInfo() {
        Version = "v1",
        Title = "Rory&::ModAS",
        Description = "Moderation tooling, embracing the power of AppServices"
    });
    c.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, "ModAS.Server.xml"));
});

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

builder.Services.AddSingleton<ModASConfiguration>();

builder.Services.AddSingleton<UserProviderService>();
builder.Services.AddSingleton<RoomContextService>();
builder.Services.AddSingleton<RoomStateCacheService>();
// builder.Services.AddScoped<UserContextService>();

builder.Services.AddSingleton<TieredStorageService>(x => new TieredStorageService(
    cacheStorageProvider: new FileStorageProvider("/run"),
    dataStorageProvider: new FileStorageProvider("/run")
));
builder.Services.AddRoryLibMatrixServices();

//trace init time for app service registration
if (File.Exists("appservice_registration.yaml")) {
    await using var stream = File.OpenRead("appservice_registration.yaml");
    builder.Services.AddSingleton<AppServiceRegistration>(await JsonSerializer.DeserializeAsync<AppServiceRegistration>(stream) ??
                                                          throw new Exception("Failed to deserialize appservice registration file"));
}
else {
    var sw = Stopwatch.StartNew();
    var asr = new AppServiceRegistration();
    File.WriteAllText("appservice_registration.yaml", JsonSerializer.Serialize(asr, new JsonSerializerOptions() { WriteIndented = true }));
    sw.Stop();
    Console.WriteLine($"Generated AppService registration file in {sw.Elapsed}!");
    builder.Services.AddSingleton<AppServiceRegistration>(asr);
}

builder.Services.AddRequestTimeouts(x => {
    x.DefaultPolicy = new RequestTimeoutPolicy {
        Timeout = TimeSpan.FromMinutes(10),
        WriteTimeoutResponse = async context => {
            context.Response.StatusCode = 504;
            context.Response.ContentType = "application/json";
            await context.Response.StartAsync();
            await context.Response.WriteAsJsonAsync(new MatrixException() {
                ErrorCode = "M_TIMEOUT",
                Error = "Request timed out"
            }.GetAsJson());
            await context.Response.CompleteAsync();
        }
    };
});

// builder.Services.AddCors(x => x.AddDefaultPolicy(y => y.AllowAnyHeader().AllowCredentials().AllowAnyOrigin().AllowAnyMethod()));
builder.Services.AddCors(options => {
    options.AddPolicy(
        "Open",
        policy => policy.AllowAnyOrigin().AllowAnyHeader());
});

var app = builder.Build();

// if (app.Environment.IsDevelopment()) {
app.UseSwagger();
app.UseSwaggerUI(c => {
    c.EnableTryItOutByDefault();
});
app.UseReDoc(c => {
    c.EnableUntrustedSpec();
    c.ScrollYOffset(10);
    c.HideHostname();
    c.HideDownloadButton();
    c.HideLoading();
    c.ExpandResponses("200,201");
    c.RequiredPropsFirst();
});
// }

app.UseAllElasticApm(builder.Configuration);
Agent.AddFilter((ISpan span) => {
    if (span.Context.Http is not null && span.Name.StartsWith($"{span.Context.Http.Method} ")) {
        span.Name = $"{span.Context.Http.Method} {span.Context.Http.Url}";
    }

    return span;
});

app.UseFileServer();

app.UseCors("Open");

app.MapControllers();

app.UseMiddleware<AuthMiddleware>();

app.Run();