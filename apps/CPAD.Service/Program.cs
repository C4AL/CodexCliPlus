using CPAD.Application.Abstractions;
using CPAD.Infrastructure.Layout;
using CPAD.Infrastructure.Status;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "CPAD Service";
});

builder.WebHost.UseUrls("http://127.0.0.1:17320");

builder.Services.AddSingleton<ICpadLayoutService, CpadLayoutService>();
builder.Services.AddSingleton<IHostSnapshotService, HostSnapshotService>();

var app = builder.Build();

app.MapGet("/", () => Results.Content("""
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>CPAD Service Host</title>
    <style>
      body {
        margin: 0;
        font-family: "Segoe UI", sans-serif;
        background: #0f172a;
        color: #e2e8f0;
        display: grid;
        place-items: center;
        min-height: 100vh;
      }

      main {
        max-width: 760px;
        padding: 40px;
      }

      h1 {
        margin-top: 0;
        font-size: 36px;
      }

      p {
        line-height: 1.7;
        color: #cbd5e1;
      }

      code {
        color: #93c5fd;
      }
    </style>
  </head>
  <body>
    <main>
      <h1>CPAD Service Host</h1>
      <p>This is the local host for the unified .NET 10 CPAD architecture.</p>
      <p>The native WPF desktop consumes <code>/api/system/status</code> and can embed a management preview through <code>WebView2</code>.</p>
      <p>The remaining migration work moves runtime control, plugin market actions, and update workflows from the legacy stacks into this service.</p>
    </main>
  </body>
</html>
""", "text/html"));

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "CPAD.Service",
    runtime = ".NET 10"
}));

app.MapGet("/api/system/status", async (IHostSnapshotService snapshotService, CancellationToken cancellationToken) =>
{
    var snapshot = await snapshotService.GetSnapshotAsync(cancellationToken);
    return Results.Ok(snapshot);
});

app.Run();
