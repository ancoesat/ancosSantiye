using AncosSantiye;

var builder = WebApplication.CreateBuilder(args);

// Loopback only. `--urls` can still change the port, but F7 rejects any non-loopback host.
if (string.IsNullOrEmpty(builder.Configuration["urls"]))
{
    builder.WebHost.UseUrls(ServerOptions.DefaultUrl);
}

var app = builder.Build();

app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }));

app.Run();
