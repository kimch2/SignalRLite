using SignalRTestServer.Hubs;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

// Allow all origins so the Unity client (any platform) can connect.
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod()));

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors      = true;   // full error messages to client
    options.KeepAliveInterval         = TimeSpan.FromSeconds(10);
    options.ClientTimeoutInterval     = TimeSpan.FromSeconds(30);
    options.HandshakeTimeout          = TimeSpan.FromSeconds(10);
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1 MB
})
// Use PascalCase JSON serialization so Unity JsonUtility can deserialize by field name.
// ASP.NET Core SignalR defaults to camelCase, which breaks JsonUtility (case-sensitive).
.AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = null; // null = PascalCase
});

// ── App ───────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.UseCors();

// ── Routes ────────────────────────────────────────────────────────────────────

// Info endpoint – visit http://localhost:5000/ to confirm the server is running.
app.MapGet("/", () => new
{
    status    = "SignalR Test Server running",
    hub       = "ws://localhost:5000/testhub",
    negotiate = "http://localhost:5000/testhub/negotiate",
    time      = DateTime.UtcNow.ToString("O"),
});

// The hub endpoint used by the Unity client.
app.MapHub<TestHub>("/testhub");

// ── Run ───────────────────────────────────────────────────────────────────────

Console.WriteLine("==========================================");
Console.WriteLine("  SignalR Test Server");
Console.WriteLine("  Hub:  http://localhost:5000/testhub");
Console.WriteLine("  Info: http://localhost:5000/");
Console.WriteLine("==========================================");

app.Run("http://0.0.0.0:5000");
